namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Describes the outcome of <c>StartRecorder</c>: the SAP Logon process and the values in force.
    /// </summary>
    public class SapRecorderStartModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets a value indicating whether the recorder launched SAP Logon for this request.
        /// </summary>
        public bool IsLaunched { get; set; }

        /// <summary>
        /// Gets or sets the resolved values in force, with the source of each one.
        /// </summary>
        public SapRecorderOptionsModel Options { get; set; }

        /// <summary>
        /// Gets or sets the SAP Logon process identifier; pass it to <c>StopRecorder</c> to end a launched SAP Logon.
        /// </summary>
        /// <remarks>
        /// Zero when the recorder attached to a SAP Logon that was already running, or when no SAP Logon is available.
        /// </remarks>
        public int ProcessId { get; set; }
        #endregion
    }
}
