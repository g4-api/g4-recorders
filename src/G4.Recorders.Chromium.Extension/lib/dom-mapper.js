/**
 * DOM-to-contract mapping for the G4 Chromium Recorder extension.
 *
 * Converts a DOM element into a recorder node, and a trigger element into a full chain
 * (the element plus every ancestor up to the document root). Field mapping follows the
 * approved scheme:
 *   controlType  <- ARIA role or tag name
 *   automationId <- id or data-test-id
 *   name         <- accessible name (labelledby, aria-label, <label>, alt, title, or text)
 *   className    <- class attribute
 *   bounds       <- getBoundingClientRect
 * UIA-only fields are left at their contract defaults by the contract builder.
 *
 * The chain locator and the ranked locator list of the trigger node come from the candidate
 * engine (locator-candidates.js), so a recorded event and an on-demand peek describe an element
 * with the same verified locators.
 *
 * @remarks
 * Attached to the shared `globalThis.g4Recorder` namespace (see constants.js). Loaded
 * as a content script in every frame. Depends on the contract, locator, accessibility,
 * candidate and DOM context modules, which are listed before it in the manifest order.
 */
(function initializeDomMapperModule(globalScope) {
    // Create or reuse the single extension namespace shared across every context.
    const namespace = globalScope.g4Recorder = globalScope.g4Recorder || {};

    // Pull the sibling modules this mapper builds on. All are loaded earlier in the
    // content-script list, so they are guaranteed to be present here.
    const { accessibility, contract, locator, locatorCandidates } = namespace;

    /**
     * Builds a chain (trigger node plus ancestors) for a recorded interaction.
     *
     * @remarks
     * Compute-only. The path starts at the trigger element and climbs to the document
     * root, mirroring the UiaPeek ChainModel. The first node is flagged as the trigger
     * and the last as the top window.
     *
     * @param {object} options Chain inputs.
     * @param {Element} options.element The trigger element the interaction targeted.
     * @param {string} options.trigger The event name that produced this chain.
     * @param {object|null} options.point The trigger point, or null when not applicable.
     * @param {object|null} [options.domContextOptions] The DOM context limits ({ ancestorLevels,
     *   maximumKilobytes }) when the trigger node should carry a DOM context, otherwise null.
     * @param {object|null} [options.locatorSettings] The locator settings ({ maximumTextLength,
     *   preference, testHookAttributes }). When null the chain carries the absolute XPath only.
     * @param {Element|null} [options.originalElement] The raw event target when `element` is the
     *   actionable ancestor it was resolved to, otherwise null.
     * @returns {object} The chain in contract shape.
     */
    function newChainFromElement(options) {
        const {
            element,
            trigger,
            point,
            domContextOptions = null,
            locatorSettings = null,
            originalElement = null
        } = options;

        // Collect the trigger element and each ancestor element up to the root so the
        // path can be built from the bottom up, matching the desktop recorder.
        const elementChain = getElementChain(element);

        // Resolve the machine descriptor once and reuse it for every node in the chain,
        // since all nodes in one document share the same host context.
        const machine = newMachineForDocument(element.ownerDocument);

        // Map each element to a node, flagging the trigger (first) and root (last).
        const lastIndex = elementChain.length - 1;
        const path = elementChain.map((chainElement, index) => newNodeFromElement({
            element: chainElement,
            isTriggerElement: index === 0,
            isTopWindow: index === lastIndex,
            machine
        }));

        // Place the markup around the element on the trigger node when the caller asked for it,
        // next to the CSS selector, so consumers find every web detail of the target in one map.
        const domContext = domContextOptions
            ? namespace.domContext.getDomContext({ element, ...domContextOptions })
            : null;

        if (domContext && path.length > 0) {
            Object.assign(path[0].properties, domContext);
        }

        // Resolve the ranked, verified locators of the target; without settings there are none.
        const locatorResult = locatorSettings
            ? locatorCandidates.getLocatorCandidates({ element, settings: locatorSettings })
            : null;

        // Publish the locators on the trigger node, next to the other web details of the target.
        const isLocatorPublishable = Boolean(locatorResult) && path.length > 0;

        if (isLocatorPublishable) {
            const locatorProperties = newLocatorProperties({ locatorResult, originalElement });

            Object.assign(path[0].properties, locatorProperties);
        }

        // The best XPath becomes the chain locator; the absolute path is the fallback.
        const isLocatorResolved = Boolean(locatorResult) && Boolean(locatorResult.locator);
        const chainLocator = isLocatorResolved
            ? locatorResult.locator
            : locator.getAbsoluteXpath(element);

        // The top window node is the final node in the bottom-up path, when present.
        const topWindow = path.length > 0
            ? path[path.length - 1]
            : null;

        return contract.newChain({
            locator: chainLocator,
            path,
            point,
            topWindow,
            trigger
        });
    }

    /**
     * Maps a single DOM element to a recorder node.
     *
     * @remarks
     * Compute-only. Web-specific detail that has no contract field (CSS selector, tag
     * name, key attributes) is placed in `properties` so consumers can still use it.
     *
     * @param {object} options Node inputs.
     * @param {Element} options.element The element to map.
     * @param {boolean} options.isTriggerElement Whether this element is the trigger.
     * @param {boolean} options.isTopWindow Whether this element is the top window.
     * @param {object} options.machine The shared machine descriptor for the document.
     * @returns {object} The node in contract shape.
     */
    function newNodeFromElement(options) {
        const { element, isTriggerElement, isTopWindow, machine } = options;

        // Resolve the friendly control type and accessible name once for reuse below.
        const controlType = getControlType(element);
        const accessibleName = accessibility.getAccessibleName(element);

        // Read geometry from the live layout box; detached nodes yield a zero rectangle.
        const boundingRectangle = element.getBoundingClientRect();

        // SVG and other namespaced elements expose className as an object, so only a
        // string class attribute is recorded directly.
        const className = typeof element.className === "string"
            ? element.className
            : "";

        return contract.newNode({
            automationId: getAutomationId(element),
            bounds: contract.newBounds(boundingRectangle),
            className,
            controlType,
            isTopWindow,
            isTriggerElement,
            machine,
            name: accessibleName,
            properties: newNodeProperties(element)
        });
    }

    /**
     * Resolves the automation id of an element.
     *
     * @remarks
     * Compute-only helper. A real id wins; otherwise a data-test-id is used because it
     * is the most stable test handle in this codebase's conventions.
     *
     * @param {Element} element The element whose automation id is needed.
     * @returns {string} The automation id, or an empty string when none is found.
     */
    function getAutomationId(element) {
        // A native id is the closest analogue to a desktop AutomationId.
        if (element.id) {
            return element.id;
        }

        // Fall back to the test handle convention used across this project's UIs.
        const testId = element.getAttribute && element.getAttribute("data-test-id");

        return testId || "";
    }

    /**
     * Resolves a friendly control type for an element.
     *
     * @remarks
     * Compute-only helper. An explicit ARIA role is the most semantic signal; without
     * one, the tag name (capitalised) is used so the field is never empty.
     *
     * @param {Element} element The element whose control type is needed.
     * @returns {string} The friendly control type.
     */
    function getControlType(element) {
        // An explicit role best describes the element's purpose to consumers.
        const role = element.getAttribute && element.getAttribute("role");

        if (role) {
            return role.trim();
        }

        // Without a role, present the tag name in a readable capitalised form.
        const tagName = element.nodeName.toLowerCase();

        return `${tagName.charAt(0).toUpperCase()}${tagName.slice(1)}`;
    }

    /**
     * Collects an element and each of its ancestor elements up to the document root.
     *
     * @remarks
     * Compute-only helper. The returned list is bottom-up: index 0 is the element and
     * the final index is the outermost ancestor (usually the html element).
     *
     * @param {Element} element The element to start from.
     * @returns {Element[]} The element followed by its ancestors.
     */
    function getElementChain(element) {
        // Walk parent links from the element to the root, recording each element node.
        const elementChain = [];
        let currentElement = element;

        while (currentElement && currentElement.nodeType === Node.ELEMENT_NODE) {
            elementChain.push(currentElement);

            currentElement = currentElement.parentElement;
        }

        return elementChain;
    }

    /**
     * Returns the first non-empty value among the given attribute names.
     *
     * @remarks
     * Compute-only helper extracted so attribute lookups do not rely on a dense `||`
     * chain that would hide the priority order of the attribute names.
     *
     * @param {Element} element The element to read attributes from.
     * @param {string[]} attributeNames The attribute names to try, in priority order.
     * @returns {string} The first non-empty attribute value, or an empty string.
     */
    function getFirstAttributeValue(element, attributeNames) {
        // Without a getAttribute method there is nothing to read.
        if (typeof element.getAttribute !== "function") {
            return "";
        }

        // Return the first attribute that carries a non-empty value, in priority order.
        for (const attributeName of attributeNames) {
            const attributeValue = element.getAttribute(attributeName);

            if (attributeValue) {
                return attributeValue;
            }
        }

        return "";
    }

    /**
     * Builds the locator properties placed on the trigger node.
     *
     * @remarks
     * Compute-only helper. The identifier lets a later hot-test verdict find this exact event
     * again, so it is created here, once, with the locators it belongs to. The optional fields
     * are added only when they apply, so an ordinary element does not carry empty values.
     *
     * @param {object} options Property inputs.
     * @param {object} options.locatorResult The candidate engine result for the target.
     * @param {Element|null} options.originalElement The raw event target, when retargeted.
     * @returns {object} The properties ({ locatorId, locatorStrength, locators, ... }).
     */
    function newLocatorProperties(options) {
        const { locatorResult, originalElement } = options;
        const properties = {
            locatorId: globalScope.crypto.randomUUID(),
            locatorStrength: locatorResult.locatorStrength,
            locators: locatorResult.locators
        };

        // An element inside a shadow root is reached through its hosts, one segment per level.
        if (locatorResult.shadowPath) {
            properties.shadowPath = locatorResult.shadowPath;
        }

        // Keep the raw target so a client can still address the exact node the event landed on.
        if (originalElement) {
            properties.originalLocator = locator.getAbsoluteXpath(originalElement);
        }

        return properties;
    }

    /**
     * Builds the machine descriptor for a document.
     *
     * @remarks
     * Compute-only helper. A page cannot read the host machine name, so the document
     * hostname stands in for the name and the origin for the public address.
     *
     * @param {Document} ownerDocument The document the recorded element belongs to.
     * @returns {object} The machine descriptor in contract shape.
     */
    function newMachineForDocument(ownerDocument) {
        // Derive a host identity from the document location when it is available.
        const documentLocation = ownerDocument && ownerDocument.location;
        const hostName = documentLocation ? documentLocation.hostname : "";
        const originAddress = documentLocation ? documentLocation.origin : "";

        return contract.newMachine({
            name: hostName,
            publicAddress: originAddress
        });
    }

    /**
     * Collects web-specific properties for a node into a free-form map.
     *
     * @remarks
     * Compute-only helper. The contract leaves `properties` open, so it carries the CSS
     * selector and other web detail that has no dedicated contract field.
     *
     * @param {Element} element The element whose properties are collected.
     * @returns {object} The properties map.
     */
    function newNodeProperties(element) {
        // Start with locators and identity that are always useful to a consumer.
        const tagName = element.nodeName.toLowerCase();
        const properties = {
            tagName,
            cssSelector: locator.getCssSelector(element)
        };

        // Record the navigable target for links so consumers can correlate navigation.
        const hrefValue = getFirstAttributeValue(element, ["href"]);
        const isLinkWithHref = tagName === "a" && Boolean(hrefValue);

        if (isLinkWithHref) {
            properties.href = hrefValue;
        }

        // Record the input type so consumers can distinguish text, checkbox, etc.
        const typeValue = getFirstAttributeValue(element, ["type"]);

        if (typeValue) {
            properties.type = typeValue;
        }

        return properties;
    }

    // Expose the mapper API on the shared namespace.
    namespace.mapper = {
        newChainFromElement,
        newNodeFromElement
    };
})(globalThis);
