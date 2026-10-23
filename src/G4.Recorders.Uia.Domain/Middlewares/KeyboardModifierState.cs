using System.Collections.Generic;

namespace G4.Recorders.Uia.Domain.Middlewares
{
    /// <summary>
    /// Tracks the modifier keys held at the moment each low-level keyboard event is captured.
    /// </summary>
    /// <remarks>
    /// The state is derived from the hook's own Down and Up stream, so it reflects the press order the
    /// application received rather than the live key state the background worker observes later. The
    /// low-level keyboard hook thread is the only writer and reader, so the type carries no locking.
    /// </remarks>
    internal sealed class KeyboardModifierState
    {
        #region *** Constants    ***
        /// <summary>
        /// The modifier name emitted for either Alt key.
        /// </summary>
        internal const string Alt = "Alt";

        /// <summary>
        /// The modifier name emitted for either Control key.
        /// </summary>
        internal const string Control = "Ctrl";

        /// <summary>
        /// The modifier name emitted for either Shift key.
        /// </summary>
        internal const string Shift = "Shift";

        /// <summary>
        /// The modifier name emitted for either Windows key.
        /// </summary>
        internal const string Windows = "Win";

        // High bit that marks a key as pressed in a 256-entry keyboard state.
        private const byte PressedBit = 0x80;

        // Low bit that marks a toggle key as on in a 256-entry keyboard state.
        private const byte ToggledBit = 0x01;

        // Generic Alt (Menu) key; injected input can report it instead of a sided code.
        private const int VirtualKeyAlt = 0x12;

        // Caps Lock key whose toggle bit drives letter case in the translation state.
        private const int VirtualKeyCapsLock = 0x14;

        // Generic Control key; injected input can report it instead of a sided code.
        private const int VirtualKeyControl = 0x11;

        // Left Alt (Menu) key reported by physical input.
        private const int VirtualKeyLeftAlt = 0xA4;

        // Left Control key reported by physical input.
        private const int VirtualKeyLeftControl = 0xA2;

        // Left Shift key reported by physical input.
        private const int VirtualKeyLeftShift = 0xA0;

        // Left Windows key; the translation state has no generic Windows key.
        private const int VirtualKeyLeftWindows = 0x5B;

        // Right Alt (Menu) key, which Windows also reports as part of AltGr.
        private const int VirtualKeyRightAlt = 0xA5;

        // Right Control key reported by physical input.
        private const int VirtualKeyRightControl = 0xA3;

        // Right Shift key reported by physical input.
        private const int VirtualKeyRightShift = 0xA1;

        // Right Windows key; the translation state has no generic Windows key.
        private const int VirtualKeyRightWindows = 0x5C;

        // Generic Shift key; injected input can report it instead of a sided code.
        private const int VirtualKeyShift = 0x10;
        #endregion

        #region *** Fields       ***
        // Whether left Alt (or a generic Alt) is held; either side activates the Alt modifier.
        private bool _isLeftAlt;

        // Whether left Control (or a generic Control) is held; either side activates the Control modifier.
        private bool _isLeftControl;

        // Whether left Shift (or a generic Shift) is held; either side activates the Shift modifier.
        private bool _isLeftShift;

        // Whether the left Windows key is held; either side activates the Windows modifier.
        private bool _isLeftWindows;

        // Whether right Alt is held; it stays independent so releasing left Alt keeps AltGr active.
        private bool _isRightAlt;

        // Whether right Control is held; it stays independent of the left side's release.
        private bool _isRightControl;

        // Whether right Shift is held; it stays independent of the left side's release.
        private bool _isRightShift;

        // Whether the right Windows key is held; it stays independent of the left side's release.
        private bool _isRightWindows;
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets a value indicating whether either Alt key is held.
        /// </summary>
        internal bool IsAlt => _isLeftAlt || _isRightAlt;

        /// <summary>
        /// Gets a value indicating whether either Control key is held.
        /// </summary>
        internal bool IsControl => _isLeftControl || _isRightControl;

        /// <summary>
        /// Gets a value indicating whether either Shift key is held.
        /// </summary>
        internal bool IsShift => _isLeftShift || _isRightShift;

        /// <summary>
        /// Gets a value indicating whether either Windows key is held.
        /// </summary>
        internal bool IsWindows => _isLeftWindows || _isRightWindows;
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Releases every tracked modifier so a new capture lifecycle starts from a neutral state.
        /// </summary>
        internal void Clear()
        {
            // Reset both sides of every modifier together so no partial state survives the reset.
            _isLeftAlt = false;
            _isLeftControl = false;
            _isLeftShift = false;
            _isLeftWindows = false;
            _isRightAlt = false;
            _isRightControl = false;
            _isRightShift = false;
            _isRightWindows = false;
        }

        /// <summary>
        /// Gets the names of the held modifiers in a fixed Ctrl, Alt, Shift, Win order.
        /// </summary>
        /// <returns>The held modifier names; an empty array when no modifier is held.</returns>
        internal string[] GetModifiers()
        {
            // Emit a stable order so equal chords always serialize identically.
            var modifiers = new List<string>(capacity: 4);

            if (IsControl)
            {
                modifiers.Add(Control);
            }

            if (IsAlt)
            {
                modifiers.Add(Alt);
            }

            if (IsShift)
            {
                modifiers.Add(Shift);
            }

            if (IsWindows)
            {
                modifiers.Add(Windows);
            }

            return [.. modifiers];
        }

        /// <summary>
        /// Builds the 256-entry keyboard state that character translation reads for this event.
        /// </summary>
        /// <param name="isCapsLockOn">A value indicating whether Caps Lock was toggled on at capture time.</param>
        /// <returns>A new keyboard state holding only the tracked modifiers and the Caps Lock toggle.</returns>
        internal byte[] NewKeyboardState(bool isCapsLockOn)
        {
            // Start from an empty state so stale global key flags never influence the translation.
            var keyboardState = new byte[256];

            // Mirror each side and its generic key, as the system state does for a physical press.
            SetPressed(keyboardState, VirtualKeyLeftControl, _isLeftControl);
            SetPressed(keyboardState, VirtualKeyRightControl, _isRightControl);
            SetPressed(keyboardState, VirtualKeyControl, IsControl);
            SetPressed(keyboardState, VirtualKeyLeftAlt, _isLeftAlt);
            SetPressed(keyboardState, VirtualKeyRightAlt, _isRightAlt);
            SetPressed(keyboardState, VirtualKeyAlt, IsAlt);
            SetPressed(keyboardState, VirtualKeyLeftShift, _isLeftShift);
            SetPressed(keyboardState, VirtualKeyRightShift, _isRightShift);
            SetPressed(keyboardState, VirtualKeyShift, IsShift);
            SetPressed(keyboardState, VirtualKeyLeftWindows, _isLeftWindows);
            SetPressed(keyboardState, VirtualKeyRightWindows, _isRightWindows);

            // Apply the Caps Lock toggle so unshifted letters translate to the case the application typed.
            if (isCapsLockOn)
            {
                keyboardState[VirtualKeyCapsLock] |= ToggledBit;
            }

            return keyboardState;

            // Sets the pressed bit only for held keys; released keys keep the empty default.
            static void SetPressed(byte[] state, int virtualKey, bool isPressed)
            {
                if (isPressed)
                {
                    state[virtualKey] |= PressedBit;
                }
            }
        }

        /// <summary>
        /// Tests whether a virtual key is a Control, Alt, Shift, or Windows modifier.
        /// </summary>
        /// <param name="virtualKey">The Windows virtual-key code.</param>
        /// <returns>
        /// <see langword="true"/> when the key is a tracked modifier; otherwise <see langword="false"/>.
        /// </returns>
        internal static bool TestModifierKey(uint virtualKey)
        {
            return virtualKey is
                VirtualKeyControl or VirtualKeyLeftControl or VirtualKeyRightControl or
                VirtualKeyAlt or VirtualKeyLeftAlt or VirtualKeyRightAlt or
                VirtualKeyShift or VirtualKeyLeftShift or VirtualKeyRightShift or
                VirtualKeyLeftWindows or VirtualKeyRightWindows;
        }

        /// <summary>
        /// Records one Down or Up transition of a modifier key.
        /// </summary>
        /// <param name="virtualKey">The Windows virtual-key code reported by the hook.</param>
        /// <param name="isDown">A value indicating whether the transition is a press.</param>
        /// <remarks>
        /// Low-level hooks report sided codes (for example Left Shift), but injected input can report the
        /// generic code; a generic code updates the left side so its release clears the same flag.
        /// </remarks>
        internal void Update(uint virtualKey, bool isDown)
        {
            // Route each code to its side; non-modifier keys leave the state unchanged.
            switch (virtualKey)
            {
                case VirtualKeyControl:
                case VirtualKeyLeftControl:
                    _isLeftControl = isDown;
                    break;
                case VirtualKeyRightControl:
                    _isRightControl = isDown;
                    break;
                case VirtualKeyAlt:
                case VirtualKeyLeftAlt:
                    _isLeftAlt = isDown;
                    break;
                case VirtualKeyRightAlt:
                    _isRightAlt = isDown;
                    break;
                case VirtualKeyShift:
                case VirtualKeyLeftShift:
                    _isLeftShift = isDown;
                    break;
                case VirtualKeyRightShift:
                    _isRightShift = isDown;
                    break;
                case VirtualKeyLeftWindows:
                    _isLeftWindows = isDown;
                    break;
                case VirtualKeyRightWindows:
                    _isRightWindows = isDown;
                    break;
            }
        }
        #endregion
    }
}
