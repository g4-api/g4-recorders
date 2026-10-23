using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Uia.Domain.Hubs
{
    /// <summary>
    /// Counts captured input events from hook enqueue to worker broadcast so a recorder client can wait
    /// until every event captured before its stop request has been sent.
    /// </summary>
    /// <remarks>
    /// The capture service increments the enqueued count on the hook thread and the processed count on the
    /// worker thread after each broadcast attempt. A flush captures the enqueued count once and waits for the
    /// processed count to reach it, so input that arrives after the request never extends the wait.
    /// </remarks>
    internal sealed class RecorderEventQueueState
    {
        #region *** Constants    ***
        /// <summary>
        /// The process-wide state shared by the capture service and the recorder hub.
        /// </summary>
        internal static readonly RecorderEventQueueState Instance = new();

        // Poll interval while waiting; short enough to add no visible delay to a stop request.
        private const int PollMilliseconds = 10;
        #endregion

        #region *** Fields       ***
        // Total events placed on the capture queue since the process started.
        private long _enqueuedCount;

        // Total events the worker has finished handling, including events it skipped or failed to resolve.
        private long _processedCount;
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the total number of events placed on the capture queue.
        /// </summary>
        internal long EnqueuedCount => Interlocked.Read(ref _enqueuedCount);

        /// <summary>
        /// Gets the total number of events the worker has finished handling.
        /// </summary>
        internal long ProcessedCount => Interlocked.Read(ref _processedCount);
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Records one event placed on the capture queue.
        /// </summary>
        internal void AddEnqueued()
        {
            Interlocked.Increment(ref _enqueuedCount);
        }

        /// <summary>
        /// Records one event the worker finished handling.
        /// </summary>
        internal void AddProcessed()
        {
            Interlocked.Increment(ref _processedCount);
        }

        /// <summary>
        /// Waits until every event enqueued before this call has been handled, or until the timeout elapses.
        /// </summary>
        /// <param name="timeout">The longest time to wait for the worker.</param>
        /// <param name="cancellationToken">A token that cancels the wait.</param>
        /// <returns><see langword="true"/> when the queue drained in time; otherwise <see langword="false"/>.</returns>
        internal async Task<bool> WaitForDrainAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Fix the target before waiting so input captured during the wait cannot extend it.
            var target = EnqueuedCount;
            var stopwatch = Stopwatch.StartNew();

            // Poll the worker's progress until it reaches the target or the timeout elapses.
            while (ProcessedCount < target)
            {
                if (stopwatch.Elapsed >= timeout)
                {
                    return false;
                }

                await Task.Delay(millisecondsDelay: PollMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        #endregion
    }
}
