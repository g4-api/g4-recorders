namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Represents a captured region of the virtual desktop and the coordinate-space metadata needed to translate
    /// an image pixel into an absolute physical screen coordinate.
    /// </summary>
    /// <remarks>
    /// Carries the image as inline base64 rather than a filesystem path, because a path on the recorder's own
    /// disk is meaningless to a remote caller driving the recorder over the network.
    /// </remarks>
    public class RecorderScreenshotModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the captured image encoded as a base64 PNG.
        /// </summary>
        /// <remarks>
        /// Null when the request only asked for coordinate-space metrics, so the caller can distinguish a
        /// metrics-only response from an unexpectedly empty capture.
        /// </remarks>
        public string ImageBase64 { get; set; }

        /// <summary>
        /// Gets or sets the virtual-desktop height in physical pixels.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Gets or sets the virtual-desktop horizontal origin in physical pixels, relative to the primary monitor.
        /// </summary>
        /// <remarks>
        /// Negative when a monitor is positioned to the left of the primary monitor.
        /// </remarks>
        public int OriginX { get; set; }

        /// <summary>
        /// Gets or sets the virtual-desktop vertical origin in physical pixels, relative to the primary monitor.
        /// </summary>
        /// <remarks>
        /// Negative when a monitor is positioned above the primary monitor.
        /// </remarks>
        public int OriginY { get; set; }

        /// <summary>
        /// Gets or sets the virtual-desktop width in physical pixels.
        /// </summary>
        public int Width { get; set; }
        #endregion
    }
}
