using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Uia.Domain.Middlewares;
using G4.Recorders.Uia.Domain.Models;

namespace G4.Recorders.Uia.Domain.UnitTests.Middlewares
{
    [TestClass]
    [TestCategory(nameof(MouseDragResolver))]
    [TestCategory("UnitTest")]
    public sealed class MouseDragResolverTests
    {
        // Provides the locator of the dragged list item; its ancestors and descendants share this prefix.
        private const string SourceLocator = "/Desktop/Window[@Name='Board']/List[@Name='To do']/ListItem[1]";

        // Provides the locator of a drop list that is not related to the dragged item.
        private const string TargetLocator = "/Desktop/Window[@Name='Board']/List[@Name='Done']";

        [TestMethod(DisplayName = "Verify that a release is a drag only once it leaves the threshold square " +
            "around the press on either axis")]
        #region *** Data Set ***
        [DataRow(100, 100, false)]
        [DataRow(107, 93, false)]
        [DataRow(108, 100, true)]
        [DataRow(100, 92, true)]
        [DataRow(300, 250, true)]
        #endregion
        public void MouseDragThresholdTest(int x, int y, bool expected)
        {
            // Act: compare a release with a press at (100, 100) and an eight-pixel threshold.
            var isDrag = MouseDragResolver.TestDrag(start: (100, 100), end: (x, y), thresholdPixels: 8);

            // Assert: a wobble inside the square stays a click.
            Assert.AreEqual(expected, isDrag);
        }

        [TestMethod(DisplayName = "Verify that the threshold is the larger system drag size, never below the " +
            "recorder floor")]
        #region *** Data Set ***
        [DataRow(4, 4, 8)]
        [DataRow(0, 0, 8)]
        [DataRow(12, 10, 12)]
        [DataRow(6, 20, 20)]
        #endregion
        public void MouseDragResolveThresholdTest(int systemWidth, int systemHeight, int expected)
        {
            // Act: resolve the threshold from the system drag metrics.
            var threshold = MouseDragResolver.ResolveThreshold(systemWidth, systemHeight);

            // Assert: the default Windows value of four pixels is raised to the floor.
            Assert.AreEqual(expected, threshold);
        }

        [TestMethod(DisplayName = "Verify that a drag onto a distinct element carries the grab and drop offsets " +
            "from the element corners")]
        public void MouseDragTargetOffsetsTest()
        {
            // Arrange: a 100 x 40 item at (100, 100) and a 200 x 300 list at (500, 200).
            var source = NewChain(locator: SourceLocator, left: 100, top: 100, width: 100, height: 40);
            var target = NewChain(locator: TargetLocator, left: 500, top: 200, width: 200, height: 300);

            // Act: grab the item near its left edge and release in the upper part of the list.
            var drag = MouseDragResolver.NewDrag(source, target, start: (140, 123), end: (610, 330));

            // Assert: the press point and both offsets are kept, and the drop element is named.
            Assert.AreEqual(140, drag.StartX);
            Assert.AreEqual(123, drag.StartY);
            Assert.AreEqual(40, drag.SourceOffsetX);
            Assert.AreEqual(23, drag.SourceOffsetY);
            Assert.AreSame(target, drag.Target);
            Assert.AreEqual(110, drag.TargetOffsetX);
            Assert.AreEqual(130, drag.TargetOffsetY);
        }

        [TestMethod(DisplayName = "Verify that a release over the dragged element, an ancestor, a descendant, or " +
            "nothing names no drop element")]
        #region *** Data Set ***
        [DataRow(SourceLocator)]
        [DataRow("/Desktop/Window[@Name='Board']/List[@Name='To do']")]
        [DataRow(SourceLocator + "/Text[1]")]
        [DataRow(null)]
        #endregion
        public void MouseDragRelatedTargetTest(string targetLocator)
        {
            // Arrange: the release resolves to the given element, or to nothing.
            var source = NewChain(locator: SourceLocator, left: 100, top: 100, width: 100, height: 40);
            var target = targetLocator == null
                ? null
                : NewChain(locator: targetLocator, left: 0, top: 0, width: 800, height: 600);

            // Act: describe a drag that ends over that element.
            var drag = MouseDragResolver.NewDrag(source, target, start: (150, 120), end: (400, 120));

            // Assert: the drag is kept, without a drop element, so it is replayed by its distance.
            Assert.IsNull(drag.Target);
            Assert.IsNull(drag.TargetOffsetX);
            Assert.IsNull(drag.TargetOffsetY);
            Assert.AreEqual(150, drag.StartX);
        }

        [TestMethod(DisplayName = "Verify that an element without usable bounds carries no offset, so it is grabbed " +
            "at its center")]
        public void MouseDragMissingBoundsTest()
        {
            // Arrange: a source chain whose trigger has no bounds, as UIA reports for some virtual elements.
            var source = new UiaChainModel
            {
                Locator = SourceLocator,
                Path = [new UiaNodeModel { IsTriggerElement = true }]
            };

            // Act: describe a drag by distance from that element.
            var drag = MouseDragResolver.NewDrag(source, target: null, start: (140, 123), end: (300, 123));

            // Assert: no offset is reported, so the Hub keeps the plugin's center alignment.
            Assert.IsNull(drag.SourceOffsetX);
            Assert.IsNull(drag.SourceOffsetY);
        }

        // Creates a single-trigger chain with explicit geometry, so the tests do not depend on UIA providers.
        private static UiaChainModel NewChain(string locator, double left, double top, double width, double height)
        {
            return new UiaChainModel
            {
                Locator = locator,
                Path =
                [
                    new UiaNodeModel
                    {
                        Bounds = new UiaNodeModel.BoundsRectangle
                        {
                            Height = height,
                            Left = left,
                            Top = top,
                            Width = width
                        },
                        IsTriggerElement = true
                    }
                ]
            };
        }
    }
}
