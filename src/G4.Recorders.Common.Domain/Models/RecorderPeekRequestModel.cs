namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Carries a normalized peek target whose mode has already applied coordinate and focus precedence.
    /// </summary>
    public class RecorderPeekRequestModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the resolution mode selected from the caller's optional inputs.
        /// </summary>
        public RecorderPeekMode Mode { get; set; }

        /// <summary>
        /// Gets or sets the horizontal coordinate used by coordinate mode.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the vertical coordinate used by coordinate mode.
        /// </summary>
        public int Y { get; set; }
        #endregion
    }
}
