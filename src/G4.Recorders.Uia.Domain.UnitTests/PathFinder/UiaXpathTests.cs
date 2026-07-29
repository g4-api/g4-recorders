using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Uia.Domain;

namespace G4.Recorders.Uia.Domain.UnitTests.PathFinder
{
    [TestClass]
    [TestCategory(nameof(UiaXpath))]
    [TestCategory("UnitTest")]
    public sealed class UiaXpathTests
    {
        [TestMethod(DisplayName = "Verify that canonical indexed syntax is split into direct-child segments")]
        public void GetHierarchyCanonicalIndexedSyntaxTest()
        {
            // Arrange: use the canonical right-branch locator emitted by the recorder.
            const string xpath = "/Desktop/Window[@Name='Twin Panels Demo']"
                + "/Pane[@AutomationId='TwinPanel'][2]"
                + "/Button[@Name='Submit']";

            // Act: split the locator into executable UIA hierarchy segments.
            var hierarchy = UiaXpath.GetHierarchy(xpath);

            // Assert: verify that the position stays attached to the matching Pane selector.
            CollectionAssert.AreEqual(
                new[]
                {
                    "Window[@Name='Twin Panels Demo']",
                    "Pane[@AutomationId='TwinPanel'][2]",
                    "Button[@Name='Submit']"
                },
                hierarchy
            );
        }

        [TestMethod(DisplayName = "Verify that descendant separators remain attached to the following segment")]
        public void GetHierarchyDescendantSeparatorTest()
        {
            // Arrange: use a locator containing the supported UWP hierarchy gap.
            const string xpath = "/Desktop/Window[@Name='App']//Button[@Name='Submit']";

            // Act: split the locator into executable UIA hierarchy segments.
            var hierarchy = UiaXpath.GetHierarchy(xpath);

            // Assert: verify that descendant scope applies only to the segment after the gap.
            CollectionAssert.AreEqual(
                new[]
                {
                    "Window[@Name='App']",
                    "/Button[@Name='Submit']"
                },
                hierarchy
            );
        }

        [TestMethod(DisplayName = "Verify that supported positions convert from one-based to zero-based")]
        public void GetPositionSelectionSupportedPositionTest()
        {
            // Arrange: use the second selector match from a two-element result.
            const string segment = "Pane[@AutomationId='TwinPanel'][2]";

            // Act: resolve its collection selection.
            var selection = UiaXpath.GetPositionSelection(segment, matchCount: 2);

            // Assert: verify that XPath position two selects collection index one.
            Assert.IsTrue(selection.HasPosition);
            Assert.AreEqual(1, selection.Index);
        }

        [TestMethod(DisplayName = "Verify that zero is rejected as an XPath position")]
        public void GetPositionSelectionZeroPositionTest()
        {
            // Arrange: use a zero position, which is outside the recorder's 1-based contract.
            const string segment = "Pane[@AutomationId='TwinPanel'][0]";

            // Act: resolve its collection selection.
            var selection = UiaXpath.GetPositionSelection(segment, matchCount: 2);

            // Assert: verify that the invalid position cannot silently select the first match.
            Assert.IsTrue(selection.HasPosition);
            Assert.AreEqual(-1, selection.Index);
        }

        [TestMethod(DisplayName = "Verify that an out-of-range XPath position is rejected")]
        public void GetPositionSelectionOutOfRangeTest()
        {
            // Arrange: request a third selector match from a two-element result.
            const string segment = "Pane[@AutomationId='TwinPanel'][3]";

            // Act: resolve its collection selection.
            var selection = UiaXpath.GetPositionSelection(segment, matchCount: 2);

            // Assert: verify that the invalid position cannot reach collection access.
            Assert.IsTrue(selection.HasPosition);
            Assert.AreEqual(-1, selection.Index);
        }
    }
}
