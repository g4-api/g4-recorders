namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents a JSON-RPC response envelope returned by a recorder's MCP endpoint.
    /// </summary>
    /// <remarks>
    /// Used for every response shape (<c>initialize</c>, <c>tools/list</c>, <c>tools/call</c>) - the actual
    /// payload shape lives in <see cref="Result"/>, populated by whichever result model the calling method built.
    /// </remarks>
    public class McpResponseModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the request identifier, echoed back so the caller can match this response to its request.
        /// </summary>
        public object Id { get; set; }

        /// <summary>
        /// Gets or sets the JSON-RPC protocol version. Defaults to <c>2.0</c>.
        /// </summary>
        public string JsonRpc { get; set; } = "2.0";

        /// <summary>
        /// Gets or sets the successful result payload, or <c>null</c> when <see cref="Error"/> is set.
        /// </summary>
        public object Result { get; set; }

        /// <summary>
        /// Gets or sets the error payload when the call failed, or <c>null</c> on success.
        /// </summary>
        public McpErrorModel Error { get; set; }
        #endregion
    }
}
