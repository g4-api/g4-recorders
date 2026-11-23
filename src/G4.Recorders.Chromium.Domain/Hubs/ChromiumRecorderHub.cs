using Microsoft.AspNetCore.SignalR;

using System;
using System.IO;
using System.Threading.Tasks;

using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Chromium.Domain.Middlewares;
using G4.Recorders.Chromium.Domain.Models;

namespace G4.Recorders.Chromium.Domain.Hubs
{
    /// <summary>
    /// SignalR hub of the Chromium recorder, served at <c>/hub/v4/g4/peek</c>.
    /// Provides real-time communication for heartbeat checks, DOM ancestor chain
    /// inspection, locator tests, and the relay of recording events and locator amendments.
    /// </summary>
    /// <remarks>
    /// Two kinds of client connect. The recorder extension registers itself, pushes recording events and locator
    /// amendments, and answers correlated peek and locator test requests. Consumers start and stop browsers, peek,
    /// and receive the relayed messages.
    /// </remarks>
    /// <param name="domain">Domain aggregate exposing the launcher and repository.</param>
    /// <param name="eventCapture">Service that broadcasts recorder events to consumers.</param>
    public class ChromiumRecorderHub(IChromiumRecorderDomain domain, IChromiumEventCaptureService eventCapture) : Hub
    {
        #region *** Constants    ***
        /// <summary>
        /// Identifies the server-to-extension method that requests graceful browser closure.
        /// </summary>
        public const string CloseBrowserClientMethod = "CloseBrowser";

        /// <summary>
        /// Identifies the server-to-consumer method that relays a locator amendment sent by the extension.
        /// </summary>
        /// <remarks>
        /// The name is deliberately separate from "ReceiveRecordingEvent", so consumers that only listen to
        /// recording events are unaffected by amendments.
        /// </remarks>
        public const string ReceiveLocatorAmendmentClientMethod = "ReceiveLocatorAmendment";

        /// <summary>
        /// Identifies the server-to-extension method that carries correlated locator test requests.
        /// </summary>
        public const string ReceiveLocatorTestRequestClientMethod = "ReceiveLocatorTestRequest";

        /// <summary>
        /// Identifies the server-to-extension method that carries correlated DOM peek requests.
        /// </summary>
        public const string ReceivePeekRequestClientMethod = "ReceivePeekRequest";
        #endregion

        #region *** Fields       ***
        // Domain aggregate exposing the browser launcher and the repository that owns extension round trips.
        private readonly IChromiumRecorderDomain _domain = domain;

        // Service that owns the broadcast of recorder events to connected consumers.
        private readonly IChromiumEventCaptureService _eventCapture = eventCapture;
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Registers the calling SignalR connection as the recorder extension used for DOM lookup requests.
        /// </summary>
        [HubMethodName(name: nameof(RegisterRecorder))]
        public void RegisterRecorder()
        {
            // Trust the transport-owned connection identifier rather than caller-provided identity data.
            _domain.Repository.Register(Context.ConnectionId);
        }

        /// <summary>
        /// Sends a heartbeat response to the calling client.
        /// </summary>
        /// <returns>A task that completes after the heartbeat is sent to the caller.</returns>
        [HubMethodName(name: nameof(SendHeartbeat))]
        public Task SendHeartbeat()
        {
            // Answer the caller only, so a client can test its own connection without notifying other consumers.
            return Clients.Caller.SendAsync(
                method: "ReceiveHeartbeat",
                arg1: new HubResponseModel("Heartbeat received - connection is alive"));
        }

        /// <summary>
        /// Relays a correction the recorder extension makes to a locator it already sent with a recording event.
        /// </summary>
        /// <remarks>
        /// The capture service re-broadcasts the amendment to every consumer as "ReceiveLocatorAmendment". An
        /// amendment without a locator identifier is ignored.
        /// </remarks>
        /// <param name="amendment">The locator correction produced by the extension.</param>
        /// <returns>A task that completes once the amendment has been handed to the transport.</returns>
        [HubMethodName(name: nameof(SendLocatorAmendment))]
        public Task SendLocatorAmendment(ChromiumLocatorAmendmentModel amendment)
        {
            // Delegate the fan-out to the capture service so the amendment envelope and its message name live
            // in one place beside the recording event broadcast.
            return _eventCapture.BroadcastLocatorAmendmentAsync(amendment);
        }

        /// <summary>
        /// Completes a pending locator test using the correlated response produced by the recorder extension.
        /// </summary>
        /// <param name="response">The extension-produced correlated match summary or failure.</param>
        [HubMethodName(name: nameof(SendLocatorTestResponse))]
        public void SendLocatorTestResponse(ChromiumLocatorTestResponseModel response)
        {
            // Accept only a correlation owned by the singleton repository and sent to this connection; late
            // responses are intentionally ignored.
            _domain.Repository.Complete(response, connectionId: Context.ConnectionId);
        }

        /// <summary>
        /// Resolves the focused Chromium DOM element through the connected extension.
        /// </summary>
        /// <returns>A task that completes after the result is sent to the caller.</returns>
        [HubMethodName(name: $"{nameof(SendPeek)}Focused")]
        public async Task SendPeek()
        {
            // Select focused lookup only because this legacy hub method carries no coordinates.
            var request = RecorderPeekRequestResolver.Resolve(x: null, y: null, focused: true);
            var peekResponse = await _domain.Repository
                .GetElementChainAsync(request, Context.ConnectionAborted)
                .ConfigureAwait(false);

            // Return the extension-produced chain only to the consumer that requested it.
            await Clients.Caller.SendAsync(
                method: "ReceivePeek",
                arg1: new HubResponseModel(peekResponse),
                cancellationToken: Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a Chromium DOM element at viewport-relative coordinates through the connected extension.
        /// </summary>
        /// <param name="point">The active-frame viewport coordinates to resolve.</param>
        /// <returns>A task that completes after the result is sent to the caller.</returns>
        [HubMethodName(name: $"{nameof(SendPeek)}At")]
        public async Task SendPeek(RecorderPointModel point)
        {
            // Require the coordinate envelope before creating the normalized extension request.
            ArgumentNullException.ThrowIfNull(argument: point);

            // Normalize explicit coordinates through the same precedence contract used by REST callers.
            var request = RecorderPeekRequestResolver.Resolve(point.XPos, point.YPos, focused: false);
            var peekResponse = await _domain.Repository
                .GetElementChainAsync(request, Context.ConnectionAborted)
                .ConfigureAwait(false);

            // Return the extension-produced chain only to the consumer that requested it.
            await Clients.Caller.SendAsync(
                method: "ReceivePeek",
                arg1: new HubResponseModel(peekResponse),
                cancellationToken: Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the Chromium DOM element at the extension's latest current pointer position.
        /// </summary>
        /// <returns>A task that completes after the result is sent to the caller.</returns>
        [HubMethodName(name: $"{nameof(SendPeek)}Current")]
        public async Task SendPeekCurrent()
        {
            // Select current-pointer lookup only because this method carries neither coordinates nor focus intent.
            var request = RecorderPeekRequestResolver.Resolve(x: null, y: null, focused: false);
            var peekResponse = await _domain.Repository
                .GetElementChainAsync(request, Context.ConnectionAborted)
                .ConfigureAwait(false);

            // Return the extension-produced chain only to the consumer that requested it.
            await Clients.Caller.SendAsync(
                method: "ReceivePeek",
                arg1: new HubResponseModel(peekResponse),
                cancellationToken: Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Completes a pending server peek using the correlated response produced by the recorder extension.
        /// </summary>
        /// <param name="response">The extension-produced correlated chain or failure.</param>
        [HubMethodName(name: nameof(SendPeekResponse))]
        public void SendPeekResponse(ChromiumPeekResponseModel response)
        {
            // Accept only a correlation owned by the singleton repository; late responses are intentionally ignored.
            _domain.Repository.Complete(response, Context.ConnectionId);
        }

        /// <summary>
        /// Relays a recording event pushed by a producer, such as the Chromium recorder extension, to every
        /// connected consumer.
        /// </summary>
        /// <remarks>
        /// The browser extension cannot host a socket, so it connects as a client and invokes this method. The
        /// capture service re-broadcasts the event using the same "ReceiveRecordingEvent" message and envelope as
        /// the desktop capture service, preserving the UiaPeek contract.
        /// </remarks>
        /// <param name="recordingEvent">The event pushed by the recorder extension.</param>
        /// <returns>A task that completes once the event has been handed to the transport.</returns>
        [HubMethodName(name: nameof(SendRecordingEvent))]
        public Task SendRecordingEvent(ChromiumEventModel recordingEvent)
        {
            // Delegate the fan-out to the capture service so the broadcast envelope lives in one
            // place and stays identical to the UIA broadcast.
            return _eventCapture.BroadcastRecordingEventAsync(recordingEvent);
        }

        /// <summary>
        /// Launches a Chromium browser with the recorder extension loaded and returns its operating-system
        /// process id to the calling client.
        /// </summary>
        /// <remarks>
        /// The caller uses the process id to stop the browser later. This method replaces the former REST-only
        /// start, so start and stop travel over the same SignalR connection that carries recording events.
        /// </remarks>
        /// <param name="driverParameters">The driver parameters that describe the browser to launch.</param>
        /// <returns>The operating-system process id of the launched browser.</returns>
        /// <exception cref="HubException">The browser binary or the recorder extension is unavailable.</exception>
        [HubMethodName(name: nameof(StartRecorder))]
        public int StartRecorder(DriverParametersModel driverParameters)
        {
            // Surface invalid input (missing binary, missing extension) as a HubException so the
            // caller's invoke promise rejects with a clean message instead of a generic failure.
            try
            {
                return _domain.Launcher.Start(driverParameters);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
            {
                throw new HubException(exception.Message);
            }
        }

        /// <summary>
        /// Stops a browser previously started through <see cref="StartRecorder"/>.
        /// </summary>
        /// <param name="processId">The process id returned by <see cref="StartRecorder"/>.</param>
        /// <returns><c>true</c> when the recorder accepted the stop request; otherwise, <c>false</c>.</returns>
        [HubMethodName(name: nameof(StopRecorder))]
        public Task<bool> StopRecorder(int processId)
        {
            // Let the launcher ask the extension to close the browser gracefully first; it forces a kill only
            // when the browser does not exit.
            return _domain.Launcher.StopAsync(processId);
        }

        /// <inheritdoc />
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Remove only recorder registrations; ordinary consumer connections are harmless no-ops.
            _domain.Repository.Remove(Context.ConnectionId);

            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
        }
        #endregion

        #region *** Nested Types ***
        /// <summary>
        /// Lightweight envelope for hub-to-client messages that carry a single value.
        /// </summary>
        /// <param name="value">The payload to send to the client.</param>
        private sealed class HubResponseModel(object value)
        {
            /// <summary>
            /// The payload carried by this response. Using <see cref="object"/> allows
            /// any serializable value (string, number, DTO, etc.).
            /// </summary>
            public object Value { get; init; } = value;
        }
        #endregion
    }
}
