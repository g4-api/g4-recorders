namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Describes one live DOM element matched by a locator test, together with the frame that owns it.
    /// </summary>
    /// <remarks>
    /// The extension evaluates a locator in every frame of the page, so the frame identity is part of each match
    /// and lets a caller tell equal chains in different frames apart.
    /// </remarks>
    public class ChromiumLocatorMatchModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the DOM ancestor chain of the matched element, in the same shape a peek returns.
        /// </summary>
        public ChromiumChainModel Chain { get; set; }

        /// <summary>
        /// Gets or sets the browser frame identifier that owns the matched element; zero is the top frame.
        /// </summary>
        public int FrameId { get; set; }

        /// <summary>
        /// Gets or sets the document URL of the frame that owns the matched element.
        /// </summary>
        public string FrameUrl { get; set; }
        #endregion
    }
}
