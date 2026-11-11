package com.g4.recorders.android

import android.accessibilityservice.AccessibilityService
import android.content.ComponentName
import android.content.SharedPreferences
import android.content.pm.PackageManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.view.KeyEvent
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.view.accessibility.AccessibilityWindowInfo
import com.g4.recorders.android.core.ChainOptions
import com.g4.recorders.android.core.EventProcessor
import com.g4.recorders.android.core.EventProcessorOptions
import com.g4.recorders.android.core.NodeMapper
import com.g4.recorders.android.core.PeekResolver
import com.g4.recorders.android.core.RecorderNode
import com.g4.recorders.android.core.RecorderSettings

/**
 * The capture side of the Android recorder: an accessibility service that turns interactions in any application
 * into G4 contract events and streams them to the recorder host.
 *
 * It plays the role of the Chrome extension's content script and background worker together. Android delivers
 * every accessibility event on the main thread; the service forwards it to the [EventProcessor] there, so the
 * chain is mapped while the touched screen is still showing, and the hub connection sends the finished events
 * from its own thread in order.
 *
 * The host enables the service through `adb shell settings put secure enabled_accessibility_services` and disables
 * it again on stop; the service also stops itself when the host sends `CloseRecorder`.
 */
class RecorderAccessibilityService : AccessibilityService(), RecorderHubConnection.Listener {
    // Runs processor work on the main thread, where accessibility events arrive.
    private val mainHandler = Handler(Looper.getMainLooper())

    // Sends the open scroll gesture once no scroll event arrived for the coalescing window.
    private val scrollCommitRunnable = Runnable { processor?.commitExpiredScroll() }

    // The ordered send queue and the host's requests.
    private var connection: RecorderHubConnection? = null

    // Maps nodes to contract chains; created when the service connects.
    private var mapper: NodeMapper? = null

    // Event ordering, text commit and scroll coalescing; created when the service connects.
    private var processor: EventProcessor? = null

    // The settings the processor reads; refreshed on every preference change.
    @Volatile
    private var settings = RecorderSettings()

    // Applies settings changes at once and reconnects when the hub URL changed.
    private val preferenceListener = SharedPreferences.OnSharedPreferenceChangeListener { _, _ -> onSettingsChanged() }

    override fun onServiceConnected() {
        super.onServiceConnected()

        // Read the settings and build the capture pipeline before the first event can arrive.
        settings = SettingsStore.getSettings(this)
        val nodeMapper = NodeMapper(machineName = Build.MODEL.orEmpty(), machineAddress = getDeviceAddress())
        mapper = nodeMapper
        processor = EventProcessor(EventProcessorOptions(
            mapper = nodeMapper,
            settings = { settings },
            clock = { System.currentTimeMillis() },
            emit = { recordingEvent -> connection?.sendRecordingEvent(recordingEvent) }
        ))

        // Connect now when auto-connect is on; a later settings change can still start the connection.
        connection = RecorderHubConnection(this)
        SettingsStore.getPreferences(this).registerOnSharedPreferenceChangeListener(preferenceListener)
        RecorderState.isServiceRunning = true

        if (settings.isAutoConnectEnabled) {
            connection?.start(settings.hubUrl)
        }
    }

    override fun onAccessibilityEvent(event: AccessibilityEvent?) {
        // Ignore events before the pipeline exists and events of the recorder itself.
        val currentProcessor = processor ?: return
        val accessibilityEvent = event ?: return
        val packageName = accessibilityEvent.packageName?.toString().orEmpty()

        if (packageName == this.packageName) {
            return
        }

        // Window changes have no reliable package; they only tell whether the soft keyboard is showing.
        if (accessibilityEvent.eventType == AccessibilityEvent.TYPE_WINDOWS_CHANGED) {
            currentProcessor.onKeyboardVisibilityChanged(testKeyboardVisible())

            return
        }

        // Record only the application under test when the host named one.
        val targetPackage = settings.targetPackage
        val isOtherPackage = targetPackage.isNotEmpty() && packageName.isNotEmpty() && packageName != targetPackage

        if (isOtherPackage) {
            return
        }

        dispatchEvent(currentProcessor, accessibilityEvent, packageName)
    }

    override fun onCloseRequested() {
        // Send what is pending, then disconnect and turn the service off, like the extension closing its browser.
        mainHandler.post {
            processor?.commitPending()
            connection?.stop()
            disableSelf()
        }
    }

    override fun onDestroy() {
        // Send the last field typed, then release the connection and the listener.
        processor?.commitPending()
        mainHandler.removeCallbacks(scrollCommitRunnable)
        SettingsStore.getPreferences(this).unregisterOnSharedPreferenceChangeListener(preferenceListener)
        connection?.close()
        connection = null
        processor = null
        mapper = null
        RecorderState.isServiceRunning = false
        RecorderState.connectionState = RecorderState.STATE_STOPPED

        super.onDestroy()
    }

    override fun onFlushRequested(requestId: String) {
        // Commit pending input on the main thread; the response then queues behind the events it produced.
        mainHandler.post {
            processor?.commitPending()
            connection?.sendFlushResponse(requestId)
        }
    }

    override fun onKeyEvent(event: KeyEvent?): Boolean {
        // Record Back when it is released, once per press; every key is passed on unchanged.
        val isBackReleased = event?.keyCode == KeyEvent.KEYCODE_BACK && event.action == KeyEvent.ACTION_UP

        if (isBackReleased) {
            processor?.onBackPressed()
        }

        return false
    }

    override fun onInterrupt() {
        // The recorder gives no spoken or haptic feedback, so there is nothing to interrupt.
    }

    override fun onPeekRequested(request: PeekRequestModel) {
        // Resolve on the main thread, where the processor's last touched node lives.
        mainHandler.post {
            val response = linkedMapOf<String, Any?>("requestId" to request.requestId)

            try {
                val node = resolvePeekNode(request)
                val nodeMapper = mapper

                if (node != null && nodeMapper != null) {
                    val chainOptions = ChainOptions(target = node, activity = processor?.currentActivity.orEmpty(), trigger = "Peek", viewContext = null)
                    response["chain"] = nodeMapper.newChain(chainOptions)
                }
            } catch (exception: RuntimeException) {
                // Report the failure through the correlation so the host's caller sees it instead of a timeout.
                response["error"] = exception.message ?: exception.javaClass.simpleName
            }

            connection?.sendPeekResponse(response)
        }
    }

    // Routes one accessibility event to the processor.
    private fun dispatchEvent(currentProcessor: EventProcessor, event: AccessibilityEvent, packageName: String) {
        when (event.eventType) {
            AccessibilityEvent.TYPE_VIEW_CLICKED -> getSourceNode(event)?.let { currentProcessor.onClicked(it) }
            AccessibilityEvent.TYPE_VIEW_LONG_CLICKED -> getSourceNode(event)?.let { currentProcessor.onLongClicked(it) }
            AccessibilityEvent.TYPE_VIEW_FOCUSED -> getSourceNode(event)?.let { currentProcessor.onFocused(it) }
            AccessibilityEvent.TYPE_VIEW_TEXT_CHANGED -> getSourceNode(event)?.let { currentProcessor.onTextChanged(it, getEventText(event, it)) }
            AccessibilityEvent.TYPE_VIEW_SCROLLED -> onScrolled(currentProcessor, event)
            AccessibilityEvent.TYPE_WINDOW_STATE_CHANGED -> currentProcessor.onWindowStateChanged(packageName, resolveActivity(event, packageName))
            else -> Unit
        }
    }

    // Returns the adb serial the launcher passed as the machine address, else the user-visible device name.
    private fun getDeviceAddress(): String {
        // Apps cannot read the serial on Android 10 and later, so the launcher hands it over.
        val serial = SettingsStore.getDeviceSerial(this)

        return serial.ifEmpty { Settings.Global.getString(contentResolver, Settings.Global.DEVICE_NAME).orEmpty() }
    }

    // Returns the field content after a text change, preferring the live node over the event's copy.
    private fun getEventText(event: AccessibilityEvent, node: RecorderNode): String {
        // The node reports the whole current content; the event text is the fallback for views that do not.
        if (node.text.isNotEmpty() || event.text.isNullOrEmpty()) {
            return node.text
        }

        return event.text.joinToString(separator = "")
    }

    // Wraps the event source, or returns null when the view is already gone.
    private fun getSourceNode(event: AccessibilityEvent): RecorderNode? {
        val source: AccessibilityNodeInfo = event.source ?: return null

        return AccessibilityNodeAdapter(source)
    }

    // Applies changed settings and follows a changed hub URL or auto-connect switch.
    private fun onSettingsChanged() {
        val previous = settings
        settings = SettingsStore.getSettings(this)
        val isConnectionChanged = previous.hubUrl != settings.hubUrl || previous.isAutoConnectEnabled != settings.isAutoConnectEnabled

        if (!isConnectionChanged) {
            return
        }

        // Reconnect to the new hub, or disconnect when auto-connect was turned off.
        if (settings.isAutoConnectEnabled) {
            connection?.start(settings.hubUrl)
        } else {
            connection?.stop()
        }
    }

    // Forwards a scroll with its deltas and arms the timer that closes the gesture.
    private fun onScrolled(currentProcessor: EventProcessor, event: AccessibilityEvent) {
        val node = getSourceNode(event) ?: return

        // Android 9 reports the deltas directly; older versions report only the item range, read as a direction.
        val isDeltaAvailable = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P
        val deltaX = if (isDeltaAvailable) event.scrollDeltaX else 0
        val deltaY = if (isDeltaAvailable) event.scrollDeltaY else maxOf(1, event.fromIndex)
        currentProcessor.onScrolled(node, deltaX, deltaY)

        // Restart the coalescing timer; the gesture closes once the user stops scrolling.
        mainHandler.removeCallbacks(scrollCommitRunnable)
        mainHandler.postDelayed(scrollCommitRunnable, SCROLL_WINDOW_MILLISECONDS)
    }

    // Returns the activity class of a window state change, or empty when the window is not an activity.
    private fun resolveActivity(event: AccessibilityEvent, packageName: String): String {
        // Dialogs and popups report view classes; only the package manager knows whether a class is an activity.
        val className = event.className?.toString().orEmpty()

        if (className.isEmpty() || packageName.isEmpty()) {
            return ""
        }

        return try {
            packageManager.getActivityInfo(ComponentName(packageName, className), 0)
            className
        } catch (exception: PackageManager.NameNotFoundException) {
            ""
        }
    }

    // Resolves the node a peek request targets.
    private fun resolvePeekNode(request: PeekRequestModel): RecorderNode? {
        // Coordinates search the active window; focus asks Android; current returns the last touched node.
        return when (request.mode) {
            "Coordinates" -> rootInActiveWindow?.let { PeekResolver.findNodeAt(AccessibilityNodeAdapter(it), request.x, request.y) }
            "Focused" -> (findFocus(AccessibilityNodeInfo.FOCUS_INPUT) ?: findFocus(AccessibilityNodeInfo.FOCUS_ACCESSIBILITY))?.let { AccessibilityNodeAdapter(it) }
            else -> processor?.lastTouchedNode
        }
    }

    // Tests whether an input method window is on screen.
    private fun testKeyboardVisible(): Boolean {
        return windows.any { it.type == AccessibilityWindowInfo.TYPE_INPUT_METHOD }
    }

    private companion object {
        // Coalescing window for scroll events; matches the processor default.
        const val SCROLL_WINDOW_MILLISECONDS = 500L
    }
}
