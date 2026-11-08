using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Sap.Domain.Middlewares;

namespace G4.Recorders.Sap.Domain.UnitTests.Middlewares
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapKeyboardTranslator))]
    [TestCategory("UnitTest")]
    public sealed class SapKeyboardTranslatorTests
    {
        // Lists Ctrl alone, the modifiers of a command shortcut.
        private static readonly string[] ControlOnly = ["Ctrl"];

        // Lists Ctrl and Alt together, which AltGr layouts use to type glyphs.
        private static readonly string[] ControlAndAlt = ["Ctrl", "Alt"];

        // Lists Shift alone, which only changes the typed glyph.
        private static readonly string[] ShiftOnly = ["Shift"];

        [TestMethod(DisplayName = "Verify that Shift with a glyph is typed text, not a shortcut")]
        public void ResolveShortcutModifiersShiftGlyphTest()
        {
            // Act: classify Shift+A, which types A.
            var modifiers = SapKeyboardTranslator.ResolveShortcutModifiers(ShiftOnly, typedText: "A");

            // Assert: no shortcut modifiers.
            Assert.IsEmpty(modifiers);
        }

        [TestMethod(DisplayName = "Verify that Ctrl with a key is a shortcut that reports its modifiers")]
        public void ResolveShortcutModifiersControlTest()
        {
            // Act: classify Ctrl+S, which types a control character.
            var modifiers = SapKeyboardTranslator.ResolveShortcutModifiers(ControlOnly, typedText: "\u0013");

            // Assert: the shortcut keeps Ctrl.
            Assert.AreSequenceEqual(ControlOnly, modifiers);
        }

        [TestMethod(DisplayName = "Verify that AltGr glyphs, typed with Ctrl and Alt, are typed text")]
        public void ResolveShortcutModifiersAltGrTest()
        {
            // Act: classify Ctrl+Alt+Q, which types @ on a German layout.
            var modifiers = SapKeyboardTranslator.ResolveShortcutModifiers(ControlAndAlt, typedText: "@");

            // Assert: no shortcut modifiers.
            Assert.IsEmpty(modifiers);
        }

        [TestMethod(DisplayName = "Verify that key text is the visible glyph, or the key name for other keys")]
        #region *** Data Set ***
        [DataRow("a", "A", "a")]
        [DataRow("\r", "Enter", "Enter")]
        [DataRow(" ", "Space", "space")]
        [DataRow("", "F4", "F4")]
        #endregion
        public void ResolveKeyTextTest(string typedText, string keyName, string expected)
        {
            // Act: resolve the key text.
            var keyText = SapKeyboardTranslator.ResolveKeyText(typedText, keyName);

            // Assert: the UIA recorder's key text.
            Assert.AreEqual(expected, keyText);
        }

        [TestMethod(DisplayName = "Verify that fallback key names match the UIA recorder's names")]
        #region *** Data Set ***
        [DataRow(0x0Du, "Enter")]
        [DataRow(0x74u, "F5")]
        [DataRow(0x41u, "A")]
        [DataRow(0xFFu, "VK_255")]
        #endregion
        public void GetFallbackKeyNameTest(uint virtualKey, string expected)
        {
            // Act: read the fallback name.
            var name = SapKeyboardTranslator.GetFallbackKeyName(virtualKey);

            // Assert: the fixed name.
            Assert.AreEqual(expected, name);
        }

        [TestMethod(DisplayName = "Verify that only single typed characters count as glyphs for masking")]
        #region *** Data Set ***
        [DataRow("a", true)]
        [DataRow("Tab", false)]
        [DataRow("space", false)]
        [DataRow("", false)]
        #endregion
        public void TestGlyphTest(string keyText, bool expected)
        {
            // Act: classify the key text.
            var isGlyph = SapKeyboardTranslator.TestGlyph(keyText);

            // Assert: only typed characters are glyphs.
            Assert.AreEqual(expected, isGlyph);
        }
    }
}
