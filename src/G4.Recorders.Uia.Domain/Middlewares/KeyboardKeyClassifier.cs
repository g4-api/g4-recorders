using System;

namespace G4.Recorders.Uia.Domain.Middlewares
{
    /// <summary>
    /// Classifies captured keys as typed text or shortcuts and identifies keys that can move focus.
    /// </summary>
    /// <remarks>
    /// The rules run on the low-level keyboard hook thread with the modifier state captured for the
    /// event, so the classification never depends on how late the background worker processes it.
    /// </remarks>
    internal static class KeyboardKeyClassifier
    {
        #region *** Constants    ***
        // Generic Alt key; pressing and releasing it activates the menu bar.
        private const int VirtualKeyAlt = 0x12;

        // Context-menu key, which opens a popup menu and moves focus into it.
        private const int VirtualKeyApplications = 0x5D;

        // Down arrow, the last key of the Page Up through arrow-key navigation range.
        private const int VirtualKeyDown = 0x28;

        // Enter, which activates default buttons and can close dialogs.
        private const int VirtualKeyEnter = 0x0D;

        // Escape, which cancels dialogs and menus.
        private const int VirtualKeyEscape = 0x1B;

        // F10, which activates the menu bar.
        private const int VirtualKeyF10 = 0x79;

        // F6, which cycles focus between panes.
        private const int VirtualKeyF6 = 0x75;

        // Left Alt key; pressing and releasing it activates the menu bar.
        private const int VirtualKeyLeftAlt = 0xA4;

        // Left Windows key, which opens the Start menu.
        private const int VirtualKeyLeftWindows = 0x5B;

        // Page Up, the first key of the Page Up through arrow-key navigation range.
        private const int VirtualKeyPageUp = 0x21;

        // Right Alt key; pressing and releasing it activates the menu bar.
        private const int VirtualKeyRightAlt = 0xA5;

        // Right Windows key, which opens the Start menu.
        private const int VirtualKeyRightWindows = 0x5C;

        // Tab, which moves focus to the next control.
        private const int VirtualKeyTab = 0x09;
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Resolves the modifiers that turn a key press into a shortcut.
        /// </summary>
        /// <param name="modifierState">The modifier state captured when the key was pressed.</param>
        /// <param name="typedText">The text the key produced with that state; empty for non-text keys.</param>
        /// <returns>
        /// The held modifier names when the press is a shortcut; an empty array when it is typed text.
        /// </returns>
        /// <remarks>
        /// Shift alone and AltGr (Control plus Alt) keep a printable glyph as typed text, because the glyph
        /// already carries their effect. Control or Alt alone and the Windows key always form a shortcut,
        /// and any modifier forms one when the key produced no printable glyph (for example Shift+Tab).
        /// </remarks>
        internal static string[] ResolveShortcutModifiers(KeyboardModifierState modifierState, string typedText)
        {
            // Require the captured state so a missing tracker cannot silently drop every chord.
            ArgumentNullException.ThrowIfNull(argument: modifierState);

            // Treat any non-control character, including a space, as a printable glyph.
            var isGlyph = !string.IsNullOrEmpty(typedText) && !char.IsControl(typedText[0]);

            // Control or Alt alone is a command chord even when the layout still reports a character.
            var isSingleCommandModifier = modifierState.IsControl ^ modifierState.IsAlt;
            var isCommand = modifierState.IsWindows || isSingleCommandModifier;

            // Keep glyph text unchanged when only Shift or AltGr shaped it.
            if (isGlyph && !isCommand)
            {
                return [];
            }

            // Any held modifier turns a command or non-glyph key into a shortcut.
            return modifierState.GetModifiers();
        }

        /// <summary>
        /// Tests whether a key press can move keyboard focus to another element.
        /// </summary>
        /// <param name="virtualKey">The Windows virtual-key code.</param>
        /// <param name="isShortcut">A value indicating whether the press formed a shortcut.</param>
        /// <returns><see langword="true"/> when the press can move focus; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// Typed characters stay in the focused element, so only these keys invalidate an in-flight focus
        /// lookup or permit attributing a key to the element focused after it was processed.
        /// </remarks>
        internal static bool TestFocusMovingKey(uint virtualKey, bool isShortcut)
        {
            // Any shortcut can navigate (for example Ctrl+Tab or Alt+F), so it always counts as moving focus.
            if (isShortcut)
            {
                return true;
            }

            // Page Up, Page Down, End, Home, and the four arrows form one contiguous virtual-key range.
            var isNavigationRange = virtualKey is >= VirtualKeyPageUp and <= VirtualKeyDown;

            return isNavigationRange || virtualKey is
                VirtualKeyTab or VirtualKeyEnter or VirtualKeyEscape or
                VirtualKeyF6 or VirtualKeyF10 or VirtualKeyApplications or
                VirtualKeyAlt or VirtualKeyLeftAlt or VirtualKeyRightAlt or
                VirtualKeyLeftWindows or VirtualKeyRightWindows;
        }
        #endregion
    }
}
