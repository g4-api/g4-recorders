using Microsoft.AspNetCore.SignalR;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Sap.Domain.Middlewares;
using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.Hubs
{
    /// <summary>
    /// SignalR hub for the SAP recorder: heartbeat checks, element peeks, and the recording event stream.
    /// </summary>
    /// <remarks>
    /// Connected clients receive every recording message the capture services broadcast. The first connection starts
    /// a recording generation and the last disconnection ends it, so SAP work and hooks run only while a client
    /// listens.
    /// </remarks>
    /// <param name="repository">The repository that resolves SAP controls and their chains.</param>
    /// <param name="cursorPositionProvider">The provider that samples the physical cursor for current-position peeks.</param>
    /// <param name="statusStore">The store holding the per-session recording status.</param>
    /// <param name="settingsResolver">The resolver that turns a start request into recorder values.</param>
    /// <param name="optionsStore">The store holding the recorder values of the current recording.</param>
    /// <param name="launcher">The launcher that starts SAP Logon when SAP GUI is not running.</param>
    public class SapRecorderHub(
        ISapRecorderRepository repository,
        ISapCursorPositionProvider cursorPositionProvider,
        SapRecordingStatusStore statusStore,
        ISapRecorderSettingsResolver settingsResolver,
        SapRecorderOptionsStore optionsStore,
        ISapLogonLauncher launcher) : Hub
    {
        // Longest time a flush waits for the capture workers before reporting an incomplete drain.
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(3);

        // Shared process state that starts and ends recording generations with the first and last client.
        private readonly RecorderConnectionState _connectionState = RecorderConnectionState.Instance;

        // Shared queue counters let a stopping client wait for events already captured but not yet sent.
        private readonly RecorderEventQueueState _queueState = RecorderEventQueueState.Instance;

        // Samples the physical cursor for current-position peeks.
        private readonly ISapCursorPositionProvider _cursorPositionProvider = cursorPositionProvider;

        // Resolves SAP controls and their chains on the scripting thread.
        private readonly ISapRecorderRepository _repository = repository;

        // Holds the per-session recording status published by the SAP action capture.
        private readonly SapRecordingStatusStore _statusStore = statusStore;

        // Starts SAP Logon when SAP GUI is not running, and stops the instances it started.
        private readonly ISapLogonLauncher _launcher = launcher;

        // Holds the recorder values of the current recording.
        private readonly SapRecorderOptionsStore _optionsStore = optionsStore;

        // Turns start requests into recorder values: request, then environment, then settings.
        private readonly ISapRecorderSettingsResolver _settingsResolver = settingsResolver;

        /// <inheritdoc />
        public override async Task OnConnectedAsync()
        {
            // Complete the SignalR connection before exposing it as an active recording consumer.
            await base.OnConnectedAsync().ConfigureAwait(false);

            // Start or join the current capture generation after the client can receive events.
            _connectionState.AddConnection();
        }

        /// <inheritdoc />
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Remove the client before completing teardown so capture work stops with the final consumer; the last one
            // also ends the recording's values, so the next recording starts from the environment and settings again.
            if (_connectionState.RemoveConnection() == 0)
            {
                _optionsStore.Reset();
            }

            await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
        }

        // Waits until every event captured before this call has been broadcast, so a client that is about to stop
        // recording keeps the trailing input and SAP actions still queued. Returns false when the wait timed out.
        [HubMethodName(name: nameof(FlushRecorder))]
        public Task<bool> FlushRecorder()
        {
            // Bound the wait by the hub call's own lifetime so a disconnecting client releases it.
            return _queueState.WaitForDrainAsync(timeout: FlushTimeout, Context.ConnectionAborted);
        }

        // Returns whether each SAP session reports SAP actions, and why not when it does not.
        [HubMethodName(name: nameof(GetRecorderStatus))]
        public List<SapSessionStatusModel> GetRecorderStatus()
        {
            return _statusStore.GetSessions();
        }

        // Applies a client's values to the current recording: each value comes from the request, then the environment,
        // then appsettings.json. Invalid values, and values that differ from another client's request, are refused.
        // When SAP GUI is not running and a SAP Logon path resolved, SAP Logon is started and its process id returned.
        [HubMethodName(name: nameof(StartRecorder))]
        public SapRecorderStartModel StartRecorder(SapRecorderParametersModel parameters)
        {
            // Resolve the values; invalid stream names come back as a hub error.
            SapRecorderOptionsModel options;
            try
            {
                options = _settingsResolver.Resolve(parameters);
            }
            catch (ArgumentException exception)
            {
                throw new HubException(exception.Message);
            }

            // Apply them, unless another client's request already set different values for this recording.
            if (!_optionsStore.Apply(options))
            {
                const string message = "A recording is already running with different values. " +
                    "Stop the other client, or start with the same values.";
                throw new HubException(message);
            }

            // Make SAP GUI available; a missing executable or a SAP Logon without scripting is a hub error.
            try
            {
                var (processId, isLaunched) = _launcher.Start(options.SapLogon);

                return new SapRecorderStartModel { IsLaunched = isLaunched, Options = options, ProcessId = processId };
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidOperationException or TimeoutException)
            {
                throw new HubException(exception.Message);
            }
        }

        // Stops a SAP Logon that StartRecorder launched; returns false for other processes and ones that already exited.
        [HubMethodName(name: nameof(StopRecorder))]
        public bool StopRecorder(int processId)
        {
            return _launcher.Stop(processId);
        }

        // Sends a heartbeat message to the caller, so clients can verify the connection is alive.
        [HubMethodName(name: nameof(SendHeartbeat))]
        public Task SendHeartbeat()
        {
            // Notify the calling client with a heartbeat message.
            return Clients.Caller.SendAsync(
                method: "ReceiveHeartbeat",
                arg1: new HubResponseModel("Heartbeat received - connection is alive"));
        }

        // Resolves the SAP control at the given screen coordinates and returns its chain to the caller.
        [HubMethodName(name: $"{nameof(SendPeek)}At")]
        public Task SendPeek(RecorderPointModel point)
        {
            // Require the coordinate envelope before resolving.
            ArgumentNullException.ThrowIfNull(argument: point);

            return SendPeekResponse(() => _repository.GetElementChain(x: point.XPos, y: point.YPos));
        }

        // Resolves the focused SAP control and returns its chain to the caller.
        [HubMethodName(name: $"{nameof(SendPeek)}Focused")]
        public Task SendPeek()
        {
            return SendPeekResponse(() => _repository.GetElementChain());
        }

        // Resolves the SAP control at the current physical cursor position and returns its chain to the caller.
        [HubMethodName(name: $"{nameof(SendPeek)}Current")]
        public Task SendPeekCurrent()
        {
            // Require a current physical point so a failed read cannot silently resolve the desktop origin.
            if (!_cursorPositionProvider.GetCurrent(out var point))
            {
                throw new HubException("The current physical cursor position is unavailable.");
            }

            // Reuse coordinate resolution so current-position and explicit-point chains remain identical.
            return SendPeekResponse(() => _repository.GetElementChain(x: point.XPos, y: point.YPos));
        }

        // Resolves a chain and sends it to the caller as ReceivePeek. SAP GUI being unavailable or not answering in time
        // becomes a hub error, so the caller's invoke rejects with a readable message.
        private Task SendPeekResponse(Func<SapChainModel> resolve)
        {
            SapChainModel chain;
            try
            {
                chain = resolve();
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                throw new HubException(exception.Message);
            }

            // Return the chain only to the consumer that requested it.
            return Clients.Caller.SendAsync(method: "ReceivePeek", arg1: new HubResponseModel(chain));
        }

        /// <summary>
        /// Lightweight envelope for hub-to-client messages that carry a single value.
        /// </summary>
        /// <param name="value">The payload to send to the client.</param>
        private sealed class HubResponseModel(object value)
        {
            /// <summary>
            /// The payload carried by this response.
            /// </summary>
            public object Value { get; init; } = value;
        }
    }
}
