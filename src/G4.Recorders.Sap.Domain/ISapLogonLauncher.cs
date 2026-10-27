using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Starts SAP Logon on the recorder host when SAP GUI is not running, and stops the instances it started.
    /// </summary>
    public interface ISapLogonLauncher
    {
        #region *** Methods      ***
        /// <summary>
        /// Makes SAP GUI available: attaches to a running SAP Logon, or starts one from the resolved path.
        /// </summary>
        /// <param name="logon">
        /// The resolved SAP Logon values; a connection is opened when one is named and not open yet.
        /// </param>
        /// <returns>
        /// The process identifier, zero when SAP Logon was already running or no path is set, and whether it was started.
        /// </returns>
        /// <exception cref="System.IO.FileNotFoundException">The path does not exist on the recorder host.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// SAP Logon started, but SAP GUI Scripting did not become available.
        /// </exception>
        (int ProcessId, bool IsLaunched) Start(SapLogonParametersModel logon);

        /// <summary>
        /// Stops a SAP Logon this launcher started; other processes are never touched.
        /// </summary>
        /// <param name="processId">The process identifier <see cref="Start"/> returned.</param>
        /// <returns>
        /// <c>true</c> when the process was stopped; <c>false</c> when the launcher did not start it or it already exited.
        /// </returns>
        bool Stop(int processId);
        #endregion
    }
}
