namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the value of a <c>user32</c> keyboard event, with the same members as the UIA recorder's keyboard value.
    /// </summary>
    /// <remarks>
    /// Members are declared in the UIA recorder's order, so both serialize identically. In password fields, typed
    /// characters arrive masked: <see cref="Key"/> is <c>*</c> and both codes are zero.
    /// </remarks>
    public class SapKeyboardValueModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the hardware scan code; zero when masked.
        /// </summary>
        public uint ScanCode { get; set; }

        /// <summary>
        /// Gets or sets the virtual-key code; zero when masked.
        /// </summary>
        public uint VirtualKey { get; set; }

        /// <summary>
        /// Gets or sets the key text: the typed glyph, or the key name, such as <c>Enter</c>.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the shortcut modifiers, such as <c>Ctrl</c>; empty for typed text.
        /// </summary>
        public string[] Modifiers { get; set; } = [];

        /// <summary>
        /// Gets or sets how the target was resolved, such as <c>CapturedSnapshot</c> or <c>PairedRelease</c>.
        /// </summary>
        public string TargetSource { get; set; }
        #endregion
    }
}
