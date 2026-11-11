package com.g4.recorders.android.core

/**
 * Builds the XPath locator of a recorded node in the UiAutomator2 page-source vocabulary.
 *
 * Replay runs on the G4 `AndroidDriver` through Appium's UiAutomator2 driver, whose page source uses the Android
 * class as the element tag and exposes `resource-id`, `content-desc` and `text` as attributes under a `hierarchy`
 * root. Writing the locator in that vocabulary lets a recorded flow replay without any translation.
 *
 * Preference order: a unique resource id, a unique content description, a unique non-editable text, then the
 * absolute indexed path, which always matches exactly one node.
 */
object XPathLocatorBuilder {
    /**
     * Returns the locator of a node.
     *
     * Uniqueness is tested against the node's own window, which is what the replaying driver searches.
     *
     * @param target The node that triggered the event.
     * @return The XPath locator.
     */
    fun getLocator(target: RecorderNode): String {
        // Collect the window once so each candidate is checked against the same snapshot.
        val root = NodeTree.getRoot(target)
        val windowNodes = NodeTree.getDescendants(root).toList()
        val tag = getTag(target)

        // Prefer the resource id because developers keep it stable across releases and languages.
        val isResourceIdUnique = target.resourceId.isNotEmpty() &&
            windowNodes.count { it.className == target.className && it.resourceId == target.resourceId } == 1

        if (isResourceIdUnique) {
            return "//$tag[@resource-id=${convertToXPathLiteral(target.resourceId)}]"
        }

        // Fall back to the content description, which icon buttons usually carry instead of text.
        val isDescriptionUnique = target.contentDescription.isNotEmpty() &&
            windowNodes.count { it.className == target.className && it.contentDescription == target.contentDescription } == 1

        if (isDescriptionUnique) {
            return "//$tag[@content-desc=${convertToXPathLiteral(target.contentDescription)}]"
        }

        // Use the visible text only for labels and buttons; an editable field's text is user content that changes.
        val isTextCandidate = target.text.isNotEmpty() && !target.isEditable && !target.isPassword
        val isTextUnique = isTextCandidate &&
            windowNodes.count { it.className == target.className && it.text == target.text } == 1

        if (isTextUnique) {
            return "//$tag[@text=${convertToXPathLiteral(target.text)}]"
        }

        // A row or card is often a bare container whose label is a child; reach it through that label. A scrollable
        // container is skipped, because its children change as the content moves.
        val labelLocator = if (target.isScrollable) null else getLabelLocator(target, windowNodes)

        if (labelLocator != null) {
            return labelLocator
        }

        // Nothing identifies the node, so address it by its position from the hierarchy root.
        return getAbsolutePath(target)
    }

    /**
     * Returns a locator that finds a container through a unique label inside it, for example
     * `//android.widget.TextView[@text='Network & internet']/ancestor::android.widget.LinearLayout[1]`.
     *
     * The label must be the only node of its class with that text in the window, and the target must be the nearest
     * ancestor of its class above the label, so the XPath matches exactly the target.
     *
     * @param target The node to address.
     * @param windowNodes Every node of the target's window.
     * @return The locator, or null when no descendant label qualifies.
     */
    fun getLabelLocator(target: RecorderNode, windowNodes: List<RecorderNode>): String? {
        // Search the target's own subtree in document order; the first qualifying label is the most prominent one.
        val labels = NodeTree.getDescendants(target).drop(1).filter { it.text.isNotEmpty() && !it.isEditable && !it.isPassword }

        for (label in labels) {
            // The label alone must identify one node in the window.
            val isUnique = windowNodes.count { it.className == label.className && it.text == label.text } == 1

            if (!isUnique) {
                continue
            }

            // Walking up from the label, the first node of the target's class must be the target itself.
            val nearest = NodeTree.getAncestors(label).firstOrNull { it.className == target.className }

            if (nearest != null && nearest.testSameNode(target)) {
                return "//${getTag(label)}[@text=${convertToXPathLiteral(label.text)}]/ancestor::${getTag(target)}[1]"
            }
        }

        return null
    }

    /**
     * Returns the absolute indexed path of a node, for example `/hierarchy/android.widget.FrameLayout[1]/...`.
     *
     * Positions are 1-based among the siblings with the same class, the XPath rule for `tag[n]`. The window root is
     * always `[1]` because the UiAutomator2 driver lists the active window only.
     *
     * @param target The node to address.
     * @return The absolute path.
     */
    fun getAbsolutePath(target: RecorderNode): String {
        // Build the segments from the target up, then reverse them so the path reads from the root down.
        val segments = mutableListOf<String>()
        var current: RecorderNode? = target
        var depth = 0

        while (current != null && depth < MAXIMUM_DEPTH) {
            val parent = current.parent
            val position = if (parent == null) 1 else getClassPosition(current, parent)
            segments.add("${getTag(current)}[$position]")
            current = parent
            depth++
        }

        return "/hierarchy/" + segments.asReversed().joinToString(separator = "/")
    }

    /**
     * Converts a value to an XPath 1.0 string literal, using `concat()` when it holds both quote characters.
     *
     * @param value The raw attribute value.
     * @return The quoted literal.
     */
    fun convertToXPathLiteral(value: String): String {
        // Pick the quote character the value does not contain; XPath 1.0 has no escape sequence.
        if (!value.contains('\'')) {
            return "'$value'"
        }

        if (!value.contains('"')) {
            return "\"$value\""
        }

        // Split on the single quote and rejoin the parts with a double-quoted quote inside concat().
        val parts = value.split('\'').map { "'$it'" }

        return "concat(" + parts.joinToString(separator = ", \"'\", ") + ")"
    }

    // Returns the 1-based position of a node among the parent's children of the same class.
    private fun getClassPosition(node: RecorderNode, parent: RecorderNode): Int {
        // Count same-class siblings up to and including the node itself.
        var position = 0

        for (sibling in parent.children) {
            if (sibling.className == node.className) {
                position++
            }

            if (sibling.testSameNode(node)) {
                return maxOf(1, position)
            }
        }

        // The parent no longer lists the node (the tree changed under us); the first position is the best guess.
        return 1
    }

    // Returns the XML tag of a node, a wildcard when Android reports no class.
    private fun getTag(node: RecorderNode): String {
        return node.className.ifEmpty { "*" }
    }

    // Deepest path the builder follows; real Android trees stay far below it.
    private const val MAXIMUM_DEPTH = 256
}
