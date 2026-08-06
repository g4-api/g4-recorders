namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Identifies the MCP server implementation, as returned by <c>initialize</c>.
    /// </summary>
    public class McpServerInfoModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the server's name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the server's version.
        /// </summary>
        public string Version { get; set; }
        #endregion
    }
}
