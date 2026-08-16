using System.Collections.Generic;

namespace G4.Recorders.Common.Domain.Models.Mcp
{
    /// <summary>
    /// Represents the <c>result</c> payload of a <c>tools/list</c> response.
    /// </summary>
    public class McpToolsListResultModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the recorder's fixed tool catalog.
        /// </summary>
        public IEnumerable<McpToolDefinitionModel> Tools { get; set; } = [];
        #endregion
    }
}
