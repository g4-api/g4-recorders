using G4.Recorders.Common.Domain.Models;

using G4.Recorders.Chromium.Domain.Hubs;
using G4.Recorders.Chromium.Domain.Models;

using Microsoft.AspNetCore.SignalR;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Chromium.Domain
{
    /// <summary>
    /// Coordinates Chromium peek requests through the recorder extension's SignalR connection.
    /// </summary>
    /// <remarks>
    /// This service is registered as a singleton because extension registrations and pending correlations span
    /// controller and hub invocations. Exactly one registered extension is required until the public API exposes
    /// an explicit recorder selector.
    /// </remarks>
    /// <param name="hubContext">The hub context used to send requests to the registered extension connection.</param>
    public class ChromiumRecorderRepository(IHubContext<ChromiumRecorderHub> hubContext) : IChromiumRecorderRepository
    {
        #region *** Constants    ***
        // Limits an extension round trip so abandoned browser contexts cannot retain server requests indefinitely.
        private static readonly TimeSpan PeekTimeout = TimeSpan.FromSeconds(5);
        #endregion

        #region *** Fields       ***
        // Tracks SignalR connections that explicitly identified themselves as recorder extensions.
        private readonly ConcurrentDictionary<string, byte> _extensionConnections = new();

        // Sends server-to-extension peek requests without coupling the repository to a live hub instance.
        private readonly IHubContext<ChromiumRecorderHub> _hubContext = hubContext;

        // Owns request correlations until the matching response, cancellation, timeout, or disconnect completes them.
        private readonly ConcurrentDictionary<string, PendingPeekModel> _pendingPeeks = new();
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public bool Complete(ChromiumPeekResponseModel response, string connectionId)
        {
            // Reject malformed or late responses before they can affect an unrelated pending request.
            if (response == null || string.IsNullOrWhiteSpace(response.RequestId))
            {
                return false;
            }

            if (!_pendingPeeks.TryGetValue(response.RequestId, out var pendingPeek))
            {
                return false;
            }

            // Accept results only from the extension connection that received this correlation.
            if (!string.Equals(pendingPeek.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return false;
            }

            // Surface extension execution failures through the original waiting request.
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return pendingPeek.Completion.TrySetException(new InvalidOperationException(response.Error));
            }

            // Normalize an element-not-found response to the same empty chain shape used by the UIA recorder.
            var chain = response.Chain ?? new ChromiumChainModel();

            return pendingPeek.Completion.TrySetResult(chain);
        }

        /// <inheritdoc />
        public async Task<ChromiumChainModel> GetAsync(
            RecorderPeekRequestModel request,
            CancellationToken cancellationToken)
        {
            // Require a normalized request before allocating correlation and cancellation resources.
            ArgumentNullException.ThrowIfNull(
                argument: request,
                paramName: nameof(request));

            // Select one producer explicitly so simultaneous extensions cannot race to complete the same query.
            var connectionIds = _extensionConnections.Keys.ToArray();

            if (connectionIds.Length != 1)
            {
                var message = connectionIds.Length == 0
                    ? "No Chromium recorder extension is connected."
                    : "More than one Chromium recorder extension is connected; the target is ambiguous.";
                throw new InvalidOperationException(message);
            }

            // Allocate a cryptographically strong correlation and register it before sending the request.
            var requestId = Guid.NewGuid().ToString("N");
            var completion = new TaskCompletionSource<ChromiumChainModel>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingPeek = new PendingPeekModel(connectionIds[0], completion);

            if (!_pendingPeeks.TryAdd(requestId, pendingPeek))
            {
                throw new InvalidOperationException("Failed to register the Chromium peek request correlation.");
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(PeekTimeout);

            using var cancellationRegistration = timeoutSource.Token.Register(
                () => completion.TrySetCanceled(timeoutSource.Token));

            try
            {
                // Send the normalized request only to the selected extension connection.
                var extensionRequest = new ChromiumPeekRequestModel
                {
                    Mode = request.Mode,
                    RequestId = requestId,
                    X = request.X,
                    Y = request.Y
                };

                await _hubContext.Clients
                    .Client(connectionIds[0])
                    .SendAsync(
                        method: ChromiumRecorderHub.ReceivePeekRequestClientMethod,
                        arg1: extensionRequest,
                        cancellationToken: timeoutSource.Token)
                    .ConfigureAwait(false);

                // Await the independently returned extension response without blocking a hub or request thread.
                return await completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The Chromium recorder extension did not answer the peek request in time.", exception);
            }
            finally
            {
                // Remove correlation state for every terminal path so late responses are safely ignored.
                _pendingPeeks.TryRemove(requestId, out _);
            }
        }

        /// <inheritdoc />
        public void Register(string connectionId)
        {
            // Require a transport-owned identifier before exposing the connection as an extension target.
            ArgumentException.ThrowIfNullOrWhiteSpace(
                argument: connectionId,
                paramName: nameof(connectionId));

            _extensionConnections[connectionId] = 0;
        }

        /// <inheritdoc />
        public void Remove(string connectionId)
        {
            // Ignore framework teardown without an identifier because it cannot own registered request state.
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            _extensionConnections.TryRemove(connectionId, out _);

            // Fail only correlations sent to this connection so other extension lifecycles remain isolated.
            var disconnectedPeeks = _pendingPeeks
                .Where(item => item.Value.ConnectionId == connectionId)
                .ToArray();

            foreach (var disconnectedPeek in disconnectedPeeks)
            {
                disconnectedPeek.Value.Completion.TrySetException(
                    new InvalidOperationException("The Chromium recorder extension disconnected during the peek request."));
            }
        }
        #endregion

        #region *** Nested Types ***
        // Retains the exact extension connection and completion source that own one in-flight correlation.
        private sealed class PendingPeekModel(
            string connectionId,
            TaskCompletionSource<ChromiumChainModel> completion)
        {
            // Identifies the extension connection selected when the request was sent.
            internal string ConnectionId { get; } = connectionId;

            // Completes the awaiting server caller exactly once when a response or failure arrives.
            internal TaskCompletionSource<ChromiumChainModel> Completion { get; } = completion;
        }
        #endregion
    }
}
