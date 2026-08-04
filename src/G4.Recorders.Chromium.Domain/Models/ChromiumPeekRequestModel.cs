using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Carries one correlated normalized peek request from the Chromium domain to the recorder extension.
    /// </summary>
    public class ChromiumPeekRequestModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the lookup mode already resolved from optional API inputs.
        /// </summary>
        public RecorderPeekMode Mode { get; set; }

        /// <summary>
        /// Gets or sets the correlation identifier returned unchanged by the extension.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the viewport-relative horizontal coordinate used by coordinate mode.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the viewport-relative vertical coordinate used by coordinate mode.
        /// </summary>
        public int Y { get; set; }
        #endregion
    }
}
