package com.g4.recorders.android.core

/**
 * Read-only view of one node of the Android accessibility tree.
 *
 * The recorder core maps, locates and snapshots nodes through this interface only. The service wraps each
 * `AccessibilityNodeInfo` in an adapter that implements it, and the JVM unit tests use plain fakes, so the core
 * never touches an Android API and runs without a device.
 *
 * Every text member returns an empty string, never null, when Android reports no value.
 */
interface RecorderNode {
    /** Screen rectangle of the node, in physical pixels. */
    val bounds: NodeBounds

    /** Direct children in drawing order; empty for a leaf. */
    val children: List<RecorderNode>

    /** Fully qualified Android class, for example `android.widget.Button`; UiAutomator2 uses it as the XML tag. */
    val className: String

    /** Content description, read by screen readers and exposed as `content-desc` by UiAutomator2. */
    val contentDescription: String

    /** Hint text of an editable field. */
    val hintText: String

    /** True when the node can be checked. */
    val isCheckable: Boolean

    /** True when the node is checked. */
    val isChecked: Boolean

    /** True when the node reacts to a click. */
    val isClickable: Boolean

    /** True when the node accepts typed text. */
    val isEditable: Boolean

    /** True when the node is enabled. */
    val isEnabled: Boolean

    /** True when the node can take input focus. */
    val isFocusable: Boolean

    /** True when the node holds input focus. */
    val isFocused: Boolean

    /** True when the node reacts to a long press. */
    val isLongClickable: Boolean

    /** True when the node is a password field; its text is never recorded. */
    val isPassword: Boolean

    /** True when the node scrolls its content. */
    val isScrollable: Boolean

    /** True when the node is selected. */
    val isSelected: Boolean

    /** True when the node is visible to the user. */
    val isVisible: Boolean

    /** Package of the application that owns the node. */
    val packageName: String

    /** Parent node, or null for the root of a window. */
    val parent: RecorderNode?

    /** Resource id such as `com.app:id/email`, or empty when the view has none. */
    val resourceId: String

    /** Text the node shows; for an editable field this is its current content. */
    val text: String

    /**
     * Tests whether this node and [other] stand for the same view.
     *
     * Adapters create a new wrapper on every tree walk, so identity comparison is not enough; the Android
     * adapter compares the wrapped node infos, which compare their source ids.
     *
     * @param other The node to compare with.
     * @return True when both refer to the same view.
     */
    fun isSameNode(other: RecorderNode): Boolean
}

/**
 * Screen rectangle of a node in physical pixels, with exclusive right and bottom edges like `android.graphics.Rect`.
 *
 * @property left Left edge.
 * @property top Top edge.
 * @property right Right edge, exclusive.
 * @property bottom Bottom edge, exclusive.
 */
data class NodeBounds(
    val left: Int,
    val top: Int,
    val right: Int,
    val bottom: Int,
) {
    /** Height in pixels, never negative. */
    val height: Int get() = maxOf(0, bottom - top)

    /** Width in pixels, never negative. */
    val width: Int get() = maxOf(0, right - left)

    /**
     * Tests whether a screen point lies inside the rectangle.
     *
     * @param x Horizontal screen coordinate.
     * @param y Vertical screen coordinate.
     * @return True when the point is inside.
     */
    fun contains(
        x: Int,
        y: Int,
    ): Boolean = x >= left && x < right && y >= top && y < bottom

    /**
     * Converts the rectangle to the UiAutomator2 text form, for example `[0,0][1080,2400]`.
     *
     * @return The bounds text.
     */
    fun toBoundsText(): String = "[$left,$top][$right,$bottom]"
}

/**
 * Tree helpers shared by the locator, the mapper and the view context builder.
 */
object NodeTree {
    /**
     * Returns the ancestors of a node, nearest first, without the node itself.
     *
     * @param node The node to start from.
     * @return The parent, the grandparent, and so on up to the window root.
     */
    fun getAncestors(node: RecorderNode): List<RecorderNode> {
        // Walk up until the window root; the guard stops a malformed, cyclic tree from looping forever.
        val ancestors = mutableListOf<RecorderNode>()
        var current = node.parent

        while (current != null && ancestors.size < MAXIMUM_DEPTH) {
            ancestors.add(current)
            current = current.parent
        }

        return ancestors
    }

    /**
     * Returns every node of a tree in document order, starting with the root.
     *
     * @param root The root of the tree.
     * @return A lazy sequence over the tree.
     */
    fun getDescendants(root: RecorderNode): Sequence<RecorderNode> {
        // Use an explicit stack so very deep trees cannot overflow the call stack.
        return sequence {
            val stack = ArrayDeque<RecorderNode>()
            stack.addLast(root)

            while (stack.isNotEmpty()) {
                val node = stack.removeLast()
                yield(node)

                // Push the children in reverse so the first child is visited next.
                for (child in node.children.asReversed()) {
                    stack.addLast(child)
                }
            }
        }
    }

    /**
     * Returns the root of the window that holds a node.
     *
     * @param node Any node of the window.
     * @return The top ancestor, or the node itself when it has no parent.
     */
    fun getRoot(node: RecorderNode): RecorderNode = getAncestors(node).lastOrNull() ?: node

    /**
     * Returns the 0-based position of a node among all children of its parent, the UiAutomator2 `index` attribute.
     *
     * @param node The node to locate.
     * @return The position, or 0 for a root or a node its parent no longer lists.
     */
    fun getSiblingIndex(node: RecorderNode): Int {
        val parent = node.parent ?: return 0
        val index = parent.children.indexOfFirst { it.isSameNode(node) }

        return maxOf(0, index)
    }

    // Deepest ancestor chain the helpers follow; real Android trees stay far below it.
    private const val MAXIMUM_DEPTH = 256
}
