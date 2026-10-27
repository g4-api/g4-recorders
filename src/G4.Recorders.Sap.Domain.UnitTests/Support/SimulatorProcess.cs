using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.UnitTests.Support
{
    /// <summary>
    /// Starts the SAP GUI simulator from the sibling sap-driver-server repository for end-to-end tests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The simulator registers <c>SAPGUI</c> in the running object table like SAP Logon, so the recorder attaches to it
    /// through its production path. Tests run only when <c>G4_SAP_SIMULATOR</c> is <c>true</c>; otherwise, and when the
    /// simulator is not built or another SAP GUI is running, they end as inconclusive. Settings come from
    /// <see cref="SimulatorTestSettings"/>: <c>.runsettings</c> parameters, then the environment, then appsettings.json.
    /// </para>
    /// <para>
    /// The executable is <c>G4_SAP_SIMULATOR_PATH</c> when set, otherwise the Debug or Release build next to this
    /// repository: <c>../sap-driver-server/src/Sap.DriverServer.Simulator/bin/...</c>.
    /// </para>
    /// </remarks>
    internal sealed class SimulatorProcess : IDisposable
    {
        #region *** Constants    ***
        /// <summary>
        /// Names the environment variable that opts in to the simulator tests.
        /// </summary>
        internal const string EnabledVariable = "G4_SAP_SIMULATOR";

        /// <summary>
        /// Names the environment variable that also opts in to tests that move the mouse and click.
        /// </summary>
        internal const string InputVariable = "G4_SAP_SIMULATOR_INPUT";

        // Names the environment variable that overrides the simulator executable.
        private const string PathVariable = "G4_SAP_SIMULATOR_PATH";

        // Names the 64-bit runtime location the test host passes down; the 32-bit simulator finds its own runtime.
        private const string DotnetRootVariable = "DOTNET_ROOT";

        // Bounds the wait for the simulator to register SAPGUI.
        private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(20);
        #endregion

        #region *** Fields       ***
        // Holds the running simulator process.
        private readonly Process _process;
        #endregion

        #region *** Constructors ***
        // Wraps a started simulator; created only by Start.
        private SimulatorProcess(Process process)
        {
            _process = process;
        }
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public void Dispose()
        {
            // End the simulator; its windows close and its SAPGUI entry goes with the process.
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(milliseconds: 5000);
            }

            _process.Dispose();
        }

        /// <summary>
        /// Ends the test as inconclusive unless an environment variable is <c>true</c>.
        /// </summary>
        /// <param name="variable">The environment variable name.</param>
        internal static void AssertEnabled(string variable)
        {
            // Read the switch; anything but true skips the test.
            if (!SimulatorTestSettings.TestEnabled(variable))
            {
                var message = $"Set {variable}=true in .runsettings, the environment, or " +
                    $"{SimulatorTestSettings.GetSettingsPath()} to run this SAP GUI simulator test.";
                Assert.Inconclusive(message);
            }
        }

        /// <summary>
        /// Starts the simulator and waits until it registered <c>SAPGUI</c>.
        /// </summary>
        /// <param name="arguments">Extra simulator arguments, such as <c>--recording-disabled</c>; <c>--quiet</c> is always added.</param>
        /// <returns>The running simulator; the caller disposes it.</returns>
        internal static SimulatorProcess Start(params string[] arguments)
        {
            // Skip unless opted in, built, and alone in the running object table.
            AssertEnabled(EnabledVariable);

            var path = GetExecutablePath();
            if (path is null)
            {
                Assert.Inconclusive($"The SAP GUI simulator is not built. Build sap-driver-server or set {PathVariable}.");
            }

            if (TestRotEntry())
            {
                Assert.Inconclusive("SAP GUI or another simulator is running; the simulator tests use the SAPGUI entry exclusively.");
            }

            // Start the simulator without message boxes, and without the 64-bit runtime location.
            var startInfo = new ProcessStartInfo(path) { UseShellExecute = false };
            startInfo.ArgumentList.Add("--quiet");
            startInfo.Environment.Remove(DotnetRootVariable);

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var simulator = new SimulatorProcess(Process.Start(startInfo));
            simulator.WaitForRegistration();

            return simulator;
        }

        /// <summary>
        /// Ends the test as inconclusive unless the simulator is enabled, built, and the SAPGUI entry is free.
        /// </summary>
        /// <returns>The simulator executable path.</returns>
        internal static string AssertAvailable()
        {
            // Apply the same skips as Start, without starting anything.
            AssertEnabled(EnabledVariable);

            var path = GetExecutablePath();
            if (path is null)
            {
                Assert.Inconclusive($"The SAP GUI simulator is not built. Build sap-driver-server or set {PathVariable}.");
            }

            if (TestRotEntry())
            {
                Assert.Inconclusive("SAP GUI or another simulator is running; the simulator tests use the SAPGUI entry exclusively.");
            }

            return path;
        }

        // Resolves the simulator executable: the override, or the first build found next to this repository.
        private static string GetExecutablePath()
        {
            // Prefer the explicit override.
            var overridePath = SimulatorTestSettings.Get(PathVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return File.Exists(overridePath) ? overridePath : null;
            }

            // Walk up to the directory that holds both repositories, then try the usual build outputs.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "sap-driver-server")))
            {
                directory = directory.Parent;
            }

            if (directory is null)
            {
                return null;
            }

            var binDirectory = Path.Combine(directory.FullName, "sap-driver-server", "src", "Sap.DriverServer.Simulator", "bin");
            string[] candidates =
            [
                Path.Combine(binDirectory, "Debug", "net10.0-windows", "Sap.DriverServer.Simulator.exe"),
                Path.Combine(binDirectory, "Release", "net10.0-windows", "win-x86", "Sap.DriverServer.Simulator.exe"),
                Path.Combine(binDirectory, "Release", "net10.0-windows", "Sap.DriverServer.Simulator.exe")
            ];

            return Array.Find(candidates, File.Exists);
        }

        // Tests whether a SAPGUI entry is registered, releasing the reference the lookup returns.
        private static bool TestRotEntry()
        {
            // Look the entry up; release it at once, because only its presence matters.
            var entry = RunningObjectTableReader.GetObject("SAPGUI");
            if (entry is null)
            {
                return false;
            }

            Marshal.ReleaseComObject(entry);
            return true;
        }

        // Waits until the simulator registered SAPGUI, failing when it exits or the timeout passes.
        private void WaitForRegistration()
        {
            // Poll the running object table; the simulator registers once its sessions are open.
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < RegistrationTimeout)
            {
                if (_process.HasExited)
                {
                    Assert.Fail($"The SAP GUI simulator exited with code {_process.ExitCode} before registering SAPGUI.");
                }

                if (TestRotEntry())
                {
                    return;
                }

                Thread.Sleep(millisecondsTimeout: 200);
            }

            Assert.Fail("The SAP GUI simulator did not register SAPGUI in time.");
        }
        #endregion
    }
}
