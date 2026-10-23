using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Uia.Domain.Middlewares;

namespace G4.Recorders.Uia.Domain.UnitTests.Middlewares
{
    [TestClass]
    [TestCategory(nameof(KeyboardKeyClassifier))]
    [TestCategory("UnitTest")]
    public sealed class KeyboardKeyClassifierTests
    {
        // Expected chord for Alt held alone.
        private static readonly string[] s_alt = ["Alt"];

        // Expected chord for Control held alone.
        private static readonly string[] s_control = ["Ctrl"];

        // Expected chord for Control and Shift held together.
        private static readonly string[] s_controlShift = ["Ctrl", "Shift"];

        // Expected chord for Shift held with a non-text key.
        private static readonly string[] s_shift = ["Shift"];

        [TestMethod(DisplayName = "Verify that AltGr glyphs remain typed text")]
        public void ClassifierAltGrGlyphIsTextTest()
        {
            // Arrange: hold left Control and right Alt, as Windows reports AltGr.
            var state = NewState(0xA2, 0xA5);

            // Act: classify the glyph AltGr produced.
            var modifiers = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "@");

            // Assert: verify that the glyph stays text.
            Assert.IsEmpty(collection: modifiers);
        }

        [TestMethod(DisplayName = "Verify that Alt with a letter forms a menu shortcut")]
        public void ClassifierAltLetterIsShortcutTest()
        {
            // Arrange: hold left Alt, which leaves the letter glyph unchanged.
            var state = NewState(0xA4);

            // Act: classify the letter.
            var modifiers = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "f");

            // Assert: verify that Alt makes the press a shortcut.
            Assert.AreSequenceEqual(expected: s_alt, actual: modifiers);
        }

        [TestMethod(DisplayName = "Verify that Ctrl with a letter forms a shortcut")]
        public void ClassifierControlLetterIsShortcutTest()
        {
            // Arrange: hold left Control; translation yields a control character for a letter.
            var state = NewState(0xA2);

            // Act: classify Ctrl+A, whose translation is U+0001.
            var modifiers = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "\u0001");

            // Assert: verify that Control makes the press a shortcut.
            Assert.AreSequenceEqual(expected: s_control, actual: modifiers);
        }

        [TestMethod(DisplayName = "Verify that Ctrl+Shift chords keep both modifiers")]
        public void ClassifierControlShiftChordTest()
        {
            // Arrange: hold left Control and left Shift.
            var state = NewState(0xA2, 0xA0);

            // Act: classify Ctrl+Shift+S, whose translation is U+0013.
            var modifiers = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "\u0013");

            // Assert: verify that Shift is part of the chord.
            Assert.AreSequenceEqual(expected: s_controlShift, actual: modifiers);
        }

        [TestMethod(DisplayName = "Verify that unmodified keys are never shortcuts")]
        public void ClassifierPlainKeyTest()
        {
            // Arrange: hold no modifier.
            var state = NewState();

            // Act: classify a letter and a non-text key.
            var letter = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "a");
            var tab = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "\t");

            // Assert: verify that both stay plain input.
            Assert.IsEmpty(collection: letter);
            Assert.IsEmpty(collection: tab);
        }

        [TestMethod(DisplayName = "Verify that Shift with a glyph is typed text")]
        public void ClassifierShiftGlyphIsTextTest()
        {
            // Arrange: hold left Shift.
            var state = NewState(0xA0);

            // Act: classify the capital letter and the colon that Shift produced.
            var capital = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "E");
            var colon = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: ":");

            // Assert: verify that the glyph already carries Shift.
            Assert.IsEmpty(collection: capital);
            Assert.IsEmpty(collection: colon);
        }

        [TestMethod(DisplayName = "Verify that Shift with a non-text key forms a shortcut")]
        public void ClassifierShiftNonTextIsShortcutTest()
        {
            // Arrange: hold left Shift.
            var state = NewState(0xA0);

            // Act: classify Shift+Tab, whose translation is a control character.
            var modifiers = KeyboardKeyClassifier.ResolveShortcutModifiers(modifierState: state, typedText: "\t");

            // Assert: verify that Shift is retained for the non-text key.
            Assert.AreSequenceEqual(expected: s_shift, actual: modifiers);
        }

        [TestMethod(DisplayName = "Verify that only navigation keys and shortcuts are classified as moving focus")]
        #region *** Data Set ***
        [DataRow(0x09u, false, true)]
        [DataRow(0x0Du, false, true)]
        [DataRow(0x1Bu, false, true)]
        [DataRow(0x21u, false, true)]
        [DataRow(0x28u, false, true)]
        [DataRow(0x75u, false, true)]
        [DataRow(0xA4u, false, true)]
        [DataRow(0x41u, false, false)]
        [DataRow(0x20u, false, false)]
        [DataRow(0xBAu, false, false)]
        [DataRow(0x41u, true, true)]
        #endregion
        public void ClassifierFocusMovingKeyTest(uint virtualKey, bool isShortcut, bool expected)
        {
            // Act: classify the key and shortcut combination.
            var isFocusMoving = KeyboardKeyClassifier.TestFocusMovingKey(virtualKey, isShortcut);

            // Assert: verify that only navigation keys and shortcuts can move focus.
            Assert.AreEqual(expected: expected, actual: isFocusMoving);
        }

        // Creates a modifier state with the given sided virtual keys held.
        private static KeyboardModifierState NewState(params uint[] heldKeys)
        {
            // Press each key in order so the state matches a physical chord.
            var state = new KeyboardModifierState();

            foreach (var key in heldKeys)
            {
                state.Update(virtualKey: key, isDown: true);
            }

            return state;
        }
    }
}
