using G4.Recorders.Common.Domain.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G4.Recorders.Uia.Domain.UnitTests.Models
{
    [TestClass]
    [TestCategory("RecorderPeekRequestResolver")]
    [TestCategory("UnitTest")]
    public class RecorderPeekRequestResolverTests
    {
        #region *** Data Set ***
        [DataRow(10, 20, false, (int)RecorderPeekMode.Coordinates, 10, 20)]
        [DataRow(10, null, false, (int)RecorderPeekMode.Coordinates, 10, 0)]
        [DataRow(null, 20, false, (int)RecorderPeekMode.Coordinates, 0, 20)]
        [DataRow(10, null, true, (int)RecorderPeekMode.Coordinates, 10, 0)]
        [DataRow(null, 20, true, (int)RecorderPeekMode.Coordinates, 0, 20)]
        [DataRow(null, null, true, (int)RecorderPeekMode.Focused, 0, 0)]
        [DataRow(null, null, false, (int)RecorderPeekMode.Current, 0, 0)]
        #endregion
        [TestMethod(DisplayName = "Verify that Resolve applies coordinate, focus, and current-pointer precedence.")]
        public void ResolveSelectionMatrixTest(
            int? x,
            int? y,
            bool focused,
            int expectedMode,
            int expectedX,
            int expectedY)
        {
            // Arrange: the DataRow supplies one complete combination from the public selection matrix.

            // Act: normalize the query inputs through the shared resolver used by both recorder hosts.
            var request = RecorderPeekRequestResolver.Resolve(x, y, focused);

            // Assert: the resolved mode and coordinates preserve the complete public selection matrix.
            Assert.AreEqual((RecorderPeekMode)expectedMode, request.Mode);
            Assert.AreEqual(expectedX, request.X);
            Assert.AreEqual(expectedY, request.Y);
        }
    }
}
