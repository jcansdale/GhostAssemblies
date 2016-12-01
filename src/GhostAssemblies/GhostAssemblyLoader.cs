namespace GhostAssemblies
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

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

        static IDictionary<string, GhostAssembly> createGhostAssemblies(string[] assemblyFiles)
        {
            var ghostAssemblies = new Dictionary<string, GhostAssembly>();
            foreach (var assemblyFile in assemblyFiles)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(assemblyFile);
                ghostAssemblies[assemblyName] = new GhostAssembly(assemblyFile);
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
            string assemblyFile;
            System.Reflection.Assembly assembly;
            DateTime lastWriteTime;

            internal GhostAssembly(string assemblyFile)
            {
                this.assemblyFile = assemblyFile;
            }

            public string AssemblyFile => assemblyFile;

            public System.Reflection.Assembly Assembly => assembly;

            public System.Reflection.Assembly GetAssembly()
            {
                if(!File.Exists(assemblyFile))
                {
                    var message = GhostAssemblyException.CreateGhostNotFoundMessage(assemblyFile);
                    throw new GhostAssemblyException(message);
                }

                var lastWriteTime = File.GetLastWriteTime(assemblyFile);
                if (lastWriteTime != this.lastWriteTime)
                {
                    assembly = loadAssemblyFromBytes(assemblyFile);
                    this.lastWriteTime = lastWriteTime;
                }

                return assembly;
            }

            static System.Reflection.Assembly loadAssemblyFromBytes(string assemblyFile)
            {
                var asmBytes = File.ReadAllBytes(assemblyFile);
                var pdbFile = Path.ChangeExtension(assemblyFile, "pdb");
                if (File.Exists(pdbFile))
                {
                    var pdbBytes = File.ReadAllBytes(pdbFile);
                    return System.Reflection.Assembly.Load(asmBytes, pdbBytes);
                }

                return System.Reflection.Assembly.Load(asmBytes);
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

        public const string NoDefaultGhostMessage = "No default ghost assembly name name was specified.";
    }
}
