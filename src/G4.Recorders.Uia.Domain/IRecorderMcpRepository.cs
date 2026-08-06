using G4.Recorders.Common.Domain.Models.Mcp;

using System.Text.Json;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Dispatches MCP JSON-RPC requests against the recorder's fixed tool catalog.
    /// </summary>
    public interface IRecorderMcpRepository
    {
        #region *** Methods      ***
        /// <summary>
        /// Handles a <c>tools/call</c> request by dispatching to the matching repository method.
        /// </summary>
        /// <param name="parameters">The JSON-RPC <c>params</c> payload, shaped as <c>{name, arguments}</c>.</param>
        /// <param name="id">The request identifier to echo back in the response.</param>
        /// <returns>The JSON-RPC response, either a successful result or a structured error.</returns>
        McpResponseModel CallTool(JsonElement parameters, object id);

        /// <summary>
        /// Handles a <c>tools/list</c> request.
        /// </summary>
        /// <param name="id">The request identifier to echo back in the response.</param>
        /// <returns>The JSON-RPC response containing the fixed tool catalog.</returns>
        McpResponseModel FindTools(object id);

        /// <summary>
        /// Handles an <c>initialize</c> request.
        /// </summary>
        /// <param name="id">The request identifier to echo back in the response.</param>
        /// <returns>The JSON-RPC response describing this server's protocol version, capabilities, and identity.</returns>
        McpResponseModel Initialize(object id);
        #endregion
    }
}
