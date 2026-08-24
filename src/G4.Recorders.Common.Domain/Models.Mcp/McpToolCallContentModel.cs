namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents one content block of a <c>tools/call</c> result.
    /// </summary>
    public class McpToolCallContentModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the content block type, such as <c>"text"</c> or <c>"image"</c>.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the base64-encoded payload for binary content such as an image.
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// Gets or sets the media type for binary content.
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// Gets or sets the text payload for a text content block.
        /// </summary>
        public string Text { get; set; }
        #endregion
    }
}
