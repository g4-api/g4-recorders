/**
 * Locator candidate engine for the G4 Chromium Recorder extension.
 *
 * Builds the ranked list of locators for a recorded element. The best locator is always an
 * XPath written the way a user would describe the element (its label, its role and name, its
 * placeholder), followed by explicit test hooks, stable attributes, anchored structure, and the
 * absolute path as the floor. The other W3C WebDriver strategies (CssSelector, LinkText,
 * PartialLinkText, TagName) are added as fallbacks.
 *
 * @remarks
 * Nothing is trusted until it is tested: every candidate is evaluated against the live page and
 * kept only when it resolves to exactly the recorded element. A candidate that matches several
 * elements is scoped to a nearby user-facing anchor (a dialog, a form, a table row) before a
 * positional index is ever used. Generated attribute values are rejected up front.
 *
 * The work runs in the page's event path, so it is time-boxed: when the budget is spent the
 * remaining, weaker candidates are skipped and the floor locator still ships.
 *
 * Attached to the shared `globalThis.g4Recorder` namespace (see constants.js). Loaded as a
 * content script in every frame, after dom-locator.js and dom-accessibility.js.
 */
(function initializeLocatorCandidatesModule(globalScope) {
    // Create or reuse the single extension namespace shared across every context.
    const namespace = globalScope.g4Recorder = globalScope.g4Recorder || {};

    // Pull the sibling modules this engine builds on; both are loaded earlier in the manifest.
    const { accessibility, locator } = namespace;
    const { LOCATOR_STRATEGIES } = locator;

    // How long one element may spend generating and verifying candidates. The listener runs on
    // the page's main thread, so the budget keeps a click on a very large page responsive.
    const TIME_BUDGET_MILLISECONDS = 30;

    // How many ancestors are inspected when looking for an anchor to scope an ambiguous locator.
    const MAXIMUM_ANCHOR_LEVELS = 10;

    // How many anchors are kept, and how many ambiguous locators are scoped with them. Each
    // combination costs one evaluation, so both stay small.
    const MAXIMUM_ANCHORS = 3;
    const MAXIMUM_SCOPED_DRAFTS = 3;

    // How many leaf texts of a row are tried as its identifying text.
    const MAXIMUM_ROW_KEYS = 6;

    // How many XPath candidates are published for one element.
    const MAXIMUM_XPATH_CANDIDATES = 6;

    // Above this many anchors on a page the link-text fallbacks are skipped: reading the rendered
    // text of every link forces layout and is not worth it for a fallback.
    const MAXIMUM_LINK_COUNT = 1500;

    // Length of the leading fragment used when a text or a link is too long to match in full.
    const TEXT_PREFIX_LENGTH = 40;

    // Longest attribute value accepted in a locator; longer values are data, not identity.
    const MAXIMUM_ATTRIBUTE_LENGTH = 100;

    // Score penalties. An anchor adds a second thing that must stay stable, text holding digits
    // is often data that changes, and a positional index is the weakest way to disambiguate.
    const ATTRIBUTE_ANCHOR_PENALTY = 10;
    const CONTENT_ANCHOR_PENALTY = 14;
    const DIGIT_TEXT_PENALTY = 10;
    const INDEX_PENALTY = 35;

    // Only reasonably strong locators are worth scoping with an anchor.
    const MINIMUM_SCOPED_SCORE = 60;

    // Score thresholds of the strength label shown to the user and stored with the rule.
    const STRONG_SCORE = 70;
    const FAIR_SCORE = 40;

    // The score a locator keeps after a hot test proved it broken, so it sorts last.
    const BROKEN_SCORE = 0;

    // The base score of every strategy. Higher wins; user-facing strategies lead by design.
    const SCORES = Object.freeze({
        absolute: 10,
        alt: 86,
        ariaLabel: 95,
        cssAriaLabel: 66,
        cssCustomTag: 70,
        cssId: 73,
        cssName: 68,
        cssPlaceholder: 64,
        cssShadowTag: 45,
        cssStructure: 20,
        cssTestHook: 78,
        customTagName: 35,
        href: 62,
        id: 75,
        labelFor: 100,
        labelText: 88,
        labelTarget: 60,
        labelWrapping: 97,
        linkText: 70,
        name: 72,
        nameValue: 73,
        partialLinkText: 55,
        placeholder: 88,
        roleName: 92,
        roleNamePrefix: 70,
        source: 55,
        structure: 30,
        tagName: 15,
        testHook: 80,
        testHookPreferred: 110,
        text: 60,
        textPrefix: 50,
        title: 82,
        type: 45,
        value: 90
    });

    // The strength label of a primary locator, shown to the user and stored with the rule.
    const STRENGTHS = Object.freeze({
        fair: "Fair",
        strong: "Strong",
        weak: "Weak"
    });

    // The verdict a locator carries: how much evidence backs it.
    const VERDICTS = Object.freeze({
        broken: "Broken",
        proven: "Proven",
        stable: "Stable",
        unique: "Unique",
        unverified: "Unverified"
    });

    // The strategy name of the one candidate that addresses an element inside a shadow root: a
    // CSS shadow path. The Hub recognises it by this name and writes it as the rule's locator.
    const SHADOW_PATH_STRATEGY = "ShadowPath";

    // Added to the rank of a working shadow path so it always leads the list of a shadow target,
    // whose other candidates only address the host element.
    const SHADOW_PATH_RANK_BONUS = 1000;

    // Roles whose name comes from their own content, so role plus text identifies them.
    const NAME_FROM_CONTENT_ROLES = [
        "button", "checkbox", "heading", "link", "menuitem", "menuitemcheckbox", "menuitemradio",
        "option", "radio", "switch", "tab", "treeitem"
    ];

    // Input types that are buttons and show their value attribute as a caption.
    const BUTTON_INPUT_TYPES = ["button", "submit", "reset"];

    // Tags whose `name` attribute is an authored, stable handle.
    const NAMED_TAGS = ["button", "form", "frame", "iframe", "input", "select", "textarea"];

    // Containers that repeat (rows, list items, cards), identified by a text they contain.
    const ROW_SELECTOR = "tr, li, article, [role='row'], [role='listitem'], [role='article']";

    // Direct children that title their container.
    const HEADING_SELECTOR = ":scope > h1, :scope > h2, :scope > h3, :scope > h4, :scope > h5, "
        + ":scope > h6, :scope > legend, :scope > caption";

    // A safe attribute name; anything else in the configured test-hook list is ignored.
    const ATTRIBUTE_NAME_PATTERN = /^[a-z][a-z0-9_.:-]*$/i;

    // A single decimal digit. Text that holds one is often data (a count, a date, a price).
    const DIGIT_PATTERN = /\d/;

    // Tags that exist once per document by definition and therefore identify nothing.
    const DOCUMENT_TAGS = ["html", "head", "body"];

    /**
     * Captures the facts that re-identify an element on a later load of the same page.
     *
     * @remarks
     * Compute-only. Element identity does not survive a reload, so a revisit test recognises the
     * element by what the user sees: its tag, role, type and name. The name is part of the
     * fingerprint on purpose: when the visible state differs (a "Follow" button that now reads
     * "Following") nothing matches and the test stays inconclusive instead of blaming a locator.
     *
     * @param {Element} element The recorded element.
     * @returns {object} The fingerprint ({ name, role, tagName, type }).
     */
    function getElementFingerprint(element) {
        return {
            name: accessibility.getAccessibleName(element),
            role: accessibility.getElementRole(element),
            tagName: element.localName,
            type: element.getAttribute("type") || ""
        };
    }

    /**
     * Builds the ranked, verified locator candidates of an element.
     *
     * @remarks
     * Compute-only over the live DOM; the page is never changed. An element inside a shadow root
     * cannot be reached by an XPath or a plain CSS selector, so its leading locator is a CSS
     * shadow path (one selector per shadow level). The locators of the outermost host follow it,
     * for a consumer that can only address an element of the document.
     *
     * @param {object} options Engine inputs.
     * @param {Element} options.element The element to locate.
     * @param {object} options.settings The locator settings ({ maximumTextLength, preference,
     *   testHookAttributes }).
     * @returns {object} The result ({ locator, locators, locatorStrength, shadowPath }).
     */
    function getLocatorCandidates(options) {
        const { element, settings } = options;
        const root = element.getRootNode();

        // An element of a shadow tree is described through its hosts.
        if (testShadowRoot(root)) {
            return getShadowCandidates(element, settings);
        }

        // A detached element has no tree to verify against, so only the floor locator is known.
        if (root.nodeType !== Node.DOCUMENT_NODE) {
            const floorCandidate = newCandidate({
                score: SCORES.absolute,
                strategy: "Absolute",
                using: LOCATOR_STRATEGIES.xpath,
                value: locator.getAbsoluteXpath(element),
                verdict: VERDICTS.unverified
            });

            return { ...getLocatorRanking([floorCandidate]), shadowPath: null };
        }

        // Collect the XPath candidates first, then add the other W3C strategies as fallbacks.
        const context = newContext({ element, root, settings });
        const candidates = [
            ...getXpathCandidates(context),
            ...getCssCandidates(context),
            ...getLinkCandidates(context),
            ...getTagNameCandidates(context)
        ];

        return { ...getLocatorRanking(candidates), shadowPath: null };
    }

    /**
     * Orders a locator list and resolves the primary locator and the strength label.
     *
     * @remarks
     * Compute-only. Used when candidates are first built and again after a hot test changed a
     * verdict, so both paths rank identically. Broken locators sort last and can never be the
     * primary one while a working XPath exists. The primary locator is always an XPath, because
     * XPath is the default locator type of a G4 rule. For a target inside a shadow root that
     * XPath addresses the host, and the working shadow path leads the list instead: it is the
     * locator a rule uses, so the strength label describes it.
     *
     * @param {object[]} locators The candidates to rank.
     * @returns {object} The ranking ({ locator, locators, locatorStrength }).
     */
    function getLocatorRanking(locators) {
        // Returns the value a locator is ordered by; higher sorts first. A working shadow path is
        // lifted above every other candidate of its element, because it is the only one that
        // addresses the element rather than its host.
        const getRank = (candidate) => {
            const effectiveScore = getEffectiveScore(candidate);
            const isShadowPath = candidate.strategy === SHADOW_PATH_STRATEGY;
            const isWorking = candidate.verdict !== VERDICTS.broken;

            return isShadowPath && isWorking
                ? effectiveScore + SHADOW_PATH_RANK_BONUS
                : effectiveScore;
        };

        // Tells whether two entries are the same locator: the same strategy and the same text.
        const testSameLocator = (left, right) => {
            const isSameStrategy = left.using === right.using;
            const isSameValue = left.value === right.value;

            return isSameStrategy && isSameValue;
        };

        // Order a copy by rank, best first, so the caller's list is left untouched.
        const rankedLocators = locators
            .slice()
            .sort((left, right) => getRank(right) - getRank(left));

        // Remove duplicates, keeping the highest scored entry of each locator text.
        const uniqueLocators = [];

        rankedLocators.forEach((candidate) => {
            const isDuplicate = uniqueLocators.some((kept) => testSameLocator(kept, candidate));

            if (!isDuplicate) {
                uniqueLocators.push(candidate);
            }
        });

        // The primary locator is the best XPath; a broken one is used only when nothing else exists.
        const primaryCandidate = uniqueLocators.find(
            (candidate) => candidate.using === LOCATOR_STRATEGIES.xpath
        );
        const primaryLocator = primaryCandidate
            ? primaryCandidate.value
            : "";
        const primaryScore = primaryCandidate
            ? getEffectiveScore(primaryCandidate)
            : BROKEN_SCORE;

        // A shadow target is as strong as its shadow path. Without a working one a rule can only
        // address the host, which is never better than Fair.
        const shadowCandidates = uniqueLocators.filter(
            (candidate) => candidate.strategy === SHADOW_PATH_STRATEGY
        );
        const workingShadowCandidate = shadowCandidates.find(
            (candidate) => candidate.verdict !== VERDICTS.broken
        );
        const isHostOnly = shadowCandidates.length > 0 && !workingShadowCandidate;
        const strengthScore = workingShadowCandidate
            ? workingShadowCandidate.score
            : primaryScore;
        const strength = getStrength(strengthScore);
        const isCapped = isHostOnly && strength === STRENGTHS.strong;
        const locatorStrength = isCapped
            ? STRENGTHS.fair
            : strength;

        return {
            locator: primaryLocator,
            locators: uniqueLocators,
            locatorStrength
        };
    }

    /**
     * Re-tests recorded locators against the element they were built for.
     *
     * @remarks
     * Compute-only. This is the settle test: it runs after the recorded action and the page's
     * reaction to it. A locator that still resolves to the same element is upgraded to Stable.
     * A locator that no longer does is left as it was, never demoted: the replay uses a locator
     * before the action, so text the action itself changed is still the right text to look for.
     *
     * @param {object} options Test inputs.
     * @param {Element} options.element The recorded element, still attached to the page.
     * @param {object[]} options.locators The locators recorded for it.
     * @returns {object} The outcome ({ isChanged, locators }).
     */
    function testLocators(options) {
        const { element, locators } = options;

        // Every published locator is evaluated from the document. For a target inside a shadow
        // root only the shadow path reaches the element; the other locators address its host.
        const hostElement = getOutermostHost(element);
        const root = hostElement.getRootNode();
        let isChanged = false;

        // Upgrade every locator that survived the page's reaction to the action.
        const testedLocators = locators.map((candidate) => {
            // Only a locator that was unique at record time has something left to prove here.
            if (candidate.verdict !== VERDICTS.unique) {
                return candidate;
            }

            // Resolve the locator again, now that the page has reacted to the action.
            const matches = locator.findElements({
                root,
                using: candidate.using,
                value: candidate.value
            });

            // The shadow path must reach the element itself; every other locator reaches its host.
            const expectedElement = candidate.strategy === SHADOW_PATH_STRATEGY
                ? element
                : hostElement;

            // A locator the action itself invalidated keeps its verdict; it is never demoted.
            if (!testSingleMatch(matches, expectedElement)) {
                return candidate;
            }

            // Record the upgrade so the caller knows there is a new verdict to report.
            isChanged = true;

            return { ...candidate, verdict: VERDICTS.stable };
        });

        return { isChanged, locators: testedLocators };
    }

    /**
     * Re-tests recorded locators on a later load of the page they were recorded on.
     *
     * @remarks
     * Compute-only. This is the revisit test and the closest thing to a replay the recorder can
     * observe: a fresh document, new generated ids, a re-rendered tree. The element is considered
     * present only when at least one locator resolves to a single element with the recorded
     * fingerprint. From then on that element is the reference: locators that reach it are Proven
     * and locators that do not are Broken. When no locator re-identifies the element the page is
     * in another state and nothing is concluded. A target inside a shadow root whose shadow path
     * stopped resolving is still looked for, by its fingerprint, below the host the other
     * locators reach, so a broken shadow path is reported as Broken instead of going unnoticed.
     *
     * @param {object} options Test inputs.
     * @param {object} options.fingerprint The fingerprint captured at record time.
     * @param {object[]} options.locators The locators recorded for the element.
     * @param {Document} options.root The freshly loaded document.
     * @returns {object} The outcome ({ isChanged, isElementPresent, locators }).
     */
    function testLocatorsOnRevisit(options) {
        const { fingerprint, locators, root } = options;

        // Resolve each locator once; a single match is the only acceptable result.
        const resolutions = locators.map((candidate) => {
            const matches = locator.findElements({
                root,
                using: candidate.using,
                value: candidate.value
            });

            return matches.length === 1
                ? matches[0]
                : null;
        });

        // The first match that carries the recorded fingerprint becomes the reference element.
        const resolvedReference = resolutions.find((resolvedElement) => {
            const isResolved = resolvedElement !== null;

            return isResolved && testFingerprint(resolvedElement, fingerprint);
        });

        // A shadow target whose shadow path no longer resolves is searched for below its host.
        const referenceElement = resolvedReference
            || getShadowReference({ fingerprint, locators, resolutions });

        // Without a recognised element the page is in another state, so nothing is concluded.
        if (!referenceElement) {
            return { isChanged: false, isElementPresent: false, locators };
        }

        // Judge every locator against the reference element. For a target inside a shadow root
        // only the shadow path reaches the element; the other locators must reach its host.
        const referenceHost = getOutermostHost(referenceElement);
        let isChanged = false;
        const testedLocators = locators.map((candidate, index) => {
            const expectedElement = candidate.strategy === SHADOW_PATH_STRATEGY
                ? referenceElement
                : referenceHost;
            const verdict = resolutions[index] === expectedElement
                ? VERDICTS.proven
                : VERDICTS.broken;

            // Remember that at least one verdict moved, so the caller knows there is news to report.
            if (verdict !== candidate.verdict) {
                isChanged = true;
            }

            return { ...candidate, verdict };
        });

        return { isChanged, isElementPresent: true, locators: testedLocators };
    }

    /**
     * Verifies one XPath draft and, when it is ambiguous, remembers it for scoping.
     *
     * @remarks
     * Owns the candidate and ambiguity lists of the context. A draft that resolves to exactly the
     * element becomes a candidate; one that matches several elements including the element is
     * kept for the anchoring stage; anything else is dropped.
     *
     * @param {object} context The engine context of the element.
     * @param {object} draft The draft ({ score, strategy, value }).
     * @returns {void}
     */
    function addXpathDraft(context, draft) {
        // Resolve the draft against the live tree; only what it matches now decides its fate.
        const matches = locator.findElements({
            root: context.root,
            using: LOCATOR_STRATEGIES.xpath,
            value: draft.value
        });

        // A draft that resolves to exactly the element becomes a verified candidate.
        if (testSingleMatch(matches, context.element)) {
            context.candidates.push(newCandidate({
                ...draft,
                using: LOCATOR_STRATEGIES.xpath,
                verdict: VERDICTS.unique
            }));

            return;
        }

        // Keep a draft that matches the element among others; an anchor may make it unique.
        const isAmbiguous = matches.length > 1 && matches.includes(context.element);

        if (isAmbiguous) {
            context.ambiguousDrafts.push({ draft, matches });
        }
    }

    /**
     * Writes the identity locators an ancestor could be anchored on.
     *
     * @remarks
     * Compute-only. Authored attributes come first because they are the cheapest and most stable
     * identity; a titling heading and a row's own text follow.
     *
     * @param {object} context The engine context of the element.
     * @param {Element} ancestor The ancestor to describe.
     * @returns {object[]} The anchor drafts ({ penalty, value }), best first.
     */
    function getAnchorDrafts(context, ancestor) {
        const drafts = [];
        const nodeTest = locator.getXpathNodeTest(ancestor);

        // Test hooks come first: they are an explicit contract between the page and its tests.
        getTestHooks(context, ancestor).forEach((hook) => {
            const hookLiteral = locator.getXpathLiteral(hook.value);

            drafts.push({
                penalty: ATTRIBUTE_ANCHOR_PENALTY,
                value: `//*[@${hook.name}=${hookLiteral}]`
            });
        });

        // A stable id is the next authored handle; a generated one would break on the next load.
        const isIdUsable = Boolean(ancestor.id) && !locator.testGeneratedValue(ancestor.id);

        if (isIdUsable) {
            const idLiteral = locator.getXpathLiteral(ancestor.id);

            drafts.push({
                penalty: ATTRIBUTE_ANCHOR_PENALTY,
                value: `//*[@id=${idLiteral}]`
            });
        }

        // An aria-label names a region (a dialog, a form) the way it is announced to the user.
        const ariaLabel = getAttributeText(ancestor, "aria-label");

        if (ariaLabel) {
            const ariaLabelLiteral = locator.getXpathLiteral(ariaLabel);

            drafts.push({
                penalty: ATTRIBUTE_ANCHOR_PENALTY,
                value: `//${nodeTest}[@aria-label=${ariaLabelLiteral}]`
            });
        }

        // A direct heading, legend or caption titles its container for the user.
        const heading = ancestor.querySelector(HEADING_SELECTOR);
        const headingText = heading
            ? locator.getXpathText(heading)
            : "";
        const isHeadingUsable = Boolean(heading)
            && !heading.contains(context.element)
            && testUsableText(context, headingText);

        if (isHeadingUsable) {
            const headingTest = locator.getXpathNodeTest(heading);
            const headingLiteral = locator.getXpathLiteral(headingText);

            drafts.push({
                penalty: CONTENT_ANCHOR_PENALTY,
                value: `//${nodeTest}[${headingTest}[normalize-space()=${headingLiteral}]]`
            });
        }

        // A repeated row is identified by a text it contains, the way a user picks a row.
        const rowKeys = ancestor.matches(ROW_SELECTOR)
            ? getRowKeys(context, ancestor)
            : [];

        rowKeys.forEach((rowKey) => {
            const rowKeyLiteral = locator.getXpathLiteral(rowKey);

            drafts.push({
                penalty: CONTENT_ANCHOR_PENALTY,
                value: `//${nodeTest}[.//*[normalize-space()=${rowKeyLiteral}]]`
            });
        });

        return drafts;
    }

    /**
     * Finds the anchors that can scope an ambiguous locator of the element.
     *
     * @remarks
     * Compute-only over the element's ancestors, nearest first. An anchor is an ancestor with a
     * unique identity of its own: an authored attribute, a heading that titles it, or, for a
     * repeated row, a text it contains. Every anchor is verified to select that one ancestor.
     *
     * @param {object} context The engine context of the element.
     * @returns {object[]} The anchors ({ element, penalty, value }), nearest first.
     */
    function getAnchors(context) {
        const anchors = [];
        let ancestor = context.element.parentElement;
        let level = 0;

        // Tells whether the climb may go on: an ancestor is left, the level bound is not reached,
        // and more anchors are still wanted.
        const testClimbOpen = () => {
            const isAncestorLeft = Boolean(ancestor);
            const isLevelAllowed = level < MAXIMUM_ANCHOR_LEVELS;
            const isAnchorWanted = anchors.length < MAXIMUM_ANCHORS;

            return isAncestorLeft && isLevelAllowed && isAnchorWanted;
        };

        // Climb until enough anchors are found, the tree ends, or the budget is spent.
        while (testClimbOpen()) {
            if (!testTimeBudget(context)) {
                break;
            }

            // Describe this ancestor. The document tags exist once per page and anchor nothing.
            const anchorElement = ancestor;
            const anchorDrafts = DOCUMENT_TAGS.includes(anchorElement.localName)
                ? []
                : getAnchorDrafts(context, anchorElement);

            // Keep the first draft that selects this one ancestor and nothing else.
            const anchorDraft = anchorDrafts.find((draft) => {
                const matches = locator.findElements({
                    root: context.root,
                    using: LOCATOR_STRATEGIES.xpath,
                    value: draft.value
                });

                return testSingleMatch(matches, anchorElement);
            });

            if (anchorDraft) {
                anchors.push({ ...anchorDraft, element: anchorElement });
            }

            // Move one level up for the next round.
            ancestor = ancestor.parentElement;
            level += 1;
        }

        return anchors;
    }

    /**
     * Returns a trimmed attribute value that is short enough to use in a locator.
     *
     * @param {Element} element The element to read.
     * @param {string} attributeName The attribute name.
     * @returns {string} The value, or an empty string when absent or too long.
     */
    function getAttributeText(element, attributeName) {
        const attributeValue = (element.getAttribute(attributeName) || "").trim();

        return attributeValue.length <= MAXIMUM_ATTRIBUTE_LENGTH
            ? attributeValue
            : "";
    }

    /**
     * Builds the verified CSS selector fallbacks of the element.
     *
     * @remarks
     * Compute-only. CSS is the one strategy that also works inside a shadow root, so this is
     * shared by the document and the shadow paths. The best attribute-based selector and the
     * structural selector are published; more would only repeat what the XPath list says.
     *
     * @param {object} context The engine context of the element.
     * @returns {object[]} The CSS candidates.
     */
    function getCssCandidates(context) {
        const { element, root } = context;
        const tagName = locator.getCssEscaped(element.localName);
        const drafts = [];

        // Tells whether a selector draft resolves to exactly the element in its own tree.
        const testUniqueSelector = (draft) => {
            const matches = locator.findElements({
                root,
                using: LOCATOR_STRATEGIES.cssSelector,
                value: draft.value
            });

            return testSingleMatch(matches, element);
        };

        // Attribute-based selectors, strongest first: a test hook is an explicit contract.
        getTestHooks(context, element).forEach((hook) => {
            drafts.push({
                score: SCORES.cssTestHook,
                strategy: "TestHook",
                value: locator.getCssAttributeSelector(hook.name, hook.value)
            });
        });

        // A stable id is the next authored handle; a generated one would break on the next load.
        const isIdUsable = Boolean(element.id) && !locator.testGeneratedValue(element.id);

        if (isIdUsable) {
            const escapedId = locator.getCssEscaped(element.id);

            drafts.push({
                score: SCORES.cssId,
                strategy: "Id",
                value: `#${escapedId}`
            });
        }

        // A custom element is named by its author, and a page often holds exactly one of it,
        // which makes the bare tag a stable handle for a web component host.
        if (element.localName.includes("-")) {
            drafts.push({
                score: SCORES.cssCustomTag,
                strategy: "CustomTag",
                value: tagName
            });
        }

        // Inside a shadow root the tree is one small component, where a control is often the only
        // one of its tag. The bare tag then outlives a positional selector through re-renders.
        if (testShadowRoot(root)) {
            drafts.push({
                score: SCORES.cssShadowTag,
                strategy: "Tag",
                value: tagName
            });
        }

        // The remaining naming attributes are qualified by the tag, so they stay specific.
        const attributeDrafts = [
            { attributeName: "name", score: SCORES.cssName, strategy: "Name" },
            { attributeName: "aria-label", score: SCORES.cssAriaLabel, strategy: "AriaLabel" },
            { attributeName: "placeholder", score: SCORES.cssPlaceholder, strategy: "Placeholder" }
        ];

        attributeDrafts.forEach((attributeDraft) => {
            const { attributeName, score, strategy } = attributeDraft;
            const attributeValue = getAttributeText(element, attributeName);
            const isUsable = Boolean(attributeValue) && !locator.testGeneratedValue(attributeValue);

            if (isUsable) {
                const attributeSelector = locator.getCssAttributeSelector(attributeName, attributeValue);

                drafts.push({
                    score,
                    strategy,
                    value: `${tagName}${attributeSelector}`
                });
            }
        });

        // Keep the best selector that resolves to exactly the element, strongest draft first.
        const rankedDrafts = drafts
            .slice()
            .sort((left, right) => right.score - left.score);
        const attributeCandidate = rankedDrafts.find(testUniqueSelector);

        // The structural selector is always offered, as the CSS counterpart of the floor path.
        const structureDraft = {
            score: SCORES.cssStructure,
            strategy: "Structure",
            value: locator.getCssSelector(element)
        };

        // Publish only what exists and still resolves to exactly the element.
        return [attributeCandidate, structureDraft]
            .filter(Boolean)
            .filter(testUniqueSelector)
            .map((draft) => newCandidate({
                ...draft,
                using: LOCATOR_STRATEGIES.cssSelector,
                verdict: VERDICTS.unique
            }));
    }

    /**
     * Returns the score a locator currently ranks with.
     *
     * @param {object} candidate The locator candidate.
     * @returns {number} Its score, or the broken score when a hot test proved it broken.
     */
    function getEffectiveScore(candidate) {
        return candidate.verdict === VERDICTS.broken
            ? BROKEN_SCORE
            : candidate.score;
    }

    /**
     * Builds the link-text fallbacks of an anchor element.
     *
     * @remarks
     * Compute-only. WebDriver matches link text against rendered text, which can differ from the
     * DOM text an XPath sees (`text-transform`, hidden parts), so this fallback covers a case the
     * XPath list cannot. A short single-line text is matched in full; a long or multi-line one
     * through its leading fragment.
     *
     * @param {object} context The engine context of the element.
     * @returns {object[]} The link candidates.
     */
    function getLinkCandidates(context) {
        const { element, root } = context;

        // Link strategies consider anchors only, and are skipped on pages with very many links
        // or once the time budget of the element is spent.
        const isAnchor = element.localName === "a";
        const isAffordable = isAnchor && root.querySelectorAll("a").length <= MAXIMUM_LINK_COUNT;
        const isSearchAllowed = isAffordable && testTimeBudget(context);

        if (!isSearchAllowed) {
            return [];
        }

        // Write both shapes: the full rendered text, and the leading fragment of its first line.
        const renderedText = locator.getRenderedText(element);
        const firstLine = renderedText.split("\n")[0].trim();
        const fullTextDraft = {
            score: SCORES.linkText,
            using: LOCATOR_STRATEGIES.linkText,
            value: renderedText
        };
        const prefixDraft = {
            score: SCORES.partialLinkText,
            using: LOCATOR_STRATEGIES.partialLinkText,
            value: firstLine.slice(0, TEXT_PREFIX_LENGTH).trim()
        };

        // Choose the full text when it is short and single-line, otherwise its leading fragment.
        const isFullTextUsable = renderedText.length > 0
            && renderedText.length <= context.settings.maximumTextLength
            && !renderedText.includes("\n");
        const draft = isFullTextUsable
            ? fullTextDraft
            : prefixDraft;

        // Publish it only when it resolves to exactly this link.
        const matches = locator.findElements({ root, using: draft.using, value: draft.value });

        if (!testSingleMatch(matches, element)) {
            return [];
        }

        return [newCandidate({ ...draft, strategy: draft.using, verdict: VERDICTS.unique })];
    }

    /**
     * Returns the outermost shadow host of an element, or the element when it has none.
     *
     * @remarks
     * Compute-only helper. A locator evaluated from the document can reach the outermost host of
     * a shadow target at most, so the hot tests compare host locators against this element.
     *
     * @param {Element} element The element, inside or outside a shadow root.
     * @returns {Element} The outermost host, or the element itself in a document tree.
     */
    function getOutermostHost(element) {
        let currentElement = element;
        let root = currentElement.getRootNode();

        // Step out through every shadow level until the tree is a document.
        while (testShadowRoot(root)) {
            currentElement = root.host;
            root = currentElement.getRootNode();
        }

        return currentElement;
    }

    /**
     * Collects the leaf texts of a repeated row that could identify it.
     *
     * @remarks
     * Compute-only. A row is told apart from its siblings by something the user reads in it (an
     * invoice number, a name), never by its position. Texts that belong to the recorded element
     * itself are skipped, since the row must be identified independently of it.
     *
     * @param {object} context The engine context of the element.
     * @param {Element} row The row-like ancestor.
     * @returns {string[]} The candidate texts, in document order.
     */
    function getRowKeys(context, row) {
        const rowKeys = [];

        // Scan the row's leaf elements in document order until enough keys are collected.
        for (const descendant of row.querySelectorAll("*")) {
            if (rowKeys.length >= MAXIMUM_ROW_KEYS) {
                break;
            }

            // Read only leaf elements outside the recorded element; a leaf holds one piece of
            // text, and the element's own text must not be what identifies its row.
            const isLeaf = descendant.childElementCount === 0;
            const isOwnContent = descendant === context.element || context.element.contains(descendant);
            const isKeySource = isLeaf && !isOwnContent;
            const leafText = isKeySource
                ? locator.getXpathText(descendant)
                : "";

            // Keep a text once, and only when it is short enough to identify anything.
            const isNewKey = testUsableText(context, leafText) && !rowKeys.includes(leafText);

            if (isNewKey) {
                rowKeys.push(leafText);
            }
        }

        return rowKeys;
    }

    /**
     * Builds the anchored and indexed candidates of the ambiguous drafts.
     *
     * @remarks
     * Compute-only. Each strong ambiguous locator is first scoped to a verified anchor, nearest
     * anchor first. Only when no anchor makes it unique, and no other XPath candidate exists, is
     * the best one disambiguated by its position among the matches.
     *
     * @param {object} context The engine context of the element.
     * @param {object[]} anchors The verified anchors of the element.
     * @returns {object[]} The scoped candidates.
     */
    function getScopedCandidates(context, anchors) {
        const { element, root } = context;
        const scopedCandidates = [];

        // Only the strongest ambiguous locators are worth an anchor, and only a few of them,
        // because every anchor tried costs one evaluation.
        const scopedDrafts = context.ambiguousDrafts
            .filter((ambiguous) => ambiguous.draft.score >= MINIMUM_SCOPED_SCORE)
            .slice(0, MAXIMUM_SCOPED_DRAFTS);

        // Scope each ambiguous locator to the nearest anchor that makes it unique.
        scopedDrafts.forEach((ambiguous) => {
            // Drop the leading "//" so the draft can continue the anchor's own path.
            const relativePath = ambiguous.draft.value.slice(2);
            const anchor = anchors.find((candidateAnchor) => {
                // Stop trying anchors once the budget is spent; the draft stays unscoped.
                if (!testTimeBudget(context)) {
                    return false;
                }

                const matches = locator.findElements({
                    root,
                    using: LOCATOR_STRATEGIES.xpath,
                    value: `${candidateAnchor.value}//${relativePath}`
                });

                return testSingleMatch(matches, element);
            });

            // Publish the scoped locator, charged with the price of depending on its anchor.
            if (anchor) {
                scopedCandidates.push(newCandidate({
                    score: ambiguous.draft.score - anchor.penalty,
                    strategy: `Anchored${ambiguous.draft.strategy}`,
                    using: LOCATOR_STRATEGIES.xpath,
                    value: `${anchor.value}//${relativePath}`,
                    verdict: VERDICTS.unique
                }));
            }
        });

        // Fall back to a positional index only when nothing better identifies the element.
        const isIndexNeeded = scopedCandidates.length === 0
            && context.candidates.length === 0
            && context.ambiguousDrafts.length > 0;

        if (isIndexNeeded) {
            // The drafts were verified best first, so the first ambiguous one is the strongest.
            // XPath positions are 1-based, hence the shift of the match index.
            const bestAmbiguous = context.ambiguousDrafts[0];
            const matchIndex = bestAmbiguous.matches.indexOf(element) + 1;

            scopedCandidates.push(newCandidate({
                score: bestAmbiguous.draft.score - INDEX_PENALTY,
                strategy: `Indexed${bestAmbiguous.draft.strategy}`,
                using: LOCATOR_STRATEGIES.xpath,
                value: `(${bestAmbiguous.draft.value})[${matchIndex}]`,
                verdict: VERDICTS.unique
            }));
        }

        return scopedCandidates;
    }

    /**
     * Builds the locator result of an element inside a shadow root.
     *
     * @remarks
     * Compute-only. Walks out through every shadow level, recording one verified CSS selector per
     * level, until the outermost host in the document is reached. The segments are then joined
     * into one CSS shadow path that addresses the element itself, verified from the document like
     * any other candidate; it is as strong as its weakest segment. The host's own candidates
     * follow it, so a consumer that cannot use a shadow path still has the host to act on.
     *
     * @param {Element} element The element inside a shadow root.
     * @param {object} settings The locator settings.
     * @returns {object} The result ({ locator, locators, locatorStrength, shadowPath }).
     */
    function getShadowCandidates(element, settings) {
        const innerSegments = [];
        const segmentScores = [];
        let currentElement = element;
        let root = currentElement.getRootNode();

        // Record one CSS selector per shadow level, innermost last.
        while (testShadowRoot(root)) {
            // Prefer a verified selector; the structural one is the floor inside a shadow tree.
            const context = newContext({ element: currentElement, root, settings });
            const cssCandidates = getCssCandidates(context);
            const isVerified = cssCandidates.length > 0;
            const selector = isVerified
                ? cssCandidates[0].value
                : locator.getCssSelector(currentElement);
            const segmentScore = isVerified
                ? cssCandidates[0].score
                : SCORES.cssStructure;

            innerSegments.unshift({ using: LOCATOR_STRATEGIES.cssSelector, value: selector });
            segmentScores.push(segmentScore);

            // Step out to the host and continue from the tree that contains it.
            currentElement = root.host;
            root = currentElement.getRootNode();
        }

        // Locate the outermost host like any other document element.
        const hostResult = getLocatorCandidates({ element: currentElement, settings });

        // Describe the walk level by level as well: the host's XPath, then one CSS selector per
        // shadow level. This is published with the result whether or not the joined path verifies.
        const shadowPath = [
            { using: LOCATOR_STRATEGIES.xpath, value: hostResult.locator },
            ...innerSegments
        ];

        // Join the host's best CSS selector and the inner segments into one shadow path, and keep
        // it only when it resolves from the document to exactly the element.
        const hostCandidate = hostResult.locators.find(
            (candidate) => candidate.using === LOCATOR_STRATEGIES.cssSelector
        );
        const pathSegments = hostCandidate
            ? [hostCandidate.value, ...innerSegments.map((segment) => segment.value)]
            : [];
        const pathValue = pathSegments.join(` ${locator.SHADOW_PATH_SEPARATOR} `);
        const pathMatches = locator.findElements({
            root: currentElement.getRootNode(),
            using: LOCATOR_STRATEGIES.cssSelector,
            value: pathValue
        });
        const isPathVerified = Boolean(hostCandidate) && testSingleMatch(pathMatches, element);

        // Without a verified path a rule can only address the host, which is never better than Fair.
        if (!isPathVerified) {
            const isHostStrong = hostResult.locatorStrength === STRENGTHS.strong;
            const locatorStrength = isHostStrong
                ? STRENGTHS.fair
                : hostResult.locatorStrength;

            return {
                locator: hostResult.locator,
                locators: hostResult.locators,
                locatorStrength,
                shadowPath
            };
        }

        // The path is as strong as its weakest segment, since every segment has to keep matching.
        const shadowCandidate = newCandidate({
            score: Math.min(hostCandidate.score, ...segmentScores),
            strategy: SHADOW_PATH_STRATEGY,
            using: LOCATOR_STRATEGIES.cssSelector,
            value: pathValue,
            verdict: VERDICTS.unique
        });

        // Rank the path together with the host's candidates, so it leads the list and its score
        // sets the strength label.
        return {
            ...getLocatorRanking([shadowCandidate, ...hostResult.locators]),
            shadowPath
        };
    }

    /**
     * Finds a shadow target by its fingerprint below the host its locators still reach.
     *
     * @remarks
     * Compute-only. On a revisit the shadow path is the only locator that reaches a target inside
     * a shadow root, so when it stops resolving nothing re-identifies the target directly. The
     * host locators still name the component, though, and the target is somewhere inside it.
     * Every shadow level below that host is searched for the recorded fingerprint, and exactly one
     * match is accepted. None or several conclude nothing, as on any other revisit.
     *
     * @param {object} options Search inputs.
     * @param {object} options.fingerprint The fingerprint captured at record time.
     * @param {object[]} options.locators The locators recorded for the element.
     * @param {Array<Element|null>} options.resolutions What each locator resolved to, by index.
     * @returns {Element|null} The re-identified element, or null when it cannot be told apart.
     */
    function getShadowReference(options) {
        const { fingerprint, locators, resolutions } = options;

        // Only a shadow target has an element below the host, and a host must still resolve.
        const isShadowTarget = locators.some(
            (candidate) => candidate.strategy === SHADOW_PATH_STRATEGY
        );
        const hostIndex = locators.findIndex((candidate, index) => {
            const isHostLocator = candidate.strategy !== SHADOW_PATH_STRATEGY;

            return isHostLocator && resolutions[index] !== null;
        });
        const isSearchable = isShadowTarget && hostIndex >= 0;

        if (!isSearchable) {
            return null;
        }

        // Search every shadow level below the host for elements that carry the fingerprint.
        const fingerprintMatches = [];
        const pendingHosts = [resolutions[hostIndex]];

        while (pendingHosts.length > 0) {
            const shadowRoot = locator.getShadowRoot(pendingHosts.pop());
            const descendants = shadowRoot
                ? Array.from(shadowRoot.querySelectorAll("*"))
                : [];

            descendants.forEach((descendant) => {
                // Compare the tag first, since it is free and rules out nearly every element.
                const isSameTag = descendant.localName === fingerprint.tagName;

                if (isSameTag && testFingerprint(descendant, fingerprint)) {
                    fingerprintMatches.push(descendant);
                }
            });

            // Any descendant may host a deeper shadow root of its own.
            pendingHosts.push(...descendants);
        }

        // Exactly one match re-identifies the element; anything else concludes nothing.
        return fingerprintMatches.length === 1
            ? fingerprintMatches[0]
            : null;
    }

    /**
     * Converts a locator score into the strength label shown to the user.
     *
     * @param {number} score The score of the primary locator.
     * @returns {string} "Strong", "Fair" or "Weak".
     */
    function getStrength(score) {
        if (score >= STRONG_SCORE) {
            return STRENGTHS.strong;
        }

        return score >= FAIR_SCORE
            ? STRENGTHS.fair
            : STRENGTHS.weak;
    }

    /**
     * Builds the anchored structural candidate of the element.
     *
     * @remarks
     * Compute-only. When no user-facing or attribute locator exists, a short indexed path from
     * the nearest attribute-anchored ancestor is still far more stable than the absolute path,
     * because changes outside that ancestor cannot break it.
     *
     * @param {object} context The engine context of the element.
     * @param {object[]} anchors The verified anchors of the element.
     * @returns {object[]} The structural candidate, or an empty list.
     */
    function getStructureCandidates(context, anchors) {
        const { element, root } = context;

        // Only an ancestor anchored on an authored attribute is stable enough to start a path from.
        const anchor = anchors.find(
            (candidateAnchor) => candidateAnchor.penalty === ATTRIBUTE_ANCHOR_PENALTY
        );

        if (!anchor) {
            return [];
        }

        // Write one indexed step per level between the anchor and the element.
        const steps = [];
        let currentElement = element;

        while (currentElement && currentElement !== anchor.element) {
            steps.unshift(locator.getXpathStep(currentElement));
            currentElement = currentElement.parentElement;
        }

        // Publish the path only when it resolves to exactly the element.
        const relativePath = steps.join("/");
        const value = `${anchor.value}/${relativePath}`;
        const matches = locator.findElements({ root, using: LOCATOR_STRATEGIES.xpath, value });

        if (!testSingleMatch(matches, element)) {
            return [];
        }

        return [newCandidate({
            score: SCORES.structure,
            strategy: "Structure",
            using: LOCATOR_STRATEGIES.xpath,
            value,
            verdict: VERDICTS.unique
        })];
    }

    /**
     * Builds the tag-name fallback of the element.
     *
     * @remarks
     * Compute-only. A tag name identifies an element only when the page holds exactly one of it,
     * which is realistic for custom elements and little else, so it ranks at the bottom.
     *
     * @param {object} context The engine context of the element.
     * @returns {object[]} The tag-name candidate, or an empty list.
     */
    function getTagNameCandidates(context) {
        const { element, root } = context;
        const tagName = element.localName;

        // The document tags exist once per page by definition and identify nothing.
        if (DOCUMENT_TAGS.includes(tagName)) {
            return [];
        }

        // Publish the tag only when the element is the single one of its kind in the tree.
        const matches = locator.findElements({
            root,
            using: LOCATOR_STRATEGIES.tagName,
            value: tagName
        });

        if (!testSingleMatch(matches, element)) {
            return [];
        }

        // A custom element (its name holds a dash) is authored, so it ranks above a plain tag.
        const score = tagName.includes("-")
            ? SCORES.customTagName
            : SCORES.tagName;

        return [newCandidate({
            score,
            strategy: LOCATOR_STRATEGIES.tagName,
            using: LOCATOR_STRATEGIES.tagName,
            value: tagName,
            verdict: VERDICTS.unique
        })];
    }

    /**
     * Reads the configured test-hook attributes an element carries.
     *
     * @remarks
     * Compute-only. A test hook is an explicit contract between the page and its tests, so its
     * value is accepted as written, digits included; only its length is limited.
     *
     * @param {object} context The engine context of the element.
     * @param {Element} element The element to read.
     * @returns {object[]} The hooks ({ name, value }) in configured order.
     */
    function getTestHooks(context, element) {
        // Read only safely named attributes, and keep the ones the element actually carries.
        return context.settings.testHookAttributes
            .filter((attributeName) => ATTRIBUTE_NAME_PATTERN.test(attributeName))
            .map((attributeName) => ({
                name: attributeName,
                value: getAttributeText(element, attributeName)
            }))
            .filter((hook) => hook.value.length > 0);
    }

    /**
     * Builds the verified XPath candidates of the element, best first.
     *
     * @remarks
     * Owns the context's candidate list. Drafts are verified in rank order while the time budget
     * lasts, ambiguous ones are scoped through anchors, and the absolute path is always appended
     * so the element is never left without a locator.
     *
     * @param {object} context The engine context of the element.
     * @returns {object[]} The XPath candidates.
     */
    function getXpathCandidates(context) {
        // Verify the drafts in rank order; stop early when the budget is spent.
        for (const draft of getXpathDrafts(context)) {
            if (!testTimeBudget(context)) {
                break;
            }

            addXpathDraft(context, draft);
        }

        // Anchors are looked for only when they can help: to scope an ambiguous draft, or to
        // give an element with no verified candidate a short structural path.
        const isAnchoringUseful = context.ambiguousDrafts.length > 0 || context.candidates.length === 0;
        const anchors = isAnchoringUseful
            ? getAnchors(context)
            : [];

        // Add the scoped drafts and the anchored structural path to the verified candidates.
        context.candidates.push(
            ...getScopedCandidates(context, anchors),
            ...getStructureCandidates(context, anchors)
        );

        // Build the absolute path, the floor locator, and note whether it could be verified.
        const absoluteValue = locator.getAbsoluteXpath(context.element);
        const absoluteMatches = locator.findElements({
            root: context.root,
            using: LOCATOR_STRATEGIES.xpath,
            value: absoluteValue
        });
        const absoluteVerdict = testSingleMatch(absoluteMatches, context.element)
            ? VERDICTS.unique
            : VERDICTS.unverified;

        // Keep the strongest candidates and always end with the absolute path, so the element
        // is never left without a locator.
        const strongestCandidates = context.candidates
            .slice()
            .sort((left, right) => right.score - left.score)
            .slice(0, MAXIMUM_XPATH_CANDIDATES - 1);

        return [
            ...strongestCandidates,
            newCandidate({
                score: SCORES.absolute,
                strategy: "Absolute",
                using: LOCATOR_STRATEGIES.xpath,
                value: absoluteValue,
                verdict: absoluteVerdict
            })
        ];
    }

    /**
     * Writes the XPath drafts of the element, ordered by the configured preference.
     *
     * @remarks
     * Compute-only; nothing is evaluated here. Each draft describes the element one way: by its
     * label, its role and name, a naming attribute, a test hook, a stable attribute, or its text.
     * Generated values never produce a draft, and typed field content is never read.
     *
     * @param {object} context The engine context of the element.
     * @returns {object[]} The drafts ({ score, strategy, value }), best first.
     */
    function getXpathDrafts(context) {
        const { element, settings } = context;
        const nodeTest = locator.getXpathNodeTest(element);
        const tagName = element.localName;
        const role = accessibility.getElementRole(element);
        const text = locator.getXpathText(element);
        const drafts = [];

        // Adds one attribute-equality draft when the attribute holds a usable value.
        const addAttributeDraft = (options) => {
            const { attributeName, isGeneratedAllowed = false, score, strategy } = options;
            const attributeValue = getAttributeText(element, attributeName);

            // A user-facing attribute is accepted as written; an identity one must not be generated.
            const isValueStable = isGeneratedAllowed || !locator.testGeneratedValue(attributeValue);
            const isUsable = Boolean(attributeValue) && isValueStable;

            if (isUsable) {
                const attributeLiteral = locator.getXpathLiteral(attributeValue);

                drafts.push({
                    score,
                    strategy,
                    value: `//${nodeTest}[@${attributeName}=${attributeLiteral}]`
                });
            }
        };

        // Label: the text a user reads next to a form control. A linked label is reached through
        // its `for` attribute and a wrapping label through its descendant, so each has its own shape.
        const label = accessibility.getLabel(element);
        const labelText = label.element
            ? locator.getXpathText(label.element)
            : "";
        const isLabelUsable = testUsableText(context, labelText);
        const isLinkedLabelUsable = isLabelUsable && label.kind === "For";
        const isWrappingLabelUsable = isLabelUsable && label.kind === "Wrapping";

        if (isLinkedLabelUsable) {
            const labelLiteral = locator.getXpathLiteral(labelText);

            drafts.push({
                score: SCORES.labelFor,
                strategy: "Label",
                value: `//${nodeTest}[@id=//label[normalize-space()=${labelLiteral}]/@for]`
            });
        }

        if (isWrappingLabelUsable) {
            const labelLiteral = locator.getXpathLiteral(labelText);

            drafts.push({
                score: SCORES.labelWrapping,
                strategy: "Label",
                value: `//label[normalize-space()=${labelLiteral}]//${nodeTest}`
            });
        }

        // Accessible attributes: the name assistive technology announces.
        addAttributeDraft({
            attributeName: "aria-label",
            isGeneratedAllowed: true,
            score: SCORES.ariaLabel,
            strategy: "AriaLabel"
        });

        // Role and name: a button, link, tab or option identified by its caption. An explicit role
        // is matched as written; an implicit one is matched through the element's own tag.
        const isNamedByContent = NAME_FROM_CONTENT_ROLES.includes(role) || tagName === "label";
        const roleValue = element.getAttribute("role");
        const rolePredicate = roleValue === null
            ? `//${nodeTest}`
            : `//*[@role=${locator.getXpathLiteral(roleValue)}]`;
        const captionScore = tagName === "label"
            ? SCORES.labelText
            : SCORES.roleName;

        // Text holding a digit is often data, so every text draft pays for it. Text that is too
        // long to match in full is matched by its leading fragment instead.
        const digitPenalty = DIGIT_PATTERN.test(text)
            ? DIGIT_TEXT_PENALTY
            : 0;
        const isTextUsable = testUsableText(context, text);
        const textPrefix = text.slice(0, TEXT_PREFIX_LENGTH).trim();
        const isPrefixUsable = text.length > settings.maximumTextLength && textPrefix.length > 0;
        const isCaptionUsable = isNamedByContent && isTextUsable;
        const isCaptionPrefixUsable = isNamedByContent && isPrefixUsable;

        if (isCaptionUsable) {
            const textLiteral = locator.getXpathLiteral(text);

            drafts.push({
                score: captionScore - digitPenalty,
                strategy: "RoleName",
                value: `${rolePredicate}[normalize-space()=${textLiteral}]`
            });
        }

        if (isCaptionPrefixUsable) {
            const prefixLiteral = locator.getXpathLiteral(textPrefix);

            drafts.push({
                score: SCORES.roleNamePrefix - digitPenalty,
                strategy: "RoleNamePrefix",
                value: `${rolePredicate}[starts-with(normalize-space(), ${prefixLiteral})]`
            });
        }

        // A button-like input shows its value attribute as its caption.
        const inputType = (element.getAttribute("type") || "").toLowerCase();
        const captionValue = getAttributeText(element, "value");
        const isButtonInput = tagName === "input" && BUTTON_INPUT_TYPES.includes(inputType);
        const isCaptionValueUsable = isButtonInput && Boolean(captionValue);

        if (isCaptionValueUsable) {
            const typeLiteral = locator.getXpathLiteral(element.getAttribute("type"));
            const captionLiteral = locator.getXpathLiteral(captionValue);

            drafts.push({
                score: SCORES.value,
                strategy: "Value",
                value: `//input[@type=${typeLiteral}][@value=${captionLiteral}]`
            });
        }

        // Naming attributes a user can see or hear.
        addAttributeDraft({
            attributeName: "placeholder",
            isGeneratedAllowed: true,
            score: SCORES.placeholder,
            strategy: "Placeholder"
        });
        addAttributeDraft({
            attributeName: "alt",
            isGeneratedAllowed: true,
            score: SCORES.alt,
            strategy: "Alt"
        });
        addAttributeDraft({
            attributeName: "title",
            isGeneratedAllowed: true,
            score: SCORES.title,
            strategy: "Title"
        });

        // Test hooks: explicit contracts, ranked first when the user prefers them.
        const testHookScore = settings.preference === "TestHooks"
            ? SCORES.testHookPreferred
            : SCORES.testHook;

        getTestHooks(context, element).forEach((hook) => {
            const hookLiteral = locator.getXpathLiteral(hook.value);

            drafts.push({
                score: testHookScore,
                strategy: "TestHook",
                value: `//*[@${hook.name}=${hookLiteral}]`
            });
        });

        // Stable attributes: authored identity that is not user-facing.
        const isIdUsable = Boolean(element.id) && !locator.testGeneratedValue(element.id);

        if (isIdUsable) {
            const idLiteral = locator.getXpathLiteral(element.id);

            drafts.push({
                score: SCORES.id,
                strategy: "Id",
                value: `//*[@id=${idLiteral}]`
            });
        }

        // A form name identifies a control; a radio button or check box also needs its value,
        // because every choice of one group shares the group's name.
        const nameValue = getAttributeText(element, "name");
        const nameLiteral = locator.getXpathLiteral(nameValue);
        const isNameUsable = NAMED_TAGS.includes(tagName)
            && Boolean(nameValue)
            && !locator.testGeneratedValue(nameValue);
        const isChoiceInput = inputType === "radio" || inputType === "checkbox";
        const isChoiceValueUsable = isChoiceInput
            && Boolean(captionValue)
            && !locator.testGeneratedValue(captionValue);
        const isNameValueUsable = isNameUsable && isChoiceValueUsable;

        if (isNameValueUsable) {
            const choiceLiteral = locator.getXpathLiteral(captionValue);

            drafts.push({
                score: SCORES.nameValue,
                strategy: "NameValue",
                value: `//${nodeTest}[@name=${nameLiteral}][@value=${choiceLiteral}]`
            });
        }

        if (isNameUsable) {
            drafts.push({
                score: SCORES.name,
                strategy: "Name",
                value: `//${nodeTest}[@name=${nameLiteral}]`
            });
        }

        // A link target, a frame source, a label target and a control type, weakest last.
        const hrefValue = getAttributeText(element, "href");
        const isHrefUsable = tagName === "a"
            && hrefValue.length > 1
            && !hrefValue.toLowerCase().startsWith("javascript:")
            && !locator.testGeneratedValue(hrefValue);

        if (isHrefUsable) {
            const hrefLiteral = locator.getXpathLiteral(hrefValue);

            drafts.push({
                score: SCORES.href,
                strategy: "Href",
                value: `//a[@href=${hrefLiteral}]`
            });
        }

        const isFrame = tagName === "iframe" || tagName === "frame";

        if (isFrame) {
            addAttributeDraft({ attributeName: "src", score: SCORES.source, strategy: "Source" });
        }

        if (tagName === "label") {
            addAttributeDraft({ attributeName: "for", score: SCORES.labelTarget, strategy: "LabelFor" });
        }

        // Text: any other element a user would point at by what it says. The leading fragment is
        // used for a leaf only, since the text of a container is the text of all its children.
        const isPlainTextUsable = !isNamedByContent && isTextUsable;
        const isLeaf = element.childElementCount === 0;
        const isPlainPrefixUsable = !isNamedByContent && isPrefixUsable && isLeaf;

        if (isPlainTextUsable) {
            const textLiteral = locator.getXpathLiteral(text);

            drafts.push({
                score: SCORES.text - digitPenalty,
                strategy: "Text",
                value: `//${nodeTest}[normalize-space()=${textLiteral}]`
            });
        }

        if (isPlainPrefixUsable) {
            const prefixLiteral = locator.getXpathLiteral(textPrefix);

            drafts.push({
                score: SCORES.textPrefix - digitPenalty,
                strategy: "TextPrefix",
                value: `//${nodeTest}[starts-with(normalize-space(), ${prefixLiteral})]`
            });
        }

        const isTypedControl = tagName === "input" || tagName === "button";

        if (isTypedControl) {
            addAttributeDraft({ attributeName: "type", score: SCORES.type, strategy: "Type" });
        }

        // Verify the strongest drafts first so the time budget is spent where it matters.
        return drafts.sort((left, right) => right.score - left.score);
    }

    /**
     * Creates one published locator candidate.
     *
     * @param {object} options Candidate inputs ({ score, strategy, using, value, verdict }).
     * @returns {object} The candidate in contract shape.
     */
    function newCandidate(options) {
        const { score, strategy, using, value, verdict } = options;

        return { using, value, strategy, score, verdict };
    }

    /**
     * Creates the working context of one engine run.
     *
     * @remarks
     * The context is the explicit state of one run: the element and its tree, the settings, the
     * deadline of the time budget, and the lists the verification stages fill. It is created per
     * element and discarded with the result, so the module itself stays stateless.
     *
     * @param {object} options Context inputs ({ element, root, settings }).
     * @returns {object} The context.
     */
    function newContext(options) {
        const { element, root, settings } = options;

        return {
            ambiguousDrafts: [],
            candidates: [],
            deadlineMilliseconds: globalScope.performance.now() + TIME_BUDGET_MILLISECONDS,
            element,
            root,
            settings
        };
    }

    /**
     * Tests whether an element carries a recorded fingerprint.
     *
     * @param {Element} element The element found on the revisited page.
     * @param {object} fingerprint The fingerprint captured at record time.
     * @returns {boolean} True when tag, role, type and name all match.
     */
    function testFingerprint(element, fingerprint) {
        // Compare what the user sees of the element now with what was seen at record time.
        const currentFingerprint = getElementFingerprint(element);
        const isSameTag = currentFingerprint.tagName === fingerprint.tagName;
        const isSameRole = currentFingerprint.role === fingerprint.role;
        const isSameType = currentFingerprint.type === fingerprint.type;
        const isSameName = currentFingerprint.name === fingerprint.name;

        return isSameTag && isSameRole && isSameType && isSameName;
    }

    /**
     * Tests whether a tree root is a shadow root.
     *
     * @remarks
     * Compute-only helper. A shadow root is a document fragment with a host; a detached subtree
     * is a fragment or an element without one, and it must not be walked as a shadow tree.
     *
     * @param {Node} root The root node of an element's tree.
     * @returns {boolean} True when the root is a shadow root attached to a host.
     */
    function testShadowRoot(root) {
        const isFragment = root.nodeType === Node.DOCUMENT_FRAGMENT_NODE;

        return isFragment && Boolean(root.host);
    }

    /**
     * Tests whether a locator resolved to exactly one element, and to the expected one.
     *
     * @remarks
     * Compute-only helper. This is the single definition of "verified" every stage of the engine
     * shares: a locator that matches the element among others, or another element alone, is not
     * a locator of that element.
     *
     * @param {Element[]} matches The elements the locator resolved to.
     * @param {Element} element The element the locator is expected to address.
     * @returns {boolean} True when the only match is the expected element.
     */
    function testSingleMatch(matches, element) {
        const isSingle = matches.length === 1;

        return isSingle && matches[0] === element;
    }

    /**
     * Tests whether the time budget of the run still allows more work.
     *
     * @param {object} context The engine context of the element.
     * @returns {boolean} True while the deadline has not passed.
     */
    function testTimeBudget(context) {
        return globalScope.performance.now() < context.deadlineMilliseconds;
    }

    /**
     * Tests whether a text is short enough to identify an element.
     *
     * @param {object} context The engine context of the element.
     * @param {string} text The normalized text.
     * @returns {boolean} True when the text is non-empty and within the configured length.
     */
    function testUsableText(context, text) {
        const isTextPresent = text.length > 0;
        const isTextShort = text.length <= context.settings.maximumTextLength;

        return isTextPresent && isTextShort;
    }

    // Expose the candidate engine on the shared namespace.
    namespace.locatorCandidates = {
        VERDICTS,
        getElementFingerprint,
        getLocatorCandidates,
        getLocatorRanking,
        testLocators,
        testLocatorsOnRevisit
    };
})(globalThis);
