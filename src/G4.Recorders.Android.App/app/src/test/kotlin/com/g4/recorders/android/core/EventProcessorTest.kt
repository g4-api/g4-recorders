package com.g4.recorders.android.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class EventProcessorTest {
    // Events the processor emitted, in order.
    private val emitted = mutableListOf<Map<String, Any?>>()

    // The fake clock, advanced by the tests.
    private var now = 1_000L

    // The settings the processor reads; tests replace them.
    private var settings = RecorderSettings()

    private val processor = EventProcessor(EventProcessorOptions(
        mapper = NodeMapper(machineName = "device", machineAddress = "serial"),
        settings = { settings },
        clock = { now },
        emit = { emitted.add(it) }
    ))

    // A login screen: window > email field, password field, sign-in button, list.
    private val root = FakeNode("android.widget.FrameLayout", bounds = NodeBounds(0, 0, 1080, 2400))
    private val email = root.add(FakeNode("android.widget.EditText", resourceId = "com.app:id/email", isEditable = true))
    private val password = root.add(FakeNode("android.widget.EditText", resourceId = "com.app:id/password", isEditable = true, isPassword = true))
    private val signIn = root.add(FakeNode("android.widget.Button", text = "Sign in", isClickable = true))
    private val list = root.add(FakeNode("androidx.recyclerview.widget.RecyclerView", resourceId = "com.app:id/list"))

    // Returns the event names emitted so far.
    private fun getNames(): List<Any?> = emitted.map { it["event"] }

    // Returns the value map of an emitted event.
    private fun getValue(index: Int): Map<*, *> = emitted[index]["value"] as Map<*, *>

    @Test
    fun typedTextIsOneSendKeysWithTheFinalTextBeforeTheNextTap() {
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
    fun tapOnTheEditedFieldKeepsItsTextOpen() {
        processor.onTextChanged(email, "ab")
        processor.onClicked(email)
        processor.onTextChanged(email, "abc")
        processor.commitPending()

        assertEquals(listOf("InvokeClick", "SendKeys"), getNames())
        assertEquals("abc", getValue(1)["text"])
    }

    @Test
    fun focusOnAnotherFieldCommitsTheText() {
        processor.onTextChanged(email, "me@example.com")
        processor.onFocused(password)

        assertEquals(listOf("SendKeys"), getNames())
    }

    @Test
    fun passwordIsRedactedWhenTheSettingIsOn() {
        settings = RecorderSettings(isRedactPasswordsEnabled = true)

        processor.onTextChanged(password, "secret")
        processor.commitPending()

        assertEquals(mapOf("text" to "***", "isRedacted" to true), getValue(0))
    }

    @Test
    fun keyboardClosingCommitsTextThenHides() {
        processor.onKeyboardVisibilityChanged(true)
        processor.onTextChanged(email, "abc")
        processor.onKeyboardVisibilityChanged(false)
        processor.onKeyboardVisibilityChanged(false)

        assertEquals(listOf("SendKeys", "HideSoftKeyboard"), getNames())
    }

    @Test
    fun backKeyCommitsTextThenNavigatesBack() {
        // Act: type into a field, then press Back.
        processor.onTextChanged(email, "abc")
        processor.onBackPressed()

        // Assert: the text precedes the navigation, which carries the Chromium navigation type and no target.
        assertEquals(listOf("SendKeys", "UndoNavigation"), getNames())
        assertEquals("Navigation", emitted[1]["type"])
        assertEquals("", (emitted[1]["chain"] as Map<*, *>)["locator"])
    }

    @Test
    fun scrollEventsCoalesceIntoOneGesture() {
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
    fun scrollGestureExpiresAfterTheWindow() {
        processor.onScrolled(list, 0, -50)
        now += 200
        processor.commitExpiredScroll()
        assertTrue(emitted.isEmpty())

        now += 400
        processor.commitExpiredScroll()

        assertEquals(listOf("InvokeScroll"), getNames())
        assertEquals("Up", getValue(0)["direction"])
    }

    @Test
    fun directionChangeStartsANewGesture() {
        processor.onScrolled(list, 0, 50)
        processor.onScrolled(list, 0, -50)
        processor.commitPending()

        assertEquals(2, emitted.size)
        assertEquals("Down", getValue(0)["direction"])
        assertEquals("Up", getValue(1)["direction"])
    }

    @Test
    fun activityChangeIsAContextEventAndPopupsAreIgnored() {
        processor.onWindowStateChanged("com.example.app", "com.example.app.LoginActivity")
        processor.onWindowStateChanged("com.example.app", "")
        processor.onWindowStateChanged("com.example.app", "com.example.app.LoginActivity")
        processor.onTextChanged(email, "abc")
        processor.onWindowStateChanged("com.example.app", "com.example.app.HomeActivity")

        assertEquals(listOf("SwitchActivity", "SendKeys", "SwitchActivity"), getNames())
        assertEquals("Context", emitted[0]["type"])
        assertEquals(mapOf("package" to "com.example.app", "activity" to "com.example.app.HomeActivity"), getValue(2))
        assertEquals("com.example.app.HomeActivity", processor.currentActivity)
    }

    @Test
    fun elementEventsCarryTheViewContextAndScrollsDoNot() {
        processor.onClicked(signIn)
        processor.onScrolled(list, 0, 50)
        processor.commitPending()

        assertTrue(getTriggerProperties(0).containsKey("domContext"))
        assertFalse(getTriggerProperties(1).containsKey("domContext"))
    }

    @Test
    fun viewContextSettingOffRemovesTheSnapshot() {
        settings = RecorderSettings(isViewContextEnabled = false)

        processor.onClicked(signIn)

        assertFalse(getTriggerProperties(0).containsKey("domContext"))
    }

    @Test
    fun disabledEventsAreNotRecordedButStillTracked() {
        settings = RecorderSettings(enabledEvents = RecorderSettings.ALL_EVENT_NAMES - EventCatalog.INVOKE_CLICK)

        processor.onClicked(signIn)

        assertTrue(emitted.isEmpty())
        assertEquals(signIn, processor.lastTouchedNode)
    }

    @Test
    fun peekFindsTheTopmostNodeAtAPoint() {
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
