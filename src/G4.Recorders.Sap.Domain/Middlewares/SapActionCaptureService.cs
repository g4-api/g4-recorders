using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using G4.Recorders.Sap.Domain.Hubs;
using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Captures SAP actions from <c>GuiSession.Change</c> events and broadcasts them as the <c>nativeSap</c> stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once a second, the service synchronizes the session subscriptions on the scripting thread: while a hub client is
    /// connected, every open session is subscribed and set to recording mode; once the last client disconnects, every
    /// session is unsubscribed and its previous <c>Record</c> value restored. Events arrive on the scripting thread and
    /// are sent at once, each in a message whose <c>nativeSap</c> key is set.
    /// </para>
    /// <para>
    /// The service never logs event values, because they hold what the user typed.
    /// </para>
    /// </remarks>
    /// <param name="scriptingHost">The host whose scripting thread owns the subscriptions.</param>
    /// <param name="broadcaster">The broadcaster that sends recording messages to hub clients.</param>
    /// <param name="statusStore">The store that publishes the per-session recording status.</param>
    /// <param name="optionsStore">The store holding the recorder values: stream, record-mode permission, and scope.</param>
    /// <param name="logger">The logger for subscription failures; it never receives event values.</param>
    public sealed class SapActionCaptureService(
        ISapScriptingHost scriptingHost,
        ISapRecordingBroadcaster broadcaster,
        SapRecordingStatusStore statusStore,
        SapRecorderOptionsStore optionsStore,
        ILogger<SapActionCaptureService> logger) : BackgroundService
    {
        #region *** Constants    ***
        // Spaces synchronization passes; new sessions start recording within this interval.
        private static readonly TimeSpan SynchronizeInterval = TimeSpan.FromSeconds(1);

        // Bounds how long shutdown waits for the scripting thread to restore recording mode.
        private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
        #endregion

        #region *** Fields       ***
        // Sends recording messages to hub clients.
        private readonly ISapRecordingBroadcaster _broadcaster = broadcaster;

        // Tells whether a hub client is listening.
        private readonly RecorderConnectionState _connectionState = RecorderConnectionState.Instance;

        // Logs subscription failures without values.
        private readonly ILogger<SapActionCaptureService> _logger = logger;

        // Holds the recorder values in force for the current recording.
        private readonly SapRecorderOptionsStore _optionsStore = optionsStore;

        // Counts events from arrival to broadcast, for flush requests.
        private readonly RecorderEventQueueState _queueState = RecorderEventQueueState.Instance;

        // Runs every subscription change on the scripting thread.
        private readonly ISapScriptingHost _scriptingHost = scriptingHost;

        // Publishes the per-session recording status.
        private readonly SapRecordingStatusStore _statusStore = statusStore;

        // Owns the session subscriptions; created on first use, on the scripting thread.
        private SapSessionSubscriptions _subscriptions;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Synchronize periodically until the host stops.
                while (!stoppingToken.IsCancellationRequested)
                {
                    _scriptingHost.Post(Synchronize);
                    await Task.Delay(SynchronizeInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopping is the normal way out of the loop.
            }

            // Restore every session's recording mode before the process ends.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _scriptingHost.Post(application =>
            {
                _subscriptions?.DetachAll(isRestoring: application is not null);
                completion.TrySetResult();
            });

            try
            {
                await completion.Task.WaitAsync(ShutdownTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // A hung SAP call keeps the scripting thread busy; the sessions keep recording mode until SAP Logon exits.
                _logger.LogWarning("SAP recording mode was not restored within {Timeout}.", ShutdownTimeout);
            }
        }

        // Publishes one mapped event as a nativeSap message, counting it for flush requests.
        private void Publish(SapEventModel recordingEvent)
        {
            // Count the event before sending, so a flush issued meanwhile waits for it.
            _queueState.AddEnqueued();
            try
            {
                _ = _broadcaster.SendAsync(new SapRecordingMessageModel { NativeSap = recordingEvent });
            }
            finally
            {
                _queueState.AddProcessed();
            }
        }

        // Subscribes to a session's events through its COM object.
        private static IDisposable Subscribe(ISapGuiObject session, Action<int, object[]> handler)
        {
            return SapEventSink.Advise(session.ComObject, handler);
        }

        // One synchronization pass on the scripting thread: attach or detach according to the connected clients.
        private void Synchronize(ISapGuiObject application)
        {
            // Create the subscriptions on the thread that will own them.
            _subscriptions ??= new SapSessionSubscriptions(Subscribe, Publish);

            try
            {
                // Without a client, without SAP GUI, or with nativeSap switched off, release everything, restoring Record
                // when SAP GUI still answers.
                var options = _optionsStore.Current;
                var isCapturing = _connectionState.CaptureActive && options.TestStream(SapRecordingStreams.NativeSap);
                if (!isCapturing || application is null)
                {
                    _subscriptions.DetachAll(isRestoring: application is not null);
                }
                else
                {
                    _subscriptions.Synchronize(SapGuiNavigator.GetSessions(application), options);
                }
            }
            catch (COMException exception)
            {
                // SAP Logon exiting mid-pass; the next pass starts over.
                _logger.LogWarning("Skipped a SAP session synchronization: {Reason}", exception.Message);
            }

            // Publish the status after every pass, so clients see refusals and new sessions within a second.
            _statusStore.SetSessions(_subscriptions.GetStatus());
        }
        #endregion
    }
}
