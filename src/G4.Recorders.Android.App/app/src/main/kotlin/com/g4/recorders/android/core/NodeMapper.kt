package com.g4.recorders.android.core

/**
 * Maps accessibility nodes to the shared G4 recorder contract.
 *
 * The output is plain maps and lists, the shape the SignalR client serializes to JSON and the host binds to
 * `AndroidEventModel` (`RecorderEventModel`, `ChainModel` and `RecorderNodeModel` in `G4.Recorders.Common.Domain`).
 * The JSON names are camelCase except the bounds and point coordinates, which the C# models name `X` and `Y`.
 *
 * Chains run target first, the Chromium orientation, because the Hub converts Android events with its Chromium
 * rule builder.
 *
 * @param machineName Name reported for the device, `Build.MODEL` on Android.
 * @param machineAddress Address reported for the device, its serial number on Android.
 */
class NodeMapper(
    private val machineName: String,
    private val machineAddress: String,
) {
    /**
     * Returns the contract chain of a target node.
     *
     * @param options What to put into the chain.
     * @return The chain map: `locator`, `path`, `point`, `topWindow` and `trigger`.
     */
    fun newChain(options: ChainOptions): Map<String, Any?> {
        // Resolve the ancestors once; the last one is the window root, the chain's top window.
        val target = options.target
        val ancestors = NodeTree.getAncestors(target)

        // Build the path from the target up; only the trigger node carries the view context.
        val path = mutableListOf<Map<String, Any?>>()
        path.add(
            newNode(
                NodeOptions(
                    node = target,
                    activity = options.activity,
                    isTrigger = true,
                    isTopWindow = ancestors.isEmpty(),
                    viewContext = options.viewContext,
                ),
            ),
        )

        for ((index, ancestor) in ancestors.withIndex()) {
            val isRoot = index == ancestors.lastIndex
            path.add(
                newNode(
                    NodeOptions(node = ancestor, activity = options.activity, isTrigger = false, isTopWindow = isRoot, viewContext = null),
                ),
            )
        }

        // Use the event's own point when it has one, else the center of the target, where a replayed tap lands.
        val bounds = target.bounds
        val pointX = options.pointX ?: (bounds.left + bounds.width / 2)
        val pointY = options.pointY ?: (bounds.top + bounds.height / 2)

        return linkedMapOf(
            "locator" to XPathLocatorBuilder.getLocator(target),
            "path" to path,
            "point" to linkedMapOf("X" to pointX, "Y" to pointY),
            "topWindow" to path.last(),
            "trigger" to options.trigger,
        )
    }

    /**
     * Returns an empty chain for page-level events such as `SwitchActivity` and `HideSoftKeyboard`.
     *
     * The shape matches the Chromium recorder's context events: no locator and no path, so the Hub writes no
     * `onElement` for the rule.
     *
     * @param trigger The event that produced the chain.
     * @return The empty chain map.
     */
    fun newEmptyChain(trigger: String): Map<String, Any?> =
        linkedMapOf(
            "locator" to "",
            "path" to emptyList<Any>(),
            "point" to null,
            "topWindow" to null,
            "trigger" to trigger,
        )

    /**
     * Returns the contract event wrapping a chain and a catalog entry.
     *
     * @param chain The chain map from [newChain] or [newEmptyChain].
     * @param entry The event name, type and value.
     * @param timestamp Unix epoch milliseconds of the capture.
     * @return The event map: `chain`, `event`, `machineName`, `timestamp`, `type` and `value`.
     */
    fun newEvent(
        chain: Map<String, Any?>,
        entry: CatalogEntry,
        timestamp: Long,
    ): Map<String, Any?> =
        linkedMapOf(
            "chain" to chain,
            "event" to entry.event,
            "machineName" to machineName,
            "timestamp" to timestamp,
            "type" to entry.type,
            "value" to entry.value,
        )

    /**
     * Returns the contract node of one accessibility node.
     *
     * UI Automation-only fields (`controlTypeId`, `patterns`, `runtimeId`) have no Android equivalent and carry the
     * same empty defaults the Chromium recorder sends. An editable field's content never becomes the node name.
     *
     * @param options The node and its role in the chain.
     * @return The node map.
     */
    fun newNode(options: NodeOptions): Map<String, Any?> {
        // Collect the Android state UiAutomator2 exposes, so a client can rebuild or heal a locator from the chain.
        val node = options.node
        val properties =
            linkedMapOf<String, Any?>(
                "package" to node.packageName,
                "index" to NodeTree.getSiblingIndex(node),
                "checkable" to node.isCheckable,
                "checked" to node.isChecked,
                "clickable" to node.isClickable,
                "enabled" to node.isEnabled,
                "focused" to node.isFocused,
                "longClickable" to node.isLongClickable,
                "password" to node.isPassword,
                "scrollable" to node.isScrollable,
            )

        // Record the activity on every node so a client can tell which screen the chain belongs to.
        if (options.activity.isNotEmpty()) {
            properties["activity"] = options.activity
        }

        // Only the trigger node of element events carries the snapshot.
        val viewContext = options.viewContext

        if (viewContext != null) {
            properties["domContext"] = viewContext.domContext
            properties["domContextLength"] = viewContext.domContextLength
        }

        val bounds = node.bounds

        return linkedMapOf(
            "automationId" to node.resourceId,
            "bounds" to linkedMapOf("height" to bounds.height, "width" to bounds.width, "X" to bounds.left, "Y" to bounds.top),
            "className" to node.className,
            "controlTypeId" to 0,
            "controlType" to node.className.substringAfterLast('.'),
            "frameworkId" to FRAMEWORK_ID,
            "isTopWindow" to options.isTopWindow,
            "isTriggerElement" to options.isTrigger,
            "machine" to linkedMapOf("name" to machineName, "publicAddress" to machineAddress),
            "name" to getName(node),
            "patterns" to emptyList<Any>(),
            "processId" to 0,
            "properties" to properties,
            "runtimeId" to emptyList<Any>(),
        )
    }

    private companion object {
        // Framework id every Android node reports, the counterpart of `Chromium` and `SAPGUI`.
        const val FRAMEWORK_ID = "Android"

        // Returns the accessible name: the label of a non-editable node, else its description, else its hint, else the
        // first label inside it, the way a screen reader names a row or card. A scrollable container keeps an empty
        // name, because its first child changes as the content moves. Clickability is not used: some apps report a
        // tapped row as not clickable because the click is handled by a parent.
        fun getName(node: RecorderNode): String {
            // An editable field's text is what the user typed, never a name, so it is skipped.
            val isLabel = !node.isEditable && !node.isPassword
            val isLabelled = !node.isScrollable

            return when {
                isLabel && node.text.isNotEmpty() -> node.text
                node.contentDescription.isNotEmpty() -> node.contentDescription
                node.hintText.isNotEmpty() -> node.hintText
                isLabelled -> getChildLabel(node)
                else -> ""
            }
        }

        // Returns the first non-editable text inside a node, or empty when it has none.
        fun getChildLabel(node: RecorderNode): String {
            val label = NodeTree.getDescendants(node).drop(1).firstOrNull { it.text.isNotEmpty() && !it.isEditable && !it.isPassword }

            return label?.text.orEmpty()
        }
    }
}

/**
 * Inputs of [NodeMapper.newChain].
 *
 * @property target The node that triggered the event.
 * @property activity The current activity class, or empty when unknown.
 * @property trigger The accessibility event name that produced the chain.
 * @property viewContext The snapshot for the trigger node, or null when disabled or too large.
 * @property pointX The event's horizontal screen point, or null to use the target center.
 * @property pointY The event's vertical screen point, or null to use the target center.
 */
data class ChainOptions(
    val target: RecorderNode,
    val activity: String,
    val trigger: String,
    val viewContext: ViewContext?,
    val pointX: Int? = null,
    val pointY: Int? = null,
)

/**
 * Inputs of [NodeMapper.newNode].
 *
 * @property node The node to map.
 * @property activity The current activity class, or empty when unknown.
 * @property isTrigger True for the node that triggered the event.
 * @property isTopWindow True for the window root.
 * @property viewContext The snapshot, set on the trigger node of element events only.
 */
data class NodeOptions(
    val node: RecorderNode,
    val activity: String,
    val isTrigger: Boolean,
    val isTopWindow: Boolean,
    val viewContext: ViewContext?,
)
