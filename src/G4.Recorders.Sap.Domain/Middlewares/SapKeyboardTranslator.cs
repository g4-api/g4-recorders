using System;
using System.Collections.Generic;

using G4.Recorders.Sap.Domain.Native;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Resolves the key text and shortcut modifiers of a key event, in the UIA recorder's vocabulary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Key text is the typed glyph when the key types one (<c>a</c>, <c>A</c>, <c>!</c>), and the key name otherwise
    /// (<c>Enter</c>, <c>F4</c>, <c>space</c>). Shortcuts use the key name, never a control character. Modifiers are
    /// reported in the order <c>Ctrl</c>, <c>Alt</c>, <c>Shift</c>, <c>Win</c>, and only for shortcuts: Shift on its own
    /// is part of the typed glyph, not a shortcut.
    /// </para>
    /// <para>
    /// The capture methods read the keyboard state and must run on the hook thread, at capture time. The rule methods
    /// are pure.
    /// </para>
    /// </remarks>
    internal static class SapKeyboardTranslator
    {
        #region *** Constants    ***
        // Names the Alt modifier exactly as the UIA recorder does.
        private const string AltName = "Alt";

        // Names the Ctrl modifier exactly as the UIA recorder does.
        private const string ControlName = "Ctrl";

        // Names the Shift modifier exactly as the UIA recorder does.
        private const string ShiftName = "Shift";

        // Names the Windows-key modifier exactly as the UIA recorder does.
        private const string WindowsName = "Win";

        // Marks a key as held in a keyboard state array.
        private const byte KeyHeld = 0x80;

        // Marks a lock key as toggled on in a keyboard state array.
        private const byte KeyToggled = 0x01;

        // Translates without changing the system keyboard state, so a dead key the target is about to receive survives.
        private const uint ToUnicodeKeepKeyboardState = 0x04;

        // Identifies the Alt key, held or not.
        private const int VirtualKeyAlt = 0x12;

        // Identifies the Caps Lock key, whose toggle changes letter case.
        private const int VirtualKeyCapsLock = 0x14;

        // Identifies the Ctrl key, held or not.
        private const int VirtualKeyControl = 0x11;

        // Identifies the left Windows key.
        private const int VirtualKeyLeftWindows = 0x5B;

        // Identifies the right Windows key.
        private const int VirtualKeyRightWindows = 0x5C;

        // Identifies the Shift key, held or not.
        private const int VirtualKeyShift = 0x10;

        // Lists the modifier keys, generic and left/right variants, which never form shortcuts on their own.
        private static readonly HashSet<uint> ModifierKeys = [0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Reads the modifiers held at capture time.
        /// </summary>
        /// <returns>The held modifiers, in the canonical order.</returns>
        internal static string[] GetHeldModifiers()
        {
            // Read the asynchronous state, which a low-level hook sees before the current key is applied.
            var isControl = TestHeld(VirtualKeyControl);
            var isAlt = TestHeld(VirtualKeyAlt);
            var isShift = TestHeld(VirtualKeyShift);
            var isWindows = TestHeld(VirtualKeyLeftWindows) || TestHeld(VirtualKeyRightWindows);

            return NewModifiers(isControl, isAlt, isShift, isWindows);
        }

        /// <summary>
        /// Gets the display name of a key, localized when Windows provides one.
        /// </summary>
        /// <param name="virtualKey">The virtual-key code.</param>
        /// <param name="scanCode">The hardware scan code.</param>
        /// <param name="isExtended">Whether the key is an extended key.</param>
        /// <returns>The key name; a <c>VK_n</c> form when no name is known.</returns>
        internal static string GetKeyName(uint virtualKey, uint scanCode, bool isExtended)
        {
            // Compose the GetKeyNameText parameter: scan code in bits 16-23, extended flag in bit 24.
            var parameter = (int)(scanCode << 16) | (isExtended ? 1 << 24 : 0);
            var buffer = new char[64];
            var length = NativeMethods.GetKeyNameText(parameter, buffer, buffer.Length);

            // Prefer the Windows name; fall back to the fixed names the UIA recorder uses.
            return length > 0 ? new string(buffer, 0, length) : GetFallbackKeyName(virtualKey);
        }

        /// <summary>
        /// Gets the fixed name of a virtual key, used when Windows provides no name.
        /// </summary>
        /// <param name="virtualKey">The virtual-key code.</param>
        /// <returns>The key name, or <c>VK_n</c> for unknown codes.</returns>
        internal static string GetFallbackKeyName(uint virtualKey) => virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x14 => "CapsLock",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x70 and <= 0x7B => $"F{virtualKey - 0x6F}",
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
            _ => $"VK_{virtualKey}"
        };

        /// <summary>
        /// Resolves the key text and shortcut modifiers of a key event at capture time.
        /// </summary>
        /// <param name="virtualKey">The virtual-key code.</param>
        /// <param name="scanCode">The hardware scan code.</param>
        /// <param name="isExtended">Whether the key is an extended key.</param>
        /// <param name="isKeyDown">Whether the event is a press; releases carry no shortcut modifiers.</param>
        /// <returns>The key text and the shortcut modifiers.</returns>
        internal static (string KeyText, string[] Modifiers) ResolveKeyInput(
            uint virtualKey,
            uint scanCode,
            bool isExtended,
            bool isKeyDown)
        {
            // Translate with the held modifiers, the Caps Lock toggle, and the foreground window's layout.
            var held = GetHeldModifiers();
            var isCapsLockOn = (NativeMethods.GetKeyState(VirtualKeyCapsLock) & KeyToggled) != 0;
            var typedText = ConvertToText(virtualKey, scanCode, held, isCapsLockOn);

            // Apply the shared rules: modifier keys and releases are never shortcuts; shortcuts use the key name.
            var isModifierKey = ModifierKeys.Contains(virtualKey);
            var modifiers = isModifierKey || !isKeyDown ? [] : ResolveShortcutModifiers(held, typedText);
            var keyName = GetKeyName(virtualKey, scanCode, isExtended);
            var keyText = modifiers.Length > 0 ? keyName : ResolveKeyText(typedText, keyName);

            return (keyText, modifiers);
        }

        /// <summary>
        /// Resolves the shortcut modifiers of a press: none for a typed glyph without a command modifier, all held
        /// modifiers otherwise.
        /// </summary>
        /// <param name="heldModifiers">The held modifiers, in the canonical order.</param>
        /// <param name="typedText">The text the key types, or empty.</param>
        /// <returns>The shortcut modifiers.</returns>
        /// <remarks>
        /// Ctrl and Alt together (AltGr) type glyphs on many layouts, so only one of them, or Win, makes a command.
        /// </remarks>
        internal static string[] ResolveShortcutModifiers(string[] heldModifiers, string typedText)
        {
            // Classify the press: a visible glyph, and whether a command modifier is held.
            var isGlyph = !string.IsNullOrEmpty(typedText) && !char.IsControl(typedText[0]);
            var isControl = Array.IndexOf(heldModifiers, ControlName) >= 0;
            var isAlt = Array.IndexOf(heldModifiers, AltName) >= 0;
            var isWindows = Array.IndexOf(heldModifiers, WindowsName) >= 0;
            var isCommand = isWindows || (isControl ^ isAlt);

            return isGlyph && !isCommand ? [] : heldModifiers;
        }

        /// <summary>
        /// Resolves the key text: the typed glyph when visible, otherwise the key name, with <c>Space</c> as <c>space</c>.
        /// </summary>
        /// <param name="typedText">The text the key types, or empty.</param>
        /// <param name="keyName">The key's display name.</param>
        /// <returns>The key text.</returns>
        internal static string ResolveKeyText(string typedText, string keyName)
        {
            // Return a visible, non-space glyph as typed.
            if (!string.IsNullOrEmpty(typedText) && !char.IsControl(typedText[0]) && typedText[0] != ' ')
            {
                return typedText;
            }

            // Normalize the space key the way the UIA recorder does.
            return string.Equals(keyName, "Space", StringComparison.OrdinalIgnoreCase) ? "space" : keyName;
        }

        /// <summary>
        /// Tests whether key text is a typed glyph rather than a key name.
        /// </summary>
        /// <param name="keyText">The resolved key text.</param>
        /// <returns><c>true</c> for a single typed character; otherwise, <c>false</c>.</returns>
        internal static bool TestGlyph(string keyText)
        {
            // Key names are words (Enter, F4, space); glyphs are one character, or a surrogate pair.
            return !string.IsNullOrEmpty(keyText) && (keyText.Length == 1 || char.IsSurrogatePair(keyText, 0));
        }

        // Translates a key into the text it types with the given modifiers, using the foreground window's layout.
        private static string ConvertToText(uint virtualKey, uint scanCode, string[] heldModifiers, bool isCapsLockOn)
        {
            // Build the keyboard state from the captured modifiers, not the hook thread's own state.
            var keyState = new byte[256];
            SetHeld(keyState, VirtualKeyShift, Array.IndexOf(heldModifiers, ShiftName) >= 0);
            SetHeld(keyState, VirtualKeyControl, Array.IndexOf(heldModifiers, ControlName) >= 0);
            SetHeld(keyState, VirtualKeyAlt, Array.IndexOf(heldModifiers, AltName) >= 0);
            keyState[VirtualKeyCapsLock] = isCapsLockOn ? KeyToggled : (byte)0;

            // Translate with the layout of the thread that receives the input.
            var foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
            var layout = NativeMethods.GetKeyboardLayout(foregroundThread);
            var buffer = new char[8];
            var length = NativeMethods.ToUnicodeEx(
                virtualKey,
                scanCode,
                keyState,
                buffer,
                buffer.Length,
                ToUnicodeKeepKeyboardState,
                layout);

            // A positive length is text; zero is a non-text key and negative is a dead key without a glyph yet.
            return length > 0 ? new string(buffer, 0, length) : string.Empty;
        }

        // Builds the modifier list in the canonical order: Ctrl, Alt, Shift, Win.
        private static string[] NewModifiers(bool isControl, bool isAlt, bool isShift, bool isWindows)
        {
            // Append in a fixed order, so equal chords always serialize identically.
            var modifiers = new List<string>(capacity: 4);
            if (isControl)
            {
                modifiers.Add(ControlName);
            }

            if (isAlt)
            {
                modifiers.Add(AltName);
            }

            if (isShift)
            {
                modifiers.Add(ShiftName);
            }

            if (isWindows)
            {
                modifiers.Add(WindowsName);
            }

            return [.. modifiers];
        }

        // Marks a key as held in a keyboard state array.
        private static void SetHeld(byte[] keyState, int virtualKey, bool isHeld)
        {
            keyState[virtualKey] = isHeld ? KeyHeld : (byte)0;
        }

        // Tests whether a key is held, from the asynchronous key state.
        private static bool TestHeld(int virtualKey)
        {
            return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
        #endregion
    }
}
