using G4.Recorders.Common.Domain.Models;

using UIAutomationClient;

namespace G4.Recorders.Uia.Domain.Models
{
    /// <summary>
    /// Represents a single UI Automation (UIA) node within a recorded chain.
    /// Wraps an <see cref="IUIAutomationElement"/> as the underlying UI element.
    /// </summary>
    /// <remarks>
    /// This class is a strongly-typed alias for <see cref="RecorderNodeModel{TElement}"/>,
    /// allowing the recorder pipeline to work specifically with UIA elements.
    /// Extend this class when UIA nodes require additional metadata, properties,
    /// or domain-specific behavior.
    /// </remarks>
    public class UiaNodeModel : RecorderNodeModel<IUIAutomationElement>
    {
        #region *** Properties   ***

        /// <summary>
        /// Gets or sets the 1-based index of this element among all UIA siblings under the same parent.
        /// </summary>
        public int SiblingIndex { get; set; } = 1;

        /// <summary>
        /// Gets or sets the 1-based index of this element among UIA siblings that share the same control type.
        /// </summary>
        public int SiblingIndexOfSameControlType { get; set; } = 1;

        /// <summary>
        /// Gets or sets the resolved value of the primary identity attribute (the first configured attribute).
        /// </summary>
        internal string Attribute1Value { get; set; }

        /// <summary>
        /// Gets or sets the number of same-control-type siblings that also match the primary identity attribute.
        /// </summary>
        internal int Attribute1MatchCount { get; set; }

        /// <summary>
        /// Gets or sets the 1-based position within siblings matching the control type and primary identity attribute.
        /// </summary>
        internal int Attribute1MatchIndex { get; set; } = 1;

        /// <summary>
        /// Gets or sets the resolved value of the secondary identity attribute (the second configured attribute).
        /// </summary>
        internal string Attribute2Value { get; set; }

        /// <summary>
        /// Gets or sets the number of same-control-type siblings that also match the secondary identity attribute.
        /// </summary>
        internal int Attribute2MatchCount { get; set; }

        /// <summary>
        /// Gets or sets the 1-based position within siblings matching the control type and secondary identity attribute.
        /// </summary>
        internal int Attribute2MatchIndex { get; set; } = 1;

        /// <summary>
        /// Gets or sets the number of same-control-type siblings matching both configured identity attributes.
        /// </summary>
        internal int IdentityMatchCount { get; set; }

        /// <summary>
        /// Gets or sets the 1-based position within siblings matching both configured identity attributes.
        /// </summary>
        internal int IdentityMatchIndex { get; set; } = 1;

        #endregion
    }
}
