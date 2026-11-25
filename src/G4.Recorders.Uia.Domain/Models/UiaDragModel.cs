namespace G4.Recorders.Uia.Domain.Models
{
    /// <summary>
    /// Describes a left-button press and release that moved past the drag threshold, as the drag facts the UIA
    /// recorder adds to the value of a <c>Left Up</c> event.
    /// </summary>
    /// <remarks>
    /// The event keeps its name and its chain, which is the press-time chain of the dragged element, so a consumer
    /// that does not know drags still reads a click. The Hub reads these facts to build an
    /// <c>InvokeUser32DragAndDrop</c> rule. Offsets are measured from the top-left corner of each element and are
    /// replayed with the <c>TopLeft</c> alignment, the alignment the UIA driver applies offsets to, as for the
    /// recorded click offset.
    /// </remarks>
    public sealed class UiaDragModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the horizontal distance, in screen pixels, from the dragged element's left edge to the press.
        /// </summary>
        /// <remarks>
        /// It is <see langword="null"/> when UI Automation reported no usable bounds for the element; the drag then
        /// starts at the element center.
        /// </remarks>
        public int? SourceOffsetX { get; set; }

        /// <summary>
        /// Gets or sets the vertical distance, in screen pixels, from the dragged element's top edge to the press.
        /// </summary>
        /// <remarks>
        /// It is <see langword="null"/> when UI Automation reported no usable bounds for the element; the drag then
        /// starts at the element center.
        /// </remarks>
        public int? SourceOffsetY { get; set; }

        /// <summary>
        /// Gets or sets the horizontal screen coordinate of the press.
        /// </summary>
        public int StartX { get; set; }

        /// <summary>
        /// Gets or sets the vertical screen coordinate of the press.
        /// </summary>
        public int StartY { get; set; }

        /// <summary>
        /// Gets or sets the chain of the element the drag was released over.
        /// </summary>
        /// <remarks>
        /// It is <see langword="null"/> when the release landed on the dragged element, on one of its ancestors or
        /// descendants, or on nothing UI Automation could resolve. The drag is then replayed by its distance from
        /// the press point to the release point.
        /// </remarks>
        public UiaChainModel Target { get; set; }

        /// <summary>
        /// Gets or sets the horizontal distance, in screen pixels, from the drop element's left edge to the release.
        /// </summary>
        /// <remarks>
        /// It is <see langword="null"/> when there is no <see cref="Target"/> or it has no usable bounds; the drop
        /// then lands on the element center.
        /// </remarks>
        public int? TargetOffsetX { get; set; }

        /// <summary>
        /// Gets or sets the vertical distance, in screen pixels, from the drop element's top edge to the release.
        /// </summary>
        /// <remarks>
        /// It is <see langword="null"/> when there is no <see cref="Target"/> or it has no usable bounds; the drop
        /// then lands on the element center.
        /// </remarks>
        public int? TargetOffsetY { get; set; }
        #endregion
    }
}
