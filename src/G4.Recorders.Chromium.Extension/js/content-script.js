/**
 * Content script for the G4 Chromium Recorder extension.
 *
 * Runs in the top document and every child frame. It listens for the DOM interactions
 * named in the event catalog, converts each one into a recorder event whose JSON shape
 * matches the UiaPeek contract, and forwards it to the background service worker, which
 * relays it to the hub. The script never blocks or alters page behaviour: all listeners
 * are passive and no event is ever cancelled.
 *
 * @remarks
 * This file is the last entry in the manifest content-script list, so the constants,
 * settings, contract, locator, accessibility, candidate, DOM context, mapper, monitor, catalog, and
 * drag modules are already attached to the shared g4Recorder namespace by the time this code runs.
 */
(function initializeContentScriptModule(globalScope) {
    // Read the shared modules this script composes. All are loaded earlier in the list.
    const namespace = globalScope.g4Recorder;
    const {
        accessibility,
        catalog,
        constants,
        contract,
        dragTracker,
        locator,
        locatorCandidates,
        locatorMonitor,
        mapper,
        settings
    } = namespace;
    const { MESSAGE_CHANNELS } = constants;

    // The content script runs in an isolated world per frame, so a module-level settings
    // cache is the simplest correct place to hold the latest user preferences for the
    // synchronous event handlers. It is refreshed whenever settings change.
    let activeSettings = null;

    // When a <label> is clicked, the browser fires a real click on the label plus a
    // synthetic click on the labelled control. This window collapses that duplicate: the
    // synthetic click (detail === 0) that follows a real click (detail >= 1) within it is
    // skipped, so a label-wrapped control click produces a single InvokeClick.
    const SYNTHETIC_CLICK_WINDOW_MILLISECONDS = 50;
    let lastRealClickTimestamp = 0;

    // Field-session typed-text capture. Typing is recorded from `input` and coalesced until a
    // semantic commit boundary such as focus loss, form submission, field change, or page exit.
    // Ordinary pauses do not split the session. The last input time preserves interaction order,
    // and this state is per frame because each frame has its own isolated content-script world.
    let pendingTypingElement = null;
    let pendingTypingTimestamp = 0;

    // Hover-to-record state. A MoveMouseCursor is recorded once the mouse rests over an element
    // for the configured dwell; any movement restarts the timer, and one event is emitted per
    // rest. Used to grab locators for elements that are hard to inspect. Per frame (isolated
    // world). The fallback dwell guards against a missing/invalid stored value.
    const HOVER_DWELL_FALLBACK_MILLISECONDS = 5000;
    let hoverElement = null;
    let hoverClientX = 0;
    let hoverClientY = 0;
    let hoverDwellTimerId = null;

    // Drag and drop state. A press that may start a drag is kept until it is released, dropped, or
    // replaced by the next press. It turns into a drag once the pointer moves past the threshold
    // with the button down, or when the browser starts a native drag. Per frame (isolated world).
    // The fallback threshold guards against a missing or invalid stored value.
    const DRAG_THRESHOLD_FALLBACK_PIXELS = 8;
    let pendingDrag = null;

    // The browser fires a click after the release of a pointer drag, on the element under both the
    // press and the release, often the page body. That click is part of the drag, so a click that
    // arrives within this window after a recorded drag is not recorded.
    const DRAG_CLICK_WINDOW_MILLISECONDS = 500;
    let dragClickSuppressedUntil = 0;

    // Latest trusted pointer snapshot used by on-demand current-position peek requests even
    // when hover recording is disabled. The state is invalidated when the frame loses pointer ownership.
    let isPointerInsideDocument = false;
    let pointerClientX = 0;
    let pointerClientY = 0;
    let pointerObservedAtMilliseconds = 0;

    // The pointer events whose target is found by the pointer position. A click the keyboard
    // fired carries no usable position, so it is resolved by focus instead.
    const CLICK_EVENT_NAMES = ["click", "dblclick", "contextmenu"];

    // The events whose trigger node carries a DOM context: the element actions a client may want
    // to heal a locator for. Navigation, scroll, window, frame, and form events act on no element
    // a locator would target, so they are sent without one.
    const DOM_CONTEXT_EVENT_NAMES = new Set([
        "InvokeClick",
        "InvokeContextClick",
        "InvokeDoubleClick",
        "InvokeDragAndDrop",
        "MoveMouseCursor",
        "SendKeys"
    ]);

    // The events recorded against the control the user acted on rather than the innermost node
    // the pointer landed on. Typing already targets its field and a scroll targets what scrolls,
    // so only pointer actions are retargeted.
    const RETARGET_EVENT_NAMES = new Set([
        "InvokeClick",
        "InvokeContextClick",
        "InvokeDoubleClick",
        "MoveMouseCursor"
    ]);

    // How many matched elements an on-demand locator test describes in full. The total count is
    // always reported; the chains are capped so a broad locator cannot produce a huge response.
    const MAXIMUM_LOCATOR_TEST_MATCHES = 5;

    // How long the locator of this frame's own <iframe> element is reused. It is sent with every
    // event of the frame, and recomputing it each time would repeat the same work on the parent.
    const FRAME_LOCATOR_LIFETIME_MILLISECONDS = 3000;

    // The cached frame locator and the time it was resolved. Module-level because the cache
    // spans every event of this frame; each frame runs its own isolated copy of this script.
    let frameLocatorCache = null;
    let frameLocatorCachedAtMilliseconds = 0;

    /**
     * Resolves the element an event is recorded against.
     *
     * @remarks
     * Compute-only over the module settings cache. Pointer actions are attributed to the nearest
     * actionable ancestor of the raw target when retargeting is enabled, because that control is
     * what the user acted on and what carries a stable, user-facing identity.
     *
     * @param {Element} targetElement The raw event target.
     * @param {string} eventName The contract event name, for example "InvokeClick".
     * @returns {Element} The element to record.
     */
    function getActionElement(targetElement, eventName) {
        // Retarget only pointer actions, and only while the user keeps the option switched on.
        const isRetargetable = activeSettings.isLocatorRetargetEnabled
            && RETARGET_EVENT_NAMES.has(eventName);

        return isRetargetable
            ? accessibility.resolveActionableElement(targetElement)
            : targetElement;
    }

    /**
     * Resolves the real target of an event that was retargeted to a shadow host.
     *
     * @remarks
     * Compute-only. The event path already exposes targets inside open shadow roots, but stops at
     * the host of a closed one. From that host the walk goes down one shadow root at a time: by
     * the pointer position for pointer events, and by focus for typing and keyboard clicks. It
     * stops as soon as a root yields nothing deeper, so an event on the host itself stays there.
     *
     * @param {Element} element The target the event path reported.
     * @param {Event} domEvent The DOM event being recorded.
     * @returns {Element} The deepest target the extension can read.
     */
    function getDeepTarget(element, domEvent) {
        // Decide how to look inside a root: where the pointer is, or what holds the focus.
        const isClickEvent = CLICK_EVENT_NAMES.includes(domEvent.type);
        const isKeyboardClick = isClickEvent && domEvent.detail === 0;
        const isPositioned = typeof domEvent.clientX === "number"
            && typeof domEvent.clientY === "number";
        const isPointerWalk = isPositioned && !isKeyboardClick;

        // Go down while the current element hosts a root with something deeper inside it.
        let currentElement = element;
        let shadowRoot = locator.getShadowRoot(currentElement);

        while (shadowRoot) {
            const innerElement = isPointerWalk
                ? shadowRoot.elementFromPoint(domEvent.clientX, domEvent.clientY)
                : shadowRoot.activeElement;
            const isDeeper = Boolean(innerElement)
                && innerElement !== currentElement
                && innerElement.getRootNode() === shadowRoot;

            // Stop when the root yields nothing of its own: no element, or one outside its tree.
            if (!isDeeper) {
                break;
            }

            // Step into the inner element and continue with the root it may host.
            currentElement = innerElement;
            shadowRoot = locator.getShadowRoot(currentElement);
        }

        return currentElement;
    }

    /**
     * Resolves the deepest real target of a DOM event.
     *
     * @remarks
     * Compute-only helper. composedPath exposes the original target inside shadow DOM,
     * which a retargeted event.target would hide. Falls back to event.target, then to
     * the document element, so the result is always an element when one exists.
     *
     * @param {Event} domEvent The DOM event being recorded.
     * @returns {Element|null} The resolved element, or null when none is available.
     */
    function getEventTarget(domEvent) {
        // Prefer the composed path so shadow-DOM targets are not lost to retargeting.
        const composedPath = typeof domEvent.composedPath === "function"
            ? domEvent.composedPath()
            : [];
        const composedTarget = composedPath.length > 0
            ? composedPath[0]
            : null;

        // Choose the first available candidate that is an element node.
        const candidateTarget = composedTarget || domEvent.target || document.documentElement;

        // Use the candidate only when it is an element node. A closed shadow root hides its
        // content from the event path, so the walk continues from the host into every root the
        // extension can read.
        const isElementTarget = Boolean(candidateTarget)
            && candidateTarget.nodeType === Node.ELEMENT_NODE;

        if (isElementTarget) {
            return getDeepTarget(candidateTarget, domEvent);
        }

        // Wheel and scroll events can target the document; fall back to its root element.
        return document.documentElement || null;
    }

    /**
     * Resolves this frame's context so the worker can detect frame switches.
     *
     * @remarks
     * Compute-only helper. The worker assigns the numeric frameId from the message
     * sender; this provides the frame URL, whether it is a sub-frame, and a locator for
     * the owning <iframe> element. window.frameElement is only readable for a same-origin
     * parent, so a cross-origin frame returns a null locator (wrapped in try/catch) and
     * the worker falls back to URL plus frameId.
     *
     * @returns {object} The frame context ({ isSubFrame, frameUrl, frameLocator }).
     */
    function getFrameContext() {
        // The top document is never a frame switch target.
        const isSubFrame = globalScope.window !== globalScope.window.top;
        const frameUrl = document.location.href;

        if (!isSubFrame) {
            return { isSubFrame, frameUrl, frameLocator: null };
        }

        // Reuse a recent locator of the owning <iframe>, since every event of this frame carries it.
        const cacheAgeMilliseconds = Date.now() - frameLocatorCachedAtMilliseconds;
        const isCacheFresh = frameLocatorCache !== null
            && cacheAgeMilliseconds < FRAME_LOCATOR_LIFETIME_MILLISECONDS;

        if (isCacheFresh) {
            return { isSubFrame, frameUrl, frameLocator: frameLocatorCache };
        }

        // Resolve the owning <iframe> element's locators when the parent is same-origin.
        // Cross-origin access throws, so treat any failure as "no locator available".
        let frameLocator = null;

        try {
            const frameElement = globalScope.window.frameElement;

            if (frameElement) {
                const frameResult = locatorCandidates.getLocatorCandidates({
                    element: frameElement,
                    settings: getLocatorSettings()
                });

                frameLocator = {
                    xpath: frameResult.locator || locator.getAbsoluteXpath(frameElement),
                    cssSelector: locator.getCssSelector(frameElement),
                    locators: frameResult.locators
                };
            }
        } catch (error) {
            frameLocator = null;
        }

        // Remember the result, so the following events of this frame do not repeat the work.
        frameLocatorCache = frameLocator;
        frameLocatorCachedAtMilliseconds = Date.now();

        return { isSubFrame, frameUrl, frameLocator };
    }

    /**
     * Resolves the DOM context limits for an event, or null when it carries no context.
     *
     * @remarks
     * Compute-only over the module settings cache. The cache is the only source of the user's
     * DOM context settings in this isolated world, so every chain builder reads them here.
     *
     * @param {string} eventName The contract event name, for example "InvokeClick".
     * @returns {object|null} The limits ({ ancestorLevels, maximumKilobytes }) or null.
     */
    function getDomContextOptions(eventName) {
        // Settings that have not loaded yet, or a switched-off setting, attach no context.
        const isDomContextEnabled = Boolean(activeSettings && activeSettings.isDomContextEnabled);

        if (!isDomContextEnabled) {
            return null;
        }

        // Only element actions a locator targets carry the context.
        if (!DOM_CONTEXT_EVENT_NAMES.has(eventName)) {
            return null;
        }

        return {
            ancestorLevels: activeSettings.domContextAncestorLevels,
            maximumKilobytes: activeSettings.domContextMaximumKilobytes
        };
    }

    /**
     * Builds the hot-test record of a recording, or null when it has nothing to test.
     *
     * @remarks
     * Compute-only. The record is what the background worker keeps to re-test the element on a
     * later load of this page: the locators, the fingerprint that recognises the element again,
     * and this document's identity. An element inside a shadow root is included: its shadow path
     * is tested against the element and its other locators against the host.
     *
     * @param {object} recording The built recording ({ element, recordingEvent, ... }).
     * @returns {object|null} The record, or null when hot testing does not apply.
     */
    function getLocatorRecord(recording) {
        // Hot testing can be switched off, and only element actions have locators worth testing.
        const isHotTestingEnabled = Boolean(activeSettings.isLocatorHotTestingEnabled);
        const isElementEvent = DOM_CONTEXT_EVENT_NAMES.has(recording.recordingEvent.event);
        const isRecordable = isHotTestingEnabled && isElementEvent && Boolean(recording.element);

        if (!isRecordable) {
            return null;
        }

        return newLocatorRecord(recording.element, recording.recordingEvent.chain);
    }

    /**
     * Builds the hot-test record of a drag and drop's drop element, or null when it has none.
     *
     * @remarks
     * Compute-only. The drop element is recorded with its own chain in value.target, under its
     * own locator identifier, so it is re-tested and corrected like the dragged element.
     *
     * @param {object} recording The built recording ({ targetElement, recordingEvent, ... }).
     * @returns {object|null} The record, or null when hot testing does not apply.
     */
    function getTargetLocatorRecord(recording) {
        // Only a drag that ended on an element has a drop element worth testing.
        const value = recording.recordingEvent.value;
        const isTargetPresent = Boolean(value) && Boolean(value.target);
        const targetChain = isTargetPresent
            ? value.target.chain
            : null;
        const isRecordable = Boolean(activeSettings.isLocatorHotTestingEnabled)
            && Boolean(recording.targetElement)
            && Boolean(targetChain);

        if (!isRecordable) {
            return null;
        }

        return newLocatorRecord(recording.targetElement, targetChain);
    }

    /**
     * Builds the hot-test record of one element from the chain recorded for it.
     *
     * @remarks
     * Compute-only. Reads the locators the mapper published on the chain's trigger node and adds
     * what a later load needs to find the element again: its fingerprint and this page's identity.
     *
     * @param {Element} element The recorded element.
     * @param {object} chain The chain recorded for the element.
     * @returns {object|null} The record, or null when the chain carries no locators to test.
     */
    function newLocatorRecord(element, chain) {
        // Read the locators the mapper published on the trigger node.
        const triggerNode = chain.path[0];
        const properties = triggerNode
            ? triggerNode.properties
            : null;
        const isTestable = Boolean(properties) && Array.isArray(properties.locators);

        if (!isTestable) {
            return null;
        }

        // Describe the element for a later load: its locators, how to recognise it, and where.
        return {
            documentId: locatorMonitor.getDocumentId(),
            fingerprint: locatorCandidates.getElementFingerprint(element),
            locatorId: properties.locatorId,
            locators: properties.locators,
            pageKey: locatorMonitor.getPageKey()
        };
    }

    /**
     * Resolves the locator settings the candidate engine runs with.
     *
     * @remarks
     * Compute-only over the module settings cache, which is the only source of the user's
     * locator preferences in this isolated world.
     *
     * @returns {object} The settings ({ maximumTextLength, preference, testHookAttributes }).
     */
    function getLocatorSettings() {
        return {
            maximumTextLength: activeSettings.locatorMaximumTextLength,
            preference: activeSettings.locatorPreference,
            testHookAttributes: activeSettings.locatorTestHookAttributes
        };
    }

    /**
     * Builds a complete recording event from a DOM event.
     *
     * @remarks
     * Compute-only. Returns null when the event is not in the catalog or is disabled in
     * settings, so the caller can simply skip it.
     *
     * @param {Event} domEvent The DOM event being recorded.
     * @returns {object|null} An object with the recording event and its frame-switch hint
     *   ({ recordingEvent, isFrameSwitchTrigger }), or null to skip.
     */
    function newRecordingEventFromDom(domEvent) {
        // Resolve the contract descriptor; unknown events yield null and are ignored.
        const descriptor = catalog.resolveEventDescriptor({ domEvent });

        if (!descriptor) {
            return null;
        }

        // Resolve the element the interaction targeted; without one there is no chain.
        const rawTargetElement = getEventTarget(domEvent);

        if (!rawTargetElement) {
            return null;
        }

        // Attribute a pointer action to the control the user acted on, keeping the raw target
        // only when it differs, so the chain can still report the exact node the event landed on.
        const targetElement = getActionElement(rawTargetElement, descriptor.event);
        const originalElement = targetElement === rawTargetElement
            ? null
            : rawTargetElement;

        // Resolve the trigger point and convert it to the contract point shape, when the
        // event family carries coordinates.
        const pointInput = catalog.getEventPoint(domEvent);
        const point = pointInput
            ? contract.newPoint(pointInput)
            : null;

        // Build the ancestor chain rooted at the target element, with its verified locators and,
        // when enabled, its DOM context.
        const chain = mapper.newChainFromElement({
            element: targetElement,
            trigger: descriptor.event,
            point,
            domContextOptions: getDomContextOptions(descriptor.event),
            locatorSettings: getLocatorSettings(),
            originalElement
        });

        // Assemble the top-level event, stamping the page host and capture time.
        const recordingEvent = contract.newRecordingEvent({
            chain,
            event: descriptor.event,
            machineName: document.location.hostname,
            timestamp: Date.now(),
            type: descriptor.type,
            value: descriptor.value
        });

        // Return the event with the catalog's frame-switch hint. The worker uses it so only
        // genuine in-frame gestures (clicks/scroll) change the active frame; commit events
        // (SendKeys from a field session, SubmitForm from `submit`) can fire in a background or mirror
        // frame and must not trigger a SwitchFrame. Default to a trigger unless the descriptor
        // opts out, so any future event keeps switching unless it declares otherwise.
        return {
            element: targetElement,
            recordingEvent,
            isFrameSwitchTrigger: descriptor.isFrameSwitchTrigger !== false
        };
    }

    /**
     * Handles any catalogued DOM event by recording and forwarding it.
     *
     * @remarks
     * Owns the per-event flow and closes a pending field session before form submission.
     * Guards on the settings cache and per-event toggle so disabled catalog events never
     * build their own payload or cross the messaging boundary.
     *
     * @param {Event} domEvent The DOM event being recorded.
     * @returns {void}
     */
    function onRecordableEvent(domEvent) {
        // Do nothing until settings have loaded, so toggles are always respected.
        if (!activeSettings) {
            return;
        }

        // Commit the active field before its form submission so SendKeys precedes SubmitForm even
        // when pressing Enter submits without moving focus away from the field.
        if (domEvent.type === "submit") {
            sendPendingTyping();
        }

        // Find the catalog entry for this DOM event so its toggle key can be checked.
        const catalogEntry = catalog.getCatalogEntries().find(
            (entry) => entry.domEventName === domEvent.type
        );

        const isEventEnabled = catalogEntry
            && activeSettings.enabledEvents[catalogEntry.settingKey];

        if (!isEventEnabled) {
            return;
        }

        // Skip the click the browser fires after the release of a recorded pointer drag.
        const isDragClick = domEvent.type === "click" && Date.now() < dragClickSuppressedUntil;

        if (isDragClick) {
            dragClickSuppressedUntil = 0;

            return;
        }

        // Collapse the duplicate click a browser dispatches on a control when its <label>
        // is clicked: skip the synthetic follow-up, but keep keyboard-activated clicks
        // (also detail 0, yet not following a real click) and direct control clicks.
        if (domEvent.type === "click") {
            const isSyntheticClick = domEvent.detail === 0;
            const isFollowingRealClick =
                (Date.now() - lastRealClickTimestamp) < SYNTHETIC_CLICK_WINDOW_MILLISECONDS;

            if (isSyntheticClick && isFollowingRealClick) {
                return;
            }

            if (!isSyntheticClick) {
                lastRealClickTimestamp = Date.now();
            }
        }

        // Build the recording event; skip when the catalog declines to describe it.
        const recording = newRecordingEventFromDom(domEvent);

        if (!recording) {
            return;
        }

        // Forward the event to the background worker for relay to the hub.
        sendRecordingMessage(recording);
    }

    /**
     * Forwards a built recording event to the background worker.
     *
     * @remarks
     * Owns the messaging boundary for every recorded event (clicks, scroll, submit, and the
     * field-session SendKeys). Sends this frame's context and the frame-switch hint so the worker
     * emits a SwitchFrame only when a genuine in-frame gesture moved to a different frame.
     * Ignores "no receiver" rejections that occur while the worker is briefly asleep. An element
     * action also hands its hot-test record to the worker and starts the settle test.
     *
     * @param {object} recording The event plus its context ({ element, recordingEvent,
     *   isFrameSwitchTrigger, targetElement }). Only a drag and drop names a target element.
     * @returns {void}
     */
    function sendRecordingMessage(recording) {
        // Resolve the hot-test records first, so they travel with the event they belong to.
        const locatorRecord = getLocatorRecord(recording);
        const targetLocatorRecord = getTargetLocatorRecord(recording);
        const recordingMessage = {
            channel: MESSAGE_CHANNELS.recordingEvent,
            recordingEvent: recording.recordingEvent,
            frameContext: getFrameContext(),
            isFrameSwitchTrigger: recording.isFrameSwitchTrigger,
            locatorRecord,
            targetLocatorRecord
        };

        chrome.runtime.sendMessage(recordingMessage).catch(() => {
            // The worker will respawn on the next event; nothing to do here.
        });

        // Re-test the locators once the page has reacted to the action that was just recorded.
        if (locatorRecord) {
            locatorMonitor.watchLocator({
                element: recording.element,
                locatorId: locatorRecord.locatorId,
                locators: locatorRecord.locators
            }).catch(() => {
                // Hot testing is an improvement; a failed test leaves the recorded locators as they are.
            });
        }

        // Re-test the drop element of a drag and drop the same way, under its own identifier.
        if (targetLocatorRecord) {
            locatorMonitor.watchLocator({
                element: recording.targetElement,
                locatorId: targetLocatorRecord.locatorId,
                locators: targetLocatorRecord.locators
            }).catch(() => {
                // Hot testing is an improvement; a failed test leaves the recorded locators as they are.
            });
        }
    }

    /**
     * Tests whether an element accepts typed text (so its typing should be recorded).
     *
     * @remarks
     * Compute-only helper. Text areas, contenteditable hosts, and text-like inputs qualify;
     * checkboxes, radios, buttons, and other non-text inputs do not (those are captured via
     * their click), so a toggle never produces a SendKeys.
     *
     * @param {Element} element The element that produced an input event.
     * @returns {boolean} True when the element accepts typed text.
     */
    function testTypeableElement(element) {
        // Without an element there is nothing typeable.
        if (!element) {
            return false;
        }

        // A contenteditable host accepts typed text directly.
        if (element.isContentEditable) {
            return true;
        }

        // A textarea always accepts typed text.
        if (element.nodeName === "TEXTAREA") {
            return true;
        }

        // An input accepts typed text unless it is a non-text control.
        if (element.nodeName === "INPUT") {
            const nonTextTypes = [
                "checkbox", "radio", "button", "submit", "reset", "file", "image", "range", "color", "hidden"
            ];
            const inputType = (element.type || "text").toLowerCase();

            return !nonTextTypes.includes(inputType);
        }

        return false;
    }

    /**
     * Builds a SendKeys recording event for a field's current text.
     *
     * @remarks
     * Compute-only. Mirrors newRecordingEventFromDom but starts from a stored element (not a
     * live DOM event), because typing is flushed after the fact. Typing carries no point.
     *
     * @param {Element} element The field that was typed into.
     * @param {number} typedTimestamp The epoch-ms time of the last keystroke.
     * @returns {object} The event plus its hint ({ recordingEvent, isFrameSwitchTrigger }).
     */
    function newSendKeysRecording(element, typedTimestamp) {
        // Resolve the SendKeys descriptor (value + redaction) from the field element.
        const descriptor = catalog.newSendKeysDescriptor({
            targetElement: element,
            isRedactPasswordsEnabled: activeSettings.isRedactPasswordsEnabled
        });

        // Build the ancestor chain rooted at the typed element, with its verified locators and,
        // when enabled, its DOM context.
        const chain = mapper.newChainFromElement({
            element,
            trigger: descriptor.event,
            point: null,
            domContextOptions: getDomContextOptions(descriptor.event),
            locatorSettings: getLocatorSettings()
        });

        // Stamp the event with the last input time so it orders at typing time, not flush time.
        const recordingEvent = contract.newRecordingEvent({
            chain,
            event: descriptor.event,
            machineName: document.location.hostname,
            timestamp: typedTimestamp,
            type: descriptor.type,
            value: descriptor.value
        });

        return {
            element,
            recordingEvent,
            isFrameSwitchTrigger: descriptor.isFrameSwitchTrigger !== false
        };
    }

    /**
     * Sends the pending typed-text SendKeys, if any, and closes the field session.
     *
     * @remarks
     * Owns the typing commit. Called only at semantic boundaries: focus loss, form submission,
     * page exit, or typing moving to a different field. Skips a field that has since detached so
     * a stale, unlocatable chain is never sent.
     *
     * @returns {void}
     */
    function sendPendingTyping() {
        // Capture and clear the pending state before building so a new field session starts clean.
        const element = pendingTypingElement;
        const typedTimestamp = pendingTypingTimestamp;
        pendingTypingElement = null;
        pendingTypingTimestamp = 0;

        // Nothing can be committed when there is no session or its field left the document.
        if (!element || !element.isConnected) {
            return;
        }

        // Build and forward one SendKeys carrying the field's current text.
        const recording = newSendKeysRecording(element, typedTimestamp);

        sendRecordingMessage(recording);
    }

    /**
     * Tracks typing as one field session until a semantic commit boundary emits SendKeys.
     *
     * @remarks
     * Owns the typing capture. Repeated inputs update the same session regardless of pauses, and
     * moving to a new field commits the previous one first so text is never merged across fields.
     * Respects the `input` toggle and ignores non-text targets.
     *
     * @param {Event} domEvent The input event being recorded.
     * @returns {void}
     */
    function onTypingInput(domEvent) {
        // Do nothing until settings have loaded, so toggles are always respected.
        if (!activeSettings) {
            return;
        }

        // Ignore programmatic/synthetic input: only real user typing should produce a SendKeys.
        // This also stops a value synced into a background/mirror frame (dispatched, not typed)
        // from being recorded as a duplicate SendKeys in that frame.
        if (!domEvent.isTrusted) {
            return;
        }

        // Respect the toggle that governs typed-text capture.
        if (!activeSettings.enabledEvents.input) {
            return;
        }

        // Only text-like fields produce SendKeys; non-text controls are captured via click.
        const element = getEventTarget(domEvent);

        if (!testTypeableElement(element)) {
            return;
        }

        // Commit the previous field when typing changes targets so separate fields never merge.
        if (pendingTypingElement && pendingTypingElement !== element) {
            sendPendingTyping();
        }

        // Track the field and the time of this keystroke as the SendKeys ordering timestamp.
        pendingTypingElement = element;
        pendingTypingTimestamp = Date.now();
    }

    /**
     * Commits pending typed text when the tracked field loses focus.
     *
     * @remarks
     * Owns the blur commit. Guarantees a type-then-click-away interaction is recorded and ordered
     * before the click that caused the blur.
     *
     * @param {Event} domEvent The focusout event.
     * @returns {void}
     */
    function onTypingBlur(domEvent) {
        // Without a tracked field there is nothing to commit.
        if (!pendingTypingElement) {
            return;
        }

        // Commit only when the field losing focus is the one currently being tracked. For a field
        // inside a closed shadow root the event reports the host, and by now the root no longer
        // has a focused element to walk to, so a host of the tracked field counts as that field.
        const element = getEventTarget(domEvent);
        const isTrackedField = pendingTypingElement === element;
        const isTrackedHost = testShadowHost(element, pendingTypingElement);

        if (isTrackedField || isTrackedHost) {
            sendPendingTyping();
        }
    }

    /**
     * Commits pending typed text before this frame's document leaves its current page.
     *
     * @remarks
     * Owns the page-exit boundary. The synchronous send request is started while the content
     * script is still alive so navigation or window closure does not silently discard a focused
     * field that never emitted focusout.
     *
     * @returns {void}
     */
    function onTypingPageHide() {
        sendPendingTyping();
    }

    /**
     * Resolves the configured hover dwell, falling back to a safe default.
     *
     * @remarks
     * Compute-only helper. Settings are merged over defaults, so the value is normally present;
     * the guard covers a missing or non-positive stored value.
     *
     * @returns {number} The dwell in milliseconds.
     */
    function getHoverDwellMilliseconds() {
        const configured = activeSettings.hoverDwellMilliseconds;

        return Number.isFinite(configured) && configured > 0
            ? configured
            : HOVER_DWELL_FALLBACK_MILLISECONDS;
    }

    /**
     * Builds a MoveMouseCursor recording event for the element the mouse rested on.
     *
     * @remarks
     * Compute-only. Mirrors newRecordingEventFromDom but starts from the resting element and
     * cursor position captured on the last move, since there is no live event at dwell time.
     *
     * @param {Element} element The element under the cursor at rest.
     * @param {number} clientX The cursor client X at rest.
     * @param {number} clientY The cursor client Y at rest.
     * @returns {object} The event plus its hint ({ recordingEvent, isFrameSwitchTrigger }).
     */
    function newMoveMouseCursorRecording(element, clientX, clientY) {
        const dwellMilliseconds = getHoverDwellMilliseconds();

        // Resolve the descriptor (coordinates + dwell) and a contract point for the chain.
        const descriptor = catalog.newMoveMouseCursorDescriptor({
            x: clientX,
            y: clientY,
            dwellMilliseconds
        });
        const point = contract.newPoint({ xPosition: clientX, yPosition: clientY });

        // Build the ancestor chain rooted at the rested element, with its verified locators and,
        // when enabled, its DOM context.
        const chain = mapper.newChainFromElement({
            element,
            trigger: descriptor.event,
            point,
            domContextOptions: getDomContextOptions(descriptor.event),
            locatorSettings: getLocatorSettings()
        });

        const recordingEvent = contract.newRecordingEvent({
            chain,
            event: descriptor.event,
            machineName: document.location.hostname,
            timestamp: Date.now(),
            type: descriptor.type,
            value: descriptor.value
        });

        return {
            element,
            recordingEvent,
            isFrameSwitchTrigger: descriptor.isFrameSwitchTrigger !== false
        };
    }

    /**
     * Emits the MoveMouseCursor once the mouse has rested for the dwell.
     *
     * @remarks
     * Owns the hover flush. Fires from the dwell timer and does not re-arm, so exactly one
     * event is recorded per rest; the next move re-arms the timer. Skips a target that has
     * since detached so a stale, unlocatable chain is never sent.
     *
     * @returns {void}
     */
    function flushHoverDwell() {
        // The timer has fired; mark it inactive and do not re-arm (one event per rest).
        hoverDwellTimerId = null;

        // Capture and clear the resting target so a later move starts a fresh dwell. The rest is
        // attributed to the control under the cursor, like a click on it would be.
        const element = hoverElement
            ? getActionElement(hoverElement, "MoveMouseCursor")
            : null;
        const clientX = hoverClientX;
        const clientY = hoverClientY;
        hoverElement = null;

        // Nothing to record, or the element is gone from the document.
        if (!element || !element.isConnected) {
            return;
        }

        const recording = newMoveMouseCursorRecording(element, clientX, clientY);

        sendRecordingMessage(recording);
    }

    /**
     * Tracks mouse movement and (re)arms the hover dwell timer.
     *
     * @remarks
     * Owns the hover capture. Any real movement restarts the timer, so MoveMouseCursor fires
     * only after the mouse rests for the dwell. Respects the `hover` toggle and ignores
     * synthetic moves. Recomputes the deepest target each move so shadow-DOM elements (common
     * for hard-to-inspect controls) resolve correctly.
     *
     * @param {Event} domEvent The mousemove event.
     * @returns {void}
     */
    function onHoverMove(domEvent) {
        // Ignore programmatic/synthetic movement: only a real pointer resting should record.
        if (!domEvent.isTrusted) {
            return;
        }

        // Retain current-position state independently from hover recording so API peek remains available.
        isPointerInsideDocument = true;
        pointerClientX = domEvent.clientX;
        pointerClientY = domEvent.clientY;
        pointerObservedAtMilliseconds = Date.now();

        // Do nothing until settings have loaded, so recording toggles are always respected.
        if (!activeSettings) {
            return;
        }

        // Respect the toggle that governs hover capture.
        if (!activeSettings.enabledEvents.hover) {
            return;
        }

        // A drag in progress is not a rest over an element, so no hover dwell runs during it.
        const isDragInProgress = Boolean(pendingDrag) && pendingDrag.isActive;

        if (isDragInProgress) {
            stopHoverDwell();

            return;
        }

        // Track the element under the cursor and the cursor position for this move.
        hoverElement = getEventTarget(domEvent);
        hoverClientX = domEvent.clientX;
        hoverClientY = domEvent.clientY;

        // Restart the dwell timer; the mouse must stay still for the dwell to record.
        if (hoverDwellTimerId !== null) {
            clearTimeout(hoverDwellTimerId);
        }

        hoverDwellTimerId = setTimeout(flushHoverDwell, getHoverDwellMilliseconds());
    }

    /**
     * Cancels a pending hover dwell when the pointer leaves this frame.
     *
     * @remarks
     * Owns cancellation so a dwell does not complete for an element the pointer already left
     * (for example after moving into another frame).
     *
     * @returns {void}
     */
    function onHoverLeave() {
        // Invalidate current-position ownership before clearing optional hover-dwell state.
        isPointerInsideDocument = false;

        // Cancel the dwell so it cannot complete for an element the pointer already left.
        stopHoverDwell();
    }

    /**
     * Cancels a pending hover dwell and forgets its element.
     *
     * @remarks
     * Owns the hover-dwell cleanup shared by leaving the frame and by starting a drag, so neither
     * leaves a dwell that completes for an element the pointer no longer rests on.
     *
     * @returns {void}
     */
    function stopHoverDwell() {
        // Clear a running dwell timer so no MoveMouseCursor is recorded after the cancellation.
        if (hoverDwellTimerId !== null) {
            clearTimeout(hoverDwellTimerId);
            hoverDwellTimerId = null;
        }

        // Forget the element so a later flush cannot target where the pointer no longer rests.
        hoverElement = null;
    }

    /**
     * Resolves the configured drag threshold, falling back to a safe default.
     *
     * @remarks
     * Compute-only helper. Settings are merged over defaults, so the value is normally present;
     * the guard covers a missing or non-positive stored value.
     *
     * @returns {number} The threshold in pixels on either axis.
     */
    function getDragThresholdPixels() {
        // Accept only a positive finite number, so a corrupt stored value cannot disable the threshold.
        const configuredPixels = activeSettings.dragThresholdPixels;
        const isConfiguredValid = Number.isFinite(configuredPixels) && configuredPixels > 0;

        return isConfiguredValid
            ? configuredPixels
            : DRAG_THRESHOLD_FALLBACK_PIXELS;
    }

    /**
     * Resolves every element under a viewport point, the deepest one first.
     *
     * @remarks
     * Compute-only over the live document. The deepest element comes from the same shadow-aware
     * walk a peek uses. The layers of the document follow it, topmost first, so the drop element
     * is still found when a pointer drag keeps the dragged element under the pointer.
     *
     * @param {{x: number, y: number}} point The point in this frame's viewport.
     * @returns {Element[]} The elements under the point, with no empty entries.
     */
    function getElementsAtPoint(point) {
        // Read the shadow-aware deepest element, then every document layer where the API exists.
        const deepElement = resolveElementFromPoint(point.x, point.y);
        const layeredElements = typeof document.elementsFromPoint === "function"
            ? document.elementsFromPoint(point.x, point.y)
            : [];

        // Keep the deepest element first and drop empty entries, so callers only see elements.
        return [deepElement, ...layeredElements].filter(Boolean);
    }

    /**
     * Builds the chain of the element a drag starts on.
     *
     * @remarks
     * Compute-only. Called once the gesture is known to be a drag, before the release, so the
     * chain describes the element before the page moves or replaces it on the drop.
     *
     * @param {object} drag The drag state ({ sourceElement, start, ... }).
     * @returns {object} The chain, with its verified locators and, when enabled, its DOM context.
     */
    function newDragSourceChain(drag) {
        return mapper.newChainFromElement({
            element: drag.sourceElement,
            trigger: "InvokeDragAndDrop",
            point: contract.newPoint({ xPosition: drag.start.x, yPosition: drag.start.y }),
            domContextOptions: getDomContextOptions("InvokeDragAndDrop"),
            locatorSettings: getLocatorSettings()
        });
    }

    /**
     * Starts a pointer drag once the pointer moves past the threshold with the button down.
     *
     * @remarks
     * Owns the switch from a press to a drag. The source chain is built at that moment, before
     * the page changes anything on the drop. A move with the main button no longer down means
     * the release happened outside this frame, so the press is forgotten.
     *
     * @param {MouseEvent} domEvent The mousemove event.
     * @returns {void}
     */
    function onDragMove(domEvent) {
        // Only a real move of an armed, not yet started drag matters here.
        const isArmed = Boolean(pendingDrag) && !pendingDrag.isActive && domEvent.isTrusted;

        if (!isArmed) {
            return;
        }

        // A move without the main button down ends the press that armed the drag. Bit 1 of
        // MouseEvent.buttons is the main button.
        const isButtonHeld = (domEvent.buttons & 1) === 1;

        if (!isButtonHeld) {
            pendingDrag = null;

            return;
        }

        // Wait until the pointer leaves the threshold square, so a click wobble stays a click.
        const point = { x: domEvent.clientX, y: domEvent.clientY };
        const isPastThreshold = dragTracker.testDragThreshold({
            start: pendingDrag.start,
            point,
            thresholdPixels: getDragThresholdPixels()
        });

        if (!isPastThreshold) {
            return;
        }

        // Describe the dragged element now, before the page changes anything on the drop.
        pendingDrag.isActive = true;
        pendingDrag.sourceChain = newDragSourceChain(pendingDrag);

        // Stop any hover dwell, so no rest over an element is recorded for the rest of the drag.
        stopHoverDwell();
    }

    /**
     * Arms a possible drag when the main button is pressed on an element.
     *
     * @remarks
     * Owns the start of the drag state. Every press replaces what an earlier, unfinished press
     * left behind. Only the cheap facts are taken here: the press point, the time, and the center
     * of the element, because most presses end as a click. Respects the `dragAndDrop` toggle.
     *
     * @param {MouseEvent} domEvent The mousedown event.
     * @returns {void}
     */
    function onDragPress(domEvent) {
        // A new press always starts from a clean state.
        pendingDrag = null;

        // Do nothing until settings have loaded, so toggles are always respected.
        const isDragEnabled = Boolean(activeSettings) && Boolean(activeSettings.enabledEvents.dragAndDrop);

        if (!isDragEnabled) {
            return;
        }

        // Arm only a press that grabs an element, not one that works a field or a scroll bar.
        const element = getEventTarget(domEvent);
        const isDragSource = dragTracker.testDragSource({ element, domEvent });

        if (!isDragSource) {
            return;
        }

        // Keep only the cheap press facts; the chain is built once the press becomes a drag.
        pendingDrag = {
            isActive: false,
            isNative: false,
            pressTimestamp: Date.now(),
            sourceCenter: dragTracker.getElementCenter(element),
            sourceChain: null,
            sourceElement: element,
            start: { x: domEvent.clientX, y: domEvent.clientY }
        };
    }

    /**
     * Records a pointer drag when the main button is released.
     *
     * @remarks
     * Owns the end of a pointer drag. A press that never moved past the threshold is a click and
     * is left to the click recording. A release that leaves text selected was a text selection.
     * Otherwise the drop element is the first element under the release point that is not the
     * dragged element or one of its relatives.
     *
     * @param {MouseEvent} domEvent The mouseup event.
     * @returns {void}
     */
    function onDragRelease(domEvent) {
        // The press ends here whatever it was, so the next press starts clean.
        const drag = pendingDrag;
        pendingDrag = null;

        // Only the release of a started pointer drag is recorded here; a native drag ends on drop.
        const isPointerDrag = Boolean(drag)
            && drag.isActive
            && !drag.isNative
            && domEvent.isTrusted
            && domEvent.button === 0;

        if (!isPointerDrag) {
            return;
        }

        // A press and a move over unhandled text selects that text, which is not a drag and drop.
        const isTextSelection = dragTracker.testTextSelection();

        if (isTextSelection) {
            return;
        }

        // Find the drop element under the release point, skipping the dragged element and its relatives.
        const point = { x: domEvent.clientX, y: domEvent.clientY };
        const dropElement = dragTracker.getDropElement({
            candidates: getElementsAtPoint(point),
            sourceElement: drag.sourceElement
        });

        // Commit the pointer drag, so the click the browser fires next is skipped as part of it.
        sendDragAndDrop({ drag, dropElement, kind: "Pointer", point });
    }

    /**
     * Forgets a native drag that ended without a drop.
     *
     * @remarks
     * Owns the cleanup of a cancelled native drag: one released over nothing that accepts it, one
     * cancelled with Escape, or one dropped into another frame, which records nothing here.
     *
     * @returns {void}
     */
    function onNativeDragEnd() {
        // Leave a pointer press alone; only a native drag ends with dragend.
        const isNativeDrag = Boolean(pendingDrag) && pendingDrag.isNative;

        if (!isNativeDrag) {
            return;
        }

        // Forget the undropped drag, so the next press starts from a clean state.
        pendingDrag = null;
    }

    /**
     * Starts a native drag when the browser begins an HTML drag and drop.
     *
     * @remarks
     * Owns the start of a native drag. The browser fires no further mouse events during it, so the
     * drag is started here, with the source chain, and recorded on drop. The press point and time
     * are kept when the press armed the drag. A drag of selected text starts on a text node, which
     * names no element, so it is not recorded. Respects the `dragAndDrop` toggle.
     *
     * @param {DragEvent} domEvent The dragstart event.
     * @returns {void}
     */
    function onNativeDragStart(domEvent) {
        // Take over the press that armed this drag, if any, and start from a clean state.
        const press = pendingDrag;
        pendingDrag = null;

        // Do nothing until settings have loaded, so toggles are always respected.
        const isDragEnabled = Boolean(activeSettings)
            && Boolean(activeSettings.enabledEvents.dragAndDrop)
            && domEvent.isTrusted;

        if (!isDragEnabled) {
            return;
        }

        // A drag of selected text starts on a text node and has no element to address.
        const composedPath = typeof domEvent.composedPath === "function"
            ? domEvent.composedPath()
            : [];
        const isElementOrigin = composedPath.length > 0
            && composedPath[0].nodeType === Node.ELEMENT_NODE;

        if (!isElementOrigin) {
            return;
        }

        // Keep the press point and time when a press armed the drag, so the event orders where the
        // gesture began; otherwise the drag starts here.
        const element = getEventTarget(domEvent);
        const pressTimestamp = press
            ? press.pressTimestamp
            : Date.now();
        const start = press
            ? press.start
            : { x: domEvent.clientX, y: domEvent.clientY };

        // Start the native drag and describe the dragged element now, before the page reacts to the drop.
        pendingDrag = {
            isActive: true,
            isNative: true,
            pressTimestamp,
            sourceCenter: dragTracker.getElementCenter(element),
            sourceChain: null,
            sourceElement: element,
            start
        };
        pendingDrag.sourceChain = newDragSourceChain(pendingDrag);

        // No hover dwell runs while the drag lasts.
        stopHoverDwell();
    }

    /**
     * Records a native drag when it is dropped on an element that accepts it.
     *
     * @remarks
     * Owns the end of a native drag. The browser fires drop only on an element whose page accepted
     * the drag, so a drop always names a drop zone. It runs in the capture phase, before the page
     * moves anything, so the drop element is described as the user saw it.
     *
     * @param {DragEvent} domEvent The drop event.
     * @returns {void}
     */
    function onNativeDrop(domEvent) {
        // Only a native drag started in this frame is recorded here.
        const drag = pendingDrag;
        const isNativeDrag = Boolean(drag) && drag.isNative && domEvent.isTrusted;

        if (!isNativeDrag) {
            return;
        }

        // The drag ends with this drop, so the dragend that follows finds nothing to forget.
        pendingDrag = null;

        // The drop element is the event's own target, unless that is the dragged element itself.
        const point = { x: domEvent.clientX, y: domEvent.clientY };
        const dropElement = dragTracker.getDropElement({
            candidates: [getEventTarget(domEvent)],
            sourceElement: drag.sourceElement
        });

        // Commit the native drag; a native drop fires no follow-up click to skip.
        sendDragAndDrop({ drag, dropElement, kind: "Native", point });
    }

    /**
     * Builds and forwards one InvokeDragAndDrop recording.
     *
     * @remarks
     * Owns the drag commit. Pending typing is sent first so it keeps its place before the drag.
     * The event is stamped with the press time, so it orders where the gesture began. After a
     * pointer drag the browser fires a click, which the click recording then skips; a native
     * drop fires none, so the next click after it is recorded as usual.
     *
     * @param {object} options Commit inputs.
     * @param {object} options.drag The finished drag state.
     * @param {Element|null} options.dropElement The drop element, or null when there is none.
     * @param {string} options.kind "Native" or "Pointer".
     * @param {{x: number, y: number}} options.point The release point.
     * @returns {void}
     */
    function sendDragAndDrop(options) {
        const { drag, dropElement, kind, point } = options;

        // Keep typed text ahead of the drag that followed it.
        sendPendingTyping();

        // Describe the drop element with its own verified locators; a drag onto nothing has none.
        let targetChain = null;

        if (dropElement) {
            targetChain = mapper.newChainFromElement({
                element: dropElement,
                trigger: "Drop",
                point: contract.newPoint({ xPosition: point.x, yPosition: point.y }),
                locatorSettings: getLocatorSettings()
            });
        }

        // Build the event from the dragged element's chain and the drag facts.
        const descriptor = catalog.newDragAndDropDescriptor({
            kind,
            point,
            sourceCenter: drag.sourceCenter,
            durationMilliseconds: Date.now() - drag.pressTimestamp,
            targetChain
        });
        const recordingEvent = contract.newRecordingEvent({
            chain: drag.sourceChain,
            event: descriptor.event,
            machineName: document.location.hostname,
            timestamp: drag.pressTimestamp,
            type: descriptor.type,
            value: descriptor.value
        });

        // Skip the click that follows the release of a pointer drag; a native drop fires none.
        if (kind === "Pointer") {
            dragClickSuppressedUntil = Date.now() + DRAG_CLICK_WINDOW_MILLISECONDS;
        }

        // Forward the drag with both elements, so each is hot-tested under its own identifier.
        sendRecordingMessage({
            element: drag.sourceElement,
            isFrameSwitchTrigger: descriptor.isFrameSwitchTrigger !== false,
            recordingEvent,
            targetElement: dropElement
        });
    }

    /**
     * Creates a content-frame response for one on-demand locator test.
     *
     * @remarks
     * Compute-only over the current document. The locator is evaluated with the same evaluator
     * the recorder verifies its own candidates with, so a client sees exactly what a recorded
     * locator would match right now. The total count is always returned; only the first few
     * matches are described as chains.
     *
     * @param {object} request The request carrying the strategy (using) and the locator (value).
     * @returns {object} The frame result ({ chains, count, frameContext }).
     */
    function newLocatorTestResponse(request) {
        const response = {
            chains: [],
            count: 0,
            frameContext: getFrameContext()
        };

        // Reject malformed runtime messages without reading properties through implicit coercion.
        const isRequestValid = request !== null
            && typeof request === "object"
            && typeof request.using === "string"
            && typeof request.value === "string";

        if (!isRequestValid) {
            return response;
        }

        // Evaluate the locator in this frame's document and describe the first matches.
        const elements = locator.findElements({
            root: document,
            using: request.using,
            value: request.value
        });

        response.count = elements.length;
        response.chains = elements
            .slice(0, MAXIMUM_LOCATOR_TEST_MATCHES)
            .map((element) => mapper.newChainFromElement({
                element,
                point: null,
                trigger: "LocatorTest",
                locatorSettings: getLocatorSettings()
            }));

        return response;
    }

    /**
     * Creates a content-frame response for one normalized server peek request.
     *
     * @remarks
     * Compute-only over the current document plus the frame-owned pointer snapshot. Coordinate
     * and current modes resolve at viewport-relative coordinates, while focused mode walks the
     * active-element chain. The returned chain reuses the recorder contract without producing an event.
     *
     * @param {object} request The normalized request carrying mode and resolved coordinates.
     * @returns {object} The frame result consumed by the background worker's selection stage.
     */
    function newPeekResponse(request) {
        const documentFocused = document.hasFocus();
        const response = {
            chain: null,
            documentFocused,
            found: false,
            frameContext: getFrameContext(),
            pointerObservedAtMilliseconds
        };

        // Reject malformed runtime messages without reading properties through implicit coercion.
        const isRequestObject = request !== null && typeof request === "object";
        const hasRequestMode = isRequestObject && typeof request.mode === "string";

        if (!hasRequestMode) {
            return response;
        }

        let element = null;
        let point = null;
        let trigger = "";

        if (request.mode === "Coordinates") {
            const isXValid = typeof request.x === "number" && Number.isFinite(request.x);
            const isYValid = typeof request.y === "number" && Number.isFinite(request.y);

            if (!isXValid || !isYValid) {
                return response;
            }

            element = resolveElementFromPoint(request.x, request.y);
            point = contract.newPoint({ xPosition: request.x, yPosition: request.y });
            trigger = "Hover";
        }

        if (request.mode === "Focused") {
            element = resolveFocusedElement();
            trigger = "Focus";
        }

        if (request.mode === "Current") {
            if (!isPointerInsideDocument) {
                return response;
            }

            element = resolveElementFromPoint(pointerClientX, pointerClientY);
            point = contract.newPoint({ xPosition: pointerClientX, yPosition: pointerClientY });
            trigger = "Hover";
        }

        // Return an empty frame result when the selected mode has no accessible DOM element.
        if (!element) {
            return response;
        }

        // Map the resolved element through the established recorder contract so REST and events share one shape.
        response.chain = mapper.newChainFromElement({
            element,
            point,
            trigger,
            locatorSettings: getLocatorSettings()
        });
        response.found = true;

        return response;
    }

    /**
     * Receives background-worker requests addressed to this content frame.
     *
     * @param {object} message The runtime message carrying a channel and normalized request.
     * @param {object} sender Browser-owned sender metadata retained for the listener signature.
     * @param {(response: object) => void} sendResponse Callback used to return the synchronous frame result.
     * @returns {boolean} False because the response is completed synchronously.
     */
    function onRuntimeMessage(message, sender, sendResponse) {
        // Ignore unrelated extension messages so existing popup and settings traffic remains isolated.
        const isMessageObject = message !== null && typeof message === "object";
        const isLocatorTestMessage = isMessageObject
            && message.channel === MESSAGE_CHANNELS.locatorTest;
        const isPeekMessage = isMessageObject
            && message.channel === MESSAGE_CHANNELS.peekRequest;

        // Evaluate an on-demand locator test against this frame without recording anything.
        if (isLocatorTestMessage) {
            sendResponse(newLocatorTestResponse(message.request));

            return false;
        }

        if (!isPeekMessage) {
            return false;
        }

        // Resolve against this frame immediately before returning so DOM mutations are reflected in the result.
        sendResponse(newPeekResponse(message.request));

        return false;
    }

    /**
     * Invalidates pointer and hover state when the frame loses browser focus.
     *
     * @returns {void}
     */
    function onWindowBlur() {
        // Reuse the pointer-leave cleanup so a cursor moved outside Chromium cannot leave a stale current target.
        onHoverLeave();
    }

    /**
     * Resolves the deepest active element, through open and closed shadow roots.
     *
     * @remarks
     * Compute-only. Closed shadow roots are entered through the extension API that reads them.
     *
     * @returns {Element|null} The focused element owned by this document, or null when the document lacks focus.
     */
    function resolveFocusedElement() {
        if (!document.hasFocus()) {
            return null;
        }

        let element = document.activeElement;
        let shadowRoot = locator.getShadowRoot(element);

        // Descend through every shadow-root focus boundary, open or closed, to recover the actual
        // interactive control.
        while (shadowRoot) {
            const focusedElement = shadowRoot.activeElement;

            // A root without a focused element is where the focus stops.
            if (!focusedElement) {
                break;
            }

            element = focusedElement;
            shadowRoot = locator.getShadowRoot(element);
        }

        return element && element.nodeType === Node.ELEMENT_NODE
            ? element
            : null;
    }

    /**
     * Resolves the deepest element at viewport-relative coordinates, through open and closed shadow roots.
     *
     * @param {number} clientX The horizontal coordinate in this frame's viewport.
     * @param {number} clientY The vertical coordinate in this frame's viewport.
     * @returns {Element|null} The deepest accessible element at the point.
     */
    function resolveElementFromPoint(clientX, clientY) {
        let element = document.elementFromPoint(clientX, clientY);
        let shadowRoot = locator.getShadowRoot(element);

        // Reapply the same viewport point inside every shadow root, open or closed, until no
        // deeper element is exposed.
        while (shadowRoot) {
            const shadowElement = shadowRoot.elementFromPoint(clientX, clientY);
            const isDeeper = Boolean(shadowElement)
                && shadowElement !== element
                && shadowElement.getRootNode() === shadowRoot;

            // Stop when the root yields nothing of its own: no element, or one outside its tree.
            if (!isDeeper) {
                break;
            }

            // Step into the inner element and continue with the root it may host.
            element = shadowElement;
            shadowRoot = locator.getShadowRoot(element);
        }

        return element && element.nodeType === Node.ELEMENT_NODE
            ? element
            : null;
    }

    /**
     * Tests whether an element is a shadow host of another element, at any depth.
     *
     * @remarks
     * Compute-only helper. Walks out from the inner element one shadow root at a time, so it also
     * recognises the outer host of a control nested several shadow levels deep.
     *
     * @param {Element} hostElement The possible host.
     * @param {Element} innerElement The element that may live inside the host's shadow tree.
     * @returns {boolean} True when the inner element is inside a shadow root the host owns.
     */
    function testShadowHost(hostElement, innerElement) {
        // Returns the host of the shadow tree an element lives in, or null in any other tree.
        const getTreeHost = (element) => {
            const root = element.getRootNode();
            const isShadowTree = Boolean(root) && Boolean(root.host);

            return isShadowTree
                ? root.host
                : null;
        };

        // Step out through every shadow level, comparing each host on the way.
        let currentHost = getTreeHost(innerElement);

        while (currentHost) {
            if (currentHost === hostElement) {
                return true;
            }

            currentHost = getTreeHost(currentHost);
        }

        return false;
    }

    /**
     * Attaches one passive capture-phase listener per catalogued DOM event.
     *
     * @remarks
     * Owns the document event subscriptions for this frame. Capture phase ensures events
     * are seen even when a page stops propagation, and passive listeners guarantee the
     * recorder cannot affect scrolling or default behaviour.
     *
     * @returns {void}
     */
    function startListeners() {
        // Bind the shared handler for every catalogued event name in this frame.
        catalog.getCatalogEntries().forEach((entry) => {
            document.addEventListener(entry.domEventName, onRecordableEvent, {
                capture: true,
                passive: true
            });
        });

        // Track `input` events as a field session so ordinary typing pauses cannot split SendKeys.
        document.addEventListener("input", onTypingInput, {
            capture: true,
            passive: true
        });

        // Commit pending typed text when a field loses focus (fast type-then-click-away).
        document.addEventListener("focusout", onTypingBlur, {
            capture: true,
            passive: true
        });

        // Commit a still-focused field before navigation or window closure destroys this frame.
        globalScope.addEventListener("pagehide", onTypingPageHide, {
            capture: true,
            passive: true
        });

        // Record a MoveMouseCursor when the mouse rests over an element for the dwell; movement
        // restarts the timer and leaving the frame cancels it.
        document.addEventListener("mousemove", onHoverMove, {
            capture: true,
            passive: true
        });

        document.addEventListener("mouseleave", onHoverLeave, {
            capture: true,
            passive: true
        });

        // Record a drag and drop: a press arms it, moving past the threshold or a native drag
        // starts it, and the release or the drop records it. A native drag that ends without a
        // drop is forgotten.
        document.addEventListener("mousedown", onDragPress, {
            capture: true,
            passive: true
        });

        document.addEventListener("mousemove", onDragMove, {
            capture: true,
            passive: true
        });

        document.addEventListener("mouseup", onDragRelease, {
            capture: true,
            passive: true
        });

        document.addEventListener("dragstart", onNativeDragStart, {
            capture: true,
            passive: true
        });

        document.addEventListener("drop", onNativeDrop, {
            capture: true,
            passive: true
        });

        document.addEventListener("dragend", onNativeDragEnd, {
            capture: true,
            passive: true
        });

        // Receive on-demand DOM queries separately from page events so peeking never records an interaction.
        chrome.runtime.onMessage.addListener(onRuntimeMessage);

        // Invalidate pointer ownership when the browser frame loses focus so current lookup cannot return stale state.
        globalScope.addEventListener("blur", onWindowBlur, {
            capture: true,
            passive: true
        });
    }

    /**
     * Reports the recorder hub to the background worker when this is the launcher's bootstrap page.
     *
     * @remarks
     * The launcher opens each freshly launched browser on its own server origin, marked with the
     * bootstrap <meta>. Recognizing that marker here lets the worker connect back to the exact
     * server that launched this browser (derived from this page's origin), instead of the default
     * hub. Only the top document is considered, and the page itself is never recorded.
     *
     * @returns {boolean} True when this was the bootstrap page (so recording must be skipped).
     */
    function tryReportBootstrapHub() {
        // Only the top document can be the bootstrap page; sub-frames never carry the marker.
        if (globalScope.window !== globalScope.window.top) {
            return false;
        }

        // The marker <meta> identifies the launcher's bootstrap page.
        const marker = document.querySelector(`meta[name="${constants.BOOTSTRAP_META_NAME}"]`);

        if (!marker) {
            return false;
        }

        // The hub lives at this server's origin; the launcher opened this page on the server that
        // should receive this browser's events.
        const hubUrl = globalScope.location.origin + constants.SERVER_HUB_PATH;

        chrome.runtime.sendMessage({
            channel: MESSAGE_CHANNELS.setHub,
            hubUrl: hubUrl
        }).catch(() => {
            // The worker will respawn and re-read the persisted hub; nothing to do here.
        });

        return true;
    }

    /**
     * Initializes the content script: load settings, watch changes, then bind listeners.
     *
     * @remarks
     * Owns startup ordering. Listeners are bound only after the first settings load so
     * the very first handled event already respects the user's toggles.
     *
     * @returns {Promise<void>} Resolves once listeners are bound.
     */
    async function initializeContentScript() {
        // On the launcher's bootstrap page, report the hub and skip recording entirely so the
        // bootstrap tab never produces events.
        if (tryReportBootstrapHub()) {
            return;
        }

        // Load the initial settings into the cache used by the synchronous handlers.
        activeSettings = await settings.getSettings();

        // Keep the cache current so toggle changes take effect without a page reload.
        settings.watchSettings((updatedSettings) => {
            activeSettings = updatedSettings;
        });

        // Begin capturing interactions in this frame.
        startListeners();

        // Re-test the locators an earlier load of this page recorded, now that it loaded again.
        if (activeSettings.isLocatorHotTestingEnabled) {
            locatorMonitor.startRevisitTest().catch(() => {
                // Hot testing is an improvement; a failed test leaves the recorded locators as they are.
            });
        }
    }

    // Start the content script for this frame.
    initializeContentScript();
})(globalThis);
