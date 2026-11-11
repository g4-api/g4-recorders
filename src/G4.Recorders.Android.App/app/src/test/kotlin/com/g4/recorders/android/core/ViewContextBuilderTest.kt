package com.g4.recorders.android.core

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Base64

class ViewContextBuilderTest {
    // Decodes a snapshot back to its XML.
    private fun decode(viewContext: ViewContext): String {
        return String(Base64.getDecoder().decode(viewContext.domContext), Charsets.UTF_8)
    }

    @Test
    fun snapshotIsRootedTwoLevelsUpAndMarksTheTarget() {
        // Arrange: window > form > row > button.
        val root = FakeNode("android.widget.FrameLayout")
        val form = root.add(FakeNode("android.widget.LinearLayout", resourceId = "com.app:id/form"))
        val row = form.add(FakeNode("android.widget.LinearLayout"))
        val button = row.add(FakeNode("android.widget.Button", text = "Sign in"))

        // Act.
        val viewContext = ViewContextBuilder(ancestorLevels = 2, maximumKilobytes = 16).getViewContext(button)

        // Assert: the form is the root, the window is not included, and the button carries the marker.
        assertNotNull(viewContext)
        val markup = decode(viewContext!!)
        assertTrue(markup.startsWith("<android.widget.LinearLayout index=\"0\""))
        assertTrue(markup.contains("resource-id=\"com.app:id/form\""))
        assertFalse(markup.contains("android.widget.FrameLayout"))
        assertTrue(markup.contains("text=\"Sign in\""))
        assertTrue(markup.contains("g4-target=\"true\"/>"))
        assertEquals(markup.toByteArray(Charsets.UTF_8).size, viewContext.domContextLength)
    }

    @Test
    fun passwordTextIsBlankAndXmlIsEscaped() {
        // Arrange: a password field and a label with XML special characters.
        val root = FakeNode("android.widget.FrameLayout")
        root.add(FakeNode("android.widget.TextView", text = "A & <B> \"C\""))
        val password = root.add(FakeNode("android.widget.EditText", text = "secret", isEditable = true, isPassword = true))

        // Act.
        val markup = decode(ViewContextBuilder(ancestorLevels = 1, maximumKilobytes = 16).getViewContext(password)!!)

        // Assert.
        assertFalse(markup.contains("secret"))
        assertTrue(markup.contains("text=\"A &amp; &lt;B&gt; &quot;C&quot;\""))
    }

    @Test
    fun oversizedSnapshotShrinksToTheTargetTag() {
        // Arrange: a wide sibling list that cannot fit in 1 KB.
        val root = FakeNode("android.widget.FrameLayout")
        val list = root.add(FakeNode("android.widget.LinearLayout"))

        repeat(50) { list.add(FakeNode("android.widget.TextView", text = "Item $it with a long label")) }

        val target = list.add(FakeNode("android.widget.Button", text = "Go"))

        // Act.
        val markup = decode(ViewContextBuilder(ancestorLevels = 2, maximumKilobytes = 1).getViewContext(target)!!)

        // Assert: only the target element remains.
        assertTrue(markup.startsWith("<android.widget.Button"))
        assertTrue(markup.endsWith("g4-target=\"true\"/>"))
    }

    @Test
    fun targetLargerThanBudgetHasNoSnapshot() {
        // Arrange: a label longer than the 1 KB budget on its own.
        val root = FakeNode("android.widget.FrameLayout")
        val label = root.add(FakeNode("android.widget.TextView", text = "x".repeat(2000)))

        // Act and assert.
        assertNull(ViewContextBuilder(ancestorLevels = 0, maximumKilobytes = 1).getViewContext(label))
    }

    @Test
    fun zeroLevelsSnapshotsTheTargetSubtree() {
        // Arrange: a container target with one child.
        val root = FakeNode("android.widget.FrameLayout")
        val card = root.add(FakeNode("android.widget.LinearLayout", resourceId = "com.app:id/card"))
        card.add(FakeNode("android.widget.TextView", text = "Title"))

        // Act.
        val markup = decode(ViewContextBuilder(ancestorLevels = 0, maximumKilobytes = 16).getViewContext(card)!!)

        // Assert: the card is the root and keeps its child.
        assertTrue(markup.startsWith("<android.widget.LinearLayout"))
        assertTrue(markup.contains("text=\"Title\""))
        assertTrue(markup.endsWith("</android.widget.LinearLayout>"))
    }
}
