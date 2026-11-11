using G4.Recorders.Android.Domain.Middlewares;
using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Android.Domain.UnitTests.Support;
using G4.Recorders.Common.Domain.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.UnitTests
{
    [TestClass]
    [TestCategory(nameof(AndroidRecorderRepository))]
    [TestCategory("UnitTest")]
    public sealed class AndroidRecorderRepositoryTests
    {
        // A coordinate peek, the shape the controller produces.
        private static readonly RecorderPeekRequestModel CoordinatePeek = RecorderPeekRequestResolver.Resolve(x: 10, y: 20, focused: false);

        [TestMethod(DisplayName = "Verify that a peek without a connected app fails as unavailable")]
        public async Task PeekWithoutAppTestAsync()
        {
            // Arrange: a repository with no registered app.
            var repository = new AndroidRecorderRepository(new FakeHubContext());

            // Act: peek.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetElementChainAsync(CoordinatePeek, CancellationToken.None));

            // Assert: the failure says no app is connected, which the controller maps to 503.
            Assert.Contains("No Android recorder app", exception.Message);
        }

        [TestMethod(DisplayName = "Verify that a peek with two connected apps fails as ambiguous")]
        public async Task PeekAmbiguousTestAsync()
        {
            // Arrange: two registered apps.
            var repository = new AndroidRecorderRepository(new FakeHubContext());
            repository.Register("app-1");
            repository.Register("app-2");

            // Act: peek.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetElementChainAsync(CoordinatePeek, CancellationToken.None));

            // Assert: the request is refused instead of racing two answers.
            Assert.Contains("ambiguous", exception.Message);
        }

        [TestMethod(DisplayName = "Verify that a peek is sent to the app and completed by its correlated answer")]
        public async Task PeekRoundTripTestAsync()
        {
            // Arrange: the app answers every peek with a one-node chain.
            var hub = new FakeHubContext();
            var repository = new AndroidRecorderRepository(hub);
            repository.Register("app-1");
            var chain = new AndroidChainModel { Locator = "//android.widget.Button[@text='Go']" };
            hub.OnSend = (_, _, arguments) =>
            {
                var request = (AndroidPeekRequestModel)arguments[0];
                repository.Complete(new AndroidPeekResponseModel { Chain = chain, RequestId = request.RequestId }, "app-1");
            };

            // Act.
            var result = await repository.GetElementChainAsync(CoordinatePeek, CancellationToken.None);

            // Assert: the request carried the coordinates and the answer is the app's chain.
            var (target, method, sentArguments) = hub.Sent.Single();
            var sentRequest = (AndroidPeekRequestModel)sentArguments[0];
            Assert.AreEqual("app-1", target);
            Assert.AreEqual("ReceivePeekRequest", method);
            Assert.AreEqual(RecorderPeekMode.Coordinates, sentRequest.Mode);
            Assert.AreEqual(10, sentRequest.X);
            Assert.AreSame(chain, result);
        }

        [TestMethod(DisplayName = "Verify that an answer from another connection or with an error is handled safely")]
        public async Task PeekOtherConnectionAndErrorTestAsync()
        {
            // Arrange: a foreign answer first, which is ignored, then the app's own failure.
            var hub = new FakeHubContext();
            var repository = new AndroidRecorderRepository(hub);
            repository.Register("app-1");
            hub.OnSend = (_, _, arguments) =>
            {
                var requestId = ((AndroidPeekRequestModel)arguments[0]).RequestId;
                Assert.IsFalse(repository.Complete(new AndroidPeekResponseModel { RequestId = requestId }, "intruder"));
                repository.Complete(new AndroidPeekResponseModel { Error = "No window", RequestId = requestId }, "app-1");
            };

            // Act and assert.
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetElementChainAsync(CoordinatePeek, CancellationToken.None));
            Assert.AreEqual("No window", exception.Message);
        }

        [TestMethod(DisplayName = "Verify that a node-not-found answer is an empty chain")]
        public async Task PeekEmptyAnswerTestAsync()
        {
            // Arrange: an app that answers without a chain, as it does when no node is at the point.
            var hub = new FakeHubContext();
            var repository = new AndroidRecorderRepository(hub);
            repository.Register("app-1");
            hub.OnSend = (_, _, arguments) => repository.Complete(new AndroidPeekResponseModel { RequestId = ((AndroidPeekRequestModel)arguments[0]).RequestId }, "app-1");

            // Act: peek.
            var result = await repository.GetElementChainAsync(CoordinatePeek, CancellationToken.None);

            // Assert: the caller gets the empty chain every recorder returns.
            Assert.IsNotNull(result);
            Assert.IsEmpty(result.Path);
            Assert.AreEqual(string.Empty, result.Locator);
        }

        [TestMethod(DisplayName = "Verify that a flush with no app connected succeeds at once")]
        public async Task FlushWithoutAppTestAsync()
        {
            // Arrange: a repository with no registered app.
            var repository = new AndroidRecorderRepository(new FakeHubContext());

            // Act and Assert: there is nothing in flight, so the flush succeeds without a send.
            Assert.IsTrue(await repository.FlushAsync(TimeSpan.FromSeconds(1), CancellationToken.None));
        }

        [TestMethod(DisplayName = "Verify that a flush succeeds when the app acknowledges")]
        public async Task FlushAcknowledgedTestAsync()
        {
            // Arrange: an app that acknowledges every flush request.
            var hub = new FakeHubContext();
            var repository = new AndroidRecorderRepository(hub);
            repository.Register("app-1");
            hub.OnSend = (target, _, arguments) => repository.CompleteFlush((string)arguments[0], target);

            // Act: flush.
            var isFlushed = await repository.FlushAsync(TimeSpan.FromSeconds(3), CancellationToken.None);

            // Assert: the flush succeeded through one request to the app.
            Assert.IsTrue(isFlushed);
            Assert.AreEqual("ReceiveFlushRequest", hub.Sent.Single().Method);
        }

        [TestMethod(DisplayName = "Verify that a flush the app never acknowledges times out as false")]
        public async Task FlushTimeoutTestAsync()
        {
            // Arrange: an app that never answers.
            var repository = new AndroidRecorderRepository(new FakeHubContext());
            repository.Register("app-1");

            // Act and Assert: the flush ends at its timeout and reports false instead of hanging.
            Assert.IsFalse(await repository.FlushAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None));
        }

        [TestMethod(DisplayName = "Verify that an app disconnecting during a flush ends it as false")]
        public async Task FlushDisconnectTestAsync()
        {
            // Arrange: an app that disconnects when asked to flush.
            var hub = new FakeHubContext();
            var repository = new AndroidRecorderRepository(hub);
            repository.Register("app-1");
            hub.OnSend = (target, _, _) => repository.Remove(target);

            // Act and Assert: the disconnect completes the flush as unacknowledged at once.
            Assert.IsFalse(await repository.FlushAsync(TimeSpan.FromSeconds(3), CancellationToken.None));
        }

        [TestMethod(DisplayName = "Verify that close waits for the app to disconnect")]
        public async Task CloseRecordersTestAsync()
        {
            // Arrange: an app that disconnects when asked to close.
            var hub = new FakeHubContext();
            var repository = new AndroidRecorderRepository(hub);
            repository.Register("app-1");
            hub.OnSend = (target, _, _) => repository.Remove(target);

            // Act and Assert: close reports success, after one CloseRecorder sent to that app.
            Assert.IsTrue(await repository.CloseRecordersAsync(TimeSpan.FromSeconds(2)));
            Assert.AreEqual(("app-1", "CloseRecorder"), (hub.Sent.Single().Target, hub.Sent.Single().Method));
        }

        [TestMethod(DisplayName = "Verify that close reports an app that stays connected")]
        public async Task CloseRecordersTimeoutTestAsync()
        {
            // Arrange: an app that stays connected.
            var repository = new AndroidRecorderRepository(new FakeHubContext());
            repository.Register("app-1");

            // Act and Assert: close ends at its timeout and reports false, so stop can go on.
            Assert.IsFalse(await repository.CloseRecordersAsync(TimeSpan.FromMilliseconds(200)));
        }

        [TestMethod(DisplayName = "Verify that events are broadcast to every consumer in the shared value envelope")]
        public async Task BroadcastEnvelopeTestAsync()
        {
            // Arrange.
            var hub = new FakeHubContext();
            var service = new AndroidEventCaptureService(hub);
            var recordingEvent = new AndroidEventModel { Event = "InvokeClick", Type = "Mouse" };

            // Act.
            await service.BroadcastRecordingEventAsync(recordingEvent);

            // Assert: the envelope's Value property holds the event itself.
            var (target, method, arguments) = hub.Sent.Single();
            var envelope = arguments[0];
            Assert.AreEqual("all", target);
            Assert.AreEqual("ReceiveRecordingEvent", method);
            Assert.AreSame(recordingEvent, envelope.GetType().GetProperty("Value").GetValue(envelope));
        }
    }
}
