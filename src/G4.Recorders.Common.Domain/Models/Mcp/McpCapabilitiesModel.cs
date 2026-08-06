namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Describes the server's capabilities, as returned by <c>initialize</c>.
    /// </summary>
    public class McpCapabilitiesModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the server's tool-related capabilities.
        /// </summary>
        public McpToolsCapabilityModel Tools { get; set; }
        #endregion
    }
}
