namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Names the SAP GUI Scripting types the recorder treats specially.
    /// </summary>
    internal static class SapGuiTypes
    {
        #region *** Constants    ***
        /// <summary>
        /// The scripting root, <c>/app</c>.
        /// </summary>
        internal const string Application = "GuiApplication";

        /// <summary>
        /// A connection to one SAP system, <c>/app/con[n]</c>.
        /// </summary>
        internal const string Connection = "GuiConnection";

        /// <summary>
        /// A free-standing frame window.
        /// </summary>
        internal const string FrameWindow = "GuiFrameWindow";

        /// <summary>
        /// The main window of a session, <c>wnd[0]</c>.
        /// </summary>
        internal const string MainWindow = "GuiMainWindow";

        /// <summary>
        /// A popup window, <c>wnd[1]</c> and above.
        /// </summary>
        internal const string ModalWindow = "GuiModalWindow";

        /// <summary>
        /// A password field, whose text the recorder never reads or sends.
        /// </summary>
        internal const string PasswordField = "GuiPasswordField";

        /// <summary>
        /// A session, <c>/app/con[n]/ses[n]</c>.
        /// </summary>
        internal const string Session = "GuiSession";

        /// <summary>
        /// A shell control, refined by its <c>SubType</c>, such as <c>GridView</c>.
        /// </summary>
        internal const string Shell = "GuiShell";
        #endregion
    }
}
