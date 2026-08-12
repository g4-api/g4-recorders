using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Uia.Domain.Extensions;

using UIAutomationClient;

namespace G4.Recorders.Uia.Domain.UnitTests.Extensions
{
    [TestClass]
    [TestCategory(nameof(LocalExtensions))]
    [TestCategory("UnitTest")]
    public sealed class LocalExtensionsAttributeTests
    {
        [TestMethod(DisplayName = "Verify that dotted and Pattern-qualified names normalize to the concatenated key.")]
        public void NormalizeCollapsesDottedAndPatternFormsTest()
        {
            // Assert: the three pattern-property spellings collapse onto the concatenated key.
            Assert.AreEqual("AnnotationDateTime", LocalExtensions.NormalizeAttributeName("Annotation.DateTime"));
            Assert.AreEqual("AnnotationDateTime", LocalExtensions.NormalizeAttributeName("AnnotationPattern.DateTime"));
            Assert.AreEqual("AnnotationDateTime", LocalExtensions.NormalizeAttributeName("AnnotationDateTime"));
            Assert.AreEqual("ValueValue", LocalExtensions.NormalizeAttributeName("Value.Value"));

            // Assert: a dotted name whose left segment is not a Pattern qualifier still concatenates cleanly.
            Assert.AreEqual("LegacyIAccessibleName", LocalExtensions.NormalizeAttributeName("LegacyIAccessible.Name"));

            // Assert: names without a dot are returned unchanged, so element properties keep their spelling.
            Assert.AreEqual("Name", LocalExtensions.NormalizeAttributeName("Name"));
            Assert.AreEqual("IsValuePatternAvailable", LocalExtensions.NormalizeAttributeName("IsValuePatternAvailable"));
        }

        [TestMethod(DisplayName = "Verify that all spellings of a pattern property resolve to the same property id.")]
        public void PatternAliasesResolveToSamePropertyIdTest()
        {
            // Act: resolve each supported spelling of the Annotation pattern's DateTime property.
            var concatenated = LocalExtensions.TryResolvePropertyId("AnnotationDateTime", out var concatenatedId);
            var dotted = LocalExtensions.TryResolvePropertyId("Annotation.DateTime", out var dottedId);
            var qualified = LocalExtensions.TryResolvePropertyId("AnnotationPattern.DateTime", out var qualifiedId);

            // Assert: every spelling resolves to UIA_AnnotationDateTimePropertyId.
            Assert.IsTrue(concatenated);
            Assert.IsTrue(dotted);
            Assert.IsTrue(qualified);
            Assert.AreEqual(UIA_PropertyIds.UIA_AnnotationDateTimePropertyId, concatenatedId);
            Assert.AreEqual(UIA_PropertyIds.UIA_AnnotationDateTimePropertyId, dottedId);
            Assert.AreEqual(UIA_PropertyIds.UIA_AnnotationDateTimePropertyId, qualifiedId);
        }

        [TestMethod(DisplayName = "Verify that element and pattern property names resolve to their property ids.")]
        public void ElementAndPatternPropertiesResolveTest()
        {
            // Act + Assert: the common identity attributes and a couple of pattern properties resolve to their ids.
            Assert.IsTrue(LocalExtensions.TryResolvePropertyId("Name", out var nameId));
            Assert.AreEqual(UIA_PropertyIds.UIA_NamePropertyId, nameId);

            Assert.IsTrue(LocalExtensions.TryResolvePropertyId("AutomationId", out var automationId));
            Assert.AreEqual(UIA_PropertyIds.UIA_AutomationIdPropertyId, automationId);

            Assert.IsTrue(LocalExtensions.TryResolvePropertyId("Value.Value", out var valueId));
            Assert.AreEqual(UIA_PropertyIds.UIA_ValueValuePropertyId, valueId);

            Assert.IsTrue(LocalExtensions.TryResolvePropertyId("Toggle.ToggleState", out var toggleId));
            Assert.AreEqual(UIA_PropertyIds.UIA_ToggleToggleStatePropertyId, toggleId);
        }

        [TestMethod(DisplayName = "Verify that unknown, empty, and null attribute names do not resolve.")]
        public void UnknownOrEmptyAttributeDoesNotResolveTest()
        {
            // Assert: an unknown dotted name, an empty string, and null all fail to resolve.
            Assert.IsFalse(LocalExtensions.TryResolvePropertyId("NotAReal.Property", out _));
            Assert.IsFalse(LocalExtensions.TryResolvePropertyId(string.Empty, out _));
            Assert.IsFalse(LocalExtensions.TryResolvePropertyId(null, out _));
        }
    }
}
