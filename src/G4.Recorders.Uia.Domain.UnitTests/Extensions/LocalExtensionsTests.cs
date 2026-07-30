using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Uia.Domain.Extensions;
using G4.Recorders.Uia.Domain.Models;

namespace G4.Recorders.Uia.Domain.UnitTests.Extensions
{
    [TestClass]
    [TestCategory(nameof(LocalExtensions))]
    [TestCategory("UnitTest")]
    public sealed class LocalExtensionsTests
    {
        [TestMethod(DisplayName = "Verify that the first repeated branch receives selector-relative position one")]
        public void ResolveLocatorFirstRepeatedBranchTest()
        {
            // Arrange: model the first of two branches that share the same automation ID.
            var chain = NewTwinPanelChain(twinPanelMatchIndex: 1);

            // Act: resolve the canonical locator.
            var locator = chain.ResolveLocator();

            // Assert: verify that the repeated selector is positioned without dropping its ancestors.
            Assert.AreEqual(
                "/Desktop/Window[@Name='Twin Panels Demo']"
                + "/Pane[@AutomationId='MainLayout']"
                + "/Pane[@AutomationId='TwinPanel'][1]"
                + "/Pane[@AutomationId='PanelBorder']"
                + "/Pane[@AutomationId='PanelContent']"
                + "/Button[@Name='Submit']",
                locator
            );
        }

        [TestMethod(DisplayName = "Verify that the second repeated branch receives selector-relative position two")]
        public void ResolveLocatorSecondRepeatedBranchTest()
        {
            // Arrange: model the second of two branches that share the same automation ID.
            var chain = NewTwinPanelChain(twinPanelMatchIndex: 2);

            // Act: resolve the canonical locator.
            var locator = chain.ResolveLocator();

            // Assert: verify that the right branch differs at the exact repeated selector.
            Assert.AreEqual(
                "/Desktop/Window[@Name='Twin Panels Demo']"
                + "/Pane[@AutomationId='MainLayout']"
                + "/Pane[@AutomationId='TwinPanel'][2]"
                + "/Pane[@AutomationId='PanelBorder']"
                + "/Pane[@AutomationId='PanelContent']"
                + "/Button[@Name='Submit']",
                locator
            );
        }

        [TestMethod(DisplayName = "Verify that a repeated target receives its exact predicate-relative position")]
        public void ResolveLocatorRepeatedTargetTest()
        {
            // Arrange: model the second Submit button under one already-resolved parent.
            var chain = new UiaChainModel
            {
                Path =
                [
                    NewUniqueNameNode(controlType: "Window", name: "Twin Panels Demo"),
                    NewUniqueAutomationIdNode(controlType: "Pane", automationId: "PanelContent"),
                    new UiaNodeModel
                    {
                        ControlType = "Button",
                        Name = "Submit",
                        NameMatchCount = 2,
                        NameMatchIndex = 2
                    }
                ]
            };

            // Act: resolve the canonical locator.
            var locator = chain.ResolveLocator();

            // Assert: verify that the target position is attached to the supported step syntax.
            Assert.AreEqual(
                "/Desktop/Window[@Name='Twin Panels Demo']"
                + "/Pane[@AutomationId='PanelContent']"
                + "/Button[@Name='Submit'][2]",
                locator
            );
        }

        [TestMethod(DisplayName = "Verify that selector position is independent of same-control-type position")]
        public void ResolveLocatorSelectorRelativePositionTest()
        {
            // Arrange: model an unmatched Pane before two TwinPanel matches, selecting the second TwinPanel.
            var chain = new UiaChainModel
            {
                Path =
                [
                    new UiaNodeModel
                    {
                        AutomationId = "TwinPanel",
                        AutomationIdMatchCount = 2,
                        AutomationIdMatchIndex = 2,
                        ControlType = "Pane",
                        SiblingIndexOfSameControlType = 3
                    }
                ]
            };

            // Act: resolve the canonical locator.
            var locator = chain.ResolveLocator();

            // Assert: verify that the rank is two among predicate matches, not three among all Panes.
            Assert.AreEqual(
                "/Desktop/Pane[@AutomationId='TwinPanel'][2]",
                locator
            );
        }

        // Creates the hierarchy used by the identical-branch regression.
        private static UiaChainModel NewTwinPanelChain(int twinPanelMatchIndex)
        {
            return new UiaChainModel
            {
                Path =
                [
                    NewUniqueNameNode(controlType: "Window", name: "Twin Panels Demo"),
                    NewUniqueAutomationIdNode(controlType: "Pane", automationId: "MainLayout"),
                    new UiaNodeModel
                    {
                        AutomationId = "TwinPanel",
                        AutomationIdMatchCount = 2,
                        AutomationIdMatchIndex = twinPanelMatchIndex,
                        ControlType = "Pane"
                    },
                    NewUniqueAutomationIdNode(controlType: "Pane", automationId: "PanelBorder"),
                    NewUniqueAutomationIdNode(controlType: "Pane", automationId: "PanelContent"),
                    NewUniqueNameNode(controlType: "Button", name: "Submit")
                ]
            };
        }

        // Creates a node with an automation ID that is unique under its direct parent.
        private static UiaNodeModel NewUniqueAutomationIdNode(string controlType, string automationId)
        {
            return new UiaNodeModel
            {
                AutomationId = automationId,
                AutomationIdMatchCount = 1,
                ControlType = controlType
            };
        }

        // Creates a node with a name that is unique under its direct parent.
        private static UiaNodeModel NewUniqueNameNode(string controlType, string name)
        {
            return new UiaNodeModel
            {
                ControlType = controlType,
                Name = name,
                NameMatchCount = 1
            };
        }
    }
}
