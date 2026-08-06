namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents the <c>result</c> payload of an <c>initialize</c> response.
    /// </summary>
    public class McpInitializeResultModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the MCP protocol version this server implements.
        /// </summary>
        public string ProtocolVersion { get; set; }

        /// <summary>
        /// Gets or sets the server's declared capabilities.
        /// </summary>
        public McpCapabilitiesModel Capabilities { get; set; }

        /// <summary>
        /// Gets or sets the server's identity.
        /// </summary>
        public McpServerInfoModel ServerInfo { get; set; }
        #endregion
    }
}
