using System.Collections.Generic;

using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Turns captured input records into <c>user32</c> events with SAP chains, in the UIA recorder's vocabulary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only input aimed at SAP GUI is recorded: mouse events over a SAP window, and key events while a SAP window has the
    /// foreground. Windows of sessions outside the recording scope count as not SAP. A press resolves its target and keeps it, and the matching release reuses it, because the release
    /// often arrives after SAP GUI has already changed the screen. Repeated presses of a held key reuse it too.
    /// </para>
    /// <para>
    /// Typed characters in password fields are masked: the key becomes <c>*</c> and the virtual key and scan code
    /// become zero, so the event cannot reveal the character. Navigation keys, such as Tab and Enter, stay visible.
    /// </para>
    /// <para>
    /// Instances keep per-generation state and run on the scripting thread only.
    /// </para>
    /// </remarks>
    internal sealed class SapInputResolver
    {
        #region *** Constants    ***
        // Replaces a typed character in a password field.
        private const string MaskedKey = "*";

        // Names the keyboard event family exactly as the UIA recorder does.
        private const string KeyboardType = "Keyboard";

        // Names the mouse event family exactly as the UIA recorder does.
        private const string MouseType = "Mouse";

        // Reports a release that matched its retained press.
        private const string PairedReleaseSource = "PairedRelease";

        // Reports a press resolved from the SAP focus at processing time.
        private const string CapturedSnapshotSource = "CapturedSnapshot";

        // Reports a release whose press was not seen, resolved from the SAP focus instead.
        private const string OrphanFallbackSource = "OrphanFallback";

        // Reports an auto-repeated press of a held key, which reuses the first press's target.
        private const string RepeatedPressSource = "RepeatedPress";

        // Amount the wheel reports per notch.
        private const int WheelDelta = 120;
        #endregion

        #region *** Fields       ***
        // Holds the target and text of each held key, by virtual key, until its release.
        private readonly Dictionary<uint, (SapChainModel Chain, string KeyText)> _heldKeys = [];

        // Holds the target of each pressed mouse button, by button name, until its release.
        private readonly Dictionary<string, SapChainModel> _pressedButtons = [];

        // Generation the retained presses belong to; a new generation discards them.
        private long _generation;

        // Holds the recording scope of the record being resolved.
        private SapScopeParametersModel _scope;
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Builds a keyboard event, masking typed characters in password fields.
        /// </summary>
        /// <param name="record">The captured key event.</param>
        /// <param name="chain">The SAP target.</param>
        /// <param name="keyText">The key text to report, retained from the press for releases.</param>
        /// <param name="targetSource">How the target was resolved, such as <c>PairedRelease</c>.</param>
        /// <returns>The event.</returns>
        internal static SapEventModel NewKeyboardEvent(
            SapInputRecord record,
            SapChainModel chain,
            string keyText,
            string targetSource)
        {
            // Mask glyphs typed into a password field; navigation keys keep their names.
            var trigger = chain.Path.Count > 0 ? chain.Path[^1] : null;
            var isMasked = trigger?.ControlType == SapGuiTypes.PasswordField && SapKeyboardTranslator.TestGlyph(keyText);
            var isKeyDown = record.Message is SapInputRecord.KeyDown or SapInputRecord.SystemKeyDown;

            return new SapEventModel
            {
                Chain = chain,
                Event = isKeyDown ? "Key Down" : "Key Up",
                Timestamp = record.Timestamp,
                Type = KeyboardType,
                Value = new SapKeyboardValueModel
                {
                    Key = isMasked ? MaskedKey : keyText,
                    Modifiers = record.Modifiers,
                    ScanCode = isMasked ? 0 : record.ScanCode,
                    TargetSource = targetSource,
                    VirtualKey = isMasked ? 0 : record.VirtualKey
                }
            };
        }

        /// <summary>
        /// Builds a mouse button or wheel event, with the pointer offset from the target.
        /// </summary>
        /// <param name="record">The captured mouse event.</param>
        /// <param name="chain">The SAP target.</param>
        /// <returns>The event.</returns>
        internal static SapEventModel NewMouseEvent(SapInputRecord record, SapChainModel chain)
        {
            // Record the element-relative offset on the chain, where every recorder exposes it.
            chain.MouseOffset = SapChainFactory.NewMouseOffset(chain, record.X, record.Y);

            var isWheel = record.Message is SapInputRecord.MouseWheel or SapInputRecord.MouseHorizontalWheel;
            object value = isWheel
                ? new SapWheelValueModel { Notches = GetNotches(record.WheelDelta), X = record.X, Y = record.Y }
                : new SapPointerValueModel { X = record.X, Y = record.Y };

            return new SapEventModel
            {
                Chain = chain,
                Event = GetMouseEventName(record),
                Timestamp = record.Timestamp,
                Type = MouseType,
                Value = value
            };
        }

        /// <summary>
        /// Resolves one captured input record into an event.
        /// </summary>
        /// <param name="application">The <c>GuiApplication</c> object, or <c>null</c> when SAP GUI is unavailable.</param>
        /// <param name="record">The captured input record.</param>
        /// <param name="scope">The recording scope; input on windows of sessions outside it is not recorded.</param>
        /// <returns>The event, or <c>null</c> when the input was not aimed at SAP GUI in scope.</returns>
        internal SapEventModel Resolve(ISapGuiObject application, SapInputRecord record, SapScopeParametersModel scope)
        {
            // Keep the scope for the window tests of this record.
            _scope = scope;

            // Discard presses retained by an earlier recording generation.
            if (record.Generation != _generation)
            {
                _heldKeys.Clear();
                _pressedButtons.Clear();
                _generation = record.Generation;
            }

            // Without SAP GUI there is nothing to resolve against.
            if (application is null)
            {
                return null;
            }

            return record.IsKeyboard
                ? ResolveKeyboard(application, record)
                : ResolveMouse(application, record);
        }

        // Gets the button name of a mouse message: Left, Right, or Middle.
        private static string GetButtonName(int message) => message switch
        {
            SapInputRecord.LeftButtonDown or SapInputRecord.LeftButtonUp => "Left",
            SapInputRecord.RightButtonDown or SapInputRecord.RightButtonUp => "Right",
            _ => "Middle"
        };

        // Gets the UIA event name of a mouse record, such as "Left Down" or "Wheel Up".
        private static string GetMouseEventName(SapInputRecord record)
        {
            // Name wheel events by direction; the delta's sign gives it.
            if (record.Message == SapInputRecord.MouseWheel)
            {
                return record.WheelDelta > 0 ? "Wheel Up" : "Wheel Down";
            }

            if (record.Message == SapInputRecord.MouseHorizontalWheel)
            {
                return record.WheelDelta > 0 ? "Wheel Right" : "Wheel Left";
            }

            // Name buttons by button and transition.
            var transition = TestButtonDown(record.Message) ? "Down" : "Up";

            return $"{GetButtonName(record.Message)} {transition}";
        }

        // Converts a wheel delta into whole notches, at least one, because high-resolution wheels report less.
        private static int GetNotches(int delta)
        {
            // Divide by the per-notch delta, then lift partial turns to one notch.
            var notches = System.Math.Abs(delta) / WheelDelta;

            return notches == 0 ? 1 : notches;
        }

        // Tests whether a mouse message presses a button.
        private static bool TestButtonDown(int message)
        {
            return message is SapInputRecord.LeftButtonDown or SapInputRecord.RightButtonDown or SapInputRecord.MiddleButtonDown;
        }

        // Tests whether a handle belongs to a window of a SAP session in scope.
        private bool TestSapWindow(ISapGuiObject application, long window)
        {
            var (session, _) = SapGuiNavigator.FindWindow(application, window);

            return SapSessionScope.TestInScope(session, _scope);
        }

        // Resolves a key record: the press resolves the SAP focus and keeps it; the release reuses it.
        private SapEventModel ResolveKeyboard(ISapGuiObject application, SapInputRecord record)
        {
            var isKeyDown = record.Message is SapInputRecord.KeyDown or SapInputRecord.SystemKeyDown;

            // A release reuses its press, wherever the foreground has moved since.
            if (!isKeyDown)
            {
                if (_heldKeys.Remove(record.VirtualKey, out var held))
                {
                    return NewKeyboardEvent(record, held.Chain, held.KeyText, PairedReleaseSource);
                }

                // An unmatched release in SAP is still input the user gave; resolve its target now.
                return TestSapWindow(application, record.ForegroundWindow)
                    ? NewKeyboardEvent(record, NewFocusChain(application, record), record.KeyText, OrphanFallbackSource)
                    : null;
            }

            // Repeated presses of a held key keep the first press's target and text.
            if (_heldKeys.TryGetValue(record.VirtualKey, out var repeated))
            {
                return NewKeyboardEvent(record, repeated.Chain, repeated.KeyText, RepeatedPressSource);
            }

            // Record presses only while SAP GUI has the foreground.
            if (!TestSapWindow(application, record.ForegroundWindow))
            {
                return null;
            }

            var chain = NewFocusChain(application, record);
            _heldKeys[record.VirtualKey] = (chain, record.KeyText);

            return NewKeyboardEvent(record, chain, record.KeyText, CapturedSnapshotSource);
        }

        // Resolves a mouse record: a press resolves the control under the pointer and keeps it; the release reuses it.
        private SapEventModel ResolveMouse(ISapGuiObject application, SapInputRecord record)
        {
            // Wheel turns resolve where they happen, only over SAP windows.
            var isWheel = record.Message is SapInputRecord.MouseWheel or SapInputRecord.MouseHorizontalWheel;
            if (isWheel)
            {
                return TestSapWindow(application, record.RootWindow)
                    ? NewMouseEvent(record, NewPointChain(application, record))
                    : null;
            }

            // A release reuses its press; an unmatched release over SAP resolves where it happens.
            var button = GetButtonName(record.Message);
            if (!TestButtonDown(record.Message))
            {
                if (_pressedButtons.Remove(button, out var pressed))
                {
                    return NewMouseEvent(record, pressed);
                }

                return TestSapWindow(application, record.RootWindow)
                    ? NewMouseEvent(record, NewPointChain(application, record))
                    : null;
            }

            // Record presses only over SAP windows, and forget a press elsewhere so its release is ignored too.
            if (!TestSapWindow(application, record.RootWindow))
            {
                _pressedButtons.Remove(button);
                return null;
            }

            var chain = NewPointChain(application, record);
            _pressedButtons[button] = chain;

            return NewMouseEvent(record, chain);
        }

        // Resolves the chain of the SAP control that has the focus in the record's foreground window.
        private static SapChainModel NewFocusChain(ISapGuiObject application, SapInputRecord record)
        {
            return SapChainFactory.NewChain(SapGuiNavigator.GetFocused(application, record.ForegroundWindow));
        }

        // Resolves the chain of the SAP control under the record's pointer.
        private static SapChainModel NewPointChain(ISapGuiObject application, SapInputRecord record)
        {
            return SapChainFactory.NewChain(SapGuiNavigator.FindByPosition(application, record.RootWindow, record.X, record.Y));
        }
        #endregion
    }
}
