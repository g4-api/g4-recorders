using G4.Recorders.Android.Domain.Middlewares;
using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.SignalR;

using System;
using System.IO;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.Hubs
{
    /// <summary>
    /// SignalR hub of the Android recorder, served at <c>/hub/v4/g4/peek</c> like every G4 recorder.
    /// </summary>
    /// <remarks>
    /// Two kinds of client connect. The recorder app registers itself, pushes events and answers requests. Consumers,
    /// such as the G4 Hub, start and stop recordings, flush before stop, peek, and receive <c>ReceiveRecordingEvent</c>.
    /// The method names match the Chromium recorder hub, plus <c>FlushRecorder</c> from the UIA and SAP hubs.
    /// </remarks>
    /// <param name="domain">The launcher and repository.</param>
    /// <param name="eventCapture">Broadcasts the app's events to consumers.</param>
    public sealed class AndroidRecorderHub(IAndroidRecorderDomain domain, IAndroidEventCaptureService eventCapture) : Hub
    {
        #region *** Constants    ***
        /// <summary>
        /// The server-to-app message that asks the recorder app to send its pending input and stop.
        /// </summary>
        public const string CloseRecorderClientMethod = "CloseRecorder";

        /// <summary>
        /// The server-to-app message that asks the recorder app to send its pending input and acknowledge.
        /// </summary>
        public const string ReceiveFlushRequestClientMethod = "ReceiveFlushRequest";

        /// <summary>
        /// The server-to-app message that carries a correlated peek request.
        /// </summary>
        public const string ReceivePeekRequestClientMethod = "ReceivePeekRequest";

        // Longest wait for the recorder app to acknowledge a flush, matching the UIA and SAP hubs.
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);
        #endregion

        #region *** Fields       ***
        // The launcher and repository.
        private readonly IAndroidRecorderDomain _domain = domain;

        // Broadcasts the app's events to consumers.
        private readonly IAndroidEventCaptureService _eventCapture = eventCapture;
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Waits until the recorder app has sent every event it captured before this call.
        /// </summary>
        /// <returns><c>true</c> when the app acknowledged in time, or no app is connected; otherwise, <c>false</c>.</returns>
        [HubMethodName(name: nameof(FlushRecorder))]
        public Task<bool> FlushRecorder()
        {
            // Bound the wait by the caller's connection, so a disconnecting consumer releases it.
            return _domain.Repository.FlushAsync(FlushTimeout, Context.ConnectionAborted);
        }

        /// <summary>
        /// Registers the calling connection as the recorder app.
        /// </summary>
        [HubMethodName(name: nameof(RegisterRecorder))]
        public void RegisterRecorder()
        {
            // Trust the transport's connection id, not caller-supplied identity.
            _domain.Repository.Register(Context.ConnectionId);
        }

        /// <summary>
        /// Acknowledges a flush request; sent by the recorder app after every event it queued before the request.
        /// </summary>
        /// <param name="requestId">The correlation of the flush request.</param>
        [HubMethodName(name: nameof(SendFlushResponse))]
        public void SendFlushResponse(string requestId)
        {
            // Late acknowledgements are ignored by the repository.
            _domain.Repository.CompleteFlush(requestId, Context.ConnectionId);
        }

        /// <summary>
        /// Sends a heartbeat answer to the caller.
        /// </summary>
        /// <returns>A task that completes after the answer is sent.</returns>
        [HubMethodName(name: nameof(SendHeartbeat))]
        public Task SendHeartbeat()
        {
            // Answer the caller only, so a consumer can test its own connection.
            return Clients.Caller.SendAsync(method: "ReceiveHeartbeat", arg1: new HubResponseModel("Heartbeat received - connection is alive"));
        }

        /// <summary>
        /// Resolves the focused node through the recorder app.
        /// </summary>
        /// <returns>A task that completes after the chain is sent to the caller as <c>ReceivePeek</c>.</returns>
        [HubMethodName(name: $"{nameof(SendPeek)}Focused")]
        public Task SendPeek()
        {
            // Select focused lookup, because this method carries no coordinates.
            return SendPeekResultAsync(instance: this, request: RecorderPeekRequestResolver.Resolve(x: null, y: null, focused: true));
        }

        /// <summary>
        /// Resolves the node at screen coordinates through the recorder app.
        /// </summary>
        /// <param name="point">The device screen coordinates.</param>
        /// <returns>A task that completes after the chain is sent to the caller as <c>ReceivePeek</c>.</returns>
        [HubMethodName(name: $"{nameof(SendPeek)}At")]
        public Task SendPeek(RecorderPointModel point)
        {
            ArgumentNullException.ThrowIfNull(argument: point);

            // Normalize explicit coordinates through the same precedence contract REST callers use.
            return SendPeekResultAsync(instance: this, request: RecorderPeekRequestResolver.Resolve(point.XPos, point.YPos, focused: false));
        }

        /// <summary>
        /// Resolves the node of the most recent interaction through the recorder app.
        /// </summary>
        /// <returns>A task that completes after the chain is sent to the caller as <c>ReceivePeek</c>.</returns>
        [HubMethodName(name: $"{nameof(SendPeek)}Current")]
        public Task SendPeekCurrent()
        {
            // Select current-node lookup, because this method carries neither coordinates nor focus intent.
            return SendPeekResultAsync(instance: this, request: RecorderPeekRequestResolver.Resolve(x: null, y: null, focused: false));
        }

        /// <summary>
        /// Completes a pending peek with the recorder app's answer.
        /// </summary>
        /// <param name="response">The correlated chain or failure.</param>
        [HubMethodName(name: nameof(SendPeekResponse))]
        public void SendPeekResponse(AndroidPeekResponseModel response)
        {
            // Late answers are ignored by the repository.
            _domain.Repository.Complete(response, Context.ConnectionId);
        }

        /// <summary>
        /// Relays an event pushed by the recorder app to every consumer.
        /// </summary>
        /// <param name="recordingEvent">The event in the shared contract.</param>
        /// <returns>A task that completes once the event is handed to the transport.</returns>
        [HubMethodName(name: nameof(SendRecordingEvent))]
        public Task SendRecordingEvent(AndroidEventModel recordingEvent)
        {
            // Delegate the fan-out so the broadcast envelope is built in one place.
            return _eventCapture.BroadcastRecordingEventAsync(recordingEvent);
        }

        /// <summary>
        /// Prepares the device and starts recording.
        /// </summary>
        /// <param name="driverParameters">The client's driver parameters with the <c>appium:</c> capabilities.</param>
        /// <returns>The id to pass to <see cref="StopRecorder"/>.</returns>
        /// <exception cref="HubException">adb, the device, the recorder app or the application is unavailable.</exception>
        [HubMethodName(name: nameof(StartRecorder))]
        public async Task<int> StartRecorder(DriverParametersModel driverParameters)
        {
            // Turn readiness failures into a clean hub error, so the caller's invoke rejects with the message.
            try
            {
                return await _domain.Launcher.StartAsync(driverParameters).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TimeoutException)
            {
                throw new HubException(exception.Message);
            }
        }

        /// <summary>
        /// Stops a recording and restores the device.
        /// </summary>
        /// <param name="processId">The id returned by <see cref="StartRecorder"/>.</param>
        /// <returns><c>true</c> when the id belonged to a recording this host started.</returns>
        [HubMethodName(name: nameof(StopRecorder))]
        public Task<bool> StopRecorder(int processId)
        {
            // The launcher never throws on stop; failed restore steps are logged on the host.
            return _domain.Launcher.StopAsync(processId);
        }

        /// <inheritdoc />
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Remove an app registration and fail its pending requests; consumer connections are a no-op.
            _domain.Repository.Remove(Context.ConnectionId);

            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
        }

        // Resolves a peek through the recorder app and sends the chain to the calling consumer only; a missing or
        // ambiguous app and a timeout surface to the caller as hub errors.
        private static async Task SendPeekResultAsync(AndroidRecorderHub instance, RecorderPeekRequestModel request)
        {
            // Wait for the app's answer, bounded by the repository timeout and the caller's connection.
            var cancellationToken = instance.Context.ConnectionAborted;
            var chain = await instance._domain.Repository.GetElementChainAsync(request, cancellationToken).ConfigureAwait(false);

            // Return the chain in the envelope every recorder uses for ReceivePeek.
            await instance.Clients.Caller
                .SendAsync(method: "ReceivePeek", arg1: new HubResponseModel(chain), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        #endregion

        #region *** Nested Types ***
        // Envelope of hub-to-caller messages that carry one value.
        private sealed class HubResponseModel(object value)
        {
            // The payload; any serializable value.
            public object Value { get; init; } = value;
        }
        #endregion
    }
}
