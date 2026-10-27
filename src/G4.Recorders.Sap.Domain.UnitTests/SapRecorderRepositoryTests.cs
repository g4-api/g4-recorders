using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapRecorderRepository))]
    [TestCategory("UnitTest")]
    public sealed class SapRecorderRepositoryTests
    {
        [TestMethod(DisplayName = "Verify that window focus without a title or process name is refused before any process is read")]
        public void SetWindowFocusWithoutIdentifierTest()
        {
            // Arrange: a repository over a fake host.
            var repository = new SapRecorderRepository(new FakeScriptingHost(), new SapCursorPositionProvider());

            // Act and assert: the request is refused.
            Assert.ThrowsExactly<ArgumentException>(() => repository.SetWindowFocus(windowTitle: " ", processName: null));
        }

        [TestMethod(DisplayName = "Verify that window focus reports no match for a process name no process has")]
        public void SetWindowFocusNotFoundTest()
        {
            // Arrange: a repository over a fake host.
            var repository = new SapRecorderRepository(new FakeScriptingHost(), new SapCursorPositionProvider());

            // Act: look for a process that does not exist.
            var result = repository.SetWindowFocus(windowTitle: null, processName: $"g4-missing-{Guid.NewGuid():N}");

            // Assert: reported, not thrown.
            Assert.IsFalse(result.Found);
            Assert.IsFalse(result.Focused);
        }

        [TestMethod(DisplayName = "Verify that metrics-only screenshots describe the virtual desktop without pixels")]
        public void GetScreenshotMetricsOnlyTest()
        {
            // Arrange: a repository over a fake host.
            var repository = new SapRecorderRepository(new FakeScriptingHost(), new SapCursorPositionProvider());

            // Act: read the metrics.
            var screenshot = repository.GetScreenshot(metricsOnly: true);

            // Assert: a real size, no image.
            Assert.IsGreaterThan(0, screenshot.Width);
            Assert.IsGreaterThan(0, screenshot.Height);
            Assert.IsNull(screenshot.ImageBase64);
        }

        [TestMethod(DisplayName = "Verify that peeks fail with InvalidOperationException while SAP GUI is unavailable")]
        public void GetElementChainUnavailableTest()
        {
            // Arrange: a host without SAP GUI.
            var repository = new SapRecorderRepository(new FakeScriptingHost(), new SapCursorPositionProvider());

            // Act and assert: both peek forms report the unavailable SAP GUI.
            Assert.ThrowsExactly<InvalidOperationException>(() => repository.GetElementChain());
            Assert.ThrowsExactly<InvalidOperationException>(() => repository.GetElementChain(x: 1, y: 1));
        }
    }
}
