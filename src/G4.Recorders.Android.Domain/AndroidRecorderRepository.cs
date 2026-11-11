using G4.Recorders.Android.Domain.Hubs;
using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.SignalR;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain
{
    /// <summary>
    /// Default <see cref="IAndroidRecorderRepository"/>: tracks the recorder app's connections and correlates its answers.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton because registrations and pending correlations span controller and hub calls. Peek
    /// requires exactly one registered app, like the Chromium recorder requires one extension; flush and close reach
    /// every registered app.
    /// </remarks>
    /// <param name="hubContext">Sends requests to the registered app connections.</param>
    public sealed class AndroidRecorderRepository(IHubContext<AndroidRecorderHub> hubContext) : IAndroidRecorderRepository
    {
        #region *** Constants    ***
        // Limits a peek round trip so an app that went away cannot hold a server request.
        private static readonly TimeSpan PeekTimeout = TimeSpan.FromSeconds(5);

        // Interval at which close checks whether the apps disconnected.
        private static readonly TimeSpan ClosePollInterval = TimeSpan.FromMilliseconds(100);
        #endregion

        #region *** Fields       ***
        // Connections that registered as recorder apps.
        private readonly ConcurrentDictionary<string, byte> _appConnections = new();

        // Pending flushes by correlation; each completes when its app acknowledges or disconnects.
        private readonly ConcurrentDictionary<string, PendingRequest<bool>> _pendingFlushes = new();

        // Pending peeks by correlation; each completes when its app answers, fails, or disconnects.
        private readonly ConcurrentDictionary<string, PendingRequest<AndroidChainModel>> _pendingPeeks = new();

        // Sends requests without holding a live hub instance.
        private readonly IHubContext<AndroidRecorderHub> _hubContext = hubContext;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public async Task<bool> CloseRecordersAsync(TimeSpan timeout)
        {
            // Nothing to close when no app is connected.
            if (_appConnections.IsEmpty)
            {
                return true;
            }

            // Ask every registered app to close; each commits its pending input and disconnects.
            var connectionIds = _appConnections.Keys.ToArray();
            await _hubContext.Clients
                .Clients(connectionIds)
                .SendAsync(method: AndroidRecorderHub.CloseRecorderClientMethod)
                .ConfigureAwait(false);

            // Wait for the disconnects, bounded so a stuck app cannot block the stop.
            using var timeoutSource = new CancellationTokenSource(timeout);

            while (connectionIds.Any(_appConnections.ContainsKey))
            {
                try
                {
                    await Task.Delay(ClosePollInterval, timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public bool Complete(AndroidPeekResponseModel response, string connectionId)
        {
            // Ignore malformed and late answers before they can touch another request.
            if (response is null || string.IsNullOrWhiteSpace(response.RequestId))
            {
                return false;
            }

            var isOwned = _pendingPeeks.TryGetValue(response.RequestId, out var pending)
                && string.Equals(pending.ConnectionId, connectionId, StringComparison.Ordinal);

            if (!isOwned)
            {
                return false;
            }

            // Surface an app-side failure through the waiting request.
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return pending.Completion.TrySetException(new InvalidOperationException(response.Error));
            }

            // A node-not-found answer becomes the empty chain every recorder returns.
            return pending.Completion.TrySetResult(response.Chain ?? new AndroidChainModel());
        }

        /// <inheritdoc />
        public bool CompleteFlush(string requestId, string connectionId)
        {
            // Ignore an empty or unknown correlation, which a late acknowledgement after the timeout produces.
            if (string.IsNullOrWhiteSpace(requestId) || !_pendingFlushes.TryGetValue(requestId, out var pending))
            {
                return false;
            }

            // Accept the acknowledgement only from the connection the flush was sent to.
            var isOwned = string.Equals(pending.ConnectionId, connectionId, StringComparison.Ordinal);

            return isOwned && pending.Completion.TrySetResult(true);
        }

        /// <inheritdoc />
        public async Task<bool> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            // An idle host has nothing in flight.
            var connectionIds = _appConnections.Keys.ToArray();

            if (connectionIds.Length == 0)
            {
                return true;
            }

            // Register one correlation per app before any request goes out.
            var requests = connectionIds
                .Select(connectionId => (RequestId: Guid.NewGuid().ToString("N"), Pending: new PendingRequest<bool>(connectionId)))
                .ToArray();

            foreach (var (requestId, pending) in requests)
            {
                _pendingFlushes[requestId] = pending;
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                // Each app commits its pending input and answers after every event it queued before the request.
                foreach (var (requestId, pending) in requests)
                {
                    await _hubContext.Clients
                        .Client(pending.ConnectionId)
                        .SendAsync(method: AndroidRecorderHub.ReceiveFlushRequestClientMethod, arg1: requestId, cancellationToken: timeoutSource.Token)
                        .ConfigureAwait(false);
                }

                // A disconnect completes its correlation with false, so a closed app never stalls the flush.
                var acknowledgements = requests.Select(i => i.Pending.Completion.Task);
                var results = await Task
                    .WhenAll(acknowledgements)
                    .WaitAsync(timeoutSource.Token)
                    .ConfigureAwait(false);

                return results.All(isAcknowledged => isAcknowledged);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The wait ended before every app answered; the caller still exports what arrived.
                return false;
            }
            finally
            {
                // Remove every correlation so late acknowledgements are ignored.
                foreach (var (requestId, _) in requests)
                {
                    _pendingFlushes.TryRemove(requestId, out _);
                }
            }
        }

        /// <inheritdoc />
        public async Task<AndroidChainModel> GetElementChainAsync(RecorderPeekRequestModel request, CancellationToken cancellationToken)
        {
            // Require the normalized request before allocating correlation state.
            ArgumentNullException.ThrowIfNull(argument: request);

            // Pick one app explicitly, so two connected apps cannot race to answer the same request.
            var connectionIds = _appConnections.Keys.ToArray();

            if (connectionIds.Length != 1)
            {
                var message = connectionIds.Length == 0
                    ? "No Android recorder app is connected."
                    : "More than one Android recorder app is connected; the target is ambiguous.";
                throw new InvalidOperationException(message);
            }

            // Register the correlation before the request goes out.
            var requestId = Guid.NewGuid().ToString("N");
            var pending = new PendingRequest<AndroidChainModel>(connectionIds[0]);

            if (!_pendingPeeks.TryAdd(requestId, pending))
            {
                throw new InvalidOperationException("Failed to register the Android peek request correlation.");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(PeekTimeout);
            using var registration = timeoutSource.Token.Register(() => pending.Completion.TrySetCanceled(timeoutSource.Token));

            try
            {
                // Send the normalized request to the selected app only, then wait for its answer.
                var appRequest = new AndroidPeekRequestModel
                {
                    Mode = request.Mode,
                    RequestId = requestId,
                    X = request.X,
                    Y = request.Y
                };
                await _hubContext.Clients
                    .Client(connectionIds[0])
                    .SendAsync(method: AndroidRecorderHub.ReceivePeekRequestClientMethod, arg1: appRequest, cancellationToken: timeoutSource.Token)
                    .ConfigureAwait(false);

                return await pending.Completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The Android recorder app did not answer the peek request in time.", exception);
            }
            finally
            {
                // Remove the correlation on every path so a late answer is ignored.
                _pendingPeeks.TryRemove(requestId, out _);
            }
        }

        /// <inheritdoc />
        public RecorderScreenshotModel GetScreenshot(bool metricsOnly)
        {
            // The screen belongs to the device, which only the launcher reaches; the observe route asks the launcher.
            throw new NotImplementedException("The Android recorder captures the device screen through the launcher; use GET observe.");
        }

        /// <inheritdoc />
        public RecorderPointModel MovePointer(int x, int y)
        {
            // Android has no pointer to move; a tap is an action, not a pointer position.
            throw new NotImplementedException("MovePointer is not implemented for the Android recorder surface yet.");
        }

        /// <inheritdoc />
        public void Register(string connectionId)
        {
            // Require the transport's own id before the connection becomes a request target.
            ArgumentException.ThrowIfNullOrWhiteSpace(argument: connectionId);

            // Register idempotently; a reconnecting app registers its new id the same way.
            _appConnections[connectionId] = 0;
        }

        /// <inheritdoc />
        public void Remove(string connectionId)
        {
            // Consumer connections were never registered; their teardown is a no-op.
            if (string.IsNullOrWhiteSpace(connectionId) || !_appConnections.TryRemove(connectionId, out _))
            {
                return;
            }

            // Fail this app's peeks and complete its flushes as unacknowledged; other apps are not affected.
            foreach (var pending in _pendingPeeks.Values.Where(i => i.ConnectionId == connectionId))
            {
                pending.Completion.TrySetException(new InvalidOperationException("The Android recorder app disconnected during the peek request."));
            }

            foreach (var pending in _pendingFlushes.Values.Where(i => i.ConnectionId == connectionId))
            {
                pending.Completion.TrySetResult(false);
            }
        }

        /// <inheritdoc />
        public Task<AndroidChainModel> ResolveGroundedElementAsync(int x, int y, bool skipOffset, CancellationToken cancellationToken)
        {
            // Grounding needs a pointer and an offset model the Android surface does not have yet.
            throw new NotImplementedException("ResolveGroundedElementAsync is not implemented for the Android recorder surface yet.");
        }

        /// <inheritdoc />
        public RecorderWindowFocusModel SetWindowFocus(string windowTitle, string processName)
        {
            // Android has no window titles to focus; the launcher starts the application instead.
            throw new NotImplementedException("SetWindowFocus is not implemented for the Android recorder surface yet.");
        }
        #endregion

        #region *** Nested Types ***
        // One in-flight request: the app connection it was sent to and the completion its answer resolves.
        private sealed class PendingRequest<T>(string connectionId)
        {
            // The connection the request was sent to; only its answer is accepted.
            internal string ConnectionId { get; } = connectionId;

            // Completes once with the answer, a failure, or a cancellation.
            internal TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        #endregion
    }
}
