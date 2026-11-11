package com.g4.recorders.android

import android.graphics.Rect
import android.os.Build
import android.view.accessibility.AccessibilityNodeInfo
import com.g4.recorders.android.core.NodeBounds
import com.g4.recorders.android.core.RecorderNode

/**
 * Exposes an `AccessibilityNodeInfo` to the recorder core as a [RecorderNode].
 *
 * Every property reads the wrapped node on first use and caches the answer, because each read of a node's
 * children or parent is an inter-process call to the application under test. The adapter never recycles the
 * wrapped node; Android 13 and later ignore recycling, and on older versions the garbage collector reclaims it.
 *
 * @param info The wrapped node.
 */
class AccessibilityNodeAdapter(
    private val info: AccessibilityNodeInfo,
) : RecorderNode {
    override val bounds: NodeBounds by lazy {
        val rectangle = Rect()
        info.getBoundsInScreen(rectangle)

        NodeBounds(left = rectangle.left, top = rectangle.top, right = rectangle.right, bottom = rectangle.bottom)
    }

    override val children: List<RecorderNode> by lazy {
        // Skip children the application removed between the count and the read.
        (0 until info.childCount).mapNotNull { index -> info.getChild(index)?.let { AccessibilityNodeAdapter(it) } }
    }

    override val className: String by lazy { info.className?.toString() ?: "" }

    override val contentDescription: String by lazy { info.contentDescription?.toString() ?: "" }

    override val hintText: String by lazy { info.hintText?.toString() ?: "" }

    override val isCheckable: Boolean get() = info.isCheckable

    override val isChecked: Boolean
        get() {
            // Android 16 replaced the boolean with a tri-state; a partly checked box does not count as checked.
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.BAKLAVA) {
                return info.checked == AccessibilityNodeInfo.CHECKED_STATE_TRUE
            }

            @Suppress("DEPRECATION")
            return info.isChecked
        }

    override val isClickable: Boolean get() = info.isClickable

    override val isEditable: Boolean get() = info.isEditable

    override val isEnabled: Boolean get() = info.isEnabled

    override val isFocusable: Boolean get() = info.isFocusable

    override val isFocused: Boolean get() = info.isFocused

    override val isLongClickable: Boolean get() = info.isLongClickable

    override val isPassword: Boolean get() = info.isPassword

    override val isScrollable: Boolean get() = info.isScrollable

    override val isSelected: Boolean get() = info.isSelected

    override val isVisible: Boolean get() = info.isVisibleToUser

    override val packageName: String by lazy { info.packageName?.toString() ?: "" }

    override val parent: RecorderNode? by lazy { info.parent?.let { AccessibilityNodeAdapter(it) } }

    override val resourceId: String by lazy { info.viewIdResourceName ?: "" }

    override val text: String by lazy {
        // Android reports the hint as the text of an empty field; report it as empty, like UiAutomator2 does.
        if (info.isShowingHintText) "" else info.text?.toString() ?: ""
    }

    override fun isSameNode(other: RecorderNode): Boolean {
        // Node infos compare their window and source ids, which identify the view across wrapper instances.
        return other is AccessibilityNodeAdapter && other.info == info
    }
}
