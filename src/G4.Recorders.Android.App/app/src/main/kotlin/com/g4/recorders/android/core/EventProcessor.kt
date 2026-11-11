package com.g4.recorders.android.core

/**
 * Turns raw accessibility interactions into ordered contract events.
 *
 * The service forwards each accessibility event to one `on...` method. The processor owns the two pieces of state
 * that make the stream match the Chromium recorder:
 *
 * - **Field text.** Text changes on one editable node update one pending entry. It is sent as one `SendKeys`, with
 *   the final text, when another node is used, the keyboard closes, the activity changes or the recorder flushes.
 * - **Scroll gestures.** Consecutive scroll events on the same node and direction within [scrollWindowMilliseconds]
 *   coalesce into one `InvokeScroll` whose `notches` is the event count.
 *
 * A pending entry is always sent before any later event, so the output order is the user's order. The processor is
 * not thread-safe; the service calls it from the main thread only.
 *
 * @property options The collaborators and the clock.
 */
class EventProcessor(private val options: EventProcessorOptions) {
    // Coalescing window for scroll events.
    private val scrollWindowMilliseconds = options.scrollWindowMilliseconds

    // Field text not yet sent, or null when no field is being edited.
    private var pendingText: PendingText? = null

    // Scroll gesture not yet sent, or null when no gesture is open.
    private var pendingScroll: PendingScroll? = null

    // Whether the soft keyboard was showing at the last window change.
    private var isKeyboardVisible = false

    /** The current foreground activity class, or empty until the first activity change. */
    var currentActivity: String = ""
        private set

    /** The node of the most recent interaction, used by peek requests in `Current` mode. */
    var lastTouchedNode: RecorderNode? = null
        private set

    /**
     * Sends every pending entry: the field text first, then the open scroll gesture.
     *
     * Called before a flush, on `CloseRecorder`, and when the service stops, so the last field typed is recorded.
     */
    fun commitPending() {
        commitText()
        commitScroll()
    }

    /**
     * Sends the open scroll gesture when no scroll event arrived within the coalescing window.
     *
     * The service calls it from a timer [scrollWindowMilliseconds] after each scroll event.
     */
    fun commitExpiredScroll() {
        // Keep the gesture open while scroll events still arrive.
        val scroll = pendingScroll ?: return
        val isExpired = options.clock() - scroll.lastTimestamp >= scrollWindowMilliseconds

        if (isExpired) {
            commitScroll()
        }
    }

    /**
     * Records the Back key: pending input first, then `UndoNavigation`.
     *
     * Only the hardware or `adb input` Back key reaches the service; gesture navigation sends no key event.
     */
    fun onBackPressed() {
        // Send what was typed or scrolled before leaving the screen, so it precedes the navigation in the flow.
        commitPending()

        if (options.settings().testEventEnabled(EventCatalog.UNDO_NAVIGATION)) {
            emit(options.mapper.newEmptyChain(trigger = "KeyEvent"), EventCatalog.newUndoNavigation(), options.clock())
        }
    }

    /**
     * Records a tap.
     *
     * @param node The tapped node.
     */
    fun onClicked(node: RecorderNode) {
        onElementEvent(node, EventCatalog.newClick(), trigger = "ViewClicked")
    }

    /**
     * Records input focus moving to a node; focus leaving the edited field commits its text.
     *
     * @param node The node that received focus.
     */
    fun onFocused(node: RecorderNode) {
        lastTouchedNode = node
        commitTextUnless(node)
    }

    /**
     * Records the soft keyboard showing or closing; closing commits the field text and sends `HideSoftKeyboard`.
     *
     * @param isVisible True when an input method window is on screen.
     */
    fun onKeyboardVisibilityChanged(isVisible: Boolean) {
        // React only to the visible-to-hidden transition; Android reports window changes for many other reasons.
        val isHidden = isKeyboardVisible && !isVisible
        isKeyboardVisible = isVisible

        if (!isHidden) {
            return
        }

        // Send what was typed before the keyboard closed, so the text precedes the hide in the flow.
        commitPending()

        if (options.settings().testEventEnabled(EventCatalog.HIDE_SOFT_KEYBOARD)) {
            emit(options.mapper.newEmptyChain(trigger = "WindowsChanged"), EventCatalog.newHideSoftKeyboard(), options.clock())
        }
    }

    /**
     * Records a long press.
     *
     * @param node The pressed node.
     */
    fun onLongClicked(node: RecorderNode) {
        onElementEvent(node, EventCatalog.newLongClick(), trigger = "ViewLongClicked")
    }

    /**
     * Records a scroll event, coalescing it into the open gesture when it continues it.
     *
     * @param node The scrolled node.
     * @param deltaX Horizontal content delta in pixels.
     * @param deltaY Vertical content delta in pixels.
     */
    fun onScrolled(node: RecorderNode, deltaX: Int, deltaY: Int) {
        // Ignore disabled scrolls and layout passes that report no movement.
        val settings = options.settings()
        val direction = EventCatalog.resolveScrollDirection(deltaX, deltaY) ?: return

        if (!settings.testEventEnabled(EventCatalog.INVOKE_SCROLL)) {
            return
        }

        // Continue the open gesture when the same node keeps scrolling the same way inside the window.
        lastTouchedNode = node
        val now = options.clock()
        val scroll = pendingScroll
        val isContinued = scroll != null &&
            scroll.node.testSameNode(node) &&
            scroll.direction == direction &&
            now - scroll.lastTimestamp < scrollWindowMilliseconds

        if (isContinued) {
            pendingScroll = scroll!!.copy(notches = scroll.notches + 1, lastTimestamp = now)

            return
        }

        // A new gesture starts: send the edited field and the previous gesture first, then open this one.
        commitText()
        commitScroll()
        val chain = options.mapper.newChain(ChainOptions(target = node, activity = currentActivity, trigger = "ViewScrolled", viewContext = null))
        pendingScroll = PendingScroll(node = node, chain = chain, direction = direction, notches = 1, timestamp = now, lastTimestamp = now)
    }

    /**
     * Records the content of an editable field changing.
     *
     * @param node The edited field.
     * @param text The field content after the change.
     */
    fun onTextChanged(node: RecorderNode, text: String) {
        // Typing is recorded only as committed text.
        val settings = options.settings()

        if (!settings.testEventEnabled(EventCatalog.SEND_KEYS)) {
            return
        }

        // Another field started: its predecessor is finished, and so is any scroll gesture.
        lastTouchedNode = node
        commitTextUnless(node)
        commitScroll()

        // Re-map the chain on every change so the snapshot and state reflect the field as the user left it.
        val viewContext = getViewContext(node, EventCatalog.SEND_KEYS)
        val chain = options.mapper.newChain(ChainOptions(target = node, activity = currentActivity, trigger = "ViewTextChanged", viewContext = viewContext))
        pendingText = PendingText(node = node, chain = chain, text = text, isPassword = node.isPassword, timestamp = options.clock())
    }

    /**
     * Records a window state change; a new activity commits the field text and sends `SwitchActivity`.
     *
     * Dialogs, menus and popups also change the window state, but only a real activity change is a context switch.
     * The caller resolves that, because only the Android package manager can tell an activity from a view class.
     *
     * @param packageName The package of the new window.
     * @param activity The activity class, or empty when the window is not an activity.
     */
    fun onWindowStateChanged(packageName: String, activity: String) {
        // Ignore popups and an unchanged activity, which Android reports again when a window re-lays out.
        val isNewActivity = activity.isNotEmpty() && activity != currentActivity

        if (!isNewActivity) {
            return
        }

        // Finish the old screen's input before announcing the new screen.
        commitPending()
        currentActivity = activity

        if (options.settings().testEventEnabled(EventCatalog.SWITCH_ACTIVITY)) {
            emit(options.mapper.newEmptyChain(trigger = "WindowStateChanged"), EventCatalog.newSwitchActivity(packageName, activity), options.clock())
        }
    }

    // Sends the open scroll gesture, if any.
    private fun commitScroll() {
        val scroll = pendingScroll ?: return
        pendingScroll = null
        emit(scroll.chain, EventCatalog.newScroll(scroll.direction, scroll.notches), scroll.timestamp)
    }

    // Sends the pending field text, if any.
    private fun commitText() {
        val pending = pendingText ?: return
        pendingText = null
        val entry = EventCatalog.newSendKeys(pending.text, pending.isPassword, options.settings().isRedactPasswordsEnabled)
        emit(pending.chain, entry, pending.timestamp)
    }

    // Sends the pending field text unless the interaction is on that same field.
    private fun commitTextUnless(node: RecorderNode) {
        val pending = pendingText ?: return

        if (!pending.node.testSameNode(node)) {
            commitText()
        }
    }

    // Wraps a chain and an entry into an event and hands it to the sender.
    private fun emit(chain: Map<String, Any?>, entry: CatalogEntry, timestamp: Long) {
        options.emit(options.mapper.newEvent(chain, entry, timestamp))
    }

    // Returns the view context for an element event, or null when the setting or the event excludes it.
    private fun getViewContext(node: RecorderNode, eventName: String): ViewContext? {
        // Only element events carry the snapshot, and only while the setting is on.
        val settings = options.settings()
        val isIncluded = settings.isViewContextEnabled && EventCatalog.VIEW_CONTEXT_EVENT_NAMES.contains(eventName)

        if (!isIncluded) {
            return null
        }

        val builder = ViewContextBuilder(settings.viewContextAncestorLevels, settings.viewContextMaximumKilobytes)

        return builder.getViewContext(node)
    }

    // Records a tap or long press on an element: pending input first, then the element event with its snapshot.
    private fun onElementEvent(node: RecorderNode, entry: CatalogEntry, trigger: String) {
        // Remember the node for peek requests even when the event itself is disabled.
        lastTouchedNode = node

        if (!options.settings().testEventEnabled(entry.event)) {
            return
        }

        // A tap on the edited field keeps its text open; anything else finishes the field and the scroll gesture.
        commitTextUnless(node)
        commitScroll()

        // Map the chain now, while the tapped screen is still showing.
        val viewContext = getViewContext(node, entry.event)
        val chain = options.mapper.newChain(ChainOptions(target = node, activity = currentActivity, trigger = trigger, viewContext = viewContext))
        emit(chain, entry, options.clock())
    }

    // Field text waiting for its commit.
    private data class PendingText(val node: RecorderNode, val chain: Map<String, Any?>, val text: String, val isPassword: Boolean, val timestamp: Long)

    // Scroll gesture waiting for its commit; timestamp is the first event, lastTimestamp the latest.
    private data class PendingScroll(
        val node: RecorderNode,
        val chain: Map<String, Any?>,
        val direction: String,
        val notches: Int,
        val timestamp: Long,
        val lastTimestamp: Long
    )
}

/**
 * Collaborators of [EventProcessor].
 *
 * @property mapper Maps nodes to contract chains and events.
 * @property settings Returns the current settings; read on every event so a settings change applies at once.
 * @property clock Returns Unix epoch milliseconds.
 * @property emit Receives each finished event, in order.
 * @property scrollWindowMilliseconds How long a scroll gesture stays open without new scroll events.
 */
data class EventProcessorOptions(
    val mapper: NodeMapper,
    val settings: () -> RecorderSettings,
    val clock: () -> Long,
    val emit: (Map<String, Any?>) -> Unit,
    val scrollWindowMilliseconds: Long = 500
)

/**
 * Resolves the node a peek request targets.
 */
object PeekResolver {
    /**
     * Returns the deepest visible node whose bounds contain a screen point.
     *
     * Later siblings are drawn on top of earlier ones, so they are searched first; that picks the view the user
     * sees at the point, which is the one a replayed tap would hit.
     *
     * @param root The root of the active window.
     * @param x Horizontal screen coordinate.
     * @param y Vertical screen coordinate.
     * @return The node, or null when the point is outside the window.
     */
    fun findNodeAt(root: RecorderNode, x: Int, y: Int): RecorderNode? {
        // Reject points outside the window before walking it.
        if (!root.bounds.testContains(x, y)) {
            return null
        }

        // Descend into the topmost child that contains the point until no child does.
        var current = root
        var depth = 0

        while (depth < MAXIMUM_DEPTH) {
            val child = current.children.asReversed().firstOrNull { it.isVisible && it.bounds.testContains(x, y) } ?: break
            current = child
            depth++
        }

        return current
    }

    // Deepest tree the search follows; real Android trees stay far below it.
    private const val MAXIMUM_DEPTH = 256
}
