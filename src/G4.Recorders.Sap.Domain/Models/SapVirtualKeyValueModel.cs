namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the value of a <c>nativeSap</c> <c>SendSapVirtualKey</c> event: the SAP virtual key sent to a window.
    /// </summary>
    public class SapVirtualKeyValueModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the SAP virtual key number, such as <c>0</c> for Enter or <c>3</c> for Back.
        /// </summary>
        public int Key { get; set; }
        #endregion
    }
}
