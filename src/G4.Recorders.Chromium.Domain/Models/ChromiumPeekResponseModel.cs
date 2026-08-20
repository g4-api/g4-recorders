namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Carries a correlated DOM-chain result from the recorder extension back to a waiting domain request.
    /// </summary>
    public class ChromiumPeekResponseModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the resolved chain, or null when the accessible page contains no matching element.
        /// </summary>
        public ChromiumChainModel Chain { get; set; }

        /// <summary>
        /// Gets or sets a transport or browser-context failure reported by the extension.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Gets or sets the request correlation identifier supplied by the domain.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;
        #endregion
    }
}
