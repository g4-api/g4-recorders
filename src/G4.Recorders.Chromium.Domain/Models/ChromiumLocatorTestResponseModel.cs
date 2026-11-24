using System.Collections.Generic;

namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Carries a correlated locator test result from the recorder extension back to a waiting domain request.
    /// </summary>
    public class ChromiumLocatorTestResponseModel
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
        /// Gets or sets a transport, browser-context, or locator evaluation failure reported by the extension.
        /// </summary>
        /// <remarks>
        /// Null or blank when the test ran; any other text fails the waiting request with that message.
        /// </remarks>
        public string Error { get; set; }

        /// <summary>
        /// Gets or sets the first matched elements, capped by the extension.
        /// </summary>
        public List<ChromiumLocatorMatchModel> Matches { get; set; } = [];

        /// <summary>
        /// Gets or sets the request correlation identifier supplied by the domain.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;
        #endregion
    }
}
