using Microsoft.VisualStudio.TestTools.UnitTesting;

using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapRecorderOptionsStore))]
    [TestCategory("UnitTest")]
    public sealed class SapRecorderOptionsStoreTests
    {
        [TestMethod(DisplayName = "Verify that without a request the store holds the environment and settings values")]
        public void CurrentDefaultsTest()
        {
            // Arrange: a store over an empty environment and settings.
            var store = new SapRecorderOptionsStore(TestOptions.NewResolver());

            // Act: read the values in force.
            var options = store.Current;

            // Assert: both streams and record-mode permission, from their defaults.
            Assert.IsTrue(options.TestStream(SapRecordingStreams.User32));
            Assert.IsTrue(options.TestStream(SapRecordingStreams.NativeSap));
            Assert.IsTrue(options.SetRecordMode);
            Assert.AreEqual(SapRecorderOptionsModel.DefaultSource, options.Sources["streams"]);
        }

        [TestMethod(DisplayName = "Verify that a request replaces the defaults, and an equivalent second request is accepted")]
        public void ApplyRequestTest()
        {
            // Arrange: a store and a request for user32 only.
            var store = new SapRecorderOptionsStore(TestOptions.NewResolver());
            var request = new SapRecorderParametersModel { Streams = ["user32"] };

            // Act: apply the request twice, as two clients with the same values would.
            var isFirstApplied = store.Apply(TestOptions.Resolve(request));
            var isSecondApplied = store.Apply(TestOptions.Resolve(request));

            // Assert: both accepted, and nativeSap is off.
            Assert.IsTrue(isFirstApplied);
            Assert.IsTrue(isSecondApplied);
            Assert.IsFalse(store.Current.TestStream(SapRecordingStreams.NativeSap));
        }

        [TestMethod(DisplayName = "Verify that a different second request is refused and the first values stay")]
        public void ApplyConflictTest()
        {
            // Arrange: a store whose recording already runs with user32 only.
            var store = new SapRecorderOptionsStore(TestOptions.NewResolver());
            store.Apply(TestOptions.Resolve(new SapRecorderParametersModel { Streams = ["user32"] }));

            // Act: apply different values.
            var isApplied = store.Apply(TestOptions.Resolve(new SapRecorderParametersModel { Streams = ["nativeSap"] }));

            // Assert: refused, and user32 still applies.
            Assert.IsFalse(isApplied);
            Assert.IsTrue(store.Current.TestStream(SapRecordingStreams.User32));
        }

        [TestMethod(DisplayName = "Verify that reset returns to the defaults and allows a new request")]
        public void ResetTest()
        {
            // Arrange: a store with a request in force.
            var store = new SapRecorderOptionsStore(TestOptions.NewResolver());
            store.Apply(TestOptions.Resolve(new SapRecorderParametersModel { Streams = ["user32"] }));

            // Act: end the recording, then start one with different values.
            store.Reset();
            var isDefault = store.Current.TestStream(SapRecordingStreams.NativeSap);
            var isApplied = store.Apply(TestOptions.Resolve(new SapRecorderParametersModel { Streams = ["nativeSap"] }));

            // Assert: the defaults came back, and the new request applied.
            Assert.IsTrue(isDefault);
            Assert.IsTrue(isApplied);
        }
    }
}
