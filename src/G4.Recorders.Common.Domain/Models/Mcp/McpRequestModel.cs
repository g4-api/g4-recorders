using System.Text.Json;
using System.Text.Json.Serialization;

namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents a JSON-RPC request sent to a recorder's MCP endpoint.
    /// </summary>
    /// <remarks>
    /// Declared locally rather than reused from <c>G4.Models</c> (the shared G4.Services envelope) so the
    /// recorder does not take on that package's dependency chain just for this small, generic JSON-RPC shape.
    /// </remarks>
    public class McpRequestModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the request identifier used to correlate a response with its request.
        /// </summary>
        public object Id { get; set; }

        /// <summary>
        /// Gets or sets the JSON-RPC protocol version. Defaults to <c>2.0</c>.
        /// </summary>
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>
        /// Gets or sets the name of the JSON-RPC method to invoke (for example
        /// <c>initialize</c>, <c>tools/list</c>, or <c>tools/call</c>).
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// Gets or sets the raw parameters payload for the JSON-RPC method.
        /// </summary>
        /// <remarks>
        /// Mapped to the JSON field named <c>params</c>, since <c>params</c> is a reserved word in C#.
        /// </remarks>
        [JsonPropertyName(name: "params")]
        public JsonElement Parameters { get; set; }
        #endregion
    }
}
