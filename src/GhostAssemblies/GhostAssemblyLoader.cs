namespace GhostAssemblies
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Collections.Generic;
    using Internal.Mono.Cecil;
    using System.Diagnostics;

    public class GhostAssemblyLoader : IDisposable
    {
        string installDir;
        string defaultAssemblyName;
        IDictionary<string, GhostAssembly> ghostAssemblies;

        public GhostAssemblyLoader(string ghostAssemblyPaths = null, string defaultAssemblyName = null, string installDir = null)
        {
            var ghostAssemblyLocations = getAssemblyLocations(ghostAssemblyPaths ?? "");
            ghostAssemblies = createGhostAssemblies(this, ghostAssemblyLocations);

            this.defaultAssemblyName = defaultAssemblyName;
            this.installDir = installDir ?? getDirectory(System.Reflection.Assembly.GetCallingAssembly());

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        static string[] getAssemblyLocations(string paths)
        {
            return paths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
            foreach(var ghostAssembly in ghostAssemblies.Values)
            {
                ghostAssembly.Dispose();
            }
        }

        System.Reflection.Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requestingAssembly = args.RequestingAssembly;
            if (isGhost(requestingAssembly))
            {
                return ResolveAssembly(args.Name);
            }

            return null;
        }

        public System.Reflection.Assembly ResolveAssembly(string name = null)
        {
            if(name == null)
            {
                if(defaultAssemblyName == null)
                {
                    var message = GhostAssemblyException.NoDefaultGhostMessage;
                    throw new GhostAssemblyException(message);
                }

                name = defaultAssemblyName;
            }

            var assemblyName = new AssemblyName(name);

            // Resolve known "ghost" assembly.
            var ghostAssembly = findGhostAssembly(assemblyName.Name);
            if (ghostAssembly != null)
            {
                return ghostAssembly.GetAssembly();
            }

            // Resolve unknown assembly.
            var location = findAssemblyLocation(installDir, assemblyName.Name);
            if (location != null)
            {
                return Assembly.LoadFrom(location);
            }

            return null;
        }

        static string getDirectory(System.Reflection.Assembly assembly)
        {
            var localPath = new Uri(assembly.EscapedCodeBase).LocalPath;
            return Path.GetDirectoryName(localPath);
        }

        GhostAssembly findGhostAssembly(string assemblyName)
        {
            GhostAssembly ghostAssembly;
            if(ghostAssemblies.TryGetValue(assemblyName, out ghostAssembly))
            {
                return ghostAssembly;
            }

            return null;
        }

        bool isGhost(System.Reflection.Assembly assembly)
        {
            foreach(var ghostAssembly in ghostAssemblies.Values)
            {
                if(ghostAssembly.Assembly == null)
                {
                    continue;
                }

                if(assembly == ghostAssembly.Assembly)
                {
                    return true;
                }
            }

            return false;
        }

        public string FindGhostAssemblyLocation(string assemblyName)
        {
            var ghostAssembly = findGhostAssembly(assemblyName);
            return ghostAssembly?.Location;
        }

        internal DateTime GetLatestWriteTime()
        {
            var latestWriteTime = DateTime.MinValue;

            foreach(var ghostAssembly in ghostAssemblies.Values)
            {
                var lastWriteTime = ghostAssembly.GetLastWriteTime();
                if(lastWriteTime > latestWriteTime)
                {
                    latestWriteTime = lastWriteTime;
                }
            }

            return latestWriteTime;
        }

        IDictionary<string, GhostAssembly> createGhostAssemblies(GhostAssemblyLoader loader, string[] assemblyLocations)
        {
            var ghostAssemblies = new Dictionary<string, GhostAssembly>();
            foreach (var assemblyLocation in assemblyLocations)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(assemblyLocation);
                ghostAssemblies[assemblyName] = new GhostAssembly(loader, assemblyName, assemblyLocation);
            }

            return ghostAssemblies;
        }

        static string findAssemblyLocation(string dir, string name)
        {
            var assemblyLocation = Path.Combine(dir, name + ".dll");
            if(File.Exists(assemblyLocation))
            {
                return assemblyLocation;
            }

            assemblyLocation = Path.ChangeExtension(assemblyLocation, "exe");
            if (File.Exists(assemblyLocation))
            {
                return assemblyLocation;
            }

            return null;
        }

        class GhostAssembly : IDisposable
        {
            GhostAssemblyLoader ghostAssemblyLoader;
            DateTime lastWriteTime;
            System.Reflection.Assembly assembly;
            AssemblyInfo assemblyInfo;

            internal GhostAssembly(GhostAssemblyLoader ghostAssemblyLoader, string name, string location)
            {
                this.ghostAssemblyLoader = ghostAssemblyLoader;
                Name = name;
                Location = location;
            }

            public void Dispose()
            {
                assemblyInfo?.Dispose();
                assemblyInfo = null;
            }

            public string Name { get; }

            public string Location { get; }

            public System.Reflection.Assembly Assembly
            {
                get { return assembly; }

                private set
                {
                    assembly = value;
                    assemblyInfo?.Dispose();
                    assemblyInfo = new AssemblyInfo(assembly.FullName, Location);
                }
            }

            class AssemblyInfo : System.Reflection.Assembly, IDisposable
            {
                internal AssemblyInfo(string fullName, string location)
                {
                    FullName = fullName;
                    Location = location;
                    AppDomain.CurrentDomain.SetData(FullName, this);
                }

                public void Dispose()
                {
                    AppDomain.CurrentDomain.SetData(FullName, null);
                }

                public override string FullName { get; }

                public override string Location { get; }
            }

            public System.Reflection.Assembly GetAssembly()
            {
                if(!File.Exists(Location))
                {
                    var message = GhostAssemblyException.CreateGhostNotFoundMessage(Location);
                    throw new GhostAssemblyException(message);
                }

                if (Assembly == null || ReloadRequired())
                {
                    var newAssembly = loadAssemblyFromBytes(Location);
                    if(Assembly != null)
                    {
                        var ghostAssemblies = findGhostAssembliesNeedingNewVersion(ghostAssemblyLoader, Assembly, newAssembly);
                        if (ghostAssemblies.Count > 0)
                        {
                            newAssembly = loadAssemblyFromBytes(Location, ghostAssemblies);
                            Trace.WriteLine("LoadAssemblyFromBytes (tweak refs): " + Assembly + " " + lastWriteTime);
                        }
                    }

                    Assembly = newAssembly;
                    lastWriteTime = ghostAssemblyLoader.GetLatestWriteTime();
                    Trace.WriteLine("LoadAssemblyFromBytes: " + Assembly + " " + lastWriteTime);
                }

                return Assembly;
            }

            bool ReloadRequired()
            {
                if (lastWriteTime < ghostAssemblyLoader.GetLatestWriteTime())
                {
                    return true;
                }

                return false;
            }

            internal DateTime GetLastWriteTime()
            {
                if (!File.Exists(Location))
                {
                    return DateTime.MinValue;
                }

                return File.GetLastWriteTime(Location);
            }

            static IList<GhostAssembly> findGhostAssembliesNeedingNewVersion(GhostAssemblyLoader ghostAssemblyLoader,
                System.Reflection.Assembly oldAssembly, System.Reflection.Assembly newAssembly)
            {
                var ghostAssemblyList = new List<GhostAssembly>();
                foreach (var referencedAssembly in newAssembly.GetReferencedAssemblies())
                {
                    var ghostAssembly = ghostAssemblyLoader.findGhostAssembly(referencedAssembly.Name);
                    if (ghostAssembly == null)
                    {
                        continue;
                    }

                    if (ghostAssembly.ReloadRequired())
                    {
                        var oldAssemblyName = findReferencedAssembly(oldAssembly, referencedAssembly.Name);
                        if (oldAssemblyName != null && oldAssemblyName.Version == referencedAssembly.Version)
                        {
                            ghostAssemblyList.Add(ghostAssembly);
                        }
                    }
                }

                return ghostAssemblyList;
            }

            static AssemblyName findReferencedAssembly(System.Reflection.Assembly assembly, string name)
            {
                foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
                {
                    if(referencedAssembly.Name == name)
                    {
                        return referencedAssembly;
                    }
                }

                return null;
            }

            static System.Reflection.Assembly loadAssemblyFromBytes(string location,
                IEnumerable<GhostAssembly> newVersionAssemblies = null)
            {
                var asmBytes = File.ReadAllBytes(location);
                if (newVersionAssemblies != null)
                {
                    asmBytes = newVersionAssemblyReferences(asmBytes, newVersionAssemblies);
                }

                var pdbFile = Path.ChangeExtension(location, "pdb");
                if (File.Exists(pdbFile))
                {
                    var pdbBytes = File.ReadAllBytes(pdbFile);
                    return System.Reflection.Assembly.Load(asmBytes, pdbBytes);
                }

                return System.Reflection.Assembly.Load(asmBytes);
            }

            static byte[] newVersionAssemblyReferences(byte[] assemblyBytes, IEnumerable<GhostAssembly> newVersionGhostAssemblies)
            {
                var revision = getNewRevision();
                var stream = new MemoryStream();
                stream.Write(assemblyBytes, 0, assemblyBytes.Length);
                stream.Position = 0;

                using (var module = ModuleDefinition.ReadModule(stream))
                {
                    foreach (var assemblyReference in module.AssemblyReferences)
                    {
                        foreach (var newVersionGhostAssembly in newVersionGhostAssemblies)
                        {
                            if(newVersionGhostAssembly.Name == assemblyReference.Name)
                            {
                                var ver = assemblyReference.Version;
                                assemblyReference.Version = new Version(ver.Major, ver.Minor, ver.Build, revision);
                            }
                        }
                    }

                    module.Write();
                }

                return stream.ToArray();
            }

            static int getNewRevision()
            {
                // The default revision number is the number of seconds since midnight local time.
                var now = DateTime.Now;
                return (now.Hour * 60 + now.Minute) * 60 + now.Second;
            }
        }

#if DEBUG
        class File
        {
            internal static Func<string, bool> Exists = (string path) => System.IO.File.Exists(path);
            internal static Func<string, DateTime> GetLastWriteTime = (string path) => System.IO.File.GetLastWriteTime(path);
            internal static Func<string, byte[]> ReadAllBytes = (string path) => System.IO.File.ReadAllBytes(path);
        }

        class Assembly
        {
            internal static Func<string, System.Reflection.Assembly> LoadFrom = (string assemblyFile) => System.Reflection.Assembly.LoadFrom(assemblyFile);
        }
#endif
    }

    public class GhostAssemblyException : Exception
    {
        public GhostAssemblyException(string message) : base(message)
        {
        }

        public static string CreateGhostNotFoundMessage(string assemblyFile) =>
            string.Format("Couldn't find ghost assembly at '{0}'.", assemblyFile);

        public static string CreateChangeAssemblyVersionMessage(string assemblyName)
        {
            return string.Format(@"The assembly version of '{0}' must change before it can be reloaded.
This can be done automatically by adding a '*' to the '{0}' project's assembly version:
[assembly: AssemblyVersion(""1.0.0.*"")]", assemblyName);
        }

        public const string NoDefaultGhostMessage = "No default ghost assembly name name was specified.";
    }
}
