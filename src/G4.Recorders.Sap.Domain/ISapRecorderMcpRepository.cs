using G4.Recorders.Common.Domain.Models.Mcp;

using System.Text.Json;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Dispatches MCP JSON-RPC requests for the SAP recorder's fixed tool catalog.
    /// </summary>
    /// <remarks>
    /// The catalog matches the UIA recorder's: <c>g4.GetScreenshot</c>, <c>g4.ConvertScreenshotPoint</c>,
    /// <c>g4.MovePointer</c>, <c>g4.ResolveGroundedElement</c>, and <c>g4.SetWindowFocus</c>, so MCP clients use either
    /// recorder the same way; only the grounded chain describes SAP GUI controls instead of UI Automation elements.
    /// </remarks>
    public interface ISapRecorderMcpRepository
    {
        #region *** Methods      ***
        /// <summary>
        /// Runs one tool from a <c>tools/call</c> request.
        /// </summary>
        /// <param name="parameters">The request parameters: <c>name</c> and <c>arguments</c>.</param>
        /// <param name="id">The JSON-RPC request identifier.</param>
        /// <returns>The tool result, or a JSON-RPC error for an unknown tool.</returns>
        McpResponseModel CallTool(JsonElement parameters, object id);

        /// <summary>
        /// Lists the tool catalog for a <c>tools/list</c> request.
        /// </summary>
        /// <param name="id">The JSON-RPC request identifier.</param>
        /// <returns>The tool definitions.</returns>
        McpResponseModel FindTools(object id);

        /// <summary>
        /// Answers an <c>initialize</c> request with the protocol version, capabilities, and server identity.
        /// </summary>
        /// <param name="id">The JSON-RPC request identifier.</param>
        /// <returns>The initialize result.</returns>
        McpResponseModel Initialize(object id);
        #endregion
    }
}
