using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapGuiNavigator))]
    [TestCategory("UnitTest")]
    public sealed class SapGuiNavigatorTests
    {
        // Lists the hit-test call the session records for a point on the user field.
        private static readonly string[] ExpectedHitTest = ["findByPosition(110,205,False)", "findById(wnd[0]/usr/txtRSYST-BNAME,False)"];

        [TestMethod(DisplayName = "Verify that a window handle resolves to its session and window")]
        public void FindWindowTest()
        {
            // Arrange: the fake tree, whose main window has a known handle.
            var tree = new FakeSapTree();

            // Act: match the handle, and a handle no SAP window has.
            var (session, window) = SapGuiNavigator.FindWindow(tree.Application, FakeSapTree.MainWindowHandle);
            var (missingSession, missingWindow) = SapGuiNavigator.FindWindow(tree.Application, rootWindow: 42);

            // Assert: the known handle finds wnd[0], and the unknown one finds nothing.
            Assert.AreSame(tree.Session, session);
            Assert.AreSame(tree.MainWindow, window);
            Assert.IsNull(missingSession);
            Assert.IsNull(missingWindow);
        }

        [TestMethod(DisplayName = "Verify that a point is hit-tested in its window's session and a relative identifier is resolved there")]
        public void FindByPositionTest()
        {
            // Arrange: the session answers the hit test with a session-relative identifier.
            var tree = new FakeSapTree();
            tree.Session.Methods["findByPosition"] = _ => new FakeSapGuiObject.FakeSapGuiCollection("wnd[0]/usr/txtRSYST-BNAME", string.Empty);
            tree.Session.Methods["findById"] = _ => tree.UserField;

            // Act: hit-test a point on the user field inside the main window.
            var control = SapGuiNavigator.FindByPosition(tree.Application, FakeSapTree.MainWindowHandle, x: 110, y: 205);

            // Assert: the session answered both calls and the user field came back.
            Assert.AreSame(tree.UserField, control);
            Assert.AreSequenceEqual(ExpectedHitTest, tree.Session.Invocations);
        }

        [TestMethod(DisplayName = "Verify that a point outside every SAP window is not hit-tested")]
        public void FindByPositionOutsideTest()
        {
            // Arrange: the fake tree; no method is configured, so any call would throw.
            var tree = new FakeSapTree();

            // Act: hit-test with a window handle SAP does not own.
            var control = SapGuiNavigator.FindByPosition(tree.Application, rootWindow: 42, x: 110, y: 205);

            // Assert: no control, and the session was never called.
            Assert.IsNull(control);
            Assert.IsEmpty(tree.Session.Invocations);
        }

        [TestMethod(DisplayName = "Verify that focus is read from the foreground SAP window, or the first session when SAP is in the background")]
        public void GetFocusedTest()
        {
            // Arrange: the main window's focus is the user field.
            var tree = new FakeSapTree();
            tree.MainWindow.With("GuiFocus", tree.UserField);

            // Act: read focus with SAP in the foreground, and with another window in the foreground.
            var foregroundFocus = SapGuiNavigator.GetFocused(tree.Application, FakeSapTree.MainWindowHandle);
            var backgroundFocus = SapGuiNavigator.GetFocused(tree.Application, foregroundWindow: 42);

            // Assert: both resolve the user field.
            Assert.AreSame(tree.UserField, foregroundFocus);
            Assert.AreSame(tree.UserField, backgroundFocus);
        }

        [TestMethod(DisplayName = "Verify that sessions are collected across connections")]
        public void GetSessionsTest()
        {
            // Arrange: a second connection with its own session.
            var tree = new FakeSapTree();
            var connection = tree.Application.AddChild(new FakeSapGuiObject("/app/con[1]", "GuiConnection"));
            var second = connection.AddChild(new FakeSapGuiObject("/app/con[1]/ses[0]", "GuiSession"));

            // Act: list the sessions.
            var sessions = SapGuiNavigator.GetSessions(tree.Application);

            // Assert: both sessions, in SAP order.
            Assert.HasCount(2, sessions);
            Assert.AreSame(tree.Session, sessions[0]);
            Assert.AreSame(second, sessions[1]);
        }
    }
}
