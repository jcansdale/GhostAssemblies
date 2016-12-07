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
        public void ResolveAssembly_Location_IsEmptyString()
        {
            var testAssembly = GetType().Assembly;
            var testName = testAssembly.GetName().Name;
            var ghostAssemblyPaths = testAssembly.Location;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths))
            {
                var expectLocation = testAssembly.Location;

                var ghostAssembly = ghostAssemblyLoader.ResolveAssembly(testName);

                Assert.That(ghostAssembly.Location, Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void ResolveAssembly_AppDomainGetData_AssemblyWithLocation()
        {
            var testAssembly = GetType().Assembly;
            var testName = testAssembly.GetName().Name;
            var ghostAssemblyPaths = testAssembly.Location;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths))
            {
                var expectLocation = testAssembly.Location;

                var ghostAssembly = ghostAssemblyLoader.ResolveAssembly(testName);

                var asm = (Assembly)AppDomain.CurrentDomain.GetData(ghostAssembly.FullName);
                Assert.That(asm, Is.Not.Null);
                Assert.That(asm.Location, Is.EqualTo(expectLocation));
            }
        }

        [Test]
        public void Dispose_AppDomainGetData_IsNull()
        {
            var testAssembly = GetType().Assembly;
            var testName = testAssembly.GetName().Name;
            var ghostAssemblyPaths = testAssembly.Location;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths))
            {
                var expectLocation = testAssembly.Location;

                ghostAssemblyLoader.ResolveAssembly(testName);
            }

            var asm = (Assembly)AppDomain.CurrentDomain.GetData(testAssembly.FullName);
            Assert.That(asm, Is.Null);
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
            var ghostAssemblyPaths = testAssembly.Location;
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
            var ghostAssemblyPaths = testAssembly.Location;
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
            var ghostAssemblyPaths = testAssembly.Location;
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
            var assemblyFile = testAssembly.Location;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(assemblyFile, assemblyName))
            {
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(File.GetLastWriteTime(assemblyFile));
                var asm1 = ghostAssemblyLoader.ResolveAssembly();
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(DateTime.Now);
                var asm2 = ghostAssemblyLoader.ResolveAssembly();

                Assert.That(asm2, Is.Not.EqualTo(asm1));
            }
        }

        [TestCase(typeof(GhostAssemblyLoaderTests), typeof(GhostAssemblyLoader), true, false, false, Description = "Ghost & Referenced changes")]
        [TestCase(typeof(GhostAssemblyLoaderTests), typeof(GhostAssemblyLoader), true, false, false, Description = "Ghost changes")]
        [TestCase(typeof(GhostAssemblyLoaderTests), typeof(GhostAssemblyLoader), false, true, true, Description = "Referenced changes")]
        [TestCase(typeof(GhostAssemblyLoaderTests), typeof(GhostAssemblyLoader), false, false, false, Description = "No changes")]
        [TestCase(typeof(GhostAssemblyLoader), typeof(object), true, true, true, Description = "Strong-Named Ghost")]
        public void ResolveAssembly_GhostOrReferencedGhostChanges_ExpectVersionChange(
            Type ghostAssemblyType, Type referencedAssemblyType, bool updateGhost, bool updateReferenced,
            bool expectVersionChange)
        {
            var ghostAssembly = ghostAssemblyType.Assembly;
            var assemblyName = ghostAssembly.GetName().Name;
            var assemblyFile = ghostAssembly.Location;
            var referencedAssembly = referencedAssemblyType.Assembly;
            var referencedAssemblyName = referencedAssembly.GetName().Name;
            var referencedLocation = referencedAssembly.Location;
            var expectMessage = GhostAssemblyException.CreateChangeAssemblyVersionMessage(referencedAssemblyName);
            var ghostAssemblyPaths = assemblyFile + ";" + referencedLocation;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyPaths, assemblyName))
            {
                staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(File.GetLastWriteTime(assemblyFile));
                staticMock.For(() => File.GetLastWriteTime(referencedLocation)).Returns(File.GetLastWriteTime(referencedLocation));
                var asm1 = ghostAssemblyLoader.ResolveAssembly();
                var ref1 = findReferencedAssembly(asm1, referencedAssemblyName);
                ghostAssemblyLoader.ResolveAssembly(referencedAssemblyName);
                if(updateGhost) staticMock.For(() => File.GetLastWriteTime(assemblyFile)).Returns(DateTime.Now);
                if(updateReferenced) staticMock.For(() => File.GetLastWriteTime(referencedLocation)).Returns(DateTime.Now);

                var asm2 = ghostAssemblyLoader.ResolveAssembly();

                var ref2 = findReferencedAssembly(asm2, referencedAssemblyName);
                if(expectVersionChange)
                {
                    Assert.That(ref2.Version, Is.Not.EqualTo(ref1.Version));
                }
                else
                {
                    Assert.That(ref2.Version, Is.EqualTo(ref1.Version));
                }
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
        public void ResolveAssembly(string ghostAssemblyLocation, string assemblyName, bool isGhost)
        {
            var ghostAssembly = typeof(Uri).Assembly;
            var ghostAssemblyBytes = File.ReadAllBytes(ghostAssembly.Location);
            staticMock.For(() => File.Exists(ghostAssemblyLocation)).Returns(true);
            staticMock.For(() => File.ReadAllBytes(ghostAssemblyLocation)).Returns(ghostAssemblyBytes);

            var loadFromAssembly = typeof(object).Assembly;
            var appPath = getDirectory(Assembly.GetExecutingAssembly());
            var appMockPath = Path.Combine(appPath, assemblyName + ".dll");
            staticMock.For(() => File.Exists(appMockPath)).Returns(true);
            staticMock.For(() => Assembly.LoadFrom(appMockPath)).Returns(loadFromAssembly);

            var expectAssembly = isGhost ? ghostAssembly : loadFromAssembly;
            using (var ghostAssemblyLoader = new GhostAssemblyLoader(ghostAssemblyLocation))
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
            var execAsmLocation = execAsm.Location;
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
        public void FindGhostAssemblyLocation(string assemblyName, string ghostAssemblyPaths, string filesExist, string expect)
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
                var assemblyFile = ghostAssemblyLoader.FindGhostAssemblyLocation(assemblyName);

                Assert.That(assemblyFile, Is.EqualTo(expect));
            }
        }

        static string getDirectory(Assembly assembly)
        {
            var localPath = new Uri(assembly.EscapedCodeBase).LocalPath;
            var directory = Path.GetDirectoryName(localPath);
            return directory;
        }
    }
}
