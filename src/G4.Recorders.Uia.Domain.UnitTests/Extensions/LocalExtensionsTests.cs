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
                        Attribute1Value = "Submit",
                        Attribute1MatchCount = 2,
                        Attribute1MatchIndex = 2
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
                        Attribute2Value = "TwinPanel",
                        Attribute2MatchCount = 2,
                        Attribute2MatchIndex = 2,
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

        [TestMethod(DisplayName = "Verify that the primary attribute order decides the unique-selector predicate")]
        public void ResolveLocatorPrimaryAttributeOrderTest()
        {
            // Arrange: model the same element captured under each attribute order. The stored values are positional
            // to the order supplied at capture, so each chain pairs its capture order with the matching resolve order.
            var nameFirstChain = new UiaChainModel
            {
                Path = [NewDualIdentityNode(primaryValue: "Submit", secondaryValue: "SubmitButton")]
            };
            var automationIdFirstChain = new UiaChainModel
            {
                Path = [NewDualIdentityNode(primaryValue: "SubmitButton", secondaryValue: "Submit")]
            };

            // Act: resolve each chain with the same order used to capture it.
            var nameFirst = nameFirstChain.ResolveLocator(["Name", "AutomationId"]);
            var automationIdFirst = automationIdFirstChain.ResolveLocator(["AutomationId", "Name"]);

            // Assert: the first attribute in the list wins the unique-selector branch.
            Assert.AreEqual("/Desktop/Button[@Name='Submit']", nameFirst);
            Assert.AreEqual("/Desktop/Button[@AutomationId='SubmitButton']", automationIdFirst);
        }

        [TestMethod(DisplayName = "Verify that a non-default attribute name is emitted in the predicate")]
        public void ResolveLocatorNonDefaultAttributeTest()
        {
            // Arrange: model an element identified by its class name as the primary attribute.
            var chain = new UiaChainModel
            {
                Path =
                [
                    new UiaNodeModel
                    {
                        ControlType = "Pane",
                        Attribute1Value = "PanelHost",
                        Attribute1MatchCount = 1
                    }
                ]
            };

            // Act: resolve using ClassName as the primary attribute.
            var locator = chain.ResolveLocator(["ClassName", "Name"]);

            // Assert: the configured attribute name keys the predicate.
            Assert.AreEqual("/Desktop/Pane[@ClassName='PanelHost']", locator);
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
                        Attribute2Value = "TwinPanel",
                        Attribute2MatchCount = 2,
                        Attribute2MatchIndex = twinPanelMatchIndex,
                        ControlType = "Pane"
                    },
                    NewUniqueAutomationIdNode(controlType: "Pane", automationId: "PanelBorder"),
                    NewUniqueAutomationIdNode(controlType: "Pane", automationId: "PanelContent"),
                    NewUniqueNameNode(controlType: "Button", name: "Submit")
                ]
            };
        }

        // Creates a Button node whose primary and secondary attributes are both unique under the parent, using the
        // positional values captured for whichever attribute order the caller intends to resolve with.
        private static UiaNodeModel NewDualIdentityNode(string primaryValue, string secondaryValue)
        {
            return new UiaNodeModel
            {
                ControlType = "Button",
                Attribute1Value = primaryValue,
                Attribute1MatchCount = 1,
                Attribute2Value = secondaryValue,
                Attribute2MatchCount = 1
            };
        }

        // Creates a node with an automation ID (the default secondary attribute) that is unique under its direct parent.
        private static UiaNodeModel NewUniqueAutomationIdNode(string controlType, string automationId)
        {
            return new UiaNodeModel
            {
                Attribute2Value = automationId,
                Attribute2MatchCount = 1,
                ControlType = controlType
            };
        }

        // Creates a node with a name (the default primary attribute) that is unique under its direct parent.
        private static UiaNodeModel NewUniqueNameNode(string controlType, string name)
        {
            return new UiaNodeModel
            {
                ControlType = controlType,
                Attribute1Value = name,
                Attribute1MatchCount = 1
            };
        }
    }
}
