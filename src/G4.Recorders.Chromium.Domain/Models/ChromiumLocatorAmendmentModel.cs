using System.Collections.Generic;

namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Carries a correction the recorder extension makes to a locator it already sent with a recording event.
    /// </summary>
    /// <remarks>
    /// The extension sends an event first and may later find a stronger locator for the same element. The
    /// amendment names the earlier locator through <see cref="LocatorId"/> and is relayed to consumers as its
    /// own message, so the recording event contract stays unchanged.
    /// </remarks>
    public class ChromiumLocatorAmendmentModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the corrected primary locator expression.
        /// </summary>
        public string Locator { get; set; }

        /// <summary>
        /// Gets or sets the identifier of the previously sent locator this amendment corrects.
        /// </summary>
        /// <remarks>
        /// Required. An amendment with a null or blank identifier cannot be matched by a consumer and is not relayed.
        /// </remarks>
        public string LocatorId { get; set; }

        /// <summary>
        /// Gets or sets the corrected alternative locators as free-form JSON objects.
        /// </summary>
        /// <remarks>
        /// Each entry is relayed exactly as the extension sent it, for example with <c>using</c>, <c>value</c>,
        /// <c>strategy</c>, <c>score</c> and <c>verdict</c> members; the recorder host never interprets them.
        /// Null stays null, so an amendment without alternatives omits the member instead of sending an empty list.
        /// </remarks>
        public List<Dictionary<string, object>> Locators { get; set; }

        /// <summary>
        /// Gets or sets the strength grade the extension assigned to the corrected primary locator.
        /// </summary>
        public string LocatorStrength { get; set; }

        /// <summary>
        /// Gets or sets the extension's explanation of why the earlier locator was corrected.
        /// </summary>
        public string Reason { get; set; }

        /// <summary>
        /// Gets or sets the timestamp (in Unix epoch milliseconds) when the extension produced the amendment.
        /// </summary>
        public long Timestamp { get; set; }
        #endregion
    }
}
