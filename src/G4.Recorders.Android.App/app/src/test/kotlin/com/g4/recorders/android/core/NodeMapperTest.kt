package com.g4.recorders.android.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NodeMapperTest {
    private val mapper = NodeMapper(machineName = "Pixel 9", machineAddress = "emulator-5554")

    @Test
    fun chainRunsTargetFirstAndMapsTheContractFields() {
        // Arrange: window > field.
        val root = FakeNode("android.widget.FrameLayout", bounds = NodeBounds(0, 0, 1080, 2400))
        val field = root.add(FakeNode("android.widget.EditText", resourceId = "com.app:id/email", text = "me@example.com", hintText = "Email", bounds = NodeBounds(40, 200, 1040, 320), isEditable = true))
        val viewContext = ViewContext(domContext = "PGEvPg==", domContextLength = 4)

        // Act.
        val chain = mapper.newChain(ChainOptions(target = field, activity = "com.app.LoginActivity", trigger = "ViewClicked", viewContext = viewContext))

        // Assert: the chain fields.
        assertEquals("//android.widget.EditText[@resource-id='com.app:id/email']", chain["locator"])
        assertEquals(mapOf("X" to 540, "Y" to 260), chain["point"])
        assertEquals("ViewClicked", chain["trigger"])

        // Assert: the trigger node is first and carries the snapshot, the root is last and is the top window.
        @Suppress("UNCHECKED_CAST")
        val path = chain["path"] as List<Map<String, Any?>>
        assertEquals(2, path.size)
        val trigger = path[0]
        assertEquals("com.app:id/email", trigger["automationId"])
        assertEquals("EditText", trigger["controlType"])
        assertEquals("Android", trigger["frameworkId"])
        assertEquals(true, trigger["isTriggerElement"])
        assertEquals(false, trigger["isTopWindow"])
        assertEquals(mapOf("height" to 120, "width" to 1000, "X" to 40, "Y" to 200), trigger["bounds"])
        assertEquals(mapOf("name" to "Pixel 9", "publicAddress" to "emulator-5554"), trigger["machine"])

        // Assert: the typed content never becomes the name; the hint does.
        assertEquals("Email", trigger["name"])

        @Suppress("UNCHECKED_CAST")
        val properties = trigger["properties"] as Map<String, Any?>
        assertEquals("PGEvPg==", properties["domContext"])
        assertEquals(4, properties["domContextLength"])
        assertEquals("com.app.LoginActivity", properties["activity"])
        assertEquals(true, path[1]["isTopWindow"])
        assertEquals(path[1], chain["topWindow"])

        @Suppress("UNCHECKED_CAST")
        val rootProperties = path[1]["properties"] as Map<String, Any?>
        assertFalse(rootProperties.containsKey("domContext"))
    }

    @Test
    fun containerIsNamedAfterItsFirstLabel() {
        // Arrange: a row with no text of its own and a title child.
        val root = FakeNode("android.widget.FrameLayout")
        val row = root.add(FakeNode("android.widget.LinearLayout", isClickable = true))
        row.add(FakeNode("android.widget.ImageView"))
        row.add(FakeNode("android.widget.TextView", text = "Network & internet"))

        // Act.
        val node = mapper.newNode(NodeOptions(node = row, activity = "", isTrigger = true, isTopWindow = false, viewContext = null))

        // Assert.
        assertEquals("Network & internet", node["name"])
    }

    @Test
    fun scrollContainerKeepsAnEmptyName() {
        // Arrange: a scroll view whose first child has a label.
        val root = FakeNode("android.widget.FrameLayout")
        val list = root.add(FakeNode("android.widget.ScrollView", isScrollable = true))
        list.add(FakeNode("android.widget.TextView", text = "First row"))

        // Act.
        val node = mapper.newNode(NodeOptions(node = list, activity = "", isTrigger = true, isTopWindow = false, viewContext = null))

        // Assert: the container is not named after content that scrolls away.
        assertEquals("", node["name"])
    }

    @Test
    fun emptyChainMatchesTheChromiumContextShape() {
        val chain = mapper.newEmptyChain("WindowStateChanged")

        assertEquals("", chain["locator"])
        assertEquals(emptyList<Any>(), chain["path"])
        assertNull(chain["point"])
        assertNull(chain["topWindow"])
    }

    @Test
    fun eventWrapsTheEntry() {
        val event = mapper.newEvent(mapper.newEmptyChain("WindowsChanged"), EventCatalog.newHideSoftKeyboard(), 1757618366611)

        assertEquals("HideSoftKeyboard", event["event"])
        assertEquals("Keyboard", event["type"])
        assertEquals("Pixel 9", event["machineName"])
        assertEquals(1757618366611, event["timestamp"])
        assertTrue((event["value"] as Map<*, *>).isEmpty())
    }
}
