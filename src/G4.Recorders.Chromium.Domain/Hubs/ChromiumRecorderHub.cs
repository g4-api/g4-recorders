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
    /// SignalR hub for handling UI Automation (UIA) peek operations.
    /// Provides real-time communication for heartbeat checks and
    /// ancestor chain inspection at specific screen coordinates.
    /// </summary>
    /// <param name="domain">Domain aggregate exposing the launcher and repository.</param>
    /// <param name="eventCapture">Service that broadcasts recorder events to consumers.</param>
    public class ChromiumRecorderHub(IChromiumRecorderDomain domain, IChromiumEventCaptureService eventCapture) : Hub
    {
        /// <summary>
        /// Identifies the server-to-extension method that requests graceful browser closure.
        /// </summary>
        public const string CloseBrowserClientMethod = "CloseBrowser";

        /// <summary>
        /// Identifies the server-to-extension method that carries correlated DOM peek requests.
        /// </summary>
        public const string ReceivePeekRequestClientMethod = "ReceivePeekRequest";

        // Domain aggregate exposing the repository used for querying UIA elements at coordinates.
        private readonly IChromiumRecorderDomain _domain = domain;

        // Service that owns the broadcast of recorder events to connected consumers.
        private readonly IChromiumEventCaptureService _eventCapture = eventCapture;

        /// <inheritdoc />
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Remove only recorder registrations; ordinary consumer connections are harmless no-ops.
            _domain.Repository.Remove(Context.ConnectionId);

            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
        }

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
        [HubMethodName(name: nameof(SendHeartbeat))]
        public Task SendHeartbeat()
        {
            // Notify the calling client with a heartbeat message.
            return Clients.Caller.SendAsync(
                method: "ReceiveHeartbeat",
                arg1: new HubResponseModel("Heartbeat received - connection is alive"));
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
                .GetAsync(request, Context.ConnectionAborted)
                .ConfigureAwait(false);

            // Return the extension-produced chain only to the consumer that requested it.
            await Clients.Caller.SendAsync(
                method: "ReceivePeek",
                arg1: new HubResponseModel(peekResponse),
                cancellationToken: Context.ConnectionAborted)
                .ConfigureAwait(false);
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
                .GetAsync(request, Context.ConnectionAborted)
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
                .GetAsync(request, Context.ConnectionAborted)
                .ConfigureAwait(false);

            // Return the extension-produced chain only to the consumer that requested it.
            await Clients.Caller.SendAsync(
                method: "ReceivePeek",
                arg1: new HubResponseModel(peekResponse),
                cancellationToken: Context.ConnectionAborted)
                .ConfigureAwait(false);
        }

        // Relays a recording event pushed by a producer (for example, the Chromium
        // recorder extension) to every connected consumer. The browser extension cannot
        // host a socket, so it connects as a client and invokes this method; the capture
        // service re-broadcasts the event using the same "ReceiveRecordingEvent" message and
        // envelope as the desktop capture service, preserving the UiaPeek contract.
        [HubMethodName(name: nameof(SendRecordingEvent))]
        public Task SendRecordingEvent(ChromiumEventModel recordingEvent)
        {
            // Delegate the fan-out to the capture service so the broadcast envelope lives in one
            // place and stays identical to the UIA broadcast.
            return _eventCapture.BroadcastRecordingEventAsync(recordingEvent);
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

        // Launches a Chromium browser with the recorder extension loaded and returns its
        // operating-system process id to the calling client, which uses it to stop the browser
        // later. Replaces the former REST endpoint so start/stop travel over the same SignalR
        // connection that carries recording events.
        [HubMethodName(name: nameof(StartRecorder))]
        public int StartRecorder(DriverParametersModel driverParameters)
        {
            // Surface invalid input (missing binary, missing extension) as a HubException so the
            // caller's invoke promise rejects with a clean message instead of a generic failure.
            try
            {
                return _domain.Launcher.Start(driverParameters);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
            {
                throw new HubException(e.Message);
            }
        }

        // Stops a browser previously started through StartRecorder. The launcher first asks the
        // extension to close the browser gracefully, then forces a kill if it does not exit.
        [HubMethodName(name: nameof(StopRecorder))]
        public Task<bool> StopRecorder(int processId)
        {
            return _domain.Launcher.StopAsync(processId);
        }

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
    }
}
