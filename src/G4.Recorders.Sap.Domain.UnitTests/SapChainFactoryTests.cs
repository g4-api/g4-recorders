using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;

using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapChainFactory))]
    [TestCategory("UnitTest")]
    public sealed class SapChainFactoryTests
    {
        // Lists the control types of the user field's chain, root-first.
        private static readonly string[] ExpectedUserFieldPath = ["GuiMainWindow", "GuiUserArea", "GuiTextField"];

        [TestMethod(DisplayName = "Verify that a chain runs root-first from the window to the trigger and marks both ends")]
        public void NewChainPathTest()
        {
            // Arrange: a text field inside the main window's user area.
            var tree = new FakeSapTree();

            // Act: build the chain.
            var chain = SapChainFactory.NewChain(tree.UserField);

            // Assert: window first, trigger last, and both flagged.
            Assert.AreSequenceEqual(ExpectedUserFieldPath, chain.Path.Select(i => i.ControlType));
            Assert.IsTrue(chain.Path[0].IsTopWindow);
            Assert.IsTrue(chain.Path[^1].IsTriggerElement);
            Assert.AreSame(chain.Path[0], chain.TopWindow);
        }

        [TestMethod(DisplayName = "Verify that the locator is an XPath on the trigger's absolute identifier")]
        public void NewChainLocatorTest()
        {
            // Arrange: the toolbar button.
            var tree = new FakeSapTree();

            // Act: build the chain.
            var chain = SapChainFactory.NewChain(tree.EnterButton);

            // Assert: the SAP driver resolves this form through findById.
            Assert.AreEqual("//GuiButton[@id='/app/con[0]/ses[0]/wnd[0]/tbar[0]/btn[0]']", chain.Locator);
        }

        [TestMethod(DisplayName = "Verify that SAP values map into the shared node fields")]
        public void NewNodeFieldMappingTest()
        {
            // Arrange: the user field.
            var tree = new FakeSapTree();

            // Act: build the trigger node.
            var node = SapChainFactory.NewChain(tree.UserField).Path[^1];

            // Assert: identity, geometry, and identifiers sit in the shared fields.
            Assert.AreEqual("RSYST-BNAME", node.AutomationId);
            Assert.AreEqual("GuiTextField", node.ClassName);
            Assert.AreEqual("SAPGUI", node.FrameworkId);
            Assert.AreEqual(100d, node.Bounds.Left);
            Assert.AreEqual(80d, node.Bounds.Width);
            Assert.AreEqual("/app/con[0]/ses[0]/wnd[0]/usr/txtRSYST-BNAME", node.Properties["Id"]);
            Assert.AreEqual("wnd[0]/usr/txtRSYST-BNAME", node.Properties["RelativeId"]);
        }

        [TestMethod(DisplayName = "Verify that input fields never expose their text as the node name")]
        public void NewNodeHidesFieldContentsTest()
        {
            // Arrange: a user field with text, a password field with text, and a captioned button.
            var tree = new FakeSapTree();

            // Act: build each trigger node.
            var userNode = SapChainFactory.NewChain(tree.UserField).Path[^1];
            var passwordNode = SapChainFactory.NewChain(tree.PasswordField).Path[^1];
            var buttonNode = SapChainFactory.NewChain(tree.EnterButton).Path[^1];

            // Assert: the field uses its tooltip, the password nothing, and the button its caption.
            Assert.AreEqual("User", userNode.Name);
            Assert.AreEqual(string.Empty, passwordNode.Name);
            Assert.AreEqual("Enter", buttonNode.Name);
        }

        [TestMethod(DisplayName = "Verify that a shell node reports its subtype as the class name")]
        public void NewNodeShellSubTypeTest()
        {
            // Arrange: an ALV grid shell.
            var tree = new FakeSapTree();

            // Act: build the trigger node.
            var node = SapChainFactory.NewChain(tree.GridShell).Path[^1];

            // Assert: the subtype refines the class name and is kept in the properties.
            Assert.AreEqual("GuiShell", node.ControlType);
            Assert.AreEqual("GridView", node.ClassName);
            Assert.AreEqual("GridView", node.Properties["SubType"]);
            Assert.IsTrue((bool)node.Properties["ContainerType"]);
        }

        [TestMethod(DisplayName = "Verify that objects above the windows, or no object, yield an empty chain")]
        public void NewChainEmptyTest()
        {
            // Arrange: a session object, which sits above the windows.
            var tree = new FakeSapTree();

            // Act: build chains for the session and for no object.
            var sessionChain = SapChainFactory.NewChain(tree.Session);
            var nullChain = SapChainFactory.NewChain(component: null);

            // Assert: neither has a path or locator.
            Assert.IsEmpty(sessionChain.Path);
            Assert.AreEqual(string.Empty, sessionChain.Locator);
            Assert.IsEmpty(nullChain.Path);
        }

        [TestMethod(DisplayName = "Verify that the mouse offset is measured from the trigger's top-left corner")]
        public void NewMouseOffsetTest()
        {
            // Arrange: the user field at (100, 200).
            var chain = SapChainFactory.NewChain(new FakeSapTree().UserField);

            // Act: measure a point inside the field.
            var offset = SapChainFactory.NewMouseOffset(chain, x: 112, y: 207);

            // Assert: the offset is relative to the field, not the window.
            Assert.AreEqual(12, offset.X);
            Assert.AreEqual(7, offset.Y);
        }
    }
}
