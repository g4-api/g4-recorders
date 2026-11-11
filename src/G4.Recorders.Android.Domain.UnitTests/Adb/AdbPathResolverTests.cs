using G4.Recorders.Android.Domain.Adb;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.IO;

namespace G4.Recorders.Android.Domain.UnitTests.Adb
{
    [TestClass]
    [TestCategory(nameof(AdbPathResolver))]
    [TestCategory("UnitTest")]
    public sealed class AdbPathResolverTests
    {
        // Builds resolver options from a fake environment and a set of existing files.
        private static AdbPathOptions NewOptions(Dictionary<string, string> environment, HashSet<string> files)
        {
            return new AdbPathOptions(
                getEnvironmentVariable: name => environment.GetValueOrDefault(name),
                testFileExists: files.Contains,
                baseDirectory: Path.Combine(@"C:\", "g4-sandbox", "bot-utilities", "android-recorder-x64"),
                isWindows: true);
        }

        [TestMethod(DisplayName = "Verify that ANDROID_HOME wins over the sandbox and the PATH")]
        public void ResolveAndroidHomeFirstTest()
        {
            // Arrange: adb exists in the SDK, the sandbox and the PATH.
            var sdkAdb = Path.Combine(@"D:\", "sdk", "platform-tools", "adb.exe");
            var sandboxAdb = Path.Combine(@"S:\", "sandbox", "android", "platform-tools", "adb.exe");
            var pathAdb = Path.Combine(@"P:\", "tools", "adb.exe");
            var environment = new Dictionary<string, string>
            {
                ["ANDROID_HOME"] = Path.Combine(@"D:\", "sdk"),
                ["G4_SANDBOX"] = Path.Combine(@"S:\", "sandbox"),
                ["PATH"] = Path.Combine(@"P:\", "tools")
            };

            // Act.
            var adbPath = AdbPathResolver.Resolve(NewOptions(environment, [sdkAdb, sandboxAdb, pathAdb]));

            // Assert.
            Assert.AreEqual(sdkAdb, adbPath);
        }

        [TestMethod(DisplayName = "Verify that the sandbox is derived from the recorder folder when G4_SANDBOX is not set")]
        public void ResolveDerivedSandboxTest()
        {
            // Arrange: no SDK variables, adb only in the sandbox two folders above the recorder.
            var sandboxAdb = Path.GetFullPath(Path.Combine(@"C:\", "g4-sandbox", "android", "platform-tools", "adb.exe"));

            // Act.
            var adbPath = AdbPathResolver.Resolve(NewOptions([], [sandboxAdb]));

            // Assert.
            Assert.AreEqual(sandboxAdb, adbPath);
        }

        [TestMethod(DisplayName = "Verify that a PATH entry is used when neither the SDK nor the sandbox has adb")]
        public void ResolvePathEntryTest()
        {
            // Arrange: two PATH entries; only the second holds adb.
            var pathAdb = Path.Combine(@"Q:\", "platform-tools", "adb.exe");
            var environment = new Dictionary<string, string>
            {
                ["PATH"] = Path.Combine(@"P:\", "tools") + Path.PathSeparator + Path.Combine(@"Q:\", "platform-tools")
            };

            // Act.
            var adbPath = AdbPathResolver.Resolve(NewOptions(environment, [pathAdb]));

            // Assert.
            Assert.AreEqual(pathAdb, adbPath);
        }

        [TestMethod(DisplayName = "Verify that a missing adb fails with every probed location")]
        public void ResolveNotFoundTest()
        {
            // Arrange: an SDK variable that points nowhere.
            var environment = new Dictionary<string, string> { ["ANDROID_SDK_ROOT"] = Path.Combine(@"D:\", "missing") };

            // Act.
            var exception = Assert.Throws<FileNotFoundException>(() => AdbPathResolver.Resolve(NewOptions(environment, [])));

            // Assert: the message names the SDK and the sandbox candidates.
            Assert.Contains(Path.Combine(@"D:\", "missing", "platform-tools", "adb.exe"), exception.Message);
            Assert.Contains(Path.Combine("android", "platform-tools", "adb.exe"), exception.Message);
        }
    }
}
