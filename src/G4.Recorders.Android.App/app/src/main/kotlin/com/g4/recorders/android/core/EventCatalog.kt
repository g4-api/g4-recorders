package com.g4.recorders.android.core

/**
 * One catalog entry: the contract `event` name, its `type` and its `value` payload.
 *
 * @property event The G4 plugin-style event name, for example `InvokeClick`.
 * @property type The contract event type: `Mouse`, `Keyboard` or `Context`.
 * @property value The event payload; an empty map when the event carries none.
 */
data class CatalogEntry(
    val event: String,
    val type: String,
    val value: Map<String, Any?>,
)

/**
 * Maps recorded Android interactions to contract event names, types and values.
 *
 * Names follow the G4 action style the Chromium recorder uses, so the Hub passes them through as plugin names. The
 * recorder never leaves an event out for a client: `SwitchActivity` and `InvokeLongClick` have no replay plugin yet,
 * and the clients drop them through their own `excludeEvents` lists.
 */
object EventCatalog {
    /** Event name of a tap. */
    const val INVOKE_CLICK = "InvokeClick"

    /** Event name of a long press. */
    const val INVOKE_LONG_CLICK = "InvokeLongClick"

    /** Event name of a scroll gesture. */
    const val INVOKE_SCROLL = "InvokeScroll"

    /** Event name of the soft keyboard closing. */
    const val HIDE_SOFT_KEYBOARD = "HideSoftKeyboard"

    /** Event name of committed field text. */
    const val SEND_KEYS = "SendKeys"

    /** Event name of a new foreground activity. */
    const val SWITCH_ACTIVITY = "SwitchActivity"

    /** Event name of the Back key, the G4 plugin that navigates back; the Chromium recorder uses it for Back too. */
    const val UNDO_NAVIGATION = "UndoNavigation"

    /** Contract type of page-level context changes. */
    const val TYPE_CONTEXT = "Context"

    /** Contract type of keyboard events. */
    const val TYPE_KEYBOARD = "Keyboard"

    /** Contract type of pointer events. */
    const val TYPE_MOUSE = "Mouse"

    /** Contract type of back navigation, matching the Chromium recorder's navigation events. */
    const val TYPE_NAVIGATION = "Navigation"

    /** Duration reported for a long press; Android does not report the real one, and this is its default timeout. */
    const val LONG_CLICK_DURATION_MILLISECONDS = 500

    /** Text written instead of a password when redaction is on. */
    const val REDACTED_TEXT = "***"

    /** Longest field text recorded, matching the Chromium recorder's cap. */
    const val MAXIMUM_TEXT_LENGTH = 512

    /** Event names whose trigger node carries the view context, matching the Chromium element events. */
    val VIEW_CONTEXT_EVENT_NAMES: Set<String> = setOf(INVOKE_CLICK, INVOKE_LONG_CLICK, SEND_KEYS)

    /**
     * Returns the entry of a tap.
     *
     * @return The `InvokeClick` entry.
     */
    fun newClick(): CatalogEntry = CatalogEntry(event = INVOKE_CLICK, type = TYPE_MOUSE, value = emptyMap())

    /**
     * Returns the entry of the soft keyboard closing.
     *
     * @return The `HideSoftKeyboard` entry.
     */
    fun newHideSoftKeyboard(): CatalogEntry = CatalogEntry(event = HIDE_SOFT_KEYBOARD, type = TYPE_KEYBOARD, value = emptyMap())

    /**
     * Returns the entry of a long press.
     *
     * @return The `InvokeLongClick` entry.
     */
    fun newLongClick(): CatalogEntry {
        val value = mapOf<String, Any?>("durationMilliseconds" to LONG_CLICK_DURATION_MILLISECONDS)

        return CatalogEntry(event = INVOKE_LONG_CLICK, type = TYPE_MOUSE, value = value)
    }

    /**
     * Returns the entry of a scroll gesture, in the Hub's `InvokeScroll` argument shape.
     *
     * @param direction `Up`, `Down`, `Left` or `Right`.
     * @param notches How many scroll events the gesture coalesced, at least 1.
     * @return The `InvokeScroll` entry.
     */
    fun newScroll(
        direction: String,
        notches: Int,
    ): CatalogEntry {
        val value = mapOf<String, Any?>("direction" to direction, "notches" to maxOf(1, notches))

        return CatalogEntry(event = INVOKE_SCROLL, type = TYPE_MOUSE, value = value)
    }

    /**
     * Returns the entry of committed field text.
     *
     * @param text The final field text.
     * @param isPassword True when the field is a password field.
     * @param isRedactEnabled True when passwords are written as `***`.
     * @return The `SendKeys` entry, `{ text }` or `{ text: "***", isRedacted: true }`.
     */
    fun newSendKeys(
        text: String,
        isPassword: Boolean,
        isRedactEnabled: Boolean,
    ): CatalogEntry {
        // Redact before anything else so a password never reaches the payload when the user asked to hide it.
        if (isPassword && isRedactEnabled) {
            val redacted = mapOf<String, Any?>("text" to REDACTED_TEXT, "isRedacted" to true)

            return CatalogEntry(event = SEND_KEYS, type = TYPE_KEYBOARD, value = redacted)
        }

        // Cap the text like the Chromium recorder so a pasted document cannot bloat the event.
        val value = mapOf<String, Any?>("text" to text.take(MAXIMUM_TEXT_LENGTH))

        return CatalogEntry(event = SEND_KEYS, type = TYPE_KEYBOARD, value = value)
    }

    /**
     * Returns the entry of a new foreground activity.
     *
     * @param packageName The package of the activity.
     * @param activity The fully qualified activity class.
     * @return The `SwitchActivity` entry.
     */
    fun newSwitchActivity(
        packageName: String,
        activity: String,
    ): CatalogEntry {
        val value = mapOf<String, Any?>("package" to packageName, "activity" to activity)

        return CatalogEntry(event = SWITCH_ACTIVITY, type = TYPE_CONTEXT, value = value)
    }

    /**
     * Returns the entry of the Back key.
     *
     * @return The `UndoNavigation` entry.
     */
    fun newUndoNavigation(): CatalogEntry = CatalogEntry(event = UNDO_NAVIGATION, type = TYPE_NAVIGATION, value = emptyMap())

    /**
     * Resolves the scroll direction of a gesture from its content deltas.
     *
     * A positive vertical delta means the content moved so that lower content came into view, which a G4
     * `InvokeScroll` replays as `Down`. The larger axis wins when both moved.
     *
     * @param deltaX Horizontal scroll delta in pixels.
     * @param deltaY Vertical scroll delta in pixels.
     * @return The direction, or null when nothing moved.
     */
    fun resolveScrollDirection(
        deltaX: Int,
        deltaY: Int,
    ): String? {
        // Ignore an event that reports no movement (Android sends those when a list re-lays out), else pick the
        // dominant axis, then its sign.
        val isVertical = Math.abs(deltaY) >= Math.abs(deltaX)

        return when {
            deltaX == 0 && deltaY == 0 -> null
            isVertical && deltaY > 0 -> "Down"
            isVertical -> "Up"
            deltaX > 0 -> "Right"
            else -> "Left"
        }
    }
}
