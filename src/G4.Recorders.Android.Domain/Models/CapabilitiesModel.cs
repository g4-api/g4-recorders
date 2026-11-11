namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Represents the W3C capabilities object of the Android driver parameters.
    /// </summary>
    /// <remarks>
    /// Only <c>alwaysMatch</c> is read, as on the Chromium recorder; <c>firstMatch</c> is accepted and ignored.
    /// </remarks>
    public class CapabilitiesModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the capabilities every candidate session must match.
        /// </summary>
        public AlwaysMatchModel AlwaysMatch { get; set; }
        #endregion
    }
}
