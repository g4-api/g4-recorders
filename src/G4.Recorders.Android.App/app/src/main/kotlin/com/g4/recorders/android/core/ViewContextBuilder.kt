package com.g4.recorders.android.core

import java.util.Base64

/**
 * The view context snapshot attached to the trigger node of element events.
 *
 * The property names match the Chromium recorder's DOM context on purpose, so the Hub copies the snapshot into the
 * rule capabilities and the VS Code `excludeDomContext` switch removes it without any Android-specific code.
 *
 * @property domContext Base64 of the UTF-8 XML snapshot.
 * @property domContextLength Decoded byte count, so a client can check it decoded the whole snapshot.
 */
data class ViewContext(
    val domContext: String,
    val domContextLength: Int,
)

/**
 * Builds the view context snapshot: the XML of the subtree rooted a few ancestors above the target.
 *
 * The XML uses the UiAutomator2 page-source vocabulary, the same one the locator uses, so a client that heals a
 * locator can test its candidate XPath directly against the snapshot. The target carries `g4-target="true"`.
 * Password fields lose their text.
 *
 * When the snapshot is larger than the budget, the builder drops one ancestor level at a time, then falls back to
 * the target element alone; when even that is too large, there is no snapshot.
 *
 * @param ancestorLevels How many ancestors above the target the snapshot is rooted at.
 * @param maximumKilobytes Largest decoded snapshot, in kilobytes.
 */
class ViewContextBuilder(
    private val ancestorLevels: Int,
    private val maximumKilobytes: Int,
) {
    /**
     * Returns the snapshot for a target node.
     *
     * @param target The node that triggered the event.
     * @return The snapshot, or null when nothing fits in the budget.
     */
    fun getViewContext(target: RecorderNode): ViewContext? {
        // Resolve the budget and the candidate roots, nearest last, so the loop starts from the widest snapshot.
        val maximumBytes = maxOf(1, maximumKilobytes) * BYTES_PER_KILOBYTE
        val ancestors = NodeTree.getAncestors(target)
        val levels = minOf(maxOf(0, ancestorLevels), ancestors.size)

        // Try the widest root first and narrow down; the first snapshot that fits wins.
        for (level in levels downTo 0) {
            val root = if (level == 0) target else ancestors[level - 1]
            val markup = getMarkup(root, target, maximumBytes) ?: continue
            val snapshot = newViewContext(markup, maximumBytes)

            if (snapshot != null) {
                return snapshot
            }
        }

        // Fall back to the target element without its children, which still shows the attributes to heal from.
        val targetMarkup = StringBuilder()
        writeOpeningTag(targetMarkup, target, target, isSelfClosing = true)

        return newViewContext(targetMarkup.toString(), maximumBytes)
    }

    // Writes the subtree of a root as XML; returns null as soon as the text outgrows the budget.
    private fun getMarkup(
        root: RecorderNode,
        target: RecorderNode,
        maximumBytes: Int,
    ): String? {
        // A UTF-8 byte is at least one character, so a character count above the budget is already too large.
        val builder = StringBuilder()
        val isWritten = writeNode(builder, root, target, maximumBytes, depth = 0)

        return if (isWritten) builder.toString() else null
    }

    // Encodes the markup when its UTF-8 size fits the budget.
    private fun newViewContext(
        markup: String,
        maximumBytes: Int,
    ): ViewContext? {
        val bytes = markup.toByteArray(Charsets.UTF_8)

        if (bytes.size > maximumBytes) {
            return null
        }

        return ViewContext(domContext = Base64.getEncoder().encodeToString(bytes), domContextLength = bytes.size)
    }

    // Writes one node and its children; returns false when the budget or the depth guard is exceeded.
    private fun writeNode(
        builder: StringBuilder,
        node: RecorderNode,
        target: RecorderNode,
        maximumBytes: Int,
        depth: Int,
    ): Boolean {
        // Stop early instead of building a huge string that is thrown away anyway.
        if (builder.length > maximumBytes || depth > MAXIMUM_DEPTH) {
            return false
        }

        // Leaves close their tag at once, like the UiAutomator2 page source does.
        val children = node.children

        if (children.isEmpty()) {
            writeOpeningTag(builder, node, target, isSelfClosing = true)

            return builder.length <= maximumBytes
        }

        // Write the children between the opening and closing tags, failing as soon as one does not fit.
        writeOpeningTag(builder, node, target, isSelfClosing = false)

        for (child in children) {
            if (!writeNode(builder, child, target, maximumBytes, depth + 1)) {
                return false
            }
        }

        builder.append("</").append(getTag(node)).append('>')

        return builder.length <= maximumBytes
    }

    // Writes the opening tag of a node with its UiAutomator2 attributes.
    private fun writeOpeningTag(
        builder: StringBuilder,
        node: RecorderNode,
        target: RecorderNode,
        isSelfClosing: Boolean,
    ) {
        // Never write a password field's content, whatever Android reports for it.
        val text = if (node.isPassword) "" else node.text

        builder.append('<').append(getTag(node))
        writeAttribute(builder, "index", NodeTree.getSiblingIndex(node).toString())
        writeAttribute(builder, "package", node.packageName)
        writeAttribute(builder, "class", node.className)
        writeAttribute(builder, "text", text)
        writeAttribute(builder, "resource-id", node.resourceId)
        writeAttribute(builder, "content-desc", node.contentDescription)
        writeAttribute(builder, "hint", node.hintText)
        writeAttribute(builder, "checkable", node.isCheckable.toString())
        writeAttribute(builder, "checked", node.isChecked.toString())
        writeAttribute(builder, "clickable", node.isClickable.toString())
        writeAttribute(builder, "enabled", node.isEnabled.toString())
        writeAttribute(builder, "focusable", node.isFocusable.toString())
        writeAttribute(builder, "focused", node.isFocused.toString())
        writeAttribute(builder, "long-clickable", node.isLongClickable.toString())
        writeAttribute(builder, "password", node.isPassword.toString())
        writeAttribute(builder, "scrollable", node.isScrollable.toString())
        writeAttribute(builder, "selected", node.isSelected.toString())
        writeAttribute(builder, "bounds", node.bounds.toBoundsText())
        writeAttribute(builder, "displayed", node.isVisible.toString())

        // Mark the recorded element so a client finds it without re-running the locator.
        if (node.isSameNode(target)) {
            writeAttribute(builder, "g4-target", "true")
        }

        builder.append(if (isSelfClosing) "/>" else ">")
    }

    private companion object {
        // Bytes in a kilobyte, matching the Chromium recorder's budget unit.
        const val BYTES_PER_KILOBYTE = 1024

        // Deepest subtree the builder writes; real Android trees stay far below it.
        const val MAXIMUM_DEPTH = 256

        // Returns the XML tag of a node; UiAutomator2 writes a generic view tag when Android reports no class.
        fun getTag(node: RecorderNode): String = node.className.ifEmpty { "android.view.View" }

        // Writes one attribute with XML escaping.
        fun writeAttribute(
            builder: StringBuilder,
            name: String,
            value: String,
        ) {
            builder
                .append(' ')
                .append(name)
                .append("=\"")
                .append(convertToXmlText(value))
                .append('"')
        }

        // Escapes the five XML special characters and drops control characters XML 1.0 cannot carry.
        fun convertToXmlText(value: String): String {
            val builder = StringBuilder(value.length)

            for (character in value) {
                when (character) {
                    '&' -> builder.append("&amp;")
                    '<' -> builder.append("&lt;")
                    '>' -> builder.append("&gt;")
                    '"' -> builder.append("&quot;")
                    '\'' -> builder.append("&apos;")
                    '\n' -> builder.append("&#10;")
                    '\r' -> builder.append("&#13;")
                    '\t' -> builder.append("&#9;")
                    else -> if (character >= ' ') builder.append(character)
                }
            }

            return builder.toString()
        }
    }
}
