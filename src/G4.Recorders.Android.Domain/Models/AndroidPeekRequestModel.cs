using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Carries one correlated peek request from the host to the Android recorder app.
    /// </summary>
    public class AndroidPeekRequestModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the lookup mode already resolved from the optional API inputs.
        /// </summary>
        public RecorderPeekMode Mode { get; set; }

        /// <summary>
        /// Gets or sets the correlation identifier the app returns unchanged.
        /// </summary>
        public string RequestId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the horizontal screen coordinate, in device pixels, used by coordinate mode.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the vertical screen coordinate, in device pixels, used by coordinate mode.
        /// </summary>
        public int Y { get; set; }
        #endregion
    }
}
