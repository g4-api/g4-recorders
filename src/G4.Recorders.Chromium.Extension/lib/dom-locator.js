/**
 * DOM locator primitives for the G4 Chromium Recorder extension.
 *
 * Holds the low-level building blocks every locator feature shares: the absolute XPath (the
 * always-available floor locator), the structural CSS selector, safe XPath and CSS literals,
 * the detector for generated attribute values, and one evaluator for the five W3C WebDriver
 * locator strategies (named as G4 names them: Xpath, CssSelector, LinkText, PartialLinkText,
 * TagName). The ranked, user-facing candidates are built on top of these in
 * locator-candidates.js.
 *
 * @remarks
 * Every locator is scoped to the element's own tree. Inside an iframe the path is relative to
 * that frame's document, and inside a shadow root only CSS can address the element, because
 * XPath cannot cross a shadow boundary. Frame and shadow context are recorded separately.
 *
 * Attached to the shared `globalThis.g4Recorder` namespace (see constants.js). Loaded
 * as a content script in every frame.
 */
(function initializeDomLocatorModule(globalScope) {
    // Create or reuse the single extension namespace shared across every context.
    const namespace = globalScope.g4Recorder = globalScope.g4Recorder || {};

    // Characters that must be backslash-escaped inside a CSS selector when the platform
    // CSS.escape function is unavailable. Declared once so the fallback escaper does not
    // execute an inline literal regex on every call.
    const CSS_SPECIAL_CHARACTER_PATTERN = /([ !"#$%&'()*+,.\/:;<=>?@\[\\\]^`{|}~])/g;

    // The namespace of ordinary HTML elements. Only these can be named directly in an XPath
    // step of an HTML document; SVG and MathML elements need a local-name() test instead.
    const HTML_NAMESPACE = "http://www.w3.org/1999/xhtml";

    // The locator strategy names, spelled as the G4 engine spells them. They are the five
    // strategies of the W3C WebDriver specification; no other strategy is ever emitted.
    const LOCATOR_STRATEGIES = Object.freeze({
        cssSelector: "CssSelector",
        linkText: "LinkText",
        partialLinkText: "PartialLinkText",
        tagName: "TagName",
        xpath: "Xpath"
    });

    // Separates the segments of a shadow path inside one CSS selector: the first segment is found
    // in the document and every following one inside the shadow root of the element before it.
    // The token is not valid CSS, so it can never collide with a real selector. The G4 engine
    // splits a CssSelector rule on the same token, with the same quoting rules.
    const SHADOW_PATH_SEPARATOR = ">>>";

    // Values longer than this are never stable handles; they are data, not identity. The
    // generated-value patterns below only ever run on values within this length, which bounds
    // the work of their open-ended repetitions.
    const MAXIMUM_STABLE_VALUE_LENGTH = 64;

    // A single decimal digit, a hexadecimal letter and any letter. A token that mixes digits
    // with letters is a counter or a hash, while a token made of one kind is an authored word.
    const DIGIT_PATTERN = /\d/;
    const HEX_LETTER_PATTERN = /[a-f]/i;
    const LETTER_PATTERN = /[a-z]/i;

    // A run of this many digits marks a value as generated (a row key, a counter, a timestamp),
    // while short numbers such as "step-2" or "address2" stay usable.
    const GENERATED_DIGIT_RUN_PATTERN = /\d{3,}/;

    // Identifier prefixes that UI frameworks put in front of a per-render counter. A value that
    // starts with one of them and also holds a digit changes between page loads. Each entry is
    // a regular-expression fragment, so a prefix may hold a character class such as "yui[_-]".
    const GENERATED_PREFIXES = [
        "ember", "ext-gen", "ext-comp", "gwt-uid", "yui[_-]", "mat-", "cdk-", "ng-", "radix-",
        "headlessui-", "react-select-", "react-aria", "rc[_-]", "mui-", "downshift-", "el-id-",
        "uid-", "floating-ui", "tippy-", "__"
    ];

    // Matches a value that starts with one of the generated prefixes. It is an anchored
    // alternation of fixed fragments, so it is decided in one pass from the first character.
    const GENERATED_PREFIX_PATTERN = new RegExp(`^(?:${GENERATED_PREFIXES.join("|")})`, "i");

    // The shapes React gives the ids of its useId hook (":r1:", "«r1»", "_r_1_").
    const GENERATED_REACT_ID_PATTERN = /^(?::r|«r|_r_)/i;

    // A long hexadecimal token, the usual shape of a hash or a GUID fragment.
    const GENERATED_HEX_TOKEN_PATTERN = /[0-9a-f]{8,}/i;

    // The last dash or underscore separated part of a value, where build tools append a hash.
    // The separator is not part of the repeated set, so a failed attempt never rescans its text.
    const GENERATED_TAIL_PATTERN = /[-_]([a-z0-9]{5,})$/i;

    // Tag names that are safe to write directly as an XPath step. Any other name (a colon,
    // an unusual character) is addressed through local-name() so the expression always parses.
    const PLAIN_XPATH_NAME_PATTERN = /^[a-z][a-z0-9._-]*$/;

    // The whitespace XPath's normalize-space() collapses: space, tab, carriage return and line
    // feed only. A non-breaking space is kept, so the JavaScript mirror must keep it too.
    const XPATH_WHITESPACE_PATTERN = /[ \t\r\n]+/g;

    /**
     * Finds the elements a locator resolves to inside one tree.
     *
     * @remarks
     * Compute-only. This is the single evaluator behind candidate verification, hot testing and
     * the on-demand locator test, so all three agree on what a locator matches. It mirrors the
     * W3C WebDriver algorithms: link text compares the trimmed rendered text of anchors, partial
     * link text tests containment, and XPath is evaluated from the document. A CSS selector may
     * be a shadow path, which is walked one shadow root per segment. An invalid or
     * unsupported locator yields an empty list instead of throwing, because a broken candidate
     * must simply lose, never fail the recording.
     *
     * @param {object} options Search inputs.
     * @param {Document|ShadowRoot} options.root The tree to search.
     * @param {string} options.using The strategy name, one of LOCATOR_STRATEGIES.
     * @param {string} options.value The locator text.
     * @returns {Element[]} The matched elements in document order.
     */
    function findElements(options) {
        const { root, using, value } = options;

        // Without a tree or a locator text there is nothing to evaluate.
        const isSearchable = Boolean(root) && typeof value === "string" && value.length > 0;

        if (!isSearchable) {
            return [];
        }

        // Evaluate inside a guard so a syntax error in one locator never reaches the caller.
        try {
            if (using === LOCATOR_STRATEGIES.xpath) {
                return findElementsByXpath(root, value);
            }

            if (using === LOCATOR_STRATEGIES.cssSelector) {
                return findElementsByCss(root, value);
            }

            if (using === LOCATOR_STRATEGIES.tagName) {
                return Array.from(root.querySelectorAll(getCssEscaped(value)));
            }

            // Both link strategies consider anchors only and compare their rendered text.
            const anchors = Array.from(root.querySelectorAll("a"));

            if (using === LOCATOR_STRATEGIES.linkText) {
                return anchors.filter((anchor) => getRenderedText(anchor) === value);
            }

            if (using === LOCATOR_STRATEGIES.partialLinkText) {
                return anchors.filter((anchor) => getRenderedText(anchor).includes(value));
            }
        } catch {
            // An invalid expression matches nothing; the caller treats that as a lost candidate.
            return [];
        }

        return [];
    }

    /**
     * Builds an absolute XPath for an element, from the root of its tree down to it.
     *
     * @remarks
     * Compute-only. Each step is a node test plus a 1-based index among the siblings that pass
     * the same test. SVG and MathML elements are addressed through local-name(), because a plain
     * name step never matches them in an HTML document. For an element inside a shadow root the
     * path stops at that root, so it describes the position inside the shadow tree only.
     *
     * @param {Element} element The element to locate.
     * @returns {string} The absolute XPath, or an empty string for a non-element node.
     */
    function getAbsoluteXpath(element) {
        // Guard against non-element inputs so callers can pass any event target safely.
        if (!testElementNode(element)) {
            return "";
        }

        // Accumulate path steps from the element upward, then reverse for a root-down path.
        const pathSteps = [];
        let currentElement = element;

        while (testElementNode(currentElement)) {
            pathSteps.push(getXpathStep(currentElement));

            // Climb to the parent element; stop once we leave the element tree.
            currentElement = currentElement.parentElement;
        }

        // Reverse the collected steps so the path reads from the root down.
        pathSteps.reverse();

        return `/${pathSteps.join("/")}`;
    }

    /**
     * Builds a CSS attribute selector that matches one exact attribute value.
     *
     * @remarks
     * Compute-only. The value is written as a quoted string with its backslashes and quotes
     * escaped, which is valid for any text and never depends on identifier escaping rules.
     *
     * @param {string} attributeName The attribute name, already known to be a plain name.
     * @param {string} attributeValue The exact value to match.
     * @returns {string} The selector, for example `[data-test-id="save"]`.
     */
    function getCssAttributeSelector(attributeName, attributeValue) {
        // A CSS string needs each backslash doubled and each double quote escaped. Backslashes
        // go first, so the ones added in front of the quotes are not doubled again. The search
        // for a single backslash keeps its escaped literal, because a raw string cannot hold one.
        const escapedValue = attributeValue
            .replaceAll("\\", String.raw`\\`)
            .replaceAll('"', String.raw`\"`);

        return `[${attributeName}="${escapedValue}"]`;
    }

    /**
     * Escapes a string for safe use inside a CSS selector.
     *
     * @remarks
     * Compute-only helper. Uses the native CSS.escape when available and falls back to
     * escaping the characters that most commonly break id selectors otherwise.
     *
     * @param {string} value The raw value to escape.
     * @returns {string} The escaped value.
     */
    function getCssEscaped(value) {
        // Prefer the platform escaper, which handles the full CSS grammar correctly.
        const isNativeEscapeAvailable = typeof globalScope.CSS !== "undefined"
            && typeof globalScope.CSS.escape === "function";

        if (isNativeEscapeAvailable) {
            return globalScope.CSS.escape(value);
        }

        // Fallback: put a backslash in front of each character that would break a selector.
        return value.replaceAll(CSS_SPECIAL_CHARACTER_PATTERN, String.raw`\$1`);
    }

    /**
     * Builds a structural CSS selector for an element inside its own tree.
     *
     * @remarks
     * Compute-only. Anchors on the nearest id that is unique and not generated, otherwise walks
     * up building `tag:nth-of-type(n)` steps to the root of the tree. A generated id is skipped
     * on purpose: it would make the selector unique today and broken on the next page load.
     *
     * @param {Element} element The element to locate.
     * @returns {string} The CSS selector, or an empty string for a non-element node.
     */
    function getCssSelector(element) {
        // Guard against non-element inputs so callers can pass any event target safely.
        if (!testElementNode(element)) {
            return "";
        }

        // Accumulate selector steps from the element upward, then reverse for a top-down selector.
        const selectorSteps = [];
        let currentElement = element;

        while (testElementNode(currentElement)) {
            // A unique, stable id lets us anchor the selector and stop climbing immediately.
            const isIdUsable = Boolean(currentElement.id)
                && !testGeneratedValue(currentElement.id)
                && testUniqueId(currentElement);

            if (isIdUsable) {
                selectorSteps.push(`#${getCssEscaped(currentElement.id)}`);
                break;
            }

            // Without a usable id, qualify the tag by its position among same-tag siblings.
            const tagName = getCssEscaped(currentElement.localName);
            const siblingIndex = getSameTagIndex(currentElement);

            selectorSteps.push(`${tagName}:nth-of-type(${siblingIndex})`);

            // Climb to the parent element; stop once we leave the element tree.
            currentElement = currentElement.parentElement;
        }

        // Reverse the collected steps so the selector reads from the ancestor down.
        selectorSteps.reverse();

        return selectorSteps.join(" > ");
    }

    /**
     * Returns the rendered text of an element the way link-text matching sees it.
     *
     * @remarks
     * Compute-only. WebDriver compares the trimmed rendered text, which follows CSS such as
     * `text-transform` and skips hidden content. `innerText` is the closest page-side equivalent;
     * `textContent` is the fallback for elements that do not expose it (SVG).
     *
     * @param {Element} element The element to read.
     * @returns {string} The trimmed rendered text.
     */
    function getRenderedText(element) {
        // Prefer the rendered text; elements without it (SVG) fall back to their DOM text.
        const renderedText = typeof element.innerText === "string"
            ? element.innerText
            : element.textContent || "";

        return renderedText.trim();
    }

    /**
     * Returns the shadow root an element hosts, whether it is open or closed.
     *
     * @remarks
     * Compute-only. An open root is read from the element. A closed root is hidden from scripts,
     * including content scripts, so it is read through the extension API made for exactly that
     * (`chrome.dom.openOrClosedShadowRoot`). Outside an extension context, such as the lab test
     * page, that API is absent and a closed root stays invisible. The browser's own internal roots
     * (the parts of a native input) are never returned by either path.
     *
     * @param {Element} element The possible shadow host.
     * @returns {ShadowRoot|null} The shadow root, or null when the element hosts none.
     */
    function getShadowRoot(element) {
        // Only an element can host a shadow root.
        if (!testElementNode(element)) {
            return null;
        }

        // An open root is exposed on the element itself.
        if (element.shadowRoot) {
            return element.shadowRoot;
        }

        // A closed root needs the extension API, which accepts HTML elements only.
        const extensionDom = globalScope.chrome
            ? globalScope.chrome.dom
            : null;
        const isApiAvailable = Boolean(extensionDom)
            && typeof extensionDom.openOrClosedShadowRoot === "function";
        const isReadable = isApiAvailable && element instanceof globalScope.HTMLElement;

        if (!isReadable) {
            return null;
        }

        // Treat any refusal of the API as "no shadow root", so a lookup never fails the recording.
        try {
            return extensionDom.openOrClosedShadowRoot(element) || null;
        } catch {
            return null;
        }
    }

    /**
     * Splits a CSS selector into the segments of a shadow path.
     *
     * @remarks
     * Compute-only, one pass over the text. The separator counts only outside quoted strings, and
     * a backslash escapes the character after it, so a separator inside an attribute value is
     * literal text. Every segment is trimmed. A selector without the separator, or with an empty
     * segment, is not a shadow path and is returned whole as its only segment. These rules are a
     * contract with the G4 engine, which splits a CssSelector rule the same way.
     *
     * @param {string} selector The CSS selector, possibly a shadow path.
     * @returns {string[]} The segments; a single entry when the selector is not a shadow path.
     */
    function getShadowSegments(selector) {
        // Returns the quote that is open after one ordinary character: a closing quote ends the
        // string, an opening quote starts one, and any other character leaves the state as it was.
        const getNextQuote = (openQuote, character) => {
            const isClosingQuote = character === openQuote;
            const isQuote = character === '"' || character === "'";
            const isOpeningQuote = openQuote === "" && isQuote;

            if (isClosingQuote) {
                return "";
            }

            return isOpeningQuote
                ? character
                : openQuote;
        };

        // The scan state: the closed segments, where the open one starts, the quote of the string
        // being read (empty outside a string) and the position of the next character to read.
        const segments = [];
        let segmentStartIndex = 0;
        let activeQuote = "";
        let scanIndex = 0;

        // Scan once, tracking quoted strings so only a separator outside them splits the text.
        while (scanIndex < selector.length) {
            const character = selector[scanIndex];
            const isSeparator = activeQuote === ""
                && selector.startsWith(SHADOW_PATH_SEPARATOR, scanIndex);

            if (character === "\\") {
                // Skip the escaped character, whatever it is.
                scanIndex += 2;
            } else if (isSeparator) {
                // Close the segment before the separator and start the next one after it.
                segments.push(selector.slice(segmentStartIndex, scanIndex).trim());
                scanIndex += SHADOW_PATH_SEPARATOR.length;
                segmentStartIndex = scanIndex;
            } else {
                // Enter a string on its opening quote and leave it on the matching closing quote.
                activeQuote = getNextQuote(activeQuote, character);
                scanIndex += 1;
            }
        }

        // Close the last segment, which runs to the end of the text.
        segments.push(selector.slice(segmentStartIndex).trim());

        // Text without a separator, or with an empty segment, is not a shadow path; hand it back
        // whole, so it is evaluated as the plain selector it is.
        const isShadowPath = segments.length > 1 && segments.every((segment) => segment.length > 0);

        return isShadowPath
            ? segments
            : [selector];
    }

    /**
     * Writes any text as an XPath 1.0 string literal.
     *
     * @remarks
     * Compute-only. XPath 1.0 has no escape character, so text holding both quote kinds is
     * written as a concat() of single-quoted and double-quoted parts.
     *
     * @param {string} text The raw text.
     * @returns {string} The literal, ready to place inside an expression.
     */
    function getXpathLiteral(text) {
        // Text without an apostrophe fits a single-quoted literal, the common case.
        if (!text.includes("'")) {
            return `'${text}'`;
        }

        // Text with apostrophes only fits a double-quoted literal.
        if (!text.includes('"')) {
            return `"${text}"`;
        }

        // Text with both kinds is joined from apostrophe-free parts and quoted apostrophes.
        const quotedApostrophe = `, "'", `;
        const quotedParts = text
            .split("'")
            .map((part) => `'${part}'`);
        const concatArguments = quotedParts.join(quotedApostrophe);

        return `concat(${concatArguments})`;
    }

    /**
     * Returns the XPath node test that matches an element's own tag.
     *
     * @remarks
     * Compute-only. An HTML element in an HTML document is named directly. Anything else (SVG,
     * MathML, an XHTML document, an unusual tag name) is matched through local-name(), because a
     * plain name step only ever matches HTML elements in an HTML document.
     *
     * @param {Element} element The element to describe.
     * @returns {string} The node test, for example `button` or `*[local-name()='svg']`.
     */
    function getXpathNodeTest(element) {
        // A plain name step is valid only for an HTML element of an HTML document with a safe name.
        const ownerDocument = element.ownerDocument;
        const isHtmlDocument = Boolean(ownerDocument) && ownerDocument.contentType === "text/html";
        const isHtmlElement = element.namespaceURI === HTML_NAMESPACE;
        const isPlainName = PLAIN_XPATH_NAME_PATTERN.test(element.localName);
        const isDirectlyNameable = isHtmlDocument && isHtmlElement && isPlainName;

        if (isDirectlyNameable) {
            return element.localName;
        }

        // Anything else is matched by its local name, which works in every namespace.
        const nameLiteral = getXpathLiteral(element.localName);

        return `*[local-name()=${nameLiteral}]`;
    }

    /**
     * Builds one indexed XPath step for an element.
     *
     * @remarks
     * Compute-only. Shared by the absolute path and the anchored structural path. The index
     * counts the preceding siblings that pass the same node test, which is exactly how an XPath
     * positional predicate counts.
     *
     * @param {Element} element The element the step addresses.
     * @returns {string} The step, for example `div[2]` or `*[local-name()='svg'][1]`.
     */
    function getXpathStep(element) {
        // Start at the first position; every matching earlier sibling moves the element one on.
        const nodeTest = getXpathNodeTest(element);
        let stepIndex = 1;
        let previousSibling = element.previousElementSibling;

        // Count the earlier siblings the same node test would also select.
        while (previousSibling) {
            if (getXpathNodeTest(previousSibling) === nodeTest) {
                stepIndex += 1;
            }

            previousSibling = previousSibling.previousElementSibling;
        }

        return `${nodeTest}[${stepIndex}]`;
    }

    /**
     * Returns an element's text exactly as XPath's normalize-space() reports it.
     *
     * @remarks
     * Compute-only. The string value of an element is all of its descendant text, and
     * normalize-space() trims it and collapses runs of space, tab and line breaks. Mirroring
     * that here is what lets a text locator be written before it is verified.
     *
     * @param {Element} element The element to read.
     * @returns {string} The normalized text.
     */
    function getXpathText(element) {
        // Collapse every run of XPath whitespace to one space, leaving other characters as they are.
        const stringValue = element.textContent || "";
        const collapsedText = stringValue.replaceAll(XPATH_WHITESPACE_PATTERN, " ");

        // Trim only the single spaces the collapse left at the ends, as normalize-space() does.
        const startIndex = collapsedText.startsWith(" ")
            ? 1
            : 0;
        const endIndex = collapsedText.endsWith(" ")
            ? collapsedText.length - 1
            : collapsedText.length;

        return collapsedText.slice(startIndex, Math.max(startIndex, endIndex));
    }

    /**
     * Tests whether an attribute value looks generated rather than authored.
     *
     * @remarks
     * Compute-only. A generated value (a framework counter, a hash, a row key) is unique at
     * record time and different on the next page load, which is the most common reason a replay
     * breaks. Such values are never used to build a locator.
     *
     * @param {string} value The attribute value to judge.
     * @returns {boolean} True when the value must not be trusted as a stable handle.
     */
    function testGeneratedValue(value) {
        // A missing, empty or very long value cannot identify anything reliably.
        const isUsableLength = typeof value === "string"
            && value.length > 0
            && value.length <= MAXIMUM_STABLE_VALUE_LENGTH;

        if (!isUsableLength) {
            return true;
        }

        // Counters, timestamps and row keys show up as a run of digits; React ids have their own shape.
        const isDigitRun = GENERATED_DIGIT_RUN_PATTERN.test(value);
        const isReactId = GENERATED_REACT_ID_PATTERN.test(value);
        const isCounterValue = isDigitRun || isReactId;

        if (isCounterValue) {
            return true;
        }

        // A framework prefix followed by any digit is a per-render counter.
        const isDigitPresent = DIGIT_PATTERN.test(value);
        const isFrameworkCounter = isDigitPresent && GENERATED_PREFIX_PATTERN.test(value);

        if (isFrameworkCounter) {
            return true;
        }

        // A long hexadecimal token that mixes digits and letters is a hash or a GUID part.
        const hexToken = GENERATED_HEX_TOKEN_PATTERN.exec(value)?.[0] || "";
        const isHashToken = DIGIT_PATTERN.test(hexToken) && HEX_LETTER_PATTERN.test(hexToken);

        if (isHashToken) {
            return true;
        }

        // A trailing part that mixes letters and digits is a build hash such as "card_a3f9x".
        const tailToken = GENERATED_TAIL_PATTERN.exec(value)?.[1] || "";
        const isBuildHash = DIGIT_PATTERN.test(tailToken) && LETTER_PATTERN.test(tailToken);

        return isBuildHash;
    }

    /**
     * Evaluates a CSS selector, walking a shadow path one shadow root per segment.
     *
     * @remarks
     * Compute-only helper. Every segment but the last resolves to its first match, whose shadow
     * root becomes the tree the next segment is searched in; the last segment returns all of its
     * matches. A segment that matches nothing, or an element on the way that hosts no shadow
     * root, yields no matches. This mirrors how the G4 engine resolves the same rule on replay.
     *
     * @param {Document|ShadowRoot} root The tree the first segment is searched in.
     * @param {string} selector The CSS selector, possibly a shadow path.
     * @returns {Element[]} The matched elements in document order.
     */
    function findElementsByCss(root, selector) {
        // Split the selector: every segment but the last names a shadow host on the way down, and
        // a plain selector is its own single target segment.
        const segments = getShadowSegments(selector);
        const hostSegments = segments.slice(0, -1);
        const targetSegment = segments[segments.length - 1];
        let searchRoot = root;

        // Step into the shadow root of each host on the path.
        for (const hostSegment of hostSegments) {
            const host = searchRoot.querySelector(hostSegment);
            const shadowRoot = getShadowRoot(host);

            // A segment that matches nothing, or a match that hosts no shadow root, ends the walk.
            if (!shadowRoot) {
                return [];
            }

            searchRoot = shadowRoot;
        }

        // The last segment addresses the target, so every match in the innermost tree is returned.
        return Array.from(searchRoot.querySelectorAll(targetSegment));
    }

    /**
     * Evaluates an XPath expression and returns the element nodes it selects.
     *
     * @remarks
     * Compute-only helper. XPath is evaluated from the document only: an expression cannot be
     * scoped to a shadow root, so a shadow tree always yields no matches.
     *
     * @param {Document|ShadowRoot} root The tree to search.
     * @param {string} expression The XPath expression.
     * @returns {Element[]} The matched elements in document order.
     */
    function findElementsByXpath(root, expression) {
        // Only a document can evaluate XPath; a shadow root is addressed with CSS instead.
        if (root.nodeType !== Node.DOCUMENT_NODE) {
            return [];
        }

        // Take an ordered snapshot, so the result is in document order and safe to read even if
        // the page changes afterwards. A syntax error throws here and is handled by the caller.
        const snapshot = root.evaluate(
            expression,
            root,
            null,
            XPathResult.ORDERED_NODE_SNAPSHOT_TYPE,
            null
        );
        const elements = [];

        // Keep element nodes only, since an expression may also select text or attributes.
        for (let index = 0; index < snapshot.snapshotLength; index++) {
            const node = snapshot.snapshotItem(index);

            if (testElementNode(node)) {
                elements.push(node);
            }
        }

        return elements;
    }

    /**
     * Computes the 1-based index of an element among siblings that share its tag.
     *
     * @remarks
     * Compute-only helper for the CSS builder, matching `:nth-of-type` counting rules.
     *
     * @param {Element} element The element whose index is needed.
     * @returns {number} The 1-based same-tag index.
     */
    function getSameTagIndex(element) {
        // Count preceding siblings of the same tag to derive the 1-based position.
        let sameTagIndex = 1;
        let previousSibling = element.previousElementSibling;

        while (previousSibling) {
            // Only siblings with the same tag affect the index.
            const isSameTag = previousSibling.nodeName === element.nodeName;

            if (isSameTag) {
                sameTagIndex += 1;
            }

            previousSibling = previousSibling.previousElementSibling;
        }

        return sameTagIndex;
    }

    /**
     * Tests whether a value is an element node.
     *
     * @remarks
     * Compute-only helper shared by the path builders and the XPath evaluator, which accept any
     * node (or nothing at all) and must only ever work on elements.
     *
     * @param {Node|null|undefined} node The value to test.
     * @returns {boolean} True when the value is an element node.
     */
    function testElementNode(node) {
        // A missing value is not an element; it is what ends a walk up the tree.
        if (!node) {
            return false;
        }

        return node.nodeType === Node.ELEMENT_NODE;
    }

    /**
     * Tests whether an element's id is unique within its own tree.
     *
     * @remarks
     * Compute-only helper. Duplicate ids are invalid but common in real pages, so the
     * CSS builder only anchors on an id when it actually resolves to one element.
     *
     * @param {Element} element The element whose id should be tested.
     * @returns {boolean} True when the id resolves to exactly one element.
     */
    function testUniqueId(element) {
        // Resolve the element's own tree so the lookup works inside frames and shadow roots.
        const root = element.getRootNode();
        const isRootSearchable = Boolean(root) && typeof root.querySelectorAll === "function";

        if (!isRootSearchable) {
            return false;
        }

        // The id is usable only when it matches a single element in its tree.
        const matchedElements = root.querySelectorAll(`#${getCssEscaped(element.id)}`);

        return matchedElements.length === 1;
    }

    // Expose the locator API on the shared namespace.
    namespace.locator = {
        LOCATOR_STRATEGIES,
        SHADOW_PATH_SEPARATOR,
        findElements,
        getAbsoluteXpath,
        getCssAttributeSelector,
        getCssEscaped,
        getCssSelector,
        getRenderedText,
        getShadowRoot,
        getShadowSegments,
        getXpathLiteral,
        getXpathNodeTest,
        getXpathStep,
        getXpathText,
        testGeneratedValue
    };
})(globalThis);
