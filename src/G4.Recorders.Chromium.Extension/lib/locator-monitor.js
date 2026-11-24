/**
 * Locator hot testing for the G4 Chromium Recorder extension.
 *
 * Keeps testing the locators of recorded elements while the recording goes on, so a locator is
 * backed by evidence and not only by the moment it was written:
 *   - the settle test re-checks a locator after the recorded action and the page's reaction;
 *   - the revisit test re-checks every locator recorded on a page when that page loads again,
 *     which is the closest thing to a replay the recorder can observe.
 * A changed verdict is reported to the background worker, which corrects the already delivered
 * event and relays the correction to the hub as a locator amendment.
 *
 * @remarks
 * Hot testing only reads the page: it evaluates locators and observes mutations, and it never
 * dispatches an event, focuses an element, or changes the DOM.
 *
 * Attached to the shared `globalThis.g4Recorder` namespace (see constants.js). Loaded as a
 * content script in every frame, after the candidate engine.
 */
(function initializeLocatorMonitorModule(globalScope) {
    // Create or reuse the single extension namespace shared across every context.
    const namespace = globalScope.g4Recorder = globalScope.g4Recorder || {};

    // Pull the sibling modules this monitor composes; all are loaded earlier in the manifest.
    const { constants, locatorCandidates } = namespace;
    const { MESSAGE_CHANNELS } = constants;

    // How long the DOM must stay unchanged before the page counts as settled.
    const SETTLE_QUIET_MILLISECONDS = 400;

    // The longest a settle test waits for a page that never stops changing (animations, tickers).
    const SETTLE_MAXIMUM_MILLISECONDS = 2500;

    // How many settle tests may wait at once. Each one observes the document, so a burst of
    // actions beyond this simply skips the test instead of stacking observers on the page.
    const MAXIMUM_ACTIVE_WATCHES = 5;

    // The reason carried by a verdict, telling the consumer which test produced it.
    const VERDICT_REASONS = Object.freeze({
        revisit: "Revisit",
        settle: "Settle"
    });

    // Identifies this document instance. A revisit test only applies to locators recorded by an
    // earlier instance of the page, never to the ones recorded in this very document. The value
    // is per frame because every frame runs its own isolated copy of this module.
    const documentId = globalScope.crypto.randomUUID();

    // The number of settle tests currently waiting, kept at module level because the limit
    // spans every recorded action of this frame.
    let activeWatchCount = 0;

    /**
     * Returns the identity of this document instance.
     *
     * @returns {string} The per-document identifier.
     */
    function getDocumentId() {
        return documentId;
    }

    /**
     * Returns the key that tells whether two documents are loads of the same page.
     *
     * @remarks
     * Compute-only. The hash is left out because it does not cause a new document; the query is
     * kept because it usually selects different content.
     *
     * @returns {string} The page key of this document.
     */
    function getPageKey() {
        const { origin, pathname, search } = globalScope.location;

        return `${origin}${pathname}${search}`;
    }

    /**
     * Runs the revisit test for the locators an earlier load of this page recorded.
     *
     * @remarks
     * Owns one request to the background worker and, when it returns records, one wait for the
     * page to settle. Each record whose element is found again reports its changed verdicts; a
     * record whose element is not found is left alone, because the page is in another state.
     * Failures are swallowed: hot testing must never disturb the recording.
     *
     * @returns {Promise<void>} Resolves once every record has been tested.
     */
    async function startRevisitTest() {
        // Ask the worker for the locators recorded on this page by an earlier document.
        let records = [];

        try {
            const response = await chrome.runtime.sendMessage({
                channel: MESSAGE_CHANNELS.locatorRevisit,
                documentId,
                pageKey: getPageKey()
            });

            records = Array.isArray(response?.records)
                ? response.records
                : [];
        } catch {
            // The worker is asleep or reloading; a later page load will test again.
            return;
        }

        // A first visit has nothing to re-test, so the page is left unobserved.
        if (records.length === 0) {
            return;
        }

        // Let late content arrive first, so an element that renders after load is not missed.
        await waitForSettledDocument();

        // Test each record and report only the ones whose verdicts changed.
        records.forEach((record) => {
            const outcome = locatorCandidates.testLocatorsOnRevisit({
                fingerprint: record.fingerprint,
                locators: record.locators,
                root: globalScope.document
            });
            const isReportable = outcome.isElementPresent && outcome.isChanged;

            if (isReportable) {
                sendVerdict({
                    locatorId: record.locatorId,
                    locators: outcome.locators,
                    reason: VERDICT_REASONS.revisit
                });
            }
        });
    }

    /**
     * Schedules the settle test of a freshly recorded element.
     *
     * @remarks
     * Owns one settle wait. When the page has reacted to the action and the element is still
     * attached, its locators are re-tested and upgraded verdicts are reported. An element that
     * left the page (a closed menu, a navigation) is not tested: its absence says nothing about
     * the locators.
     *
     * @param {object} options Watch inputs.
     * @param {Element} options.element The recorded element.
     * @param {string} options.locatorId The identifier the event carries for its locators.
     * @param {object[]} options.locators The locators recorded for the element.
     * @returns {Promise<void>} Resolves once the test has run or was skipped.
     */
    async function watchLocator(options) {
        const { element, locatorId, locators } = options;

        // Skip the test when too many are already waiting, so observers never pile up.
        if (activeWatchCount >= MAXIMUM_ACTIVE_WATCHES) {
            return;
        }

        // Wait for the page's reaction to the action while holding one watch slot.
        activeWatchCount += 1;

        try {
            await waitForSettledDocument();
        } finally {
            activeWatchCount -= 1;
        }

        // An element that left the page cannot be tested.
        if (!element.isConnected) {
            return;
        }

        // Re-test the locators against the settled page and report only upgraded verdicts.
        const outcome = locatorCandidates.testLocators({ element, locators });

        if (outcome.isChanged) {
            sendVerdict({ locatorId, locators: outcome.locators, reason: VERDICT_REASONS.settle });
        }
    }

    /**
     * Reports re-tested locators of one recorded element to the background worker.
     *
     * @remarks
     * Owns the messaging boundary of hot testing. The locators are ranked again before they are
     * sent, so the worker and the hub receive a complete, ordered replacement. A missing receiver
     * is ignored: the verdict is an improvement, not a required part of the recording.
     *
     * @param {object} options Verdict inputs ({ locatorId, locators, reason }).
     * @returns {void}
     */
    function sendVerdict(options) {
        const { locatorId, locators, reason } = options;

        // Rank again with the new verdicts, so a broken locator can no longer be the primary one.
        const ranking = locatorCandidates.getLocatorRanking(locators);

        // Hand the complete replacement to the worker, which corrects the event it belongs to.
        chrome.runtime.sendMessage({
            channel: MESSAGE_CHANNELS.locatorVerdict,
            locatorId,
            locator: ranking.locator,
            locators: ranking.locators,
            locatorStrength: ranking.locatorStrength,
            pageKey: getPageKey(),
            reason
        }).catch(() => {
            // The worker will respawn on the next event; nothing to do here.
        });
    }

    /**
     * Waits until the document stops changing, or a maximum time passes.
     *
     * @remarks
     * Owns one temporary MutationObserver and two timers, all released before the promise
     * resolves. Every mutation restarts the quiet timer; the maximum timer guarantees the wait
     * ends on pages that never stop changing.
     *
     * @returns {Promise<void>} Resolves when the document is settled.
     */
    function waitForSettledDocument() {
        return new Promise((resolve) => {
            // The temporary resources of this wait, declared first so the callbacks can release them.
            let quietTimerId = null;
            let maximumTimerId = null;
            let observer = null;

            // Release the observer and both timers, then report the document as settled.
            const onSettled = () => {
                clearTimeout(quietTimerId);
                clearTimeout(maximumTimerId);
                observer.disconnect();
                resolve();
            };

            // Restart the quiet period whenever the page changes again.
            const onMutated = () => {
                clearTimeout(quietTimerId);
                quietTimerId = setTimeout(onSettled, SETTLE_QUIET_MILLISECONDS);
            };

            // Observe the whole document, since the reaction to an action can land anywhere.
            observer = new MutationObserver(onMutated);
            observer.observe(globalScope.document, {
                attributes: true,
                characterData: true,
                childList: true,
                subtree: true
            });

            // Arm both timers: the quiet one settles a calm page, the maximum one ends the wait
            // on a page that never stops changing.
            quietTimerId = setTimeout(onSettled, SETTLE_QUIET_MILLISECONDS);
            maximumTimerId = setTimeout(onSettled, SETTLE_MAXIMUM_MILLISECONDS);
        });
    }

    // Expose the monitor API on the shared namespace.
    namespace.locatorMonitor = {
        getDocumentId,
        getPageKey,
        startRevisitTest,
        watchLocator
    };
})(globalThis);
