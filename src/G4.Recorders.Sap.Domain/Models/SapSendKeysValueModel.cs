namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the value of a <c>nativeSap</c> <c>SendKeys</c> event, with the Chromium recorder's members.
    /// </summary>
    /// <remarks>
    /// Password text is never sent: it arrives as <c>***</c> with <see cref="IsRedacted"/> set, as in the Chromium
    /// recorder. Other text is sent as SAP reported it.
    /// </remarks>
    public class SapSendKeysValueModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the field text SAP reported, or <c>***</c> when redacted.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the text was redacted; <c>null</c>, and omitted, otherwise.
        /// </summary>
        public bool? IsRedacted { get; set; }
        #endregion
    }
}
