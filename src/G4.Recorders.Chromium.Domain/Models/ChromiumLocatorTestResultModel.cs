using System.Collections.Generic;

namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Reports the outcome of testing one locator against the live browser to a server caller.
    /// </summary>
    /// <remarks>
    /// The result echoes the tested locator beside the extension's answer and leaves out the transport-only
    /// correlation and error members of <see cref="ChromiumLocatorTestResponseModel"/>.
    /// </remarks>
    public class ChromiumLocatorTestResultModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the total number of elements the locator matched across all frames.
        /// </summary>
        /// <remarks>
        /// The total can exceed the number of entries in <see cref="Matches"/>, because the extension caps the
        /// described matches to the first few.
        /// </remarks>
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the first matched elements; empty when the locator matched nothing.
        /// </summary>
        public List<ChromiumLocatorMatchModel> Matches { get; set; } = [];

        /// <summary>
        /// Gets or sets the locator strategy that was tested, in its exact G4 casing.
        /// </summary>
        public string Using { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the locator expression that was tested.
        /// </summary>
        public string Value { get; set; } = string.Empty;
        #endregion
    }
}
