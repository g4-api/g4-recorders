namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Carries the locator a REST caller asks the recorder to test against the live browser.
    /// </summary>
    public class ChromiumLocatorTestModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the locator strategy, matched without regard to casing.
        /// </summary>
        /// <remarks>
        /// Optional. A null or blank strategy selects <c>Xpath</c>; the other accepted strategies are
        /// <c>CssSelector</c>, <c>LinkText</c>, <c>PartialLinkText</c> and <c>TagName</c>.
        /// </remarks>
        public string Using { get; set; }

        /// <summary>
        /// Gets or sets the locator expression to evaluate with the selected strategy.
        /// </summary>
        /// <remarks>
        /// Required. A null or blank expression rejects the request before it reaches the extension.
        /// </remarks>
        public string Value { get; set; }
        #endregion
    }
}
