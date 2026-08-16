using System.Collections.Generic;

namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Represents a chain of UI Automation nodes, starting from a trigger element
    /// and climbing upward through its ancestors until the top window.
    /// </summary>
    public class ChainModel<T>
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets a locator string for the trigger element.
        /// </summary>
        public string Locator { get; set; } = string.Empty;

        /// <summary>
        /// The ordered sequence of nodes, beginning with the trigger element
        /// and continuing upward through its parent elements.
        /// </summary>
        public List<T> Path { get; set; } = [];

        /// <summary>
        /// The screen coordinates of the trigger element (the last element in the chain).
        /// </summary>
        public RecorderPointModel Point { get; set; } = null;

        /// <summary>
        /// The mouse pointer's pixel offset from the trigger element's top-left corner.
        /// </summary>
        /// <remarks>
        /// Null unless a pointer-anchored operation computes it: grounding (AI tools) and manual mouse recording both
        /// populate it here, so offset lives in one place regardless of which path produced the chain. Focus/keyboard
        /// chains have no pointer and leave it null.
        /// </remarks>
        public RecorderOffsetModel MouseOffset { get; set; } = null;

        /// <summary>
        /// The top-level window node in the chain,
        /// representing the ancestor closest to the desktop root.
        /// </summary>
        public T TopWindow { get; set; } = default;

        /// <summary>
        /// The action or event that caused this chain to be recorded, if applicable.
        /// </summary>
        public string Trigger { get; set; } = string.Empty;
        #endregion
    }
}
