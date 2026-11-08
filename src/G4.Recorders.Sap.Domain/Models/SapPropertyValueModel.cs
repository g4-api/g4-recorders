namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the value of a <c>nativeSap</c> <c>SetSapProperty</c> event: a scripting property assignment.
    /// </summary>
    /// <remarks>
    /// The members match the body of the SAP driver's <c>sap/element/{e}/property</c> route, so a recorded assignment
    /// replays as one request. Values on password fields are redacted to <c>***</c> with <see cref="IsRedacted"/> set.
    /// </remarks>
    public class SapPropertyValueModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the SAP property name, such as <c>selectedRows</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the assigned value, or <c>***</c> when redacted.
        /// </summary>
        public object Value { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the value was redacted; <c>null</c>, and omitted, otherwise.
        /// </summary>
        public bool? IsRedacted { get; set; }
        #endregion
    }
}
