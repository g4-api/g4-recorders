package com.g4.recorders.android

import android.content.Intent
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity

/**
 * The recorder's status screen, the counterpart of the Chrome extension popup.
 *
 * The host launcher starts it with `am start` and the extras `hubUrl`, `targetPackage` and `deviceSerial`; the
 * activity stores them before the accessibility service reads them, so the service connects to the host that
 * launched it. Started by hand, it keeps the last stored values.
 *
 * The screen shows the connection state, the events sent in the current session and the last error, refreshed once
 * a second, with buttons to the system accessibility settings and to the recorder settings.
 */
class MainActivity : AppCompatActivity() {
    // Refreshes the status while the activity is visible.
    private val refreshHandler = Handler(Looper.getMainLooper())

    // Redraws the status and schedules the next redraw.
    private val refreshRunnable = object : Runnable {
        override fun run() {
            showStatus()
            refreshHandler.postDelayed(this, REFRESH_MILLISECONDS)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        // Store the launcher's values first, so a service that starts next reads them.
        storeLaunchValues(intent)

        // Open the system accessibility list, where the user enables the service by hand.
        findViewById<Button>(R.id.accessibility_settings_button).setOnClickListener {
            startActivity(Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))
        }

        // Open the recorder settings.
        findViewById<Button>(R.id.recorder_settings_button).setOnClickListener {
            startActivity(Intent(this, SettingsActivity::class.java))
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)

        // A second launch by the host carries new values for the same running activity.
        storeLaunchValues(intent)
    }

    override fun onPause() {
        // Stop polling while the screen is hidden.
        refreshHandler.removeCallbacks(refreshRunnable)

        super.onPause()
    }

    override fun onResume() {
        super.onResume()

        // Draw at once, then keep the numbers current.
        refreshHandler.post(refreshRunnable)
    }

    // Draws the current state.
    private fun showStatus() {
        val settings = SettingsStore.getSettings(this)
        val serviceText = if (RecorderState.isServiceRunning) getString(R.string.status_service_on) else getString(R.string.status_service_off)
        val targetText = settings.targetPackage.ifEmpty { getString(R.string.status_all_packages) }

        findViewById<TextView>(R.id.service_value).text = serviceText
        findViewById<TextView>(R.id.connection_value).text = RecorderState.connectionState
        findViewById<TextView>(R.id.events_value).text = RecorderState.eventCount.get().toString()
        findViewById<TextView>(R.id.hub_value).text = settings.hubUrl
        findViewById<TextView>(R.id.target_value).text = targetText
        findViewById<TextView>(R.id.error_value).text = RecorderState.lastError
    }

    // Stores the launcher extras; a missing extra keeps the stored value.
    private fun storeLaunchValues(launchIntent: Intent?) {
        val extras = launchIntent?.extras ?: return
        val values = LaunchValues(
            hubUrl = extras.getString(SettingsStore.KEY_HUB_URL),
            targetPackage = extras.getString(SettingsStore.KEY_TARGET_PACKAGE),
            deviceSerial = extras.getString(SettingsStore.KEY_DEVICE_SERIAL)
        )
        SettingsStore.setLaunchValues(this, values)
    }

    private companion object {
        // Status refresh interval.
        const val REFRESH_MILLISECONDS = 1000L
    }
}
