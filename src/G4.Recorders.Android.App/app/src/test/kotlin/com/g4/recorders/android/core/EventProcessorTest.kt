package com.g4.recorders.android.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class EventProcessorTest {
    // Events the processor emitted, in order.
    private val emitted = mutableListOf<Map<String, Any?>>()

    // The fake clock, advanced by the tests.
    private var now = 1_000L

    // The settings the processor reads; tests replace them.
    private var settings = RecorderSettings()

    private val processor =
        EventProcessor(
            EventProcessorOptions(
                mapper = NodeMapper(machineName = "device", machineAddress = "serial"),
                settings = { settings },
                clock = { now },
                emit = { emitted.add(it) },
            ),
        )

    // A login screen: window > email field, password field, sign-in button, list.
    private val root = FakeNode("android.widget.FrameLayout", bounds = NodeBounds(0, 0, 1080, 2400))
    private val email = root.add(FakeNode("android.widget.EditText", resourceId = "com.app:id/email", isEditable = true))
    private val password =
        root.add(
            FakeNode("android.widget.EditText", resourceId = "com.app:id/password", isEditable = true, isPassword = true),
        )
    private val signIn = root.add(FakeNode("android.widget.Button", text = "Sign in", isClickable = true))
    private val list = root.add(FakeNode("androidx.recyclerview.widget.RecyclerView", resourceId = "com.app:id/list"))

    // Returns the event names emitted so far.
    private fun getNames(): List<Any?> = emitted.map { it["event"] }

    // Returns the value map of an emitted event.
    private fun getValue(index: Int): Map<*, *> = emitted[index]["value"] as Map<*, *>

    @Test
    fun `typed text is one SendKeys with the final text before the next tap`() {
        // Act: tap the field, type three characters, tap the button.
        processor.onClicked(email)
        processor.onTextChanged(email, "a")
        processor.onTextChanged(email, "ab")
        processor.onTextChanged(email, "abc")
        processor.onClicked(signIn)

        // Assert: one SendKeys with the final text, between the two taps.
        assertEquals(listOf("InvokeClick", "SendKeys", "InvokeClick"), getNames())
        assertEquals("abc", getValue(1)["text"])
    }

    @Test
    fun `tap on the edited field keeps its text open`() {
        // Act: type, tap the same field to move the cursor, type again, then flush.
        processor.onTextChanged(email, "ab")
        processor.onClicked(email)
        processor.onTextChanged(email, "abc")
        processor.commitPending()

        // Assert: the tap is recorded, but the field stays one SendKeys with its final text.
        assertEquals(listOf("InvokeClick", "SendKeys"), getNames())
        assertEquals("abc", getValue(1)["text"])
    }

    @Test
    fun `focus on another field commits the text`() {
        // Act: type into the email field, then move the focus to the password field.
        processor.onTextChanged(email, "me@example.com")
        processor.onFocused(password)

        // Assert: leaving the field sends its text without waiting for a flush.
        assertEquals(listOf("SendKeys"), getNames())
    }

    @Test
    fun `password is redacted when the setting is on`() {
        // Arrange: turn password redaction on.
        settings = RecorderSettings(isRedactPasswordsEnabled = true)

        // Act: type a password and flush.
        processor.onTextChanged(password, "secret")
        processor.commitPending()

        // Assert: the payload carries the redaction marker, never the password.
        assertEquals(mapOf("text" to "***", "isRedacted" to true), getValue(0))
    }

    @Test
    fun `keyboard closing commits text then hides`() {
        // Act: open the keyboard, type, then report the keyboard hidden twice, as Android repeats window changes.
        processor.onKeyboardVisibilityChanged(true)
        processor.onTextChanged(email, "abc")
        processor.onKeyboardVisibilityChanged(false)
        processor.onKeyboardVisibilityChanged(false)

        // Assert: the text comes first, and only the visible-to-hidden transition is recorded.
        assertEquals(listOf("SendKeys", "HideSoftKeyboard"), getNames())
    }

    @Test
    fun `Back key commits the text then navigates back`() {
        // Act: type into a field, then press Back.
        processor.onTextChanged(email, "abc")
        processor.onBackPressed()

        // Assert: the text precedes the navigation, which carries the Chromium navigation type and no target.
        assertEquals(listOf("SendKeys", "UndoNavigation"), getNames())
        assertEquals("Navigation", emitted[1]["type"])
        assertEquals("", (emitted[1]["chain"] as Map<*, *>)["locator"])
    }

    @Test
    fun `scroll events coalesce into one gesture`() {
        // Act: three downward scrolls inside the window, then a tap.
        processor.onScrolled(list, 0, 120)
        now += 100
        processor.onScrolled(list, 0, 80)
        now += 100
        processor.onScrolled(list, 0, 60)
        processor.onClicked(signIn)

        // Assert: one scroll with three notches, sent before the tap.
        assertEquals(listOf("InvokeScroll", "InvokeClick"), getNames())
        assertEquals(mapOf("direction" to "Down", "notches" to 3), getValue(0))
    }

    @Test
    fun `scroll gesture expires after the window`() {
        // Act: scroll up, then check for expiry inside the window.
        processor.onScrolled(list, 0, -50)
        now += 200
        processor.commitExpiredScroll()

        // Assert: the gesture is still open inside the window.
        assertTrue(emitted.isEmpty())

        // Act: check again after the window has passed.
        now += 400
        processor.commitExpiredScroll()

        // Assert: the gesture is sent once, with its direction.
        assertEquals(listOf("InvokeScroll"), getNames())
        assertEquals("Up", getValue(0)["direction"])
    }

    @Test
    fun `direction change starts a new gesture`() {
        // Act: scroll down, then immediately up on the same list.
        processor.onScrolled(list, 0, 50)
        processor.onScrolled(list, 0, -50)
        processor.commitPending()

        // Assert: the reversal splits the scrolling into two gestures.
        assertEquals(2, emitted.size)
        assertEquals("Down", getValue(0)["direction"])
        assertEquals("Up", getValue(1)["direction"])
    }

    @Test
    fun `activity change is a context event and popups are ignored`() {
        // Act: enter an activity, show a popup (no activity), re-report the same activity, type, then switch.
        processor.onWindowStateChanged("com.example.app", "com.example.app.LoginActivity")
        processor.onWindowStateChanged("com.example.app", "")
        processor.onWindowStateChanged("com.example.app", "com.example.app.LoginActivity")
        processor.onTextChanged(email, "abc")
        processor.onWindowStateChanged("com.example.app", "com.example.app.HomeActivity")

        // Assert: only real activity changes are sent, as context events, and the text precedes the second one.
        assertEquals(listOf("SwitchActivity", "SendKeys", "SwitchActivity"), getNames())
        assertEquals("Context", emitted[0]["type"])
        assertEquals(mapOf("package" to "com.example.app", "activity" to "com.example.app.HomeActivity"), getValue(2))
        assertEquals("com.example.app.HomeActivity", processor.currentActivity)
    }

    @Test
    fun `element events carry the view context and scrolls do not`() {
        // Act: tap a button, then scroll a list.
        processor.onClicked(signIn)
        processor.onScrolled(list, 0, 50)
        processor.commitPending()

        // Assert: only the element event carries a snapshot on its trigger node.
        assertTrue(getTriggerProperties(0).containsKey("domContext"))
        assertFalse(getTriggerProperties(1).containsKey("domContext"))
    }

    @Test
    fun `view context setting off removes the snapshot`() {
        // Arrange: turn the view context off.
        settings = RecorderSettings(isViewContextEnabled = false)

        // Act: tap a button.
        processor.onClicked(signIn)

        // Assert: the event carries no snapshot.
        assertFalse(getTriggerProperties(0).containsKey("domContext"))
    }

    @Test
    fun `disabled events are not recorded but still tracked`() {
        // Arrange: turn taps off.
        settings = RecorderSettings(enabledEvents = RecorderSettings.ALL_EVENT_NAMES - EventCatalog.INVOKE_CLICK)

        // Act: tap a button.
        processor.onClicked(signIn)

        // Assert: nothing is sent, but the node stays available to peek requests.
        assertTrue(emitted.isEmpty())
        assertEquals(signIn, processor.lastTouchedNode)
    }

    @Test
    fun `peek finds the topmost node at a point`() {
        // Arrange: two overlapping children; the later one is drawn on top.
        val window = FakeNode("android.widget.FrameLayout", bounds = NodeBounds(0, 0, 1000, 1000))
        window.add(FakeNode("android.widget.TextView", bounds = NodeBounds(0, 0, 500, 500)))
        val overlay = window.add(FakeNode("android.widget.Button", bounds = NodeBounds(100, 100, 400, 400)))

        // Act and assert.
        assertEquals(overlay, PeekResolver.findNodeAt(window, 200, 200))
        assertEquals(window, PeekResolver.findNodeAt(window, 800, 800))
        assertNull(PeekResolver.findNodeAt(window, 2000, 2000))
    }

    // Returns the trigger node properties of an emitted element event.
    private fun getTriggerProperties(index: Int): Map<*, *> {
        val chain = emitted[index]["chain"] as Map<*, *>
        val path = chain["path"] as List<*>

        return (path[0] as Map<*, *>)["properties"] as Map<*, *>
    }
}
