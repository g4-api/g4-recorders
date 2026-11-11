using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Common.Domain.Models;

using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain
{
    /// <summary>
    /// Prepares an Android device for recording and restores it afterwards, on behalf of a client such as the G4 Hub.
    /// </summary>
    /// <remarks>
    /// The launcher is the Android counterpart of the Chromium recorder's browser launcher: start installs the recorder
    /// app, points it at this host, enables its accessibility service and starts the application under test; stop asks
    /// the app to close and undoes every device change. All device work runs through adb on the recorder host.
    /// </remarks>
    public interface IAndroidRecorderLauncher
    {
        #region *** Methods      ***
        /// <summary>
        /// Captures the screen of the device being recorded, or only its size.
        /// </summary>
        /// <param name="metricsOnly">When <c>true</c>, returns the screen size without capturing pixels.</param>
        /// <returns>The PNG screenshot as Base64 with the device screen size; the origin is always zero.</returns>
        Task<RecorderScreenshotModel> GetScreenshotAsync(bool metricsOnly);

        /// <summary>
        /// Prepares the device and starts recording.
        /// </summary>
        /// <param name="driverParameters">The client's driver parameters; the <c>appium:</c> capabilities are read.</param>
        /// <returns>
        /// The process id of the application under test, or a negative session id when no application was started.
        /// Pass it to <see cref="StopAsync"/>.
        /// </returns>
        Task<int> StartAsync(DriverParametersModel driverParameters);

        /// <summary>
        /// Stops a recording started by <see cref="StartAsync"/> and restores the device. Never throws.
        /// </summary>
        /// <param name="processId">The id returned by <see cref="StartAsync"/>.</param>
        /// <returns><c>true</c> when the id belonged to a recording this launcher started; otherwise, <c>false</c>.</returns>
        Task<bool> StopAsync(int processId);
        #endregion
    }
}
