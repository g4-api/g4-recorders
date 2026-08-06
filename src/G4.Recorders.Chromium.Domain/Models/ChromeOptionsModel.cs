namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Represents the Chromium-specific options supplied by the client under the W3C vendor
    /// extension key "goog:chromeOptions". The binary path is optional because the recorder can resolve Chrome
    /// from its own G4 sandbox; launch arguments remain caller-controlled recorder-host values.
    /// </summary>
    public class ChromeOptionsModel
    {
        /// <summary>
        /// The optional full path, on the recorder host, to the Chromium/Chrome executable to launch.
        /// When omitted, the recorder resolves browsers/chrome beneath its own G4 sandbox.
        /// </summary>
        public string Binary { get; set; }

        /// <summary>
        /// Extra command-line arguments appended after the peek base flags.
        /// </summary>
        public string[] Args { get; set; }
    }
}
