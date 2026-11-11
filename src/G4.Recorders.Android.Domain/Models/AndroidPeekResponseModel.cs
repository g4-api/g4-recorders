namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Carries the Android recorder app's answer to one correlated peek request.
    /// </summary>
    public class AndroidPeekResponseModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the resolved chain; null when no node matched, which the repository returns as an empty chain.
        /// </summary>
        public AndroidChainModel Chain { get; set; }

        /// <summary>
        /// Gets or sets the app-side failure; null or empty when the lookup ran.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Gets or sets the correlation identifier of the request being answered.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;
        #endregion
    }
}
