package com.g4.recorders.android

import java.util.concurrent.atomic.AtomicInteger

/**
 * Process-wide recorder status shown by [MainActivity].
 *
 * The service and the hub connection write it from their own threads; the activity polls it once a second. All
 * members are volatile or atomic, so readers always see a recent value without locking.
 */
object RecorderState {
    /** Shown before the service has started. */
    const val STATE_STOPPED = "stopped"

    /** Shown while the connection is being opened. */
    const val STATE_CONNECTING = "connecting"

    /** Shown while the connection is open and registered. */
    const val STATE_CONNECTED = "connected"

    /** Shown while the connection waits to retry. */
    const val STATE_RECONNECTING = "reconnecting"

    /** Shown after the connection closed for good. */
    const val STATE_DISCONNECTED = "disconnected"

    /** The current connection state, one of the `STATE_` values. */
    @Volatile
    var connectionState: String = STATE_STOPPED

    /** Events sent in the current session; each new connection starts a new session, like the Chrome extension. */
    val eventCount = AtomicInteger(0)

    /** The last connection or send failure, or empty. */
    @Volatile
    var lastError: String = ""

    /** True while the accessibility service is running. */
    @Volatile
    var isServiceRunning: Boolean = false
}
