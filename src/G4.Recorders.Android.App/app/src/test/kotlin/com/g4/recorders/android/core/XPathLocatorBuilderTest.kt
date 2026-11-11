package com.g4.recorders.android.core

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

class XPathLocatorBuilderTest {
    @Test
    fun `unique resource id wins`() {
        // Arrange: a form with one email field that also has text and a description.
        val root = FakeNode("android.widget.FrameLayout")
        val form = root.add(FakeNode("android.widget.LinearLayout"))
        val email =
            form.add(
                FakeNode("android.widget.EditText", resourceId = "com.app:id/email", contentDescription = "Email", isEditable = true),
            )

        // Act and assert: the resource id is the first choice.
        assertEquals("//android.widget.EditText[@resource-id='com.app:id/email']", XPathLocatorBuilder.getLocator(email))
    }

    @Test
    fun `duplicate resource id falls back to description`() {
        // Arrange: two buttons share an id, but their descriptions differ.
        val root = FakeNode("android.widget.FrameLayout")
        root.add(FakeNode("android.widget.ImageButton", resourceId = "com.app:id/action", contentDescription = "Share"))
        val delete = root.add(FakeNode("android.widget.ImageButton", resourceId = "com.app:id/action", contentDescription = "Delete"))

        // Act and assert.
        assertEquals("//android.widget.ImageButton[@content-desc='Delete']", XPathLocatorBuilder.getLocator(delete))
    }

    @Test
    fun `unique label text is used`() {
        // Arrange: a button identified only by its label.
        val root = FakeNode("android.widget.FrameLayout")
        val button = root.add(FakeNode("android.widget.Button", text = "Sign in"))
        root.add(FakeNode("android.widget.Button", text = "Cancel"))

        // Act and assert.
        assertEquals("//android.widget.Button[@text='Sign in']", XPathLocatorBuilder.getLocator(button))
    }

    @Test
    fun `editable text is never used`() {
        // Arrange: a field identified only by what the user typed.
        val root = FakeNode("android.widget.FrameLayout")
        val field = root.add(FakeNode("android.widget.EditText", text = "typed value", isEditable = true))

        // Act and assert: the typed text changes, so the absolute path is used.
        assertEquals("/hierarchy/android.widget.FrameLayout[1]/android.widget.EditText[1]", XPathLocatorBuilder.getLocator(field))
    }

    @Test
    fun `container is reached through its unique label`() {
        // Arrange: two list rows inside a layout of the same class; each row's title is a child TextView.
        val root = FakeNode("android.widget.FrameLayout")
        val list = root.add(FakeNode("android.widget.LinearLayout"))
        val network = list.add(FakeNode("android.widget.LinearLayout", isClickable = true))
        val title = network.add(FakeNode("android.widget.RelativeLayout"))
        title.add(FakeNode("android.widget.TextView", text = "Network & internet"))
        network.add(FakeNode("android.widget.TextView", text = "Wi-Fi"))
        val apps = list.add(FakeNode("android.widget.LinearLayout", isClickable = true))
        apps.add(FakeNode("android.widget.TextView", text = "Apps"))

        // Act and assert: the nearest LinearLayout above the label is the tapped row.
        assertEquals(
            "//android.widget.TextView[@text='Network & internet']/ancestor::android.widget.LinearLayout[1]",
            XPathLocatorBuilder.getLocator(network),
        )
    }

    @Test
    fun `label that is not unique falls back to the absolute path`() {
        // Arrange: two rows with the same title.
        val root = FakeNode("android.widget.FrameLayout")
        root.add(FakeNode("android.widget.LinearLayout", isClickable = true)).add(FakeNode("android.widget.TextView", text = "Same"))
        val second = root.add(FakeNode("android.widget.LinearLayout", isClickable = true))
        second.add(FakeNode("android.widget.TextView", text = "Same"))

        // Act and assert.
        assertEquals("/hierarchy/android.widget.FrameLayout[1]/android.widget.LinearLayout[2]", XPathLocatorBuilder.getLocator(second))
    }

    @Test
    fun `absolute path counts same class siblings only`() {
        // Arrange: the target is the second TextView, the third child.
        val root = FakeNode("android.widget.FrameLayout")
        val list = root.add(FakeNode("android.widget.LinearLayout"))
        list.add(FakeNode("android.widget.TextView"))
        list.add(FakeNode("android.widget.ImageView"))
        val target = list.add(FakeNode("android.widget.TextView"))

        // Act.
        val path = XPathLocatorBuilder.getAbsolutePath(target)

        // Assert: the position is 2 among TextViews, not 3 among all children.
        assertEquals("/hierarchy/android.widget.FrameLayout[1]/android.widget.LinearLayout[1]/android.widget.TextView[2]", path)
    }

    @Test
    fun `literals quote safely`() {
        // Act and assert: plain, single-quoted, and doubly-quoted values each become one valid XPath literal.
        assertEquals("'plain'", XPathLocatorBuilder.toXPathLiteral("plain"))
        assertEquals("\"it's\"", XPathLocatorBuilder.toXPathLiteral("it's"))
        assertEquals("concat('say \"it', \"'\", 's\"')", XPathLocatorBuilder.toXPathLiteral("say \"it's\""))
    }

    @Test
    fun `quoted resource id uses double quotes`() {
        // Arrange: a description with an apostrophe.
        val root = FakeNode("android.widget.FrameLayout")
        val button = root.add(FakeNode("android.widget.Button", contentDescription = "Don't save"))

        // Act and assert.
        assertEquals("//android.widget.Button[@content-desc=\"Don't save\"]", XPathLocatorBuilder.getLocator(button))
    }
}
