using System;
using System.Runtime.InteropServices;

namespace G4.Recorders.Sap.Domain.Scripting
{
    /// <summary>
    /// Default <see cref="ISapScriptingHost"/> that attaches through the running object table and calls SAP GUI
    /// Scripting on a dedicated STA thread.
    /// </summary>
    /// <remarks>
    /// Register it as a singleton: it owns the scripting thread and the application reference for the life of the
    /// process. The application reference is checked before every operation and dropped when SAP Logon has exited, so
    /// the next operation attaches to the new instance.
    /// </remarks>
    public sealed class SapScriptingHost : ISapScriptingHost
    {
        #region *** Constants    ***
        // Bounds how long a caller waits for one scripting operation, including attachment.
        private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

        // Names the running object table entry SAP Logon registers while it runs.
        private const string RotEntryName = "SAPGUI";
        #endregion

        #region *** Fields       ***
        // Owns every scripting reference and receives SAP events.
        private readonly SapComThread _thread = new(threadName: "SapRecorderScripting");

        // Caches the GuiApplication object; read and written only on the scripting thread.
        private ISapGuiObject _application;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public void Dispose()
        {
            // Stop the scripting thread; references it holds are released with the process.
            _thread.Dispose();
        }

        /// <inheritdoc />
        public T Invoke<T>(Func<ISapGuiObject, T> operation)
        {
            // Require the operation before queueing it.
            ArgumentNullException.ThrowIfNull(argument: operation);

            // Attach on the scripting thread and fail with a readable message when SAP GUI is unavailable.
            return _thread.Invoke(() =>
            {
                var application = GetApplication();
                if (application is null)
                {
                    var message = "SAP GUI Scripting is not available: SAP Logon is not running, or scripting is disabled.";
                    throw new InvalidOperationException(message);
                }

                return operation(application);
            }, OperationTimeout);
        }

        /// <inheritdoc />
        public void Post(Action<ISapGuiObject> operation)
        {
            // Require the operation, then run it with whatever application is attachable at that time.
            ArgumentNullException.ThrowIfNull(argument: operation);
            _thread.Post(() => operation(GetApplication()));
        }

        // Returns the attached GuiApplication, attaching or re-attaching when needed; null when SAP GUI is unavailable.
        // Runs on the scripting thread only.
        private ISapGuiObject GetApplication()
        {
            // Keep the cached application while SAP Logon still answers; drop it once the process is gone.
            if (_application is not null && TestApplication(_application))
            {
                return _application;
            }

            _application = null;

            try
            {
                // Read the SAPGUI entry, then request the engine; SAP GUI refuses it when scripting is disabled.
                var rotEntry = RunningObjectTableReader.GetObject(RotEntryName);
                if (rotEntry is null)
                {
                    return null;
                }

                _application = new ComSapGuiObject(rotEntry).InvokeMethod("GetScriptingEngine") as ISapGuiObject;

                return _application;
            }
            catch (COMException)
            {
                // Scripting disabled or SAP Logon exiting: report unavailable and retry on the next operation.
                return null;
            }
        }

        // Tests whether a cached application still answers; a disconnected proxy fails every call.
        private static bool TestApplication(ISapGuiObject application)
        {
            try
            {
                // Read a cheap property; any COM failure means SAP Logon exited or restarted.
                return application.GetProperty("Id") is not null;
            }
            catch (COMException)
            {
                return false;
            }
            catch (InvalidComObjectException)
            {
                return false;
            }
        }
        #endregion
    }
}
