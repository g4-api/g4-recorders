package com.g4.recorders.android

import android.os.Bundle
import android.text.InputType
import androidx.appcompat.app.AppCompatActivity
import androidx.preference.EditTextPreference
import androidx.preference.PreferenceFragmentCompat

/**
 * The recorder settings screen, the counterpart of the Chrome extension options page.
 *
 * The preferences write the keys [SettingsStore] reads, and the accessibility service listens for changes, so an
 * edit applies to the next event and a new hub URL reconnects at once.
 */
class SettingsActivity : AppCompatActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Host the preference screen as the whole content, once; a recreated activity keeps its fragment.
        if (savedInstanceState == null) {
            supportFragmentManager
                .beginTransaction()
                .replace(android.R.id.content, RecorderPreferenceFragment())
                .commit()
        }
    }

    /**
     * The preference list defined in `res/xml/preferences.xml`.
     */
    class RecorderPreferenceFragment : PreferenceFragmentCompat() {
        override fun onCreatePreferences(
            savedInstanceState: Bundle?,
            rootKey: String?,
        ) {
            setPreferencesFromResource(R.xml.preferences, rootKey)

            // Number settings are text preferences; show a numeric keyboard for them.
            for (key in listOf(SettingsStore.KEY_VIEW_CONTEXT_LEVELS, SettingsStore.KEY_VIEW_CONTEXT_SIZE)) {
                findPreference<EditTextPreference>(key)?.setOnBindEditTextListener { it.inputType = InputType.TYPE_CLASS_NUMBER }
            }
        }
    }
}
