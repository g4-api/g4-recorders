package com.g4.recorders.android.core

/**
 * In-memory [RecorderNode] for the JVM tests: a mutable tree the tests build with [add].
 *
 * Identity is object identity, the fake equivalent of the Android adapter comparing node infos.
 */
class FakeNode(
    override val className: String,
    override val resourceId: String = "",
    override val text: String = "",
    override val contentDescription: String = "",
    override val hintText: String = "",
    override val bounds: NodeBounds = NodeBounds(0, 0, 100, 100),
    override val isEditable: Boolean = false,
    override val isPassword: Boolean = false,
    override val isClickable: Boolean = false,
    override val isScrollable: Boolean = false,
    override val isVisible: Boolean = true,
    override val packageName: String = "com.example.app"
) : RecorderNode {
    private val childList = mutableListOf<FakeNode>()

    override var parent: RecorderNode? = null
        private set

    override val children: List<RecorderNode> get() = childList

    override val isCheckable: Boolean = false

    override val isChecked: Boolean = false

    override val isEnabled: Boolean = true

    override val isFocusable: Boolean = isEditable

    override val isFocused: Boolean = false

    override val isLongClickable: Boolean = false

    override val isSelected: Boolean = false

    /**
     * Appends a child and returns it, so tests can build a tree inline.
     *
     * @param child The node to append.
     * @return The appended child.
     */
    fun add(child: FakeNode): FakeNode {
        childList.add(child)
        child.parent = this

        return child
    }

    override fun testSameNode(other: RecorderNode): Boolean {
        return other === this
    }
}
