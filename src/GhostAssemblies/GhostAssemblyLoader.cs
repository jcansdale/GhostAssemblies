namespace GhostAssemblies
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Collections.Generic;
    using Internal.Mono.Cecil;

    public class GhostAssemblyLoader : IDisposable
    {
        string installDir;
        string defaultAssemblyName;
        IDictionary<string, GhostAssembly> ghostAssemblies;

        public GhostAssemblyLoader(string ghostAssemblyPaths, string defaultAssemblyName = null, string installDir = null)
        {
            this.defaultAssemblyName = defaultAssemblyName;
            this.installDir = installDir ?? getDirectory(System.Reflection.Assembly.GetCallingAssembly());

            var ghostAssemblyFiles = getAssemblyFiles(ghostAssemblyPaths);
            ghostAssemblies = createGhostAssemblies(ghostAssemblyFiles);

            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        static string[] getAssemblyFiles(string paths)
        {
            if(paths == null)
            {
                return new string[0];
            }

            return paths.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
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
            var assemblyFile = findAssemblyFile(installDir, assemblyName.Name);
            if (assemblyFile != null)
            {
                return Assembly.LoadFrom(assemblyFile);
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

        public string FindGhostAssemblyFile(string assemblyName)
        {
            var ghostAssembly = findGhostAssembly(assemblyName);
            return ghostAssembly?.AssemblyFile;
        }

        IDictionary<string, GhostAssembly> createGhostAssemblies(string[] assemblyFiles)
        {
            var ghostAssemblies = new Dictionary<string, GhostAssembly>();
            foreach (var assemblyFile in assemblyFiles)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(assemblyFile);
                ghostAssemblies[assemblyName] = new GhostAssembly(this, assemblyName, assemblyFile);
            }

            return ghostAssemblies;
        }

        static string findAssemblyFile(string dir, string name)
        {
            var assemblyFile = Path.Combine(dir, name + ".dll");
            if(File.Exists(assemblyFile))
            {
                return assemblyFile;
            }

            assemblyFile = Path.ChangeExtension(assemblyFile, "exe");
            if (File.Exists(assemblyFile))
            {
                return assemblyFile;
            }

            return null;
        }

        class GhostAssembly
        {
            GhostAssemblyLoader ghostAssemblyLoader;
            DateTime lastWriteTime;

            internal GhostAssembly(GhostAssemblyLoader ghostAssemblyLoader, string name, string assemblyFile)
            {
                this.ghostAssemblyLoader = ghostAssemblyLoader;
                Name = name;
                AssemblyFile = assemblyFile;
            }

            public string Name { get; }

            public string AssemblyFile { get; }

            public System.Reflection.Assembly Assembly { get; private set; }

            public System.Reflection.Assembly GetAssembly()
            {
                if(!File.Exists(AssemblyFile))
                {
                    var message = GhostAssemblyException.CreateGhostNotFoundMessage(AssemblyFile);
                    throw new GhostAssemblyException(message);
                }

                if(Assembly == null)
                {
                    Assembly = loadAssemblyFromBytes(AssemblyFile);
                    lastWriteTime = File.GetLastWriteTime(AssemblyFile);
                }

                if(reloadRequired())
                {
                    var newAssembly = loadAssemblyFromBytes(AssemblyFile);
                    var ghostAssemblies = findGhostAssembliesNeedingNewVersion(ghostAssemblyLoader, Assembly, newAssembly);
                    if(ghostAssemblies.Count > 0)
                    {
                        newAssembly = loadAssemblyFromBytes(AssemblyFile, ghostAssemblies);
                    }

                    Assembly = newAssembly;
                    lastWriteTime = File.GetLastWriteTime(AssemblyFile);
                }

                return Assembly;
            }

            bool reloadRequired(IDictionary<GhostAssembly, bool> cache = null)
            {
                cache = cache ?? new Dictionary<GhostAssembly, bool>();

                if(Assembly == null)
                {
                    return false;
                }

                foreach (var referencedAssembly in Assembly.GetReferencedAssemblies())
                {
                    var ghostAssembly = ghostAssemblyLoader.findGhostAssembly(referencedAssembly.Name);
                    if (ghostAssembly == null)
                    {
                        continue;
                    }

                    bool reload;
                    if(cache.TryGetValue(ghostAssembly, out reload))
                    {
                        return reload;
                    }

                    reload = ghostAssembly.reloadRequired(cache);
                    cache[ghostAssembly] = reload;

                    if(reload)
                    {
                        return true;
                    }
                }

                if (lastWriteTime != File.GetLastWriteTime(AssemblyFile))
                {
                    return true;
                }

                return false;
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

                    if (ghostAssembly.reloadRequired())
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

            static System.Reflection.Assembly loadAssemblyFromBytes(string assemblyFile,
                IEnumerable<GhostAssembly> newVersionAssemblies = null)
            {
                var asmBytes = File.ReadAllBytes(assemblyFile);
                if (newVersionAssemblies != null)
                {
                    asmBytes = newVersionAssemblyReferences(asmBytes, newVersionAssemblies);
                }

                var pdbFile = Path.ChangeExtension(assemblyFile, "pdb");
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
