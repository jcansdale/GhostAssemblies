namespace GhostAssemblies.Tests
{
    using System;
    using System.IO;
    using System.Reflection;
    using StaticMocks;
    using NSubstitute;
    using NUnit.Framework;

    public class GhostAssemblyLoaderTests
    {
        StaticMock staticMock;

        [SetUp]
        public void SetUp()
        {
            staticMock = new StaticMock(typeof(GhostAssemblyLoader));
        }

        [TearDown]
        public void TearDown()
        {
            staticMock.Dispose();
        }

        [Test]
        public void ResolveAssembly_NoDefaultGhostAssemblyName_ThrowsException()
        {
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(null))
            {
                var expectMessage = GhostAssemblyException.NoDefaultGhostMessage;

                var exception = Assert.Throws<GhostAssemblyException>(() => ghostAssemblyLoader.ResolveAssembly());

                Assert.That(exception.Message, Is.EqualTo(expectMessage));
            }
        }

        [Test]
        public void ResolveAssembly_Default_IsSpecifiedDefaultGhostAssemblyName()
        {
            var testAssembly = GetType().Assembly;
            var defaultName = testAssembly.GetName().Name;
            var ghostAssemblyPaths = getFile(testAssembly);
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths, defaultName))
            {
                var expectAssembly = ghostAssemblyLoader.ResolveAssembly(defaultName);

                var defaultAssembly = ghostAssemblyLoader.ResolveAssembly();

                Assert.That(defaultAssembly, Is.EqualTo(expectAssembly));
            }
        }

        [Test]
        public void ResolveAssembly_Unknown_ReturnsNull()
        {
            var unknownName = "UnknownAssemblyName";
            var testAssembly = GetType().Assembly;
            var defaultName = testAssembly.GetName().Name;
            var ghostAssemblyPaths = getFile(testAssembly);
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths, defaultName))
            {
                var unknownAssembly = ghostAssemblyLoader.ResolveAssembly(unknownName);

                Assert.That(unknownAssembly, Is.Null);
            }
        }

        [Test]
        public void ResolveAssembly_DoesNotExist_ThrowsException()
        {
            var assemblyName = "DoesNotExist";
            var assemblyFile = string.Format(@"\{0}.dll", assemblyName);
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(assemblyFile, assemblyName))
            {
                var expectMessage = GhostAssemblyException.CreateGhostNotFoundMessage(assemblyFile);

                var exception = Assert.Throws<GhostAssemblyException>(() => ghostAssemblyLoader.ResolveAssembly());

                Assert.That(exception.Message, Is.EqualTo(expectMessage));
            }
        }

        [Test]
        public void ResolveAssembly_NoChange_IsCached()
        {
            var testAssembly = GetType().Assembly;
            var defaultName = testAssembly.GetName().Name;
            var ghostAssemblyPaths = getFile(testAssembly);
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths, defaultName))
            {
                var asm1 = ghostAssemblyLoader.ResolveAssembly();
                var asm2 = ghostAssemblyLoader.ResolveAssembly();

                Assert.That(asm2, Is.EqualTo(asm1));
            }
        }

        [Test]
        public void ResolveAssembly_NewTimeStamp_AssemblyChanged()
        {
            var testAssembly = GetType().Assembly;
            var assemblyName = testAssembly.GetName().Name;
            var assemblyFile = getFile(testAssembly);
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(assemblyFile, assemblyName))
            {
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(File.GetLastWriteTime(assemblyFile));
                var asm1 = ghostAssemblyLoader.ResolveAssembly();
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(DateTime.Now);
                var asm2 = ghostAssemblyLoader.ResolveAssembly();

                Assert.That(asm2, Is.Not.EqualTo(asm1));
            }
        }

        [TestCase(typeof(GhostAssemblyLoaderTests), typeof(GhostAssemblyLoader), Description = "Weak-Named Ghost")]
        [TestCase(typeof(GhostAssemblyLoader), typeof(object), Description = "Strong-Named Ghost")]
        public void ResolveAssembly_ReferencedAssemblyChanged_ChangeReferencedAssemblyVersion(
            Type ghostAssemblyType, Type referencedAssemblyType)
        {
            var ghostAssembly = ghostAssemblyType.Assembly;
            var assemblyName = ghostAssembly.GetName().Name;
            var assemblyFile = getFile(ghostAssembly);
            var referencedAssembly = referencedAssemblyType.Assembly;
            var referencedAssemblyName = referencedAssembly.GetName().Name;
            var referencedAssemblyFile = getFile(referencedAssembly);
            var expectMessage = GhostAssemblyException.CreateChangeAssemblyVersionMessage(referencedAssemblyName);
            var ghostAssemblyPaths = assemblyFile + ";" + referencedAssemblyFile;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths, assemblyName))
            {
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(File.GetLastWriteTime(assemblyFile));
                staticMock.For(() => File.GetLastWriteTime(referencedAssemblyFile)).Returns(File.GetLastWriteTime(referencedAssemblyFile));
                var asm1 = ghostAssemblyLoader.ResolveAssembly();
                var ref1 = findReferencedAssembly(asm1, referencedAssemblyName);
                ghostAssemblyLoader.ResolveAssembly(referencedAssemblyName);
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(DateTime.Now);
                staticMock.For(() => File.GetLastWriteTime(referencedAssemblyFile)).Returns(DateTime.Now);

                var asm2 = ghostAssemblyLoader.ResolveAssembly();

                var ref2 = findReferencedAssembly(asm2, referencedAssemblyName);
                Assert.That(ref2.Version, Is.Not.EqualTo(ref1.Version));
            }
        }

        [Test]
        public void ResolveAssembly_ReferencedAssemblyNotChanged_DontChangeReferencedAssemblyVersion()
        {
            var testAssembly = GetType().Assembly;
            var assemblyName = testAssembly.GetName().Name;
            var assemblyFile = getFile(testAssembly);
            var referencedAssembly = typeof(GhostAssemblyLoader).Assembly;
            var referencedAssemblyName = referencedAssembly.GetName().Name;
            var referencedAssemblyFile = getFile(referencedAssembly);
            var ghostAssemblyPaths = assemblyFile + ";" + referencedAssemblyFile;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths, assemblyName))
            {
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(File.GetLastWriteTime(assemblyFile));
                staticMock.For(() => File.GetLastWriteTime(referencedAssemblyFile)).Returns(File.GetLastWriteTime(referencedAssemblyFile));
                var asm1 = ghostAssemblyLoader.ResolveAssembly();
                var ref1 = findReferencedAssembly(asm1, referencedAssemblyName);
                ghostAssemblyLoader.ResolveAssembly(referencedAssemblyName);
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(DateTime.Now);

                var asm2 = ghostAssemblyLoader.ResolveAssembly();

                var ref2 = findReferencedAssembly(asm2, referencedAssemblyName);
                Assert.That(ref2.Version, Is.EqualTo(ref1.Version));
            }
        }

        static AssemblyName findReferencedAssembly(Assembly assembly, string name)
        {
            foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
            {
                if (referencedAssembly.Name == name)
                {
                    return referencedAssembly;
                }
            }

            return null;
        }

        [TestCase(@"\MockGhost.dll", "MockGhost", true, Description = "Load into 'Ghost' context")]
        [TestCase(null, "MockLoadFrom", false, Description = "Load into 'LoadFrom' context")]
        public void ResolveAssembly(string ghostAssemblyFile, string assemblyName, bool isGhost)
        {
            var ghostAssembly = typeof(Uri).Assembly;
            var ghostAssemblyBytes = File.ReadAllBytes(ghostAssembly.Location);
            staticMock.For(() => File.Exists(ghostAssemblyFile)).Returns(true);
            staticMock.For(() => File.ReadAllBytes(ghostAssemblyFile)).Returns(ghostAssemblyBytes);

            var loadFromAssembly = typeof(object).Assembly;
            var appPath = getDirectory(Assembly.GetExecutingAssembly());
            var appMockPath = Path.Combine(appPath, assemblyName + ".dll");
            staticMock.For(() => File.Exists(appMockPath)).Returns(true);
            staticMock.For(() => Assembly.LoadFrom(appMockPath)).Returns(loadFromAssembly);

            var expectAssembly = isGhost ? ghostAssembly : loadFromAssembly;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyFile))
            {
                var resolvedAssembly = ghostAssemblyLoader.ResolveAssembly(assemblyName);

                Assert.That(resolvedAssembly.FullName, Is.EqualTo(expectAssembly.FullName));
            }
        }

        [TestCase(@"\MockGhost.dll", "MockGhost", true, Description = "Load into 'Ghost' context")]
        [TestCase(null, "MockLoadFrom", false, Description = "Load into 'LoadFrom' context")]
        public void AssemblyResolve_CalledFromGhostAssembly(string ghostAssemblyPath, string assemblyName, bool isGhost)
        {
            var ghostAssembly = typeof(Uri).Assembly;
            var ghostAssemblyBytes = File.ReadAllBytes(ghostAssembly.Location);
            staticMock.For(() => File.Exists(ghostAssemblyPath)).Returns(true);
            staticMock.For(() => File.ReadAllBytes(ghostAssemblyPath)).Returns(ghostAssemblyBytes);

            var loadFromAssembly = typeof(object).Assembly;
            var appPath = getDirectory(Assembly.GetExecutingAssembly());
            var appMockPath = Path.Combine(appPath, assemblyName + ".dll");
            staticMock.For(() => File.Exists(appMockPath)).Returns(true);
            staticMock.For(() => Assembly.LoadFrom(appMockPath)).Returns(loadFromAssembly);

            var expectAssembly = isGhost ? ghostAssembly : loadFromAssembly;

            var resolvedAssembly = invokeAssemblyLoadFromGhostAssembly(ghostAssemblyPath, assemblyName);
            Assert.That(resolvedAssembly.FullName, Is.EqualTo(expectAssembly.FullName));
        }

        Assembly invokeAssemblyLoadFromGhostAssembly(string ghostAssemblyPath, string assemblyName)
        {
            var execAsm = Assembly.GetExecutingAssembly();
            var execAsmName = execAsm.GetName().Name;
            var execAsmLocation = getFile(execAsm);
            var execAssemblyBytes = File.ReadAllBytes(execAsmLocation);
            staticMock.For(() => File.Exists(execAsmLocation)).Returns(true);
            staticMock.For(() => File.ReadAllBytes(execAsmLocation)).Returns(execAssemblyBytes);

            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPath + ";" + execAsmLocation))
            {
                var asm = ghostAssemblyLoader.ResolveAssembly(execAsmName);
                var ghostType = asm.GetType(GetType().FullName);
                var method = ghostType.GetMethod(nameof(AssemblyLoad));

                return (Assembly)method.Invoke(null, new object[] { assemblyName });
            }
        }

        public static Assembly AssemblyLoad(string assemblyFullName)
        {
            return Assembly.Load(assemblyFullName);
        }

        [TestCase("Test", @"\BasePath\Test.dll", @"\BasePath\Test.dll", @"\BasePath\Test.dll")]
        [TestCase("Test", @"\BasePath\Test.exe", @"\BasePath\Test.exe", @"\BasePath\Test.exe")]
        [TestCase("Test", null, @"$(AppPath)\Test.exe", null)]
        [TestCase("Test", @"\First\Test.exe", @"\First\Test.exe;Second\Test.exe", @"\First\Test.exe")]
        public void FindGhostAssemblyFile(string assemblyName, string ghostAssemblyPaths, string filesExist, string expect)
        {
            var appPath = getDirectory(typeof(GhostAssemblyLoader).Assembly);
            ghostAssemblyPaths = ghostAssemblyPaths?.Replace("$(AppPath)", appPath);
            filesExist = filesExist.Replace("$(AppPath)", appPath);
            expect = expect?.Replace("$(AppPath)", appPath);
            foreach (var path in filesExist.Split(';'))
            {
                staticMock.For(() => File.Exists(path)).Returns(true);
            }

            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths, assemblyName))
            {
                var assemblyFile = ghostAssemblyLoader.FindGhostAssemblyFile(assemblyName);

                Assert.That(assemblyFile, Is.EqualTo(expect));
            }
        }

        static string getFile(Assembly assembly)
        {
            return new Uri(assembly.EscapedCodeBase).LocalPath;
        }

        static string getDirectory(Assembly assembly)
        {
            var localPath = new Uri(assembly.EscapedCodeBase).LocalPath;
            var directory = Path.GetDirectoryName(localPath);
            return directory;
        }
    }
}
