using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Uia.Domain.Middlewares;

namespace G4.Recorders.Uia.Domain.UnitTests.Middlewares
{
    [TestClass]
    [TestCategory(nameof(KeyboardModifierState))]
    [TestCategory("UnitTest")]
    public sealed class KeyboardModifierStateTests
    {
        // Expected report order when every modifier family is held.
        private static readonly string[] s_allModifiers = ["Ctrl", "Alt", "Shift", "Win"];

        [TestMethod(DisplayName = "Verify that clearing the state releases every held modifier")]
        public void ModifierStateClearReleasesModifiersTest()
        {
            // Arrange: hold every modifier family on its left side.
            var state = new KeyboardModifierState();
            state.Update(virtualKey: 0xA2, isDown: true);
            state.Update(virtualKey: 0xA4, isDown: true);
            state.Update(virtualKey: 0xA0, isDown: true);
            state.Update(virtualKey: 0x5B, isDown: true);

            // Act: reset the state for a new capture lifecycle.
            state.Clear();

            // Assert: verify that no modifier survives the reset.
            Assert.IsEmpty(collection: state.GetModifiers());
        }

        [TestMethod(DisplayName = "Verify that a generic modifier code is released by its own generic release")]
        public void ModifierStateGenericCodeTest()
        {
            // Arrange: model injected input that reports the generic Shift code instead of a sided code.
            var state = new KeyboardModifierState();

            // Act: press and inspect, then release the generic code.
            state.Update(virtualKey: 0x10, isDown: true);
            var isShiftWhileHeld = state.IsShift;
            state.Update(virtualKey: 0x10, isDown: false);

            // Assert: verify that the generic press and release map onto the same tracked side.
            Assert.IsTrue(condition: isShiftWhileHeld);
            Assert.IsFalse(condition: state.IsShift);
        }

        [TestMethod(DisplayName = "Verify that held modifiers are reported in Ctrl, Alt, Shift, Win order")]
        public void ModifierStateOrderTest()
        {
            // Arrange: press the modifiers in the reverse of their reported order.
            var state = new KeyboardModifierState();

            // Act: hold Win, Shift, Alt, and Ctrl from both sides of the keyboard.
            state.Update(virtualKey: 0x5C, isDown: true);
            state.Update(virtualKey: 0xA1, isDown: true);
            state.Update(virtualKey: 0xA5, isDown: true);
            state.Update(virtualKey: 0xA3, isDown: true);
            var modifiers = state.GetModifiers();

            // Assert: verify that the order is stable regardless of press order.
            Assert.AreSequenceEqual(expected: s_allModifiers, actual: modifiers);
        }

        [TestMethod(DisplayName = "Verify that releasing one side keeps the modifier held through the other side")]
        public void ModifierStateSidesTest()
        {
            // Arrange: hold both Shift keys.
            var state = new KeyboardModifierState();
            state.Update(virtualKey: 0xA0, isDown: true);
            state.Update(virtualKey: 0xA1, isDown: true);

            // Act: release only the left Shift key.
            state.Update(virtualKey: 0xA0, isDown: false);

            // Assert: verify that the right Shift key still holds the modifier.
            Assert.IsTrue(condition: state.IsShift);
        }

        [TestMethod(DisplayName = "Verify that the translation state carries only captured modifiers and Caps Lock")]
        public void ModifierStateKeyboardStateTest()
        {
            // Arrange: hold left Shift only.
            var state = new KeyboardModifierState();
            state.Update(virtualKey: 0xA0, isDown: true);

            // Act: build the translation state with Caps Lock toggled on.
            var keyboardState = state.NewKeyboardState(isCapsLockOn: true);

            // Assert: verify that Shift is pressed on its side and generically, and Control stays released.
            Assert.AreEqual(expected: 0x80, actual: keyboardState[0xA0]);
            Assert.AreEqual(expected: 0x80, actual: keyboardState[0x10]);
            Assert.AreEqual(expected: 0x00, actual: keyboardState[0x11]);
            Assert.AreEqual(expected: 0x01, actual: keyboardState[0x14]);
        }

        [TestMethod(DisplayName = "Verify that non-modifier keys never change the modifier state")]
        public void ModifierStateIgnoresOtherKeysTest()
        {
            // Arrange: prepare an empty state.
            var state = new KeyboardModifierState();

            // Act: press letter A and Enter.
            state.Update(virtualKey: 0x41, isDown: true);
            state.Update(virtualKey: 0x0D, isDown: true);

            // Assert: verify that neither key is treated as a modifier.
            Assert.IsEmpty(collection: state.GetModifiers());
            Assert.IsFalse(condition: KeyboardModifierState.TestModifierKey(virtualKey: 0x41));
            Assert.IsTrue(condition: KeyboardModifierState.TestModifierKey(virtualKey: 0xA2));
        }
    }
}
