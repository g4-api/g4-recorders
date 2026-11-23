namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Carries one correlated locator test from the Chromium domain to the recorder extension.
    /// </summary>
    /// <remarks>
    /// The extension evaluates the locator against every frame of the live page and answers with a
    /// <see cref="ChromiumLocatorTestResponseModel"/> that returns <see cref="RequestId"/> unchanged.
    /// </remarks>
    public class ChromiumLocatorTestRequestModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the correlation identifier returned unchanged by the extension.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the locator strategy in its exact G4 casing, such as <c>Xpath</c> or <c>CssSelector</c>.
        /// </summary>
        public string Using { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the locator expression the extension evaluates with the selected strategy.
        /// </summary>
        public string Value { get; set; } = string.Empty;
        #endregion
    }
}
