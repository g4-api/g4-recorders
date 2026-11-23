/**
 * Locator lab test for the G4 Chromium Recorder extension.
 *
 * Proves that recorded locators survive the page changes that usually break a replay. The test
 * loads the lab page (wwwroot/recorder-locator-lab.html) in a frame, injects the extension's
 * own locator modules into it, records a locator for every lab case, and then changes the page
 * step by step: an inserted banner, an extra wrapper, reordered rows, regenerated ids and a full
 * re-render. After every change each locator must still resolve to exactly one element, and that
 * element must be the same logical control.
 *
 * The absolute XPath of the raw event target (what the recorder produced before the candidate
 * engine existed) is tracked next to it, so the result shows what the engine actually buys.
 *
 * @remarks
 * Run through run-locator-lab.mjs, which starts headless Chrome with file access between local
 * documents enabled and reads the JSON this script writes into the page.
 */
(function initializeLocatorLabTest(globalScope) {
    // The extension modules under test, in their manifest order.
    const MODULE_PATHS = [
        "../G4.Recorders.Chromium.Extension/lib/dom-locator.js",
        "../G4.Recorders.Chromium.Extension/lib/dom-accessibility.js",
        "../G4.Recorders.Chromium.Extension/lib/locator-candidates.js"
    ];

    // The locator settings the extension ships with (constants.js DEFAULT_SETTINGS).
    const LOCATOR_SETTINGS = {
        maximumTextLength: 80,
        preference: "UserFacing",
        testHookAttributes: [
            "data-testid", "data-test-id", "data-test", "data-qa", "data-cy", "data-automation-id"
        ]
    };

    // The page changes, applied cumulatively in this order through the lab's own functions.
    const PAGE_CHANGES = [
        { name: "InsertBanner", functionName: "addBanner" },
        { name: "WrapContent", functionName: "addWrapper" },
        { name: "ShuffleRows", functionName: "switchRowOrder" },
        { name: "RegenerateIds", functionName: "updateGeneratedIdentifiers" },
        { name: "RenderAgain", functionName: "showLab" }
    ];

    /**
     * Loads one extension module into the lab frame.
     *
     * @remarks
     * Owns one script element and its two temporary listeners. The module runs inside the lab
     * frame, exactly as a content script runs inside a recorded page.
     *
     * @param {Document} labDocument The lab frame's document.
     * @param {string} modulePath The module path, relative to this test page.
     * @returns {Promise<void>} Resolves once the module has run.
     */
    function addModule(labDocument, modulePath) {
        return new Promise((resolve, reject) => {
            // Settle the promise from the script's own lifecycle: it either runs or fails to load.
            const onModuleLoaded = () => resolve();
            const onModuleFailed = () => reject(new Error(`Failed to load ${modulePath}`));

            // Resolve the path against this test page, because the lab frame lives in another folder.
            const script = labDocument.createElement("script");

            script.src = new URL(modulePath, globalScope.location.href).href;

            script.addEventListener("load", onModuleLoaded);
            script.addEventListener("error", onModuleFailed);

            // Attaching the script starts the load.
            labDocument.head.append(script);
        });
    }

    /**
     * Records the locators of every lab case in its initial state.
     *
     * @remarks
     * Compute-only over the lab document. A case that marks a raw inner node starts from that
     * node and is retargeted the way a real click is, which also asserts that retargeting lands
     * on the marked control.
     *
     * @param {Window} labWindow The lab frame's window, with the extension modules loaded.
     * @returns {object[]} One record per case.
     */
    function getCaseRecords(labWindow) {
        const { accessibility, locator, locatorCandidates } = labWindow.g4Recorder;
        const labDocument = labWindow.document;
        const caseElements = Array.from(labDocument.querySelectorAll("[data-g4-case]"));

        return caseElements.map((caseElement) => {
            // Start from the node a real click lands on, then resolve the control it belongs to.
            const caseName = caseElement.dataset.g4Case;
            const rawElement = labDocument.querySelector(`[data-g4-raw="${caseName}"]`) || caseElement;
            const actionElement = accessibility.resolveActionableElement(rawElement);

            // Build the locators exactly as the extension does for an event on that control.
            const result = locatorCandidates.getLocatorCandidates({
                element: actionElement,
                settings: LOCATOR_SETTINGS
            });

            // Name the strategy behind the primary locator, for the report.
            const primaryCandidate = result.locators.find(
                (candidate) => candidate.value === result.locator
            );
            const strategy = primaryCandidate
                ? primaryCandidate.strategy
                : "";

            return {
                caseName,
                changes: {},
                fallbackCount: result.locators.length - 1,
                isExpectedWeak: caseElement.dataset.g4Expect === "weak",
                isRetargeted: actionElement === caseElement,
                legacyLocator: locator.getAbsoluteXpath(rawElement),
                locator: result.locator,
                locators: result.locators,
                strategy,
                strength: result.locatorStrength
            };
        });
    }

    /**
     * Records the shadow-DOM case, which is described by a shadow path instead of one locator.
     *
     * @param {Window} labWindow The lab frame's window.
     * @returns {object} The shadow record ({ changes, shadowPath, strength }).
     */
    function getShadowRecord(labWindow) {
        // Reach the button through its host, since a document query cannot see into a shadow tree.
        const { locatorCandidates } = labWindow.g4Recorder;
        const shadowHost = labWindow.document.getElementById("lab-shadow-host");
        const shadowButton = shadowHost.shadowRoot.querySelector("button");

        // Build its result exactly as the extension does for an event inside a shadow root.
        const result = locatorCandidates.getLocatorCandidates({
            element: shadowButton,
            settings: LOCATOR_SETTINGS
        });

        return { changes: {}, shadowPath: result.shadowPath, strength: result.locatorStrength };
    }

    /**
     * Runs the whole lab test and returns its result.
     *
     * @remarks
     * Owns the lab page's state for the duration of the run: every page change is applied
     * through the lab's own functions and stays applied, so the last check runs against a page
     * that has been changed in all five ways at once.
     *
     * @param {Window} labWindow The lab frame's window, with the extension modules loaded.
     * @returns {object} The result ({ cases, isPassed, shadow, summary }).
     */
    function invokeLabTest(labWindow) {
        // Tells whether a record kept one of its two locators through every page change.
        const testSurvival = (record, resultName) => {
            const changeResults = Object.values(record.changes);

            return changeResults.every((changeResult) => changeResult[resultName]);
        };

        // Record every case in the initial state of the page, before anything changes.
        const caseRecords = getCaseRecords(labWindow);
        const shadowRecord = getShadowRecord(labWindow);

        // Apply each page change, then re-test every recorded locator against the changed page.
        PAGE_CHANGES.forEach((pageChange) => {
            labWindow.g4LocatorLab[pageChange.functionName]();

            caseRecords.forEach((record) => {
                // The legacy path was recorded for the raw node, so it is judged against the raw
                // marker when the case has one, and against the case marker otherwise.
                const rawSelector = `[data-g4-raw="${record.caseName}"]`;
                const isRawNodeMarked = labWindow.document.querySelector(rawSelector) !== null;
                const legacyAttributeName = isRawNodeMarked
                    ? "data-g4-raw"
                    : "data-g4-case";

                record.changes[pageChange.name] = {
                    isLegacyFound: testLocator({
                        attributeName: legacyAttributeName,
                        caseName: record.caseName,
                        labWindow,
                        value: record.legacyLocator
                    }),
                    isLocatorFound: testLocator({
                        attributeName: "data-g4-case",
                        caseName: record.caseName,
                        labWindow,
                        value: record.locator
                    })
                };
            });

            shadowRecord.changes[pageChange.name] = testShadowPath(labWindow, shadowRecord.shadowPath);
        });

        // A case passes when its locator survived every change and it was retargeted correctly.
        // Cases marked as expected weak are reported but never fail the run.
        const requiredRecords = caseRecords.filter((record) => !record.isExpectedWeak);
        const failedRecords = requiredRecords.filter((record) => {
            const isLocatorSurvivor = testSurvival(record, "isLocatorFound");
            const isCasePassed = record.isRetargeted && isLocatorSurvivor;

            return !isCasePassed;
        });
        const isShadowPassed = Object.values(shadowRecord.changes).every(Boolean);
        const isPassed = failedRecords.length === 0 && isShadowPassed;

        // Count the survivors of both locator kinds, so the report shows what the engine buys.
        const legacySurvivors = caseRecords.filter((record) => testSurvival(record, "isLegacyFound"));
        const locatorSurvivors = caseRecords.filter((record) => testSurvival(record, "isLocatorFound"));
        const strongRecords = caseRecords.filter((record) => record.strength === "Strong");

        return {
            cases: caseRecords,
            isPassed,
            shadow: shadowRecord,
            summary: {
                caseCount: caseRecords.length,
                failedCases: failedRecords.map((record) => record.caseName),
                isShadowPassed,
                legacySurvivorCount: legacySurvivors.length,
                locatorSurvivorCount: locatorSurvivors.length,
                strongCount: strongRecords.length
            }
        };
    }

    /**
     * Runs the test once the lab frame has loaded and publishes the result into the page.
     *
     * @remarks
     * Starts on the window load event, which fires after the lab frame has rendered. Owns the
     * result element: it always receives JSON, a result or an error, so the runner never has to
     * guess why a run produced nothing.
     *
     * @returns {Promise<void>} Resolves once the result is written.
     */
    async function onLabLoaded() {
        // Resolve the element the runner reads and the frame that holds the lab.
        const resultElement = globalScope.document.getElementById("lab-results");
        const labWindow = globalScope.document.getElementById("lab-frame").contentWindow;

        try {
            // Load the modules one after another, because each one builds on the previous.
            for (const modulePath of MODULE_PATHS) {
                await addModule(labWindow.document, modulePath);
            }

            // Run the test and publish its result where the runner reads it.
            resultElement.textContent = encodeURIComponent(JSON.stringify(invokeLabTest(labWindow)));
        } catch (error) {
            // Publish the failure in the same place, so the runner reports it instead of nothing.
            const message = error instanceof Error
                ? error.message
                : "Unknown lab test failure.";

            resultElement.textContent = encodeURIComponent(JSON.stringify({ error: message, isPassed: false }));
        }
    }

    /**
     * Tests whether a locator still resolves to exactly the expected logical element.
     *
     * @param {object} options Test inputs ({ attributeName, caseName, labWindow, value }).
     * @returns {boolean} True when exactly one element matches and it carries the case marker.
     */
    function testLocator(options) {
        const { attributeName, caseName, labWindow, value } = options;

        // Resolve the locator with the extension's own evaluator, the way a replay would.
        const matches = labWindow.g4Recorder.locator.findElements({
            root: labWindow.document,
            using: "Xpath",
            value
        });

        // Anything but a single match is a failure: a locator must address one element.
        if (matches.length !== 1) {
            return false;
        }

        return matches[0].getAttribute(attributeName) === caseName;
    }

    /**
     * Tests whether the recorded shadow path still leads to the shadow button.
     *
     * @param {Window} labWindow The lab frame's window.
     * @param {object[]} shadowPath The recorded path (host XPath, then one CSS selector per level).
     * @returns {boolean} True when the path resolves to the marked shadow button.
     */
    function testShadowPath(labWindow, shadowPath) {
        // The first segment must resolve to one host that still carries a shadow tree.
        const hosts = labWindow.g4Recorder.locator.findElements({
            root: labWindow.document,
            using: "Xpath",
            value: shadowPath[0].value
        });
        const isHostResolved = hosts.length === 1 && Boolean(hosts[0].shadowRoot);

        if (!isHostResolved) {
            return false;
        }

        // The second segment must resolve to one element inside that tree: the marked button.
        const innerMatches = hosts[0].shadowRoot.querySelectorAll(shadowPath[1].value);

        if (innerMatches.length !== 1) {
            return false;
        }

        return innerMatches[0].dataset.g4Case === "shadow-button";
    }

    // Start when the whole page, including the lab frame, has loaded.
    globalScope.addEventListener("load", onLabLoaded);
})(globalThis);
