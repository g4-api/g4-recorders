namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Carries one captured input event from the hook thread to the scripting thread.
    /// </summary>
    /// <remarks>
    /// The hook thread fills every field it can read cheaply, including the key text, the modifiers, and the window
    /// handles, because the scripting thread runs later, when the keyboard and window state may have changed.
    /// </remarks>
    internal sealed class SapInputRecord
    {
        #region *** Constants    ***
        /// <summary>
        /// The window message for a left button press.
        /// </summary>
        internal const int LeftButtonDown = 0x0201;

        /// <summary>
        /// The window message for a left button release.
        /// </summary>
        internal const int LeftButtonUp = 0x0202;

        /// <summary>
        /// The window message for a key press.
        /// </summary>
        internal const int KeyDown = 0x0100;

        /// <summary>
        /// The window message for a key release.
        /// </summary>
        internal const int KeyUp = 0x0101;

        /// <summary>
        /// The window message for a middle button press.
        /// </summary>
        internal const int MiddleButtonDown = 0x0207;

        /// <summary>
        /// The window message for a middle button release.
        /// </summary>
        internal const int MiddleButtonUp = 0x0208;

        /// <summary>
        /// The window message for a horizontal wheel turn.
        /// </summary>
        internal const int MouseHorizontalWheel = 0x020E;

        /// <summary>
        /// The window message for a vertical wheel turn.
        /// </summary>
        internal const int MouseWheel = 0x020A;

        /// <summary>
        /// The window message for a right button press.
        /// </summary>
        internal const int RightButtonDown = 0x0204;

        /// <summary>
        /// The window message for a right button release.
        /// </summary>
        internal const int RightButtonUp = 0x0205;

        /// <summary>
        /// The window message for a system key press, such as Alt combinations.
        /// </summary>
        internal const int SystemKeyDown = 0x0104;

        /// <summary>
        /// The window message for a system key release.
        /// </summary>
        internal const int SystemKeyUp = 0x0105;
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the top-level handle of the foreground window when the event was captured.
        /// </summary>
        internal long ForegroundWindow { get; set; }

        /// <summary>
        /// Gets or sets the recording generation active when the event was captured.
        /// </summary>
        internal long Generation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the key is an extended key, such as the right Ctrl key.
        /// </summary>
        internal bool IsExtended { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the event is keyboard input; otherwise it is mouse input.
        /// </summary>
        internal bool IsKeyboard { get; set; }

        /// <summary>
        /// Gets or sets the key text resolved at capture time: the typed glyph, or the key name for other keys.
        /// </summary>
        internal string KeyText { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the window message identifier.
        /// </summary>
        internal int Message { get; set; }

        /// <summary>
        /// Gets or sets the shortcut modifiers held with the key, such as <c>Ctrl</c>; empty for typed text.
        /// </summary>
        internal string[] Modifiers { get; set; } = [];

        /// <summary>
        /// Gets or sets the top-level handle of the window under the pointer, for mouse events.
        /// </summary>
        internal long RootWindow { get; set; }

        /// <summary>
        /// Gets or sets the hardware scan code, for keyboard events.
        /// </summary>
        internal uint ScanCode { get; set; }

        /// <summary>
        /// Gets or sets the capture time in Unix epoch milliseconds.
        /// </summary>
        internal long Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the virtual-key code, for keyboard events.
        /// </summary>
        internal uint VirtualKey { get; set; }

        /// <summary>
        /// Gets or sets the signed wheel delta, in multiples of 120 per notch, for wheel events.
        /// </summary>
        internal int WheelDelta { get; set; }

        /// <summary>
        /// Gets or sets the horizontal pointer coordinate in physical pixels, for mouse events.
        /// </summary>
        internal int X { get; set; }

        /// <summary>
        /// Gets or sets the vertical pointer coordinate in physical pixels, for mouse events.
        /// </summary>
        internal int Y { get; set; }
        #endregion
    }
}
