using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Sap.Domain.Middlewares;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests.Middlewares
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapInputResolver))]
    [TestCategory("UnitTest")]
    public sealed class SapInputResolverTests
    {
        // A window handle no SAP window has, standing for any other application.
        private const long OtherWindow = 42;

        [TestMethod(DisplayName = "Verify that a click on a SAP control records Down and Up with the press-time target")]
        public void ResolveMouseClickTest()
        {
            // Arrange: the session hit-tests to the user field; the release lands after the screen changed.
            var tree = NewHitTestTree();
            var resolver = new SapInputResolver();

            // Act: press over the field, then release over another window.
            var down = resolver.Resolve(tree.Application, NewMouse(SapInputRecord.LeftButtonDown, FakeSapTree.MainWindowHandle), scope: null);
            var up = resolver.Resolve(tree.Application, NewMouse(SapInputRecord.LeftButtonUp, OtherWindow), scope: null);

            // Assert: both events target the field, with UIA names and the element-relative offset.
            Assert.AreEqual("Left Down", down.Event);
            Assert.AreEqual("Mouse", down.Type);
            Assert.AreEqual("Left Up", up.Event);
            Assert.AreSame(down.Chain, up.Chain);
            Assert.AreEqual("//GuiTextField[@id='/app/con[0]/ses[0]/wnd[0]/usr/txtRSYST-BNAME']", up.Chain.Locator);
            Assert.AreEqual(10, up.Chain.MouseOffset.X);
            Assert.AreEqual(5, up.Chain.MouseOffset.Y);
            Assert.AreEqual(110, ((SapPointerValueModel)up.Value).X);
        }

        [TestMethod(DisplayName = "Verify that clicks outside SAP windows are not recorded, including their release")]
        public void ResolveMouseOutsideSapTest()
        {
            // Arrange: a resolver and the fake tree.
            var tree = NewHitTestTree();
            var resolver = new SapInputResolver();

            // Act: press and release over another application's window.
            var down = resolver.Resolve(tree.Application, NewMouse(SapInputRecord.LeftButtonDown, OtherWindow), scope: null);
            var up = resolver.Resolve(tree.Application, NewMouse(SapInputRecord.LeftButtonUp, OtherWindow), scope: null);

            // Assert: neither event is recorded, and SAP was never hit-tested.
            Assert.IsNull(down);
            Assert.IsNull(up);
            Assert.IsEmpty(tree.Session.Invocations);
        }

        [TestMethod(DisplayName = "Verify that a key press targets the SAP focus and its release reuses the press")]
        public void ResolveKeyPressTest()
        {
            // Arrange: the user field has the focus in the foreground SAP window.
            var tree = new FakeSapTree();
            tree.MainWindow.With("GuiFocus", tree.UserField);
            var resolver = new SapInputResolver();

            // Act: press and release the A key.
            var down = resolver.Resolve(tree.Application, NewKey(SapInputRecord.KeyDown, keyText: "a", virtualKey: 0x41), scope: null);
            var up = resolver.Resolve(tree.Application, NewKey(SapInputRecord.KeyUp, keyText: "a", virtualKey: 0x41), scope: null);

            // Assert: UIA names, the field as target, and the release paired with the press.
            var downValue = (SapKeyboardValueModel)down.Value;
            var upValue = (SapKeyboardValueModel)up.Value;
            Assert.AreEqual("Key Down", down.Event);
            Assert.AreEqual("Keyboard", down.Type);
            Assert.AreEqual("a", downValue.Key);
            Assert.AreEqual(0x41u, downValue.VirtualKey);
            Assert.AreEqual("CapturedSnapshot", downValue.TargetSource);
            Assert.AreEqual("Key Up", up.Event);
            Assert.AreEqual("PairedRelease", upValue.TargetSource);
            Assert.AreSame(down.Chain, up.Chain);
        }

        [TestMethod(DisplayName = "Verify that typed characters in a password field are masked, and navigation keys are not")]
        #region *** Data Set ***
        [DataRow("a", 0x41u, "*", 0u)]
        [DataRow("Tab", 0x09u, "Tab", 0x09u)]
        #endregion
        public void ResolvePasswordMaskTest(string keyText, uint virtualKey, string expectedKey, uint expectedVirtualKey)
        {
            // Arrange: the password field has the focus.
            var tree = new FakeSapTree();
            tree.MainWindow.With("GuiFocus", tree.PasswordField);
            var resolver = new SapInputResolver();

            // Act: press the key.
            var down = resolver.Resolve(tree.Application, NewKey(SapInputRecord.KeyDown, keyText, virtualKey), scope: null);

            // Assert: glyphs lose the key and both codes; key names stay.
            var value = (SapKeyboardValueModel)down.Value;
            Assert.AreEqual(expectedKey, value.Key);
            Assert.AreEqual(expectedVirtualKey, value.VirtualKey);
            Assert.AreEqual(expectedVirtualKey == 0 ? 0u : 15u, value.ScanCode);
        }

        [TestMethod(DisplayName = "Verify that keys typed while another application has the foreground are not recorded")]
        public void ResolveKeyOutsideSapTest()
        {
            // Arrange: a resolver and the fake tree.
            var tree = new FakeSapTree();
            var resolver = new SapInputResolver();
            var record = NewKey(SapInputRecord.KeyDown, keyText: "a", virtualKey: 0x41);
            record.ForegroundWindow = OtherWindow;

            // Act: press a key in the other application.
            var down = resolver.Resolve(tree.Application, record, scope: null);

            // Assert: nothing is recorded.
            Assert.IsNull(down);
        }

        [TestMethod(DisplayName = "Verify that a new recording generation discards a press retained by the previous one")]
        public void ResolveGenerationResetTest()
        {
            // Arrange: a press in generation 1.
            var tree = NewHitTestTree();
            var resolver = new SapInputResolver();
            resolver.Resolve(tree.Application, NewMouse(SapInputRecord.LeftButtonDown, FakeSapTree.MainWindowHandle), scope: null);

            // Act: release in generation 2, outside SAP.
            var release = NewMouse(SapInputRecord.LeftButtonUp, OtherWindow);
            release.Generation = 2;
            var up = resolver.Resolve(tree.Application, release, scope: null);

            // Assert: the old press is gone, so the release outside SAP is not recorded.
            Assert.IsNull(up);
        }

        [TestMethod(DisplayName = "Verify that a wheel turn records its direction and at least one notch")]
        public void ResolveWheelTest()
        {
            // Arrange: a small downward delta from a high-resolution wheel.
            var tree = NewHitTestTree();
            var resolver = new SapInputResolver();
            var record = NewMouse(SapInputRecord.MouseWheel, FakeSapTree.MainWindowHandle);
            record.WheelDelta = -60;

            // Act: resolve the turn.
            var wheel = resolver.Resolve(tree.Application, record, scope: null);

            // Assert: UIA name and a normalized notch count.
            Assert.AreEqual("Wheel Down", wheel.Event);
            Assert.AreEqual(1, ((SapWheelValueModel)wheel.Value).Notches);
        }

        [TestMethod(DisplayName = "Verify that nothing is recorded while SAP GUI is unavailable")]
        public void ResolveWithoutApplicationTest()
        {
            // Arrange: a resolver.
            var resolver = new SapInputResolver();

            // Act: resolve a click without an application.
            var down = resolver.Resolve(application: null, NewMouse(SapInputRecord.LeftButtonDown, FakeSapTree.MainWindowHandle), scope: null);

            // Assert: no event.
            Assert.IsNull(down);
        }

        // Creates a keyboard record in generation 1 with SAP in the foreground.
        private static SapInputRecord NewKey(int message, string keyText, uint virtualKey)
        {
            return new SapInputRecord
            {
                ForegroundWindow = FakeSapTree.MainWindowHandle,
                Generation = 1,
                IsKeyboard = true,
                KeyText = keyText,
                Message = message,
                ScanCode = 15,
                Timestamp = 1000,
                VirtualKey = virtualKey
            };
        }

        // Creates a mouse record in generation 1 at a point inside the user field.
        private static SapInputRecord NewMouse(int message, long rootWindow)
        {
            return new SapInputRecord
            {
                Generation = 1,
                Message = message,
                RootWindow = rootWindow,
                Timestamp = 1000,
                X = 110,
                Y = 205
            };
        }

        // Creates the fake tree with a session that hit-tests every point to the user field.
        private static FakeSapTree NewHitTestTree()
        {
            var tree = new FakeSapTree();
            tree.Session.Methods["findByPosition"] = _ => new FakeSapGuiObject.FakeSapGuiCollection(tree.UserField.Properties["Id"], string.Empty);
            tree.Application.Methods["findById"] = _ => tree.UserField;

            return tree;
        }
    }
}
