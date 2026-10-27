using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Threading;
using System.Threading.Tasks;

using G4.Recorders.Sap.Domain.Hubs;

namespace G4.Recorders.Sap.Domain.UnitTests.Hubs
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(RecorderEventQueueState))]
    [TestCategory("UnitTest")]
    public sealed class RecorderEventQueueStateTests
    {
        [TestMethod(DisplayName = "Verify that a flush completes once the worker handles every earlier event")]
        public async Task QueueStateDrainCompletesTest()
        {
            // Arrange: enqueue two events that the worker has not handled yet.
            var state = new RecorderEventQueueState();
            state.AddEnqueued();
            state.AddEnqueued();
            var timeout = TimeSpan.FromSeconds(5);

            // Act: start the flush, then let the worker handle both events.
            var drain = state.WaitForDrainAsync(timeout, CancellationToken.None);
            state.AddProcessed();
            state.AddProcessed();
            var isDrained = await drain;

            // Assert: verify that the flush reports a complete drain.
            Assert.IsTrue(condition: isDrained);
        }

        [TestMethod(DisplayName = "Verify that events enqueued after a flush starts do not extend it")]
        public async Task QueueStateLaterEventsTest()
        {
            // Arrange: enqueue one event before the flush starts.
            var state = new RecorderEventQueueState();
            state.AddEnqueued();
            var timeout = TimeSpan.FromSeconds(5);

            // Act: start the flush, add new input, then handle only the earlier event.
            var drain = state.WaitForDrainAsync(timeout, CancellationToken.None);
            state.AddEnqueued();
            state.AddProcessed();
            var isDrained = await drain;

            // Assert: verify that the flush waits only for events captured before it.
            Assert.IsTrue(condition: isDrained);
        }

        [TestMethod(DisplayName = "Verify that a flush reports a timeout when the worker stalls")]
        public async Task QueueStateTimeoutTest()
        {
            // Arrange: enqueue one event the worker never handles.
            var state = new RecorderEventQueueState();
            state.AddEnqueued();
            var timeout = TimeSpan.FromMilliseconds(50);

            // Act: wait for a drain that cannot happen.
            var isDrained = await state.WaitForDrainAsync(timeout, CancellationToken.None);

            // Assert: verify that the flush gives up instead of blocking the stop request.
            Assert.IsFalse(condition: isDrained);
        }
    }
}
