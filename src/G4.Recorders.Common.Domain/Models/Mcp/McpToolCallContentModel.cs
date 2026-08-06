namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents one plain-text content block of a <c>tools/call</c> result.
    /// </summary>
    public class McpToolCallContentModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the content block type. Always <c>"text"</c> for this recorder's tool results.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the serialized JSON text of the tool's result.
        /// </summary>
        public string Text { get; set; }
        #endregion
    }
}
