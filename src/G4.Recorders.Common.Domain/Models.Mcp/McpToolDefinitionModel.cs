using System.Text.Json;

namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents one tool's MCP metadata, as returned by <c>tools/list</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not the official MCP SDK's own <c>Tool</c> type (<c>ModelContextProtocol.Protocol</c>) - this
    /// recorder does not take a dependency on that package, since only this small, stable shape is needed.
    /// </remarks>
    public class McpToolDefinitionModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the tool's qualified name (for example <c>g4.GetScreenshot</c>), used in <c>tools/call</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a short, human-readable title for the tool.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets a description of what the tool does and when to use it.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the JSON Schema describing the tool's <c>arguments</c> object for <c>tools/call</c>.
        /// </summary>
        public JsonElement InputSchema { get; set; }
        #endregion
    }
}
