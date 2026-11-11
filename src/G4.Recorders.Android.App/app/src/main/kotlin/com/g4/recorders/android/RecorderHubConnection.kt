package com.g4.recorders.android

import android.util.Log
import com.microsoft.signalr.Action
import com.microsoft.signalr.Action1
import com.microsoft.signalr.HubConnection
import com.microsoft.signalr.HubConnectionBuilder
import com.microsoft.signalr.HubConnectionState
import com.microsoft.signalr.OnClosedCallback
import com.microsoft.signalr.TransportEnum
import io.reactivex.rxjava3.core.Completable
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit

/**
 * The app's SignalR connection to the Android recorder host, the counterpart of the Chrome extension's
 * `signalr-connection.js`.
 *
 * The app never hosts a socket. It connects to the host hub as a client over WebSockets without negotiation,
 * registers itself with `RegisterRecorder` after every connect so the host can route peek and flush requests to
 * it, and invokes `SendRecordingEvent` for every event.
 *
 * One single-threaded executor runs the connect, every send, and every response. That keeps three guarantees:
 * events reach the host in the order they were recorded, nothing is sent before registration, and a flush
 * response is sent only after every event queued before the flush request.
 *
 * Events recorded while the connection is down are dropped, like the extension does, so a stale backlog never
 * flushes into a later session.
 *
 * @property listener Receives the host's requests on the executor thread.
 */
class RecorderHubConnection(private val listener: Listener) {
    // Runs connects, sends and responses one at a time in submission order.
    private val executor: ScheduledExecutorService = Executors.newSingleThreadScheduledExecutor()

    // The live connection, or null while stopped; only the executor thread reads or replaces it.
    private var connection: HubConnection? = null

    // The hub the connection targets; a change restarts the connection.
    private var hubUrl: String = ""

    // Bumped on every start and stop, so a close callback of an old connection cannot schedule a reconnect.
    @Volatile
    private var generation = 0

    // Index into RECONNECT_DELAYS for the next retry.
    private var reconnectAttempt = 0

    /**
     * Receives requests the host sends to the app.
     */
    interface Listener {
        /** The host asks the recorder to stop: `CloseRecorder`. */
        fun onCloseRequested()

        /**
         * The host asks the recorder to send every pending event: `ReceiveFlushRequest`.
         *
         * @param requestId The correlation to return through [sendFlushResponse].
         */
        fun onFlushRequested(requestId: String)

        /**
         * The host asks for the element chain at a point, the focused element, or the last touched element.
         *
         * @param request The correlated request to answer through [sendPeekResponse].
         */
        fun onPeekRequested(request: PeekRequestModel)
    }

    /**
     * Opens the connection to a hub, replacing any connection to another hub.
     *
     * @param url The hub URL, for example `http://localhost:9958/hub/v4/g4/peek`.
     */
    fun start(url: String) {
        // A new generation invalidates retries scheduled for the previous connection.
        val startGeneration = ++generation

        executor.execute {
            // Close a connection to another hub before opening the new one.
            if (connection != null && hubUrl != url) {
                closeConnection()
            }

            hubUrl = url
            reconnectAttempt = 0
            connect(startGeneration)
        }
    }

    /**
     * Closes the connection and stops retrying.
     */
    fun stop() {
        // Invalidate pending retries first, so a close callback cannot bring the connection back.
        generation++

        executor.execute {
            closeConnection()
            RecorderState.connectionState = RecorderState.STATE_DISCONNECTED
        }
    }

    /**
     * Stops the connection and releases the executor; the instance cannot be started again.
     */
    fun close() {
        stop()
        executor.shutdown()
    }

    /**
     * Sends a flush acknowledgement after every event queued before it.
     *
     * @param requestId The correlation of the host's flush request.
     */
    fun sendFlushResponse(requestId: String) {
        submitInvocation("SendFlushResponse", requestId)
    }

    /**
     * Sends a peek answer.
     *
     * @param response The answer: `requestId`, `chain` and, on failure, `error`.
     */
    fun sendPeekResponse(response: Map<String, Any?>) {
        submitInvocation("SendPeekResponse", response)
    }

    /**
     * Sends one recording event to the host for re-broadcast.
     *
     * @param recordingEvent The event in contract shape.
     */
    fun sendRecordingEvent(recordingEvent: Map<String, Any?>) {
        executor.execute {
            // Drop events while disconnected so a backlog never leaks into a later session.
            val current = connection ?: return@execute

            if (current.connectionState != HubConnectionState.CONNECTED) {
                return@execute
            }

            // Wait for the host to accept the event, so a binding failure is logged instead of swallowed.
            try {
                awaitCompletion(current.invoke("SendRecordingEvent", recordingEvent), INVOKE_TIMEOUT_SECONDS, "SendRecordingEvent")
                RecorderState.eventCount.incrementAndGet()
            } catch (exception: RuntimeException) {
                RecorderState.lastError = exception.message.orEmpty()
                Log.w(TAG, "The host rejected a recording event.", exception)
            }
        }
    }

    // Closes the current connection on the executor thread, ignoring a transport that already failed.
    private fun closeConnection() {
        val current = connection ?: return
        connection = null

        try {
            awaitCompletion(current.stop(), INVOKE_TIMEOUT_SECONDS, "stop")
        } catch (exception: RuntimeException) {
            Log.w(TAG, "The recorder connection did not close cleanly.", exception)
        }
    }

    // Opens and registers a connection on the executor thread; a failure schedules the next retry.
    private fun connect(connectGeneration: Int) {
        // A stop or another start superseded this attempt.
        if (connectGeneration != generation) {
            return
        }

        RecorderState.connectionState = RecorderState.STATE_CONNECTING

        try {
            // Skip negotiation and use WebSockets only, the same transport the Chrome extension uses.
            val current = HubConnectionBuilder.create(hubUrl)
                .withTransport(TransportEnum.WEBSOCKETS)
                .shouldSkipNegotiate(true)
                .build()
            registerHandlers(current, connectGeneration)
            connection = current

            // Open the socket, then register before publishing the connected state, so peek and flush requests have
            // a valid target from the first event.
            awaitCompletion(current.start(), CONNECT_TIMEOUT_SECONDS, "start")
            awaitCompletion(current.invoke("RegisterRecorder"), INVOKE_TIMEOUT_SECONDS, "RegisterRecorder")

            // A new connection is a new session: the count shown in the app starts again.
            reconnectAttempt = 0
            RecorderState.eventCount.set(0)
            RecorderState.lastError = ""
            RecorderState.connectionState = RecorderState.STATE_CONNECTED
        } catch (exception: RuntimeException) {
            // Keep the reason for the app screen and retry with backoff.
            RecorderState.lastError = exception.message.orEmpty()
            Log.w(TAG, "Connecting to $hubUrl failed.", exception)
            closeConnection()
            scheduleReconnect(connectGeneration)
        }
    }

    // Wires the host's requests and the close callback of one connection.
    private fun registerHandlers(current: HubConnection, connectGeneration: Int) {
        current.on("CloseRecorder", Action { listener.onCloseRequested() })
        current.on("ReceiveFlushRequest", Action1<String> { requestId -> listener.onFlushRequested(requestId) }, String::class.java)
        current.on("ReceivePeekRequest", Action1<PeekRequestModel> { request -> listener.onPeekRequested(request) }, PeekRequestModel::class.java)

        // A dropped transport retries with backoff unless the app stopped or replaced this connection.
        current.onClosed(OnClosedCallback { exception ->
            if (connectGeneration != generation) {
                return@OnClosedCallback
            }

            RecorderState.lastError = exception?.message.orEmpty()
            executor.execute {
                // A connection the app closed itself is already detached and needs no retry.
                if (connection !== current) {
                    return@execute
                }

                connection = null
                scheduleReconnect(connectGeneration)
            }
        })
    }

    // Schedules the next connect with the extension's backoff delays, staying on the last delay once exhausted.
    private fun scheduleReconnect(connectGeneration: Int) {
        if (connectGeneration != generation || executor.isShutdown) {
            return
        }

        val delay = RECONNECT_DELAYS_MILLISECONDS[minOf(reconnectAttempt, RECONNECT_DELAYS_MILLISECONDS.lastIndex)]
        reconnectAttempt++
        RecorderState.connectionState = RecorderState.STATE_RECONNECTING
        executor.schedule({ connect(connectGeneration) }, delay, TimeUnit.MILLISECONDS)
    }

    // Queues a hub invocation behind every earlier send; it is dropped while disconnected.
    private fun submitInvocation(method: String, argument: Any) {
        executor.execute {
            val current = connection ?: return@execute

            if (current.connectionState != HubConnectionState.CONNECTED) {
                return@execute
            }

            try {
                awaitCompletion(current.invoke(method, argument), INVOKE_TIMEOUT_SECONDS, method)
            } catch (exception: RuntimeException) {
                Log.w(TAG, "Invoking $method failed.", exception)
            }
        }
    }

    private companion object {
        // Waits for a SignalR operation and turns a timeout into a failure, so a stalled host is never read as
        // an accepted call.
        fun awaitCompletion(operation: Completable, timeoutSeconds: Long, name: String) {
            val isCompleted = operation.blockingAwait(timeoutSeconds, TimeUnit.SECONDS)

            if (!isCompleted) {
                throw IllegalStateException("The hub did not complete $name within $timeoutSeconds seconds.")
            }
        }

        // Log tag of the recorder app.
        const val TAG = "G4Recorder"

        // Longest wait for the socket to open.
        const val CONNECT_TIMEOUT_SECONDS = 15L

        // Longest wait for one hub invocation.
        const val INVOKE_TIMEOUT_SECONDS = 15L

        // Retry delays after a failed or dropped connection, matching the Chrome extension defaults.
        val RECONNECT_DELAYS_MILLISECONDS = longArrayOf(0, 2000, 5000, 10000, 30000)
    }
}

/**
 * A peek request from the host, `AndroidPeekRequestModel` on the C# side.
 *
 * The SignalR client fills the fields by reflection, so they are mutable with defaults.
 */
class PeekRequestModel {
    /** Correlation the answer must carry. */
    var requestId: String = ""

    /** `Coordinates`, `Focused` or `Current`. */
    var mode: String = ""

    /** Horizontal screen coordinate for `Coordinates` mode. */
    var x: Int = 0

    /** Vertical screen coordinate for `Coordinates` mode. */
    var y: Int = 0
}
