package com.g4.recorders.android

import android.content.Context
import android.content.SharedPreferences
import androidx.core.content.edit
import androidx.preference.PreferenceManager
import com.g4.recorders.android.core.RecorderSettings

/**
 * Reads and writes the recorder settings in the default shared preferences.
 *
 * The settings screen edits the same keys through the AndroidX preference library, and the launcher writes the
 * hub URL and target package through intent extras that [MainActivity] stores here. Text preferences hold numbers
 * as strings, so every number is parsed with a fallback to the default.
 */
object SettingsStore {
    // Ancestor levels the settings screen accepts for the view context; matches the Chromium extension's range.
    private val ANCESTOR_LEVEL_RANGE = 0..10

    // Snapshot sizes, in kilobytes, the settings screen accepts for the view context.
    private val SNAPSHOT_KILOBYTE_RANGE = 1..256

    /** Key of the hub URL. */
    const val KEY_HUB_URL = "hubUrl"

    /** Key of the package to record. */
    const val KEY_TARGET_PACKAGE = "targetPackage"

    /** Key of the device serial the launcher passes; the app cannot read the serial itself. */
    const val KEY_DEVICE_SERIAL = "deviceSerial"

    /** Key of the auto-connect switch. */
    const val KEY_AUTO_CONNECT = "isAutoConnectEnabled"

    /** Key of the password redaction switch. */
    const val KEY_REDACT_PASSWORDS = "isRedactPasswordsEnabled"

    /** Key of the view context switch. */
    const val KEY_VIEW_CONTEXT = "isViewContextEnabled"

    /** Key of the view context ancestor levels. */
    const val KEY_VIEW_CONTEXT_LEVELS = "viewContextAncestorLevels"

    /** Key of the view context size budget, in kilobytes. */
    const val KEY_VIEW_CONTEXT_SIZE = "viewContextMaximumKilobytes"

    /** Prefix of the per-event switches; the event name follows, for example `event.InvokeClick`. */
    const val EVENT_KEY_PREFIX = "event."

    /**
     * Returns the shared preferences that hold the settings.
     *
     * @param context Any context of the app.
     * @return The default shared preferences.
     */
    fun getPreferences(context: Context): SharedPreferences = PreferenceManager.getDefaultSharedPreferences(context)

    /**
     * Reads the current settings, falling back to the defaults for every missing or malformed value.
     *
     * @param context Any context of the app.
     * @return The settings.
     */
    fun getSettings(context: Context): RecorderSettings {
        // Start from the defaults so a fresh install and a partly edited screen both resolve every value.
        val preferences = getPreferences(context)
        val defaults = RecorderSettings()
        val enabledEvents = RecorderSettings.ALL_EVENT_NAMES.filter { preferences.getBoolean(EVENT_KEY_PREFIX + it, true) }.toSet()

        return RecorderSettings(
            hubUrl =
                preferences
                    .getString(KEY_HUB_URL, null)
                    .orEmpty()
                    .trim()
                    .ifEmpty { defaults.hubUrl },
            targetPackage = preferences.getString(KEY_TARGET_PACKAGE, null).orEmpty().trim(),
            isAutoConnectEnabled = preferences.getBoolean(KEY_AUTO_CONNECT, defaults.isAutoConnectEnabled),
            isRedactPasswordsEnabled = preferences.getBoolean(KEY_REDACT_PASSWORDS, defaults.isRedactPasswordsEnabled),
            isViewContextEnabled = preferences.getBoolean(KEY_VIEW_CONTEXT, defaults.isViewContextEnabled),
            viewContextAncestorLevels =
                readNumber(
                    preferences,
                    KEY_VIEW_CONTEXT_LEVELS,
                    defaults.viewContextAncestorLevels,
                    ANCESTOR_LEVEL_RANGE,
                ),
            viewContextMaximumKilobytes =
                readNumber(
                    preferences,
                    KEY_VIEW_CONTEXT_SIZE,
                    defaults.viewContextMaximumKilobytes,
                    SNAPSHOT_KILOBYTE_RANGE,
                ),
            enabledEvents = enabledEvents,
        )
    }

    /**
     * Returns the device serial stored by the launcher.
     *
     * @param context Any context of the app.
     * @return The serial, or empty when the app was started by hand.
     */
    fun getDeviceSerial(context: Context): String = getPreferences(context).getString(KEY_DEVICE_SERIAL, null).orEmpty()

    /**
     * Stores the values passed by the host launcher as intent extras.
     *
     * A null value leaves the stored one unchanged, so a manual start of the app keeps the last session's values.
     *
     * @param context Any context of the app.
     * @param values The extras.
     */
    fun setLaunchValues(
        context: Context,
        values: LaunchValues,
    ) {
        // Write every value in one edit so the service reconnects once. The service runs in this process, so the
        // in-memory values it reads change at once even though the file is written in the background.
        getPreferences(context).edit {
            values.deviceSerial?.let { putString(KEY_DEVICE_SERIAL, it.trim()) }
            values.hubUrl?.let { putString(KEY_HUB_URL, it.trim()) }
            values.targetPackage?.let { putString(KEY_TARGET_PACKAGE, it.trim()) }
        }
    }

    // Parses a number stored as text and clamps it to its range; a malformed value falls back to the default.
    private fun readNumber(
        preferences: SharedPreferences,
        key: String,
        defaultValue: Int,
        range: IntRange,
    ): Int {
        val number = preferences.getString(key, null)?.trim()?.toIntOrNull() ?: return defaultValue

        return number.coerceIn(range)
    }
}

/**
 * Intent extras the host launcher passes to [MainActivity]; a null value was not passed.
 *
 * @property hubUrl The hub on the reversed port.
 * @property targetPackage The application under test.
 * @property deviceSerial The adb serial of the device.
 */
data class LaunchValues(
    val hubUrl: String?,
    val targetPackage: String?,
    val deviceSerial: String?,
)
