/**
 * Runs the locator lab test in headless Chrome and reports the result.
 *
 * Usage: node run-locator-lab.mjs [--json]
 *
 * The browser is taken from the CHROME_PATH environment variable, or from the usual install
 * locations when it is not set. The process exits with 0 when every required lab case kept its
 * locator through all page changes, and with 1 otherwise.
 */
import * as path from "node:path";

import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import {
    fileURLToPath,
    pathToFileURL
} from "node:url";

// The usual Chrome locations, tried in order when CHROME_PATH is not set.
const DEFAULT_CHROME_PATHS = [
    String.raw`C:\Program Files\Google\Chrome\Application\chrome.exe`,
    String.raw`C:\Program Files (x86)\Google\Chrome\Application\chrome.exe`,
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
];

// How long the page gets, in virtual time, to load the lab frame and finish the test.
const VIRTUAL_TIME_BUDGET_MILLISECONDS = 10000;

// The most output accepted from the browser. The dumped document holds the lab markup and every
// locator of every case, which does not fit the default buffer of a child process.
const MAXIMUM_OUTPUT_BYTES = 64 * 1024 * 1024;

// Locates the JSON the test page writes into its result element. Each open-ended part stops at
// the one character that ends it ("[^>]*" at ">", "[^<]*" at "<"), so the pattern never rescans
// text, and the document it runs on is the dump of this project's own test page.
const RESULT_PATTERN = /<script type="application\/json" id="lab-results"[^>]*>([^<]*)<\/script>/;

/**
 * Runs the test page in headless Chrome and returns the parsed result.
 *
 * @remarks
 * Owns one short-lived browser process. File access between local documents is enabled so the
 * test page can reach into the lab frame and load the extension modules from the source tree.
 *
 * @returns {object} The lab test result.
 * @throws {Error} When the page did not publish a result.
 */
function readLabResult() {
    // Resolve the test page next to this script, as a file URL the browser can open.
    const testDirectory = path.dirname(fileURLToPath(import.meta.url));
    const testPagePath = path.join(testDirectory, "locator-lab.test.html");
    const testPageUrl = pathToFileURL(testPagePath).href;

    // Run the page to the end of its virtual time budget and take the final document as text.
    const chromeArguments = [
        "--headless=new",
        "--disable-gpu",
        "--no-first-run",
        "--allow-file-access-from-files",
        `--virtual-time-budget=${VIRTUAL_TIME_BUDGET_MILLISECONDS}`,
        "--dump-dom",
        testPageUrl
    ];
    const dumpedDocument = execFileSync(resolveChromePath(), chromeArguments, {
        encoding: "utf8",
        maxBuffer: MAXIMUM_OUTPUT_BYTES,
        stdio: ["ignore", "pipe", "ignore"]
    });

    // The page publishes the JSON URI-encoded, so a locator holding "<" or "&" cannot end the
    // capture early or be altered by the HTML serializer.
    const resultText = RESULT_PATTERN.exec(dumpedDocument)?.[1] ?? "";
    const decodedText = decodeURIComponent(resultText);

    // An empty result means the page never finished, which is a failure of the run itself.
    if (decodedText.length === 0) {
        throw new Error("The lab test page did not publish a result.");
    }

    return JSON.parse(decodedText);
}

/**
 * Resolves the Chrome executable to run the test with.
 *
 * @returns {string} The executable path.
 * @throws {Error} When no Chrome executable is found.
 */
function resolveChromePath() {
    // An explicit CHROME_PATH wins; the usual install locations follow in order.
    const candidatePaths = [process.env.CHROME_PATH, ...DEFAULT_CHROME_PATHS].filter(Boolean);
    const chromePath = candidatePaths.find((candidatePath) => existsSync(candidatePath));

    // Fail with the remedy, since the test cannot run without a browser.
    if (!chromePath) {
        throw new Error("Chrome was not found. Set CHROME_PATH to the browser executable.");
    }

    return chromePath;
}

/**
 * Writes the result as a readable table.
 *
 * @param {object} result The lab test result.
 * @returns {void}
 */
function writeReport(result) {
    // Tells whether a record kept one of its two locators through every page change.
    const testSurvival = (record, resultName) => {
        const changeResults = Object.values(record.changes);

        return changeResults.every((changeResult) => changeResult[resultName]);
    };

    // A run that could not start has no cases to tabulate; report why and stop.
    if (result.error) {
        console.error(`Lab test failed to run: ${result.error}`);

        return;
    }

    // One row per case: did the locator and the legacy absolute path survive every change?
    const rows = result.cases.map((record) => {
        const survivedText = testSurvival(record, "isLocatorFound")
            ? "yes"
            : "NO";
        const legacyText = testSurvival(record, "isLegacyFound")
            ? "yes"
            : "no";

        return {
            case: record.caseName,
            strength: record.strength,
            strategy: record.strategy,
            fallbacks: record.fallbackCount,
            survived: survivedText,
            legacy: legacyText,
            locator: record.locator
        };
    });

    console.table(rows);

    // Word the summary values first, so each line below reads as one plain sentence.
    const { summary } = result;
    const shadowText = summary.isShadowPassed
        ? "yes"
        : "NO";
    const failedCasesText = summary.failedCases.length === 0
        ? "none"
        : summary.failedCases.join(", ");
    const verdictText = result.isPassed
        ? "PASSED"
        : "FAILED";

    console.log(`Cases: ${summary.caseCount}`);
    console.log(`Locators that survived every page change: ${summary.locatorSurvivorCount}`);
    console.log(`Legacy absolute paths that survived every page change: ${summary.legacySurvivorCount}`);
    console.log(`Strong locators: ${summary.strongCount}`);
    console.log(`Shadow path survived: ${shadowText}`);
    console.log(`Failed required cases: ${failedCasesText}`);
    console.log(verdictText);
}

// Run the lab once; everything below only reports what it returned.
const result = readLabResult();
const isJsonRequested = process.argv.includes("--json");

// Print the raw result for tooling, or the readable table for a person.
if (isJsonRequested) {
    console.log(JSON.stringify(result, null, 2));
} else {
    writeReport(result);
}

// Report the outcome through the exit code, so a build step can gate on it.
process.exitCode = result.isPassed
    ? 0
    : 1;
