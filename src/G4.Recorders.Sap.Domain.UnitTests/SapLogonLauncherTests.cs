using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Diagnostics;
using System.IO;

using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapLogonLauncher))]
    [TestCategory("UnitTest")]
    public sealed class SapLogonLauncherTests
    {
        // Lists the call that opens the named connection synchronously.
        private static readonly string[] ExpectedOpenConnection = ["OpenConnection(S4H - Development,True)"];

        [TestMethod(DisplayName = "Verify that a running SAP Logon is attached to and never started again")]
        public void StartAttachesToRunningTest()
        {
            // Arrange: SAP GUI is running, and a process start would be recorded.
            var host = new FakeScriptingHost { Application = new FakeSapTree().Application };
            var isStarted = false;
            var launcher = new SapLogonLauncher(host, _ => { isStarted = true; return null; });

            // Act: start with a path.
            var (processId, isLaunched) = launcher.Start(new SapLogonParametersModel { Path = GetExistingPath() });

            // Assert: attached without starting anything.
            Assert.IsFalse(isStarted);
            Assert.IsFalse(isLaunched);
            Assert.AreEqual(0, processId);
        }

        [TestMethod(DisplayName = "Verify that without SAP GUI and without a path nothing is started")]
        public void StartWithoutPathTest()
        {
            // Arrange: SAP GUI is not running.
            var launcher = new SapLogonLauncher(new FakeScriptingHost(), _ => null);

            // Act: start without a path.
            var (processId, isLaunched) = launcher.Start(new SapLogonParametersModel { Path = string.Empty });

            // Assert: nothing launched.
            Assert.IsFalse(isLaunched);
            Assert.AreEqual(0, processId);
        }

        [TestMethod(DisplayName = "Verify that a path that does not exist is reported with the path")]
        public void StartMissingPathTest()
        {
            // Arrange: SAP GUI is not running.
            var launcher = new SapLogonLauncher(new FakeScriptingHost(), _ => null);

            // Act and assert: the missing executable is named.
            var exception = Assert.ThrowsExactly<FileNotFoundException>(() => launcher.Start(new SapLogonParametersModel { Path = @"C:\missing\saplogon.exe" }));
            Assert.Contains("saplogon.exe", exception.Message);
        }

        [TestMethod(DisplayName = "Verify that a started SAP Logon is waited for and its process id returned")]
        public void StartLaunchesTest()
        {
            // Arrange: starting the process makes SAP GUI available; the current process stands in for SAP Logon.
            var host = new FakeScriptingHost();
            var tree = new FakeSapTree();
            ProcessStartInfo startInfo = null;
            var launcher = new SapLogonLauncher(host, info =>
            {
                startInfo = info;
                host.Application = tree.Application;
                return Process.GetCurrentProcess();
            });

            // Act: start with a path and an argument.
            var (processId, isLaunched) = launcher.Start(new SapLogonParametersModel { Arguments = ["--quiet"], Path = GetExistingPath() });

            // Assert: launched with the argument and without the 64-bit runtime location.
            Assert.IsTrue(isLaunched);
            Assert.AreEqual(Environment.ProcessId, processId);
            Assert.AreEqual("--quiet", startInfo.ArgumentList[0]);
            Assert.IsFalse(startInfo.Environment.ContainsKey("DOTNET_ROOT"));
        }

        [TestMethod(DisplayName = "Verify that a named connection is opened only when no open connection has that description")]
        #region *** Data Set ***
        [DataRow("", true)]
        [DataRow("S4H - Development", false)]
        #endregion
        public void StartOpensConnectionTest(string openDescription, bool isOpenExpected)
        {
            // Arrange: SAP GUI is running with one connection whose description comes from the row.
            var tree = new FakeSapTree();
            ((FakeSapGuiObject)SapGuiNavigator.GetChildren(tree.Application)[0]).With("Description", openDescription);
            tree.Application.Methods["OpenConnection"] = _ => null;
            var launcher = new SapLogonLauncher(new FakeScriptingHost { Application = tree.Application }, _ => null);

            // Act: start with the connection name.
            launcher.Start(new SapLogonParametersModel { Connection = "S4H - Development" });

            // Assert: opened synchronously only when not open yet.
            if (isOpenExpected)
            {
                Assert.AreSequenceEqual(ExpectedOpenConnection, tree.Application.Invocations);
            }
            else
            {
                Assert.IsEmpty(tree.Application.Invocations);
            }
        }

        [TestMethod(DisplayName = "Verify that stopping a process the launcher did not start touches nothing")]
        public void StopUnknownProcessTest()
        {
            // Arrange: a launcher that started nothing.
            var launcher = new SapLogonLauncher(new FakeScriptingHost(), _ => null);

            // Act: stop the test process itself.
            var isStopped = launcher.Stop(Environment.ProcessId);

            // Assert: refused.
            Assert.IsFalse(isStopped);
        }

        // Returns a file that exists, standing in for the SAP Logon executable.
        private static string GetExistingPath()
        {
            return typeof(SapLogonLauncherTests).Assembly.Location;
        }
    }
}
