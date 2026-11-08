namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Describes the SAP Logon process the recorder starts when SAP GUI is not running.
    /// </summary>
    /// <remarks>
    /// Without a path the recorder only attaches to a running SAP Logon. A running SAP Logon is never restarted, so these
    /// values apply only when none is running.
    /// </remarks>
    public class SapLogonParametersModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the command-line arguments passed to SAP Logon.
        /// </summary>
        public string[] Arguments { get; set; }

        /// <summary>
        /// Gets or sets the SAP Logon entry to open after SAP GUI is reachable, such as <c>S4H - Development</c>.
        /// </summary>
        public string Connection { get; set; }

        /// <summary>
        /// Gets or sets the executable path on the recorder host, such as <c>saplogon.exe</c> or the SAP GUI simulator.
        /// </summary>
        public string Path { get; set; }
        #endregion
    }
}
