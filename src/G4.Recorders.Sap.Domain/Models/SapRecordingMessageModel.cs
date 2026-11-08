namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the SAP recorder's two event streams in one <c>ReceiveRecordingEvent</c> message.
    /// </summary>
    /// <remarks>
    /// Each key holds a complete event with the shared contract, and a key is omitted when that stream has nothing for
    /// the moment the message describes. Clients read the key that matches their mode: User32 and coordinate modes
    /// read <see cref="User32"/>, and the SAP mode reads <see cref="NativeSap"/>. The streams are not paired, because
    /// SAP reports typed text only when a screen is submitted, long after the keystrokes.
    /// </remarks>
    public class SapRecordingMessageModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the SAP action event, reported by SAP GUI Scripting, or <c>null</c> when this message has none.
        /// </summary>
        public SapEventModel NativeSap { get; set; }

        /// <summary>
        /// Gets or sets the user input event, captured by the low-level hooks, or <c>null</c> when this message has none.
        /// </summary>
        public SapEventModel User32 { get; set; }
        #endregion
    }
}
