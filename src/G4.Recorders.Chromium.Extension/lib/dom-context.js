/**
 * DOM context capture for the G4 Chromium Recorder extension.
 *
 * Takes a cleaned HTML snapshot of the markup around a recorded element and encodes it as
 * Base64 (UTF-8), so clients can later use the surrounding structure, for example to heal
 * or optimise a locator. The snapshot is the outer HTML of the ancestor a configured number
 * of levels above the element, with the element itself marked by `data-g4-target="true"`.
 *
 * @remarks
 * Attached to the shared `globalThis.g4Recorder` namespace (see constants.js). Loaded as a
 * content script in every frame, before the DOM mapper that places the result on the
 * trigger node's `properties` (`domContext` and `domContextLength`). The live page is never
 * changed: every cleaning step runs on a detached deep clone.
 */
(function initializeDomContextModule(globalScope) {
    // Create or reuse the single extension namespace shared across every context.
    const namespace = globalScope.g4Recorder = globalScope.g4Recorder || {};

    // Bytes in one kilobyte, used to turn the configured size limit into a byte budget.
    const BYTES_PER_KILOBYTE = 1024;

    // A cleaned snapshot is rarely below an eighth of its live markup, so a level whose live
    // outer HTML exceeds eight times the budget is skipped without cloning it at all. This
    // keeps a click on a very large page from cloning and serialising the whole document.
    const LIVE_MARKUP_SKIP_FACTOR = 8;

    // Characters converted per String.fromCharCode call while Base64-encoding, kept below the
    // engine's argument limit so a large snapshot never overflows the call stack.
    const BASE64_CHUNK_CHARACTERS = 8192;

    // The attribute that marks the recorded element inside the snapshot.
    const TARGET_ATTRIBUTE_NAME = "data-g4-target";

    // Elements whose content carries behaviour or presentation, not structure; removed whole.
    const REMOVED_ELEMENTS_SELECTOR = "script, style, noscript, template";

    // Fields whose value attribute may hold a secret; their value is blanked in the snapshot.
    // Mirrors the event catalog's sensitive-field rule (password inputs and data-sensitive).
    const SENSITIVE_FIELDS_SELECTOR = "input[type='password' i], [data-sensitive]";

    /**
     * Captures the Base64 DOM context of a recorded element.
     *
     * @remarks
     * Compute-only over the live DOM: it reads markup and clones nodes but never changes the
     * page. Levels are tried from the configured height down to the element itself, and the
     * first snapshot within the byte budget wins. When even the element's own markup is too
     * large, its opening tag alone is used; when that is still too large, nothing is returned
     * so the event is sent without a DOM context instead of failing.
     *
     * @param {object} options Capture inputs.
     * @param {Element} options.element The recorded element.
     * @param {number} options.ancestorLevels How many ancestors above the element to include.
     * @param {number} options.maximumKilobytes The size limit of the decoded snapshot.
     * @returns {object|null} The context ({ domContext, domContextLength }) or null.
     */
    function getDomContext(options) {
        const { element, ancestorLevels, maximumKilobytes } = options;

        // Without an element in a document there is no markup to describe.
        const isElementAttached = Boolean(element && element.ownerDocument);

        if (!isElementAttached) {
            return null;
        }

        // Resolve the byte budget and the ancestors that may root the snapshot.
        const maximumBytes = Math.max(1, Math.floor(maximumKilobytes)) * BYTES_PER_KILOBYTE;
        const ancestors = getAncestors(element, ancestorLevels);

        // Try the widest snapshot first and shrink one level at a time until one fits.
        for (let level = ancestors.length - 1; level >= 0; level--) {
            const rootElement = ancestors[level];
            const liveMarkupLimit = maximumBytes * LIVE_MARKUP_SKIP_FACTOR;
            const isLiveMarkupTooLarge = rootElement.outerHTML.length > liveMarkupLimit;

            if (isLiveMarkupTooLarge) {
                continue;
            }

            const markup = getCleanMarkup({ rootElement, targetElement: element });
            const markupBytes = getMarkupBytes(markup);

            if (markupBytes.length <= maximumBytes) {
                return newDomContext(markupBytes);
            }
        }

        // Fall back to the element's opening tag, which still carries its own attributes.
        const tagMarkup = getCleanMarkup({ rootElement: element, targetElement: element, isShallow: true });
        const tagBytes = getMarkupBytes(tagMarkup);

        if (tagBytes.length <= maximumBytes) {
            return newDomContext(tagBytes);
        }

        return null;
    }

    /**
     * Blanks the value a sensitive field carries in the snapshot.
     *
     * @remarks
     * Changes only a detached clone. A field's live typed text is a property and never part of
     * outer HTML, but a value attribute or a sensitive text area's content can still hold a
     * secret, so both are cleared.
     *
     * @param {Element} clonedElement A sensitive field in the snapshot clone.
     * @returns {void}
     */
    function clearSensitiveValue(clonedElement) {
        // Keep the attribute present, so the snapshot still shows that the field has one.
        if (clonedElement.hasAttribute("value")) {
            clonedElement.setAttribute("value", "");
        }

        // A text area, or any other element marked sensitive, keeps its content as text.
        if (clonedElement.nodeName !== "INPUT") {
            clonedElement.textContent = "";
        }
    }

    /**
     * Converts UTF-8 bytes into a Base64 string.
     *
     * @remarks
     * Compute-only helper. `btoa` only accepts Latin-1 text, so the bytes are first mapped to
     * one character each, in chunks that stay below the engine's argument limit.
     *
     * @param {Uint8Array} bytes The bytes to encode.
     * @returns {string} The Base64 text.
     */
    function convertToBase64(bytes) {
        // Map every byte to one Latin-1 character, a chunk at a time.
        const characterChunks = [];

        for (let index = 0; index < bytes.length; index += BASE64_CHUNK_CHARACTERS) {
            const chunk = bytes.subarray(index, index + BASE64_CHUNK_CHARACTERS);

            characterChunks.push(String.fromCharCode.apply(null, chunk));
        }

        return globalScope.btoa(characterChunks.join(""));
    }

    /**
     * Returns the element followed by its ancestors, up to the requested number of levels.
     *
     * @remarks
     * Compute-only helper. Climbing stops at the document element or at a shadow root, where
     * `parentElement` is null, so a snapshot never leaves the element's own tree.
     *
     * @param {Element} element The recorded element.
     * @param {number} ancestorLevels How many ancestors to include above the element.
     * @returns {Element[]} The element at index 0, then each ancestor in climbing order.
     */
    function getAncestors(element, ancestorLevels) {
        // Clamp the level count to a whole, non-negative number.
        const levelCount = Math.max(0, Math.floor(ancestorLevels));
        const ancestors = [element];

        // Climb until the requested height or the top of the element's tree is reached.
        let currentElement = element;

        while (ancestors.length <= levelCount && currentElement.parentElement) {
            currentElement = currentElement.parentElement;
            ancestors.push(currentElement);
        }

        return ancestors;
    }

    /**
     * Returns the child-index path from a root element down to one of its descendants.
     *
     * @remarks
     * Compute-only helper. The path lets the same element be found again inside a deep clone
     * of the root, so the clone can be marked without touching the live page.
     *
     * @param {Element} rootElement The snapshot root.
     * @param {Element} targetElement The recorded element, the root itself or a descendant.
     * @returns {number[]} The element-child indexes from the root to the target.
     */
    function getChildIndexPath(rootElement, targetElement) {
        // Walk upward from the target, recording its position among its element siblings.
        const indexPath = [];
        let currentElement = targetElement;

        while (currentElement && currentElement !== rootElement) {
            const parentElement = currentElement.parentElement;

            indexPath.unshift(Array.prototype.indexOf.call(parentElement.children, currentElement));
            currentElement = parentElement;
        }

        return indexPath;
    }

    /**
     * Returns the cleaned outer HTML of a snapshot root with the recorded element marked.
     *
     * @remarks
     * Compute-only over a detached clone. The clone is marked first, while the element-child
     * paths still match the live tree, and cleaned afterwards: behaviour and presentation
     * elements are removed, SVG drawings are reduced to their root tag, event-handler
     * attributes are removed, and sensitive field values are blanked.
     *
     * @param {object} options Markup inputs.
     * @param {Element} options.rootElement The element that roots the snapshot.
     * @param {Element} options.targetElement The recorded element inside the root.
     * @param {boolean} [options.isShallow=false] Whether to keep only the root's own tag.
     * @returns {string} The cleaned markup.
     */
    function getCleanMarkup(options) {
        const { rootElement, targetElement, isShallow = false } = options;

        // Clone the root so every change below stays off the live page.
        const rootClone = rootElement.cloneNode(!isShallow);

        // Mark the recorded element in the clone before removals shift any child positions.
        const targetClone = getChildIndexPath(rootElement, targetElement)
            .reduce((cloneElement, childIndex) => cloneElement.children[childIndex], rootClone);

        targetClone.setAttribute(TARGET_ATTRIBUTE_NAME, "true");

        // Remove behaviour and presentation elements, and reduce SVG drawings to their root tag.
        const removeElement = (removedElement) => removedElement.remove();
        const clearDrawing = (svgElement) => svgElement.replaceChildren();

        rootClone.querySelectorAll(REMOVED_ELEMENTS_SELECTOR).forEach(removeElement);
        rootClone.querySelectorAll("svg").forEach(clearDrawing);

        // Remove inline event handlers and blank sensitive values on the root and every descendant.
        const clonedElements = [rootClone, ...rootClone.querySelectorAll("*")];

        clonedElements.forEach(removeHandlerAttributes);

        clonedElements
            .filter((clonedElement) => clonedElement.matches(SENSITIVE_FIELDS_SELECTOR))
            .forEach(clearSensitiveValue);

        return rootClone.outerHTML;
    }

    /**
     * Encodes markup as UTF-8 bytes.
     *
     * @remarks
     * Compute-only helper. The byte length is what the size limit measures and what
     * `domContextLength` reports, so a client can verify that it decoded the whole snapshot.
     *
     * @param {string} markup The markup.
     * @returns {Uint8Array} The UTF-8 bytes.
     */
    function getMarkupBytes(markup) {
        return new TextEncoder().encode(markup);
    }

    /**
     * Builds the context fields placed on the trigger node.
     *
     * @remarks
     * Compute-only helper, shared by the snapshot and the opening-tag fallback.
     *
     * @param {Uint8Array} markupBytes The UTF-8 bytes of the snapshot.
     * @returns {object} The context ({ domContext, domContextLength }).
     */
    function newDomContext(markupBytes) {
        return {
            domContext: convertToBase64(markupBytes),
            domContextLength: markupBytes.length
        };
    }

    /**
     * Removes every inline event-handler attribute (on*) from one cloned element.
     *
     * @remarks
     * Changes only a detached clone. Handler code is behaviour, not structure, and can be long,
     * so it never enters the snapshot.
     *
     * @param {Element} clonedElement An element of the snapshot clone.
     * @returns {void}
     */
    function removeHandlerAttributes(clonedElement) {
        // Copy the names first, since removing attributes changes the live attribute list.
        const handlerNames = Array.from(clonedElement.attributes)
            .map((attribute) => attribute.name)
            .filter((attributeName) => attributeName.toLowerCase().startsWith("on"));

        handlerNames.forEach((attributeName) => clonedElement.removeAttribute(attributeName));
    }

    // Expose the DOM context API on the shared namespace.
    namespace.domContext = {
        getDomContext
    };
})(globalThis);
