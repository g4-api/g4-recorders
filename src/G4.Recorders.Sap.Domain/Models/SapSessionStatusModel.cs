namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Describes whether the recorder receives SAP actions from one session.
    /// </summary>
    public class SapSessionStatusModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the session's absolute identifier, such as <c>/app/con[0]/ses[0]</c>.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the session reports SAP actions to the recorder.
        /// </summary>
        public bool IsRecording { get; set; }

        /// <summary>
        /// Gets or sets why the session does not report SAP actions; <c>null</c> while it does.
        /// </summary>
        /// <remarks>
        /// The usual reason is a system that disables recording through <c>sapgui/user_scripting_disable_recording</c>,
        /// which makes SAP GUI refuse <c>Record</c>. The <c>user32</c> stream keeps working for such sessions.
        /// </remarks>
        public string Reason { get; set; }
        #endregion
    }
}
