using System.Collections.Generic;

namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents the <c>result</c> payload of a <c>tools/call</c> response.
    /// </summary>
    /// <remarks>
    /// Carries the tool's result twice, matching the MCP convention: <see cref="Content"/> is the text form MCP
    /// clients render directly, while <see cref="StructuredContent"/> is the same result as a raw object, for
    /// callers (like this recorder's own client-side skills) that parse the result programmatically instead.
    /// </remarks>
    public class McpToolCallResultModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the plain-text content blocks rendered by MCP clients.
        /// </summary>
        public IEnumerable<McpToolCallContentModel> Content { get; set; } = [];

        /// <summary>
        /// Gets or sets the tool's raw result object, for programmatic callers.
        /// </summary>
        public object StructuredContent { get; set; }
        #endregion
    }
}
