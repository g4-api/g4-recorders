using System.Text.Json.Serialization;

namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Represents the Appium capabilities the Android recorder launcher reads from <c>alwaysMatch</c>.
    /// </summary>
    /// <remarks>
    /// The names are the Appium UiAutomator2 names a G4 <c>AndroidDriver</c> flow replays with, so one recorder card
    /// serves both recording and replay. Every value is optional; other capabilities are accepted and ignored.
    /// </remarks>
    public class AlwaysMatchModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets an APK path on the recorder host to install before recording; empty skips the install.
        /// </summary>
        [JsonPropertyName("appium:app")]
        public string App { get; set; }

        /// <summary>
        /// Gets or sets the activity to start, absolute or relative to the package, such as <c>.MainActivity</c>.
        /// </summary>
        /// <remarks>
        /// When it is empty and a package is set, the launcher starts the package's launcher activity instead.
        /// </remarks>
        [JsonPropertyName("appium:appActivity")]
        public string AppActivity { get; set; }

        /// <summary>
        /// Gets or sets the package of the application under test; empty records every application.
        /// </summary>
        [JsonPropertyName("appium:appPackage")]
        public string AppPackage { get; set; }

        /// <summary>
        /// Gets or sets the platform name; the recorder records Android only and accepts any value.
        /// </summary>
        public string PlatformName { get; set; }

        /// <summary>
        /// Gets or sets the adb serial of the device; empty selects the only connected device.
        /// </summary>
        [JsonPropertyName("appium:udid")]
        public string Udid { get; set; }
        #endregion
    }
}
