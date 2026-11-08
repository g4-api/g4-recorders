namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the value of a <c>user32</c> mouse button event: the pointer position, as in the UIA recorder.
    /// </summary>
    public class SapPointerValueModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the horizontal pointer coordinate in physical pixels.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the vertical pointer coordinate in physical pixels.
        /// </summary>
        public int Y { get; set; }
        #endregion
    }
}
