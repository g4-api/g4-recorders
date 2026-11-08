namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the values a client sends with <c>StartRecorder</c>.
    /// </summary>
    /// <remarks>
    /// Every member is optional. A member the request leaves <c>null</c> is resolved from its environment variable,
    /// then from <c>appsettings.json</c>, then from its default; see <see cref="SapRecorderSettingsResolver"/>.
    /// Password redaction and value-free logging are not options and cannot be switched off.
    /// </remarks>
    public class SapRecorderParametersModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the SAP Logon to launch when SAP GUI is not running; <c>null</c> to resolve it elsewhere.
        /// </summary>
        public SapLogonParametersModel SapLogon { get; set; }

        /// <summary>
        /// Gets or sets the sessions to record; <c>null</c> to resolve the scope elsewhere.
        /// </summary>
        public SapScopeParametersModel Scope { get; set; }

        /// <summary>
        /// Gets or sets whether the recorder may switch sessions to recording mode (<c>Record = true</c>).
        /// </summary>
        /// <remarks>
        /// When <c>false</c>, the recorder still subscribes to sessions but records <c>nativeSap</c> actions only from
        /// sessions the user already set to recording mode.
        /// </remarks>
        public bool? SetRecordMode { get; set; }

        /// <summary>
        /// Gets or sets the streams to send: <c>user32</c>, <c>nativeSap</c>, or both.
        /// </summary>
        public string[] Streams { get; set; }
        #endregion
    }
}
