namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the value of a <c>user32</c> wheel event, with the same members as the UIA recorder's wheel value.
    /// </summary>
    /// <remarks>
    /// Members are declared in the UIA recorder's order, so both serialize identically.
    /// </remarks>
    public class SapWheelValueModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the number of notches turned, at least one.
        /// </summary>
        public int Notches { get; set; }

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
