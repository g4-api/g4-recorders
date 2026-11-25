/**
 * Drag and drop helpers for the G4 Chromium Recorder extension.
 *
 * Decides which presses may start a recorded drag, when a pointer drag has moved far enough to
 * count, whether a drag was really a text selection, and which element a drag ended on. The
 * content script owns the drag state and the listeners; this module only computes.
 *
 * @remarks
 * Attached to the shared `globalThis.g4Recorder` namespace (see constants.js). Loaded as a content
 * script in every frame, after the locator module. Elements are compared across shadow
 * boundaries, so a shadow host counts as an ancestor of what its shadow tree holds.
 */
(function initializeDragTrackerModule(globalScope) {
    // Create or reuse the single extension namespace shared across every context.
    const namespace = globalScope.g4Recorder = globalScope.g4Recorder || {};

    // The input types whose press selects or edits text, or moves a native thumb. A press there is
    // the user working inside the control, not grabbing something to drag.
    const NON_DRAG_INPUT_TYPES = new Set([
        "",
        "date",
        "datetime-local",
        "email",
        "month",
        "number",
        "password",
        "range",
        "search",
        "tel",
        "text",
        "time",
        "url",
        "week"
    ]);

    // The elements whose press opens, edits, or plays native browser UI rather than starting a drag.
    const NON_DRAG_TAG_NAMES = new Set(["AUDIO", "OPTION", "SELECT", "TEXTAREA", "VIDEO"]);

    /**
     * Resolves the element a drag ended on, leaving out the dragged element and its relatives.
     *
     * @remarks
     * Compute-only over the live document. A pointer-driven drag usually moves the dragged element,
     * or a copy of it, under the pointer, so the topmost element there is often the source itself.
     * The candidates are taken from the top down, and the first one that is neither the source, an
     * ancestor of it, nor inside it is the drop element. The document root and the body count as
     * no element, because a drop there names no target a locator can hold on to.
     *
     * @param {object} options Resolution inputs.
     * @param {Element[]} options.candidates The elements under the release point, topmost first.
     * @param {Element} options.sourceElement The element the drag started on.
     * @returns {Element|null} The drop element, or null when the drag ended on nothing distinct.
     */
    function getDropElement(options) {
        const { candidates, sourceElement } = options;

        // Resolves the composed parent: an element directly under a shadow root steps out to the
        // host of that root, anywhere else the parent element is the next step.
        const getComposedParent = (element) => {
            const parentNode = element.parentNode;
            const isShadowRoot = Boolean(parentNode) && Boolean(parentNode.host);

            return isShadowRoot
                ? parentNode.host
                : element.parentElement;
        };

        // Tests whether the walk up from an element, through every shadow root, reaches the ancestor.
        const testComposedAncestor = (ancestorElement, element) => {
            let currentElement = element;

            while (currentElement) {
                if (currentElement === ancestorElement) {
                    return true;
                }

                currentElement = getComposedParent(currentElement);
            }

            return false;
        };

        // Tests whether two elements are the same or nested, which names no distinct drop target.
        const testRelatedElement = (element) => {
            const isSourceInside = testComposedAncestor(element, sourceElement);
            const isElementInside = testComposedAncestor(sourceElement, element);

            return isSourceInside || isElementInside;
        };

        // Take the topmost candidate that is not related to the dragged element.
        const dropElement = candidates.find((candidate) => {
            const isElement = Boolean(candidate) && candidate.nodeType === Node.ELEMENT_NODE;

            return isElement && !testRelatedElement(candidate);
        });

        // A drop on the page itself has no element to address.
        const isPageRoot = !dropElement
            || dropElement === document.documentElement
            || dropElement === document.body;

        return isPageRoot
            ? null
            : dropElement;
    }

    /**
     * Resolves the center of an element in viewport coordinates.
     *
     * @remarks
     * Compute-only. The center is where a W3C pointer move with the element as its origin lands, so
     * the distance a drag is replayed by is measured from it.
     *
     * @param {Element} element The element to measure.
     * @returns {{x: number, y: number}} The center of its bounding box.
     */
    function getElementCenter(element) {
        // Measure the element as laid out now, so the center matches what the user pressed.
        const boundingBox = element.getBoundingClientRect();

        return {
            x: boundingBox.left + (boundingBox.width / 2),
            y: boundingBox.top + (boundingBox.height / 2)
        };
    }

    /**
     * Tests whether a press may start a recorded drag.
     *
     * @remarks
     * Compute-only. Only a press of the main button on an element counts. A press inside a text
     * field, a list box, or a media player works the control itself, a press on a scroll bar
     * scrolls, and a press on the page itself grabs nothing a locator can address, so none of
     * them is recorded as a drag.
     *
     * @param {object} options Test inputs.
     * @param {Element|null} options.element The element under the press.
     * @param {MouseEvent} options.domEvent The press.
     * @returns {boolean} True when the press may become a drag and drop.
     */
    function testDragSource(options) {
        const { element, domEvent } = options;

        // Only the main button of a real press can start a drag.
        const isMainPress = domEvent.isTrusted && domEvent.button === 0;
        const isElement = Boolean(element) && element.nodeType === Node.ELEMENT_NODE;

        if (!isMainPress || !isElement) {
            return false;
        }

        // A press on the page itself has nothing to drag.
        const isPageRoot = element === document.documentElement || element === document.body;

        if (isPageRoot) {
            return false;
        }

        // A press that edits text or works a native control is not a drag of that control.
        const isNativeControl = NON_DRAG_TAG_NAMES.has(element.nodeName);
        const isTextInput = element.nodeName === "INPUT" && NON_DRAG_INPUT_TYPES.has(element.type);
        const isEditable = element.isContentEditable || isNativeControl || isTextInput;

        if (isEditable) {
            return false;
        }

        // A press past the content box of a scrolling element is on its scroll bar.
        const isPastContentX = domEvent.offsetX >= element.clientWidth
            && element.scrollHeight > element.clientHeight;
        const isPastContentY = domEvent.offsetY >= element.clientHeight
            && element.scrollWidth > element.clientWidth;

        return !(isPastContentX || isPastContentY);
    }

    /**
     * Tests whether a pointer has moved far enough from the press to count as a drag.
     *
     * @remarks
     * Compute-only. A click wobbles by a pixel or two between the press and the release, so the
     * pointer has to leave a square around the press point before a drag is recorded.
     *
     * @param {object} options Test inputs.
     * @param {{x: number, y: number}} options.start The press point.
     * @param {{x: number, y: number}} options.point The current pointer point.
     * @param {number} options.thresholdPixels How far the pointer has to move on either axis.
     * @returns {boolean} True when the pointer left the threshold square.
     */
    function testDragThreshold(options) {
        const { start, point, thresholdPixels } = options;

        // Either axis alone may carry the pointer out of the square.
        const isPastX = Math.abs(point.x - start.x) >= thresholdPixels;
        const isPastY = Math.abs(point.y - start.y) >= thresholdPixels;

        return isPastX || isPastY;
    }

    /**
     * Tests whether the page holds a text selection.
     *
     * @remarks
     * Compute-only. A press and a move over text that the page does not handle selects the text,
     * which is the same gesture as a pointer drag. A non-empty selection at the release means the
     * user selected text, so no drag and drop is recorded.
     *
     * @returns {boolean} True when some text is selected.
     */
    function testTextSelection() {
        // Read the selection where the platform offers one; a context without it selects nothing.
        const selection = globalScope.getSelection
            ? globalScope.getSelection()
            : null;
        const isSelection = Boolean(selection) && !selection.isCollapsed;

        if (!isSelection) {
            return false;
        }

        // Selection#toString returns the selected text itself, so an empty string means no text.
        return selection.toString().length > 0;
    }

    // Expose the drag helpers on the shared namespace.
    namespace.dragTracker = {
        getDropElement,
        getElementCenter,
        testDragSource,
        testDragThreshold,
        testTextSelection
    };
})(globalThis);
