namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents a JSON-RPC error object returned when a method or tool call fails.
    /// </summary>
    public class McpErrorModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the JSON-RPC error code (for example <c>-32601</c> for "method not found").
        /// </summary>
        public int Code { get; set; }

        /// <summary>
        /// Gets or sets a human-readable description of the failure.
        /// </summary>
        public string Message { get; set; }
        #endregion
    }
}
