using System.Collections.Generic;

namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents the <c>result</c> payload of a <c>tools/call</c> response.
    /// </summary>
    /// <remarks>
    /// Carries model-facing MCP content blocks plus optional structured data. Most recorder tools provide a text
    /// rendering of the structured result; screenshot calls provide image content while keeping only compact
    /// coordinate-space metadata in <see cref="StructuredContent"/>.
    /// </remarks>
    public class McpToolCallResultModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the content blocks rendered by MCP clients, including text and images.
        /// </summary>
        public IEnumerable<McpToolCallContentModel> Content { get; set; } = [];

        /// <summary>
        /// Gets or sets the tool's raw result object, for programmatic callers.
        /// </summary>
        public object StructuredContent { get; set; }
        #endregion
    }
}
