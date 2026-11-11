package com.g4.recorders.android.core

/**
 * The recorder settings the capture core reads.
 *
 * The Android layer loads them from shared preferences; the defaults here are the defaults of a fresh install and
 * match the Chromium recorder extension where both have the same setting.
 *
 * @property hubUrl The SignalR hub the app connects to; the launcher passes the host's reversed port.
 * @property targetPackage The package to record, or empty to record every application.
 * @property isAutoConnectEnabled True when the service connects as soon as it starts.
 * @property isRedactPasswordsEnabled True when password text is written as `***`.
 * @property isViewContextEnabled True when element events carry the view context snapshot.
 * @property viewContextAncestorLevels How many ancestors above the target the snapshot is rooted at.
 * @property viewContextMaximumKilobytes Largest decoded snapshot, in kilobytes.
 * @property enabledEvents The event names that are recorded; a missing name is not recorded.
 */
data class RecorderSettings(
    val hubUrl: String = DEFAULT_HUB_URL,
    val targetPackage: String = "",
    val isAutoConnectEnabled: Boolean = true,
    val isRedactPasswordsEnabled: Boolean = false,
    val isViewContextEnabled: Boolean = true,
    val viewContextAncestorLevels: Int = 2,
    val viewContextMaximumKilobytes: Int = 16,
    val enabledEvents: Set<String> = ALL_EVENT_NAMES,
) {
    /**
     * Tests whether an event name is recorded.
     *
     * @param eventName The contract event name.
     * @return True when the event is enabled.
     */
    fun isEventEnabled(eventName: String): Boolean = enabledEvents.contains(eventName)

    /**
     * Defaults shared by the settings screen and the capture core.
     */
    companion object {
        /** The hub on the reversed recorder port; `adb reverse` maps the device's localhost to the host. */
        const val DEFAULT_HUB_URL = "http://localhost:9958/hub/v4/g4/peek"

        /** Every event name the recorder can send, all on by default. */
        val ALL_EVENT_NAMES: Set<String> =
            setOf(
                EventCatalog.INVOKE_CLICK,
                EventCatalog.INVOKE_LONG_CLICK,
                EventCatalog.SEND_KEYS,
                EventCatalog.INVOKE_SCROLL,
                EventCatalog.HIDE_SOFT_KEYBOARD,
                EventCatalog.SWITCH_ACTIVITY,
                EventCatalog.UNDO_NAVIGATION,
            )
    }
}
