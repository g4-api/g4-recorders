namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Names the SAP recorder's two event streams, as they appear in recording messages and in <c>streams</c>.
    /// </summary>
    public static class SapRecordingStreams
    {
        #region *** Constants    ***
        /// <summary>
        /// The SAP action stream, from <c>GuiSession.Change</c> events.
        /// </summary>
        public const string NativeSap = "nativeSap";

        /// <summary>
        /// The user input stream, from the global mouse and keyboard hooks.
        /// </summary>
        public const string User32 = "user32";
        #endregion
    }
}
