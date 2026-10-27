using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Default <see cref="ISapLogonLauncher"/>: starts SAP Logon through <see cref="Process"/> and waits for SAP GUI
    /// Scripting through the scripting host.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A running SAP Logon is never restarted; the launcher attaches to it and only opens the named connection when no
    /// connection with that description is open. Processes the launcher started are remembered, so <see cref="Stop"/>
    /// can end them and nothing else, as the Chromium recorder does with its browsers.
    /// </para>
    /// <para>
    /// Register it as a singleton, because it owns the list of started processes.
    /// </para>
    /// </remarks>
    public sealed class SapLogonLauncher : ISapLogonLauncher
    {
        #region *** Constants    ***
        // Bounds the wait for a started SAP Logon to make SAP GUI Scripting available.
        private static readonly TimeSpan AvailabilityTimeout = TimeSpan.FromSeconds(60);

        // Spaces availability checks while SAP Logon starts.
        private static readonly TimeSpan AvailabilityPollInterval = TimeSpan.FromMilliseconds(500);

        // Bounds the wait for a stopped SAP Logon to exit after it was asked to close.
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

        // Names the 64-bit runtime location a .NET host passes down; a 32-bit child, such as the simulator, needs its own.
        private const string DotnetRootVariable = "DOTNET_ROOT";
        #endregion

        #region *** Fields       ***
        // Holds the processes this launcher started, by identifier.
        private readonly ConcurrentDictionary<int, Process> _launched = new();

        // Runs the availability checks and the connection opening on the scripting thread.
        private readonly ISapScriptingHost _scriptingHost;

        // Starts a process; replaceable so tests never start one.
        private readonly Func<ProcessStartInfo, Process> _startProcess;
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="SapLogonLauncher"/> class.
        /// </summary>
        /// <param name="scriptingHost">The host that owns the SAP GUI Scripting attachment.</param>
        public SapLogonLauncher(ISapScriptingHost scriptingHost)
            : this(scriptingHost, startProcess: Process.Start)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SapLogonLauncher"/> class with an explicit process starter.
        /// </summary>
        /// <param name="scriptingHost">The host that owns the SAP GUI Scripting attachment.</param>
        /// <param name="startProcess">Starts a process from start information.</param>
        internal SapLogonLauncher(ISapScriptingHost scriptingHost, Func<ProcessStartInfo, Process> startProcess)
        {
            ArgumentNullException.ThrowIfNull(argument: scriptingHost);
            ArgumentNullException.ThrowIfNull(argument: startProcess);

            _scriptingHost = scriptingHost;
            _startProcess = startProcess;
        }
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public (int ProcessId, bool IsLaunched) Start(SapLogonParametersModel logon)
        {
            // Attach to a running SAP Logon; start one only when none answers and a path is set.
            var path = logon?.Path ?? string.Empty;
            var processId = 0;
            var isLaunched = false;

            if (!TestAvailable() && path.Length > 0)
            {
                processId = StartProcess(path, logon.Arguments ?? []);
                isLaunched = true;
                WaitForAvailability(processId);
            }

            // Open the named connection unless one with that description is already open.
            if (!string.IsNullOrEmpty(logon?.Connection) && TestAvailable())
            {
                OpenConnection(logon.Connection);
            }

            return (processId, isLaunched);
        }

        /// <inheritdoc />
        public bool Stop(int processId)
        {
            // Touch only processes this launcher started.
            if (!_launched.TryRemove(processId, out var process))
            {
                return false;
            }

            using (process)
            {
                // Ask SAP Logon to close, then end it when it does not exit in time.
                if (process.HasExited)
                {
                    return false;
                }

                process.CloseMainWindow();
                if (!process.WaitForExit(CloseTimeout))
                {
                    process.Kill(entireProcessTree: true);
                }

                return true;
            }
        }

        // Opens a SAP Logon connection by description unless one with that description is open.
        private void OpenConnection(string connection)
        {
            _scriptingHost.Invoke(application =>
            {
                // Compare against the open connections' descriptions, ignoring case.
                var isOpen = SapGuiNavigator
                    .GetChildren(application)
                    .Select(i => SapGuiNavigator.GetString(i, propertyName: "Description"))
                    .Any(i => string.Equals(i, connection, StringComparison.OrdinalIgnoreCase));

                // Open synchronously, so the session exists before the recorder attaches.
                if (!isOpen)
                {
                    application.InvokeMethod("OpenConnection", connection, true);
                }

                return true;
            });
        }

        // Starts the process, without the 64-bit runtime location, and remembers it.
        private int StartProcess(string path, string[] arguments)
        {
            // Require an existing executable, so a typo reports the path rather than a generic start failure.
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"SAP Logon was not found on the recorder host: '{path}'.", path);
            }

            var startInfo = new ProcessStartInfo(path) { UseShellExecute = false };
            startInfo.Environment.Remove(DotnetRootVariable);
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = _startProcess(startInfo)
                ?? throw new InvalidOperationException($"SAP Logon did not start: '{path}'.");
            _launched[process.Id] = process;

            return process.Id;
        }

        // Tests whether SAP GUI Scripting answers; an unavailable SAP Logon reports false instead of failing.
        private bool TestAvailable()
        {
            try
            {
                return _scriptingHost.Invoke(application => application is not null);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        // Waits until the started SAP Logon makes SAP GUI Scripting available, failing after the timeout.
        private void WaitForAvailability(int processId)
        {
            // Poll the scripting host; SAP Logon registers SAPGUI once it finished starting.
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < AvailabilityTimeout)
            {
                if (TestAvailable())
                {
                    return;
                }

                Thread.Sleep(AvailabilityPollInterval);
            }

            var message = $"SAP Logon (process {processId}) started, but SAP GUI Scripting did not become available " +
                $"within {AvailabilityTimeout.TotalSeconds} seconds. Check that scripting is enabled.";
            throw new InvalidOperationException(message);
        }
        #endregion
    }
}
