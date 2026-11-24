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
    /// Coordinates Chromium peek and locator test requests through the recorder extension's SignalR connection.
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
        // Limits a locator test round trip so abandoned browser contexts cannot retain server requests indefinitely.
        private static readonly TimeSpan LocatorTestTimeout = TimeSpan.FromSeconds(5);

        // Limits an extension round trip so abandoned browser contexts cannot retain server requests indefinitely.
        private static readonly TimeSpan PeekTimeout = TimeSpan.FromSeconds(5);
        #endregion

        #region *** Fields       ***
        // Tracks SignalR connections that explicitly identified themselves as recorder extensions.
        private readonly ConcurrentDictionary<string, byte> _extensionConnections = new();

        // Sends server-to-extension requests without coupling the repository to a live hub instance.
        private readonly IHubContext<ChromiumRecorderHub> _hubContext = hubContext;

        // Owns locator test correlations until the matching response, cancellation, timeout, or disconnect ends them.
        private readonly ConcurrentDictionary<string, PendingLocatorTestModel> _pendingLocatorTests = new();

        // Owns peek correlations until the matching response, cancellation, timeout, or disconnect completes them.
        private readonly ConcurrentDictionary<string, PendingPeekModel> _pendingPeeks = new();
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public bool Complete(ChromiumPeekResponseModel response, string connectionId)
        {
            // Reject malformed or late responses before they can affect an unrelated pending request.
            var isMalformed = response == null || string.IsNullOrWhiteSpace(response.RequestId);

            if (isMalformed)
            {
                return false;
            }

            // Ignore an unknown or late correlation, which an answer after the timeout produces.
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
        public bool Complete(ChromiumLocatorTestResponseModel response, string connectionId)
        {
            // Reject a malformed response before it can affect an unrelated pending locator test.
            var isMalformed = response == null || string.IsNullOrWhiteSpace(response.RequestId);

            if (isMalformed)
            {
                return false;
            }

            // Ignore an unknown or late correlation, which an answer after the timeout produces.
            if (!_pendingLocatorTests.TryGetValue(response.RequestId, out var pendingLocatorTest))
            {
                return false;
            }

            // Accept results only from the extension connection that received this correlation.
            if (!string.Equals(pendingLocatorTest.ConnectionId, connectionId, StringComparison.Ordinal))
            {
                return false;
            }

            // Surface extension execution failures through the original waiting request.
            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                return pendingLocatorTest.Completion.TrySetException(new InvalidOperationException(response.Error));
            }

            // Normalize an omitted match list so the waiting request always maps a stable, enumerable collection.
            response.Matches ??= [];

            return pendingLocatorTest.Completion.TrySetResult(response);
        }

        /// <inheritdoc />
        public async Task<ChromiumChainModel> GetElementChainAsync(
            RecorderPeekRequestModel request,
            CancellationToken cancellationToken)
        {
            // Require a normalized request before allocating correlation and cancellation resources.
            ArgumentNullException.ThrowIfNull(argument: request);

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
            var pendingPeek = new PendingPeekModel(connectionId: connectionIds[0], completion);

            if (!_pendingPeeks.TryAdd(requestId, pendingPeek))
            {
                throw new InvalidOperationException("Failed to register the Chromium peek request correlation.");
            }

            // Bound the round trip by the peek timeout and the caller's token so an unanswered request is released.
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
                // Report the elapsed timeout distinctly; a caller-requested cancellation keeps its own exception.
                throw new TimeoutException("The Chromium recorder extension did not answer the peek request in time.", exception);
            }
            finally
            {
                // Remove correlation state for every terminal path so late responses are safely ignored.
                _pendingPeeks.TryRemove(requestId, out _);
            }
        }

        /// <inheritdoc />
        public RecorderScreenshotModel GetScreenshot(bool metricsOnly)
        {
            // Report unimplemented rather than capturing a screen this host is not guaranteed to have; this
            // project targets plain net10.0 (not net10.0-windows), so it is not guaranteed to run on Windows.
            throw new NotImplementedException("GetScreenshot is not implemented for the Chromium recorder surface yet.");
        }

        /// <inheritdoc />
        public RecorderPointModel MovePointer(int x, int y)
        {
            // Report unimplemented rather than guessing at a physical-cursor mapping for a browser viewport
            // coordinate; the Chromium surface may not even run on a machine with a real desktop cursor.
            throw new NotImplementedException("MovePointer is not implemented for the Chromium recorder surface yet.");
        }

        /// <inheritdoc />
        public void Register(string connectionId)
        {
            // Require a transport-owned identifier before exposing the connection as an extension target.
            ArgumentException.ThrowIfNullOrWhiteSpace(argument: connectionId);

            // Register idempotently so a reconnecting extension becomes a request target under its new identifier.
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

            // Stop selecting the connection as a request target before failing what was already sent to it.
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

            // Fail the locator tests of this connection the same way so their callers stop waiting for the timeout.
            var disconnectedLocatorTests = _pendingLocatorTests
                .Where(item => item.Value.ConnectionId == connectionId)
                .ToArray();

            foreach (var disconnectedLocatorTest in disconnectedLocatorTests)
            {
                disconnectedLocatorTest.Value.Completion.TrySetException(
                    new InvalidOperationException("The Chromium recorder extension disconnected during the locator test."));
            }
        }

        /// <inheritdoc />
        public Task<ChromiumChainModel> ResolveGroundedElementAsync(int x, int y, bool skipOffset, CancellationToken cancellationToken)
        {
            // Report unimplemented rather than approximating pointer-move-and-offset behavior against a DOM
            // element without the physical screen mapping the UIA surface already has.
            throw new NotImplementedException("ResolveGroundedElementAsync is not implemented for the Chromium recorder surface yet.");
        }

        /// <inheritdoc />
        public RecorderWindowFocusModel SetWindowFocus(string windowTitle, string processName)
        {
            // Report unimplemented rather than guessing at browser-side window focus behavior; the Chromium
            // extension surface has no equivalent of the UIA host's own OS-level window handle to focus.
            throw new NotImplementedException("SetWindowFocus is not implemented for the Chromium recorder surface yet.");
        }

        /// <inheritdoc />
        public async Task<ChromiumLocatorTestResultModel> TestLocatorAsync(
            string @using,
            string value,
            CancellationToken cancellationToken)
        {
            // Assert the complete locator contract before allocating correlation and cancellation resources.
            AssertArguments(@using, value);

            // Select one producer explicitly so simultaneous extensions cannot race to complete the same test.
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
            var completion = new TaskCompletionSource<ChromiumLocatorTestResponseModel>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var pendingLocatorTest = new PendingLocatorTestModel(connectionId: connectionIds[0], completion);

            if (!_pendingLocatorTests.TryAdd(requestId, pendingLocatorTest))
            {
                throw new InvalidOperationException("Failed to register the Chromium locator test request correlation.");
            }

            // Bound the round trip by the test timeout and the caller's token so an unanswered request is released.
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(LocatorTestTimeout);

            using var cancellationRegistration = timeoutSource.Token.Register(
                () => completion.TrySetCanceled(timeoutSource.Token));

            // Declared before the guarded round trip because the response is mapped only after the correlation cleanup.
            ChromiumLocatorTestResponseModel response;

            try
            {
                // Send the locator only to the selected extension connection.
                var extensionRequest = new ChromiumLocatorTestRequestModel
                {
                    RequestId = requestId,
                    Using = @using,
                    Value = value
                };

                await _hubContext.Clients
                    .Client(connectionIds[0])
                    .SendAsync(
                        method: ChromiumRecorderHub.ReceiveLocatorTestRequestClientMethod,
                        arg1: extensionRequest,
                        cancellationToken: timeoutSource.Token)
                    .ConfigureAwait(false);

                // Await the independently returned extension response without blocking a hub or request thread.
                response = await completion.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Report the elapsed timeout distinctly; a caller-requested cancellation keeps its own exception.
                throw new TimeoutException("The Chromium recorder extension did not answer the locator test in time.", exception);
            }
            finally
            {
                // Remove correlation state for every terminal path so late responses are safely ignored.
                _pendingLocatorTests.TryRemove(requestId, out _);
            }

            // Echo the tested locator beside the extension's answer so the caller reads one self-describing result.
            return new ChromiumLocatorTestResultModel
            {
                Count = response.Count,
                Matches = response.Matches,
                Using = @using,
                Value = value
            };

            // Asserts both locator members before the parent method allocates a correlation or performs I/O.
            // The helper does not mutate caller data and reports each invalid parameter through a guard exception.
            static void AssertArguments(string @using, string value)
            {
                // Require the strategy and the expression because the extension cannot evaluate a locator without both.
                // The strategy guard names its parameter explicitly so the diagnostic reads "using", not "@using".
                ArgumentException.ThrowIfNullOrWhiteSpace(argument: @using, paramName: nameof(@using));
                ArgumentException.ThrowIfNullOrWhiteSpace(argument: value);
            }
        }
        #endregion

        #region *** Nested Types ***
        // Retains the exact extension connection and completion source that own one in-flight locator test.
        private sealed class PendingLocatorTestModel(
            string connectionId,
            TaskCompletionSource<ChromiumLocatorTestResponseModel> completion)
        {
            // Identifies the extension connection selected when the locator test was sent.
            internal string ConnectionId { get; } = connectionId;

            // Completes the awaiting server caller exactly once when a response or failure arrives.
            internal TaskCompletionSource<ChromiumLocatorTestResponseModel> Completion { get; } = completion;
        }

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
