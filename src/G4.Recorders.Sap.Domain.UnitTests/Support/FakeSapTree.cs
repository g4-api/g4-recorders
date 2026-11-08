namespace G4.Recorders.Sap.Domain.UnitTests.Support
{
    /// <summary>
    /// Builds a small fake SAP GUI tree: one connection and session with a logon-like main window.
    /// </summary>
    /// <remarks>
    /// The main window has handle <see cref="MainWindowHandle"/> and holds a user field, a password field, a toolbar
    /// button, and an ALV grid shell. Every object carries the properties the recorder reads.
    /// </remarks>
    internal sealed class FakeSapTree
    {
        #region *** Constants    ***
        /// <summary>
        /// The window handle the main window reports.
        /// </summary>
        internal const long MainWindowHandle = 1001;

        /// <summary>
        /// The absolute identifier of the session.
        /// </summary>
        internal const string SessionId = "/app/con[0]/ses[0]";
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="FakeSapTree"/> class and builds the tree.
        /// </summary>
        internal FakeSapTree()
        {
            // Build application > connection > session > main window.
            Application = new FakeSapGuiObject("/app", "GuiApplication");
            var connection = Application.AddChild(new FakeSapGuiObject("/app/con[0]", "GuiConnection"));
            Session = connection.AddChild(new FakeSapGuiObject(SessionId, "GuiSession"));
            MainWindow = Session.AddChild(NewVisual($"{SessionId}/wnd[0]", "GuiMainWindow", left: 0, top: 0, width: 1200, height: 800))
                .With("Handle", MainWindowHandle)
                .With("Text", "SAP Easy Access");
            Session.With("ActiveWindow", MainWindow);
            Session.With("Info", new FakeSapGuiObject($"{SessionId}/info", "GuiSessionInfo")
                .With("SystemName", "SIM")
                .With("Client", "001"));

            // Add the toolbar with its Enter button, and the user area with the logon fields and a grid shell.
            var toolbar = MainWindow.AddChild(NewVisual($"{SessionId}/wnd[0]/tbar[0]", "GuiToolbar", left: 0, top: 40, width: 1200, height: 30));
            EnterButton = toolbar.AddChild(NewVisual($"{SessionId}/wnd[0]/tbar[0]/btn[0]", "GuiButton", left: 10, top: 45, width: 20, height: 20))
                .With("Text", "Enter")
                .With("Tooltip", "Enter (Enter)");

            var userArea = MainWindow.AddChild(NewVisual($"{SessionId}/wnd[0]/usr", "GuiUserArea", left: 0, top: 80, width: 1200, height: 700));
            UserField = userArea.AddChild(NewVisual($"{SessionId}/wnd[0]/usr/txtRSYST-BNAME", "GuiTextField", left: 100, top: 200, width: 80, height: 20))
                .With("Name", "RSYST-BNAME")
                .With("Text", "BCUSER")
                .With("Tooltip", "User");
            PasswordField = userArea.AddChild(NewVisual($"{SessionId}/wnd[0]/usr/pwdRSYST-BCODE", "GuiPasswordField", left: 100, top: 230, width: 80, height: 20))
                .With("Name", "RSYST-BCODE")
                .With("Text", "secret")
                .With("Tooltip", "Password");
            GridShell = userArea.AddChild(NewVisual($"{SessionId}/wnd[0]/usr/cntlGRID/shellcont/shell", "GuiShell", left: 0, top: 300, width: 1200, height: 400))
                .With("SubType", "GridView")
                .With("ContainerType", true);
        }
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the <c>GuiApplication</c> root.
        /// </summary>
        internal FakeSapGuiObject Application { get; }

        /// <summary>
        /// Gets the toolbar's Enter button.
        /// </summary>
        internal FakeSapGuiObject EnterButton { get; }

        /// <summary>
        /// Gets the ALV grid shell.
        /// </summary>
        internal FakeSapGuiObject GridShell { get; }

        /// <summary>
        /// Gets the main window, <c>wnd[0]</c>.
        /// </summary>
        internal FakeSapGuiObject MainWindow { get; }

        /// <summary>
        /// Gets the password field.
        /// </summary>
        internal FakeSapGuiObject PasswordField { get; }

        /// <summary>
        /// Gets the session.
        /// </summary>
        internal FakeSapGuiObject Session { get; }

        /// <summary>
        /// Gets the user name field.
        /// </summary>
        internal FakeSapGuiObject UserField { get; }
        #endregion

        #region *** Methods      ***
        // Creates a visual object with a screen rectangle.
        private static FakeSapGuiObject NewVisual(string id, string type, int left, int top, int width, int height)
        {
            return new FakeSapGuiObject(id, type)
                .With("ScreenLeft", left)
                .With("ScreenTop", top)
                .With("Width", width)
                .With("Height", height);
        }
        #endregion
    }
}
