namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Represents the driver parameters a client sends to start the Android recorder.
    /// </summary>
    /// <remarks>
    /// Clients pass the same <c>driverParameters</c> object they replay with on the G4 <c>AndroidDriver</c>. Only the
    /// capabilities are read; <c>driver</c>, <c>driverBinaries</c> and <c>firstMatch</c> are accepted and ignored.
    /// </remarks>
    public class DriverParametersModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the W3C capabilities that name the device and the application under test.
        /// </summary>
        public CapabilitiesModel Capabilities { get; set; }
        #endregion
    }
}
