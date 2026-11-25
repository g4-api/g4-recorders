using System;

using G4.Recorders.Uia.Domain.Models;

namespace G4.Recorders.Uia.Domain.Middlewares
{
    /// <summary>
    /// Tells a left-button drag from a click and describes the drag as the facts a <c>Left Up</c> event carries.
    /// </summary>
    /// <remarks>
    /// Compute-only: it reads the press and release points and the chains the recorder already resolved, and never
    /// queries UI Automation, so the recorder hit-tests the drop point only after a release is known to be a drag.
    /// </remarks>
    internal static class MouseDragResolver
    {
        #region *** Constants    ***
        /// <summary>
        /// The smallest distance, in screen pixels on either axis, that turns a press and release into a drag.
        /// </summary>
        /// <remarks>
        /// Windows starts a drag after a few pixels (<c>SM_CXDRAG</c>, 4 by default), while a click often wobbles by
        /// that much. The recorder takes the larger of the system value and this floor, so a click stays a click.
        /// </remarks>
        internal const int MinimumThresholdPixels = 8;
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Creates the drag facts of a press and release that moved past the drag threshold.
        /// </summary>
        /// <param name="source">
        /// The press-time chain of the dragged element, or <see langword="null"/> when the press landed on nothing UI
        /// Automation resolved; the drag then has no grab offset and replays from its press point.
        /// </param>
        /// <param name="target">The chain under the release point, or <see langword="null"/> when none was resolved.</param>
        /// <param name="start">The screen point of the press.</param>
        /// <param name="end">The screen point of the release.</param>
        /// <returns>The drag facts, with the drop element only when it is distinct from the dragged element.</returns>
        /// <remarks>
        /// A release over the dragged element, one of its ancestors, or one of its descendants names no distinct
        /// drop element, so the target is left out and the drag is replayed by its distance. Offsets are measured
        /// from the top-left corner of each element, the alignment the UIA driver applies offsets to, and are left
        /// out for an element without usable bounds, which is then grabbed or dropped on at its center.
        /// </remarks>
        internal static UiaDragModel NewDrag(
            UiaChainModel source,
            UiaChainModel target,
            (int X, int Y) start,
            (int X, int Y) end)
        {
            // Keep the drop element only when it is not the dragged element or one of its relatives.
            var isDistinctTarget = target != null && !TestRelated(source, target);
            var dropTarget = isDistinctTarget ? target : null;

            // Measure the grab point from the source corner and the drop point from the target corner.
            var sourceOffset = GetCornerOffset(chain: source, point: start);
            var targetOffset = dropTarget != null
                ? GetCornerOffset(chain: dropTarget, point: end)
                : null;

            return new UiaDragModel
            {
                SourceOffsetX = sourceOffset?.X,
                SourceOffsetY = sourceOffset?.Y,
                StartX = start.X,
                StartY = start.Y,
                Target = dropTarget,
                TargetOffsetX = targetOffset?.X,
                TargetOffsetY = targetOffset?.Y
            };
        }

        /// <summary>
        /// Resolves the drag threshold from the system drag metrics.
        /// </summary>
        /// <param name="systemWidth">The system drag width, <c>SM_CXDRAG</c>, or zero when it is unavailable.</param>
        /// <param name="systemHeight">The system drag height, <c>SM_CYDRAG</c>, or zero when it is unavailable.</param>
        /// <returns>The larger system value, never below <see cref="MinimumThresholdPixels"/>.</returns>
        internal static int ResolveThreshold(int systemWidth, int systemHeight)
        {
            return Math.Max(MinimumThresholdPixels, Math.Max(systemWidth, systemHeight));
        }

        /// <summary>
        /// Tests whether a release moved far enough from its press to be a drag.
        /// </summary>
        /// <param name="start">The screen point of the press.</param>
        /// <param name="end">The screen point of the release.</param>
        /// <param name="thresholdPixels">The distance the pointer has to move on either axis.</param>
        /// <returns><see langword="true"/> when the release left the threshold square around the press.</returns>
        internal static bool TestDrag((int X, int Y) start, (int X, int Y) end, int thresholdPixels)
        {
            var isPastX = Math.Abs(end.X - start.X) >= thresholdPixels;
            var isPastY = Math.Abs(end.Y - start.Y) >= thresholdPixels;
            return isPastX || isPastY;
        }

        // Gets the distance from the top-left corner of a chain's trigger element to a screen point, the same measure
        // as the recorded click offset. The trigger is the node the recorder marked. An element without usable bounds
        // has no corner to measure from, so it yields null and the plugin's center alignment is used instead.
        private static (int X, int Y)? GetCornerOffset(UiaChainModel chain, (int X, int Y) point)
        {
            // Read the marked trigger's bounds; the last marked node is the innermost element.
            var trigger = chain?.Path?.FindLast(node => node != null && node.IsTriggerElement);
            var bounds = trigger?.Bounds;
            var isValidBounds = bounds != null
                && double.IsFinite(bounds.Left)
                && double.IsFinite(bounds.Top)
                && double.IsFinite(bounds.Width)
                && double.IsFinite(bounds.Height)
                && bounds.Width > 0
                && bounds.Height > 0;

            if (!isValidBounds)
            {
                return null;
            }

            // Subtract the corner on each axis and round to whole pixels, as pointer moves take whole pixels.
            var offsetX = (int)Math.Round(point.X - bounds.Left, MidpointRounding.AwayFromZero);
            var offsetY = (int)Math.Round(point.Y - bounds.Top, MidpointRounding.AwayFromZero);
            return (offsetX, offsetY);
        }

        // Tests whether two chains address the same element, or one element inside the other. UIA locators are
        // absolute paths from the desktop, so an element inside another one has the other's locator as a prefix,
        // followed by a step separator. A chain without a locator relates to nothing.
        private static bool TestRelated(UiaChainModel source, UiaChainModel target)
        {
            var sourceLocator = source?.Locator ?? string.Empty;
            var targetLocator = target?.Locator ?? string.Empty;
            var isLocated = sourceLocator.Length > 0 && targetLocator.Length > 0;
            if (!isLocated)
            {
                return false;
            }

            // The same locator, or one path continuing the other with a new step, is the same element or a relative.
            var isSame = sourceLocator.Equals(targetLocator, StringComparison.Ordinal);
            var isTargetInside = targetLocator.StartsWith(sourceLocator + "/", StringComparison.Ordinal);
            var isSourceInside = sourceLocator.StartsWith(targetLocator + "/", StringComparison.Ordinal);
            return isSame || isTargetInside || isSourceInside;
        }
        #endregion
    }
}
