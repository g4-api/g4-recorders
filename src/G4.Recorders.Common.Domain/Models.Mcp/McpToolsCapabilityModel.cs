namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Describes the server's tool-related capabilities, as returned by <c>initialize</c>.
    /// </summary>
    public class McpToolsCapabilityModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets a value indicating whether the server can notify callers when its tool list changes.
        /// </summary>
        /// <remarks>
        /// Always <c>true</c> here for shape-compatibility with MCP clients, even though this recorder's 5-tool
        /// catalog is fixed and never actually changes at runtime.
        /// </remarks>
        public bool ListChanged { get; set; }
        #endregion
    }
}
