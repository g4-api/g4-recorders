/**
 * User-facing element semantics for the G4 Chromium Recorder extension.
 *
 * Answers the three questions a user-facing locator starts from: what an element is (its
 * role), what it is called (its accessible name and its label), and which element the user
 * really acted on (the actionable ancestor of the raw event target).
 *
 * @remarks
 * This is a practical subset of the ARIA name and role computation, not a full implementation:
 * it reads the attributes and relationships that authors actually use, never forces layout, and
 * never changes the page. Every lookup stays inside the element's own tree, so it works the same
 * in a frame and in a shadow root.
 *
 * Attached to the shared `globalThis.g4Recorder` namespace (see constants.js). Loaded as a
 * content script in every frame, before the candidate engine and the mapper that use it.
 */
(function initializeDomAccessibilityModule(globalScope) {
    // Create or reuse the single extension namespace shared across every context.
    const namespace = globalScope.g4Recorder = globalScope.g4Recorder || {};

    // How many ancestors are inspected when looking for the actionable element behind a raw
    // event target. Deep enough for icon-in-span-in-button markup, shallow enough that a click on
    // plain content is never attributed to a far-away clickable container.
    const ACTIONABLE_ANCESTOR_LEVELS = 6;

    // Elements a user acts on directly. A click that lands on their inner markup belongs to them.
    const ACTIONABLE_SELECTOR = [
        "a[href]",
        "button",
        "input",
        "select",
        "textarea",
        "label",
        "summary",
        "option",
        "[contenteditable='']",
        "[contenteditable='true']",
        "[role='button']",
        "[role='link']",
        "[role='checkbox']",
        "[role='radio']",
        "[role='switch']",
        "[role='tab']",
        "[role='menuitem']",
        "[role='menuitemcheckbox']",
        "[role='menuitemradio']",
        "[role='option']",
        "[role='combobox']",
        "[role='textbox']",
        "[role='searchbox']",
        "[role='slider']",
        "[role='spinbutton']",
        "[role='treeitem']",
        "[role='gridcell']"
    ].join(", ");

    // Form controls a <label> can name.
    const LABELABLE_SELECTOR = "input, select, textarea, meter, progress, output, button";

    // Input types that behave as buttons and take their name from the value attribute.
    const BUTTON_INPUT_TYPES = ["button", "submit", "reset"];

    // The implicit role of each input type; a missing or unknown type is a text box.
    const INPUT_ROLES_BY_TYPE = Object.freeze({
        button: "button",
        checkbox: "checkbox",
        email: "textbox",
        image: "button",
        number: "spinbutton",
        password: "textbox",
        radio: "radio",
        range: "slider",
        reset: "button",
        search: "searchbox",
        submit: "button",
        tel: "textbox",
        text: "textbox",
        url: "textbox"
    });

    // The implicit role of the tags a locator can sensibly be built on.
    const ROLES_BY_TAG = Object.freeze({
        article: "article",
        aside: "complementary",
        button: "button",
        dialog: "dialog",
        fieldset: "group",
        footer: "contentinfo",
        form: "form",
        h1: "heading",
        h2: "heading",
        h3: "heading",
        h4: "heading",
        h5: "heading",
        h6: "heading",
        header: "banner",
        img: "img",
        li: "listitem",
        main: "main",
        nav: "navigation",
        ol: "list",
        option: "option",
        progress: "progressbar",
        section: "region",
        summary: "button",
        table: "table",
        td: "cell",
        textarea: "textbox",
        th: "columnheader",
        tr: "row",
        ul: "list"
    });

    // Maximum number of name characters kept, so one long text block cannot bloat an event.
    const MAXIMUM_NAME_LENGTH = 256;

    // Matches any run of whitespace, used to collapse multi-line text into one line.
    const WHITESPACE_PATTERN = /\s+/g;

    /**
     * Resolves the accessible name of an element.
     *
     * @remarks
     * Compute-only. Follows the ARIA precedence in the order authors rely on: aria-labelledby,
     * aria-label, the element's <label>, then the native naming attribute of its kind (alt, a
     * button input's value, placeholder, title), and finally its text. The first non-empty
     * source wins. Typed field content is never used as a name.
     *
     * @param {Element} element The element whose name is needed.
     * @returns {string} The accessible name, or an empty string when none is found.
     */
    function getAccessibleName(element) {
        // Without attribute access (a non-element node) there is nothing to name.
        if (!testAttributeReadable(element)) {
            return "";
        }

        // An author-supplied reference to naming elements takes precedence over everything.
        const labelledByText = getLabelledByText(element);

        if (labelledByText) {
            return labelledByText.slice(0, MAXIMUM_NAME_LENGTH);
        }

        // An explicit aria-label is the next strongest author statement.
        const ariaLabel = getTrimmedAttribute(element, "aria-label");

        if (ariaLabel) {
            return ariaLabel.slice(0, MAXIMUM_NAME_LENGTH);
        }

        // A form control is named by its <label> before any of its own attributes.
        const labelText = getLabel(element).text;

        if (labelText) {
            return labelText.slice(0, MAXIMUM_NAME_LENGTH);
        }

        // A button-like input shows its value attribute as its caption.
        const isButtonInput = element.localName === "input"
            && BUTTON_INPUT_TYPES.includes(getInputType(element));
        const nativeName = isButtonInput
            ? getTrimmedAttribute(element, "value")
            : "";

        // The first non-empty native naming attribute wins, in the order a browser applies them.
        const attributeName = nativeName
            || getTrimmedAttribute(element, "alt")
            || getTrimmedAttribute(element, "placeholder")
            || getTrimmedAttribute(element, "title");

        if (attributeName) {
            return attributeName.slice(0, MAXIMUM_NAME_LENGTH);
        }

        // Fall back to the text content, collapsed and trimmed to a single line.
        return getCollapsedText(element).slice(0, MAXIMUM_NAME_LENGTH);
    }

    /**
     * Resolves the role of an element.
     *
     * @remarks
     * Compute-only. An explicit `role` attribute wins (its first token); otherwise the implicit
     * role of the tag is used, so a native <button> and a `role="button"` element are described
     * the same way. Elements without a meaningful role yield an empty string.
     *
     * @param {Element} element The element whose role is needed.
     * @returns {string} The role, or an empty string when the element has none.
     */
    function getElementRole(element) {
        // Without attribute access there is no role to read.
        if (!testAttributeReadable(element)) {
            return "";
        }

        // An explicit role is the author's own statement; fallback roles after the first are ignored.
        const explicitRole = getTrimmedAttribute(element, "role").split(WHITESPACE_PATTERN)[0];

        if (explicitRole) {
            return explicitRole.toLowerCase();
        }

        // A link is a link only while it navigates somewhere.
        const tagName = element.localName;

        if (tagName === "a") {
            return element.hasAttribute("href")
                ? "link"
                : "";
        }

        // An input's role depends on its type; an unknown type behaves as a text box.
        if (tagName === "input") {
            const inputType = getInputType(element);

            // Read own entries only, so a type such as "constructor" never resolves to an inherited member.
            return Object.hasOwn(INPUT_ROLES_BY_TYPE, inputType)
                ? INPUT_ROLES_BY_TYPE[inputType]
                : "textbox";
        }

        // A select is a list box when it shows several rows, otherwise a collapsed combo box.
        if (tagName === "select") {
            const isListBox = element.multiple || element.size > 1;

            return isListBox
                ? "listbox"
                : "combobox";
        }

        // Every other tag has a fixed implicit role, or none worth describing.
        // Read own entries only, so a tag such as <constructor> never resolves to an inherited member.
        return Object.hasOwn(ROLES_BY_TAG, tagName)
            ? ROLES_BY_TAG[tagName]
            : "";
    }

    /**
     * Resolves the <label> that names a form control.
     *
     * @remarks
     * Compute-only. A label linked through `for` is preferred over a wrapping label because it
     * is the explicit relationship; `kind` tells the caller which one was found, since the two
     * need different locator shapes. The lookup stays inside the control's own tree.
     *
     * @param {Element} element The control whose label is needed.
     * @returns {object} The label ({ element, kind, text }); `kind` is "For", "Wrapping" or "".
     */
    function getLabel(element) {
        // The answer for an element no label names, so callers can read the fields unguarded.
        const emptyLabel = { element: null, kind: "", text: "" };

        // Only form controls are named by a <label>.
        const isLabelable = Boolean(element)
            && typeof element.matches === "function"
            && element.matches(LABELABLE_SELECTOR);

        if (!isLabelable) {
            return emptyLabel;
        }

        // Prefer the label that points at the control's id. Only a control with an id inside a
        // searchable tree can be linked this way.
        const root = element.getRootNode();
        const isRootSearchable = Boolean(root) && typeof root.querySelectorAll === "function";
        const isLinkable = isRootSearchable && Boolean(element.id);
        const linkingLabels = isLinkable
            ? Array.from(root.querySelectorAll("label[for]"))
            : [];
        const linkedLabel = linkingLabels.find((label) => label.htmlFor === element.id);

        if (linkedLabel) {
            return { element: linkedLabel, kind: "For", text: getCollapsedText(linkedLabel) };
        }

        // Otherwise use the label that contains the control.
        const wrappingLabel = element.closest("label");

        if (wrappingLabel) {
            return { element: wrappingLabel, kind: "Wrapping", text: getCollapsedText(wrappingLabel) };
        }

        return emptyLabel;
    }

    /**
     * Resolves the element the user acted on from the raw target of an event.
     *
     * @remarks
     * Compute-only. A click usually lands on the innermost node (an icon, a span inside a
     * button), but the action belongs to the surrounding control, which is also the element with
     * a stable, user-facing identity. The nearest actionable ancestor within a few levels is
     * returned; a drawing without one is lifted to its outermost <svg>; anything else is
     * returned unchanged, so plain content keeps its own locator.
     *
     * @param {Element} element The raw event target.
     * @returns {Element} The actionable element, or the input element when none applies.
     */
    function resolveActionableElement(element) {
        // Without an element that can be matched there is nothing to resolve.
        const isMatchable = Boolean(element) && typeof element.matches === "function";

        if (!isMatchable) {
            return element;
        }

        // Walk a bounded number of ancestors, staying inside the element's own tree.
        let currentElement = element;
        let outermostDrawing = null;

        for (let level = 0; level <= ACTIONABLE_ANCESTOR_LEVELS; level++) {
            // Stop at the top of the tree; a shadow root or a document has no parent element.
            if (!currentElement) {
                break;
            }

            // The nearest control wins, because it is the one the user aimed at.
            if (currentElement.matches(ACTIONABLE_SELECTOR)) {
                return currentElement;
            }

            // Remember the outermost drawing root passed on the way up.
            if (currentElement.localName === "svg") {
                outermostDrawing = currentElement;
            }

            currentElement = currentElement.parentElement;
        }

        // A click inside a drawing with no control around it targets the drawing as a whole.
        return outermostDrawing || element;
    }

    /**
     * Returns an element's text collapsed to a single trimmed line.
     *
     * @param {Element} element The element to read.
     * @returns {string} The collapsed text.
     */
    function getCollapsedText(element) {
        return (element.textContent || "").replaceAll(WHITESPACE_PATTERN, " ").trim();
    }

    /**
     * Returns the lower-case type of an input, defaulting to "text".
     *
     * @param {Element} element The input element.
     * @returns {string} The input type.
     */
    function getInputType(element) {
        return (element.getAttribute("type") || "text").toLowerCase();
    }

    /**
     * Resolves the combined text referenced by an element's aria-labelledby attribute.
     *
     * @remarks
     * Compute-only helper. Returns an empty string when the attribute is absent or none of the
     * referenced ids resolve, so callers can treat it as "no labelledby name".
     *
     * @param {Element} element The element whose aria-labelledby should be resolved.
     * @returns {string} The combined referenced text, collapsed and trimmed.
     */
    function getLabelledByText(element) {
        // Without the attribute there is nothing to resolve.
        const labelledBy = getTrimmedAttribute(element, "aria-labelledby");

        if (!labelledBy) {
            return "";
        }

        // Each whitespace-separated id may reference an element contributing name text.
        const root = element.getRootNode();
        const isRootSearchable = Boolean(root) && typeof root.getElementById === "function";

        if (!isRootSearchable) {
            return "";
        }

        // Join the text of the referenced elements in the order the attribute lists them,
        // skipping ids that do not resolve.
        const referencedTexts = labelledBy
            .split(WHITESPACE_PATTERN)
            .map((referencedId) => root.getElementById(referencedId))
            .filter(Boolean)
            .map(getCollapsedText);

        return referencedTexts.join(" ").trim();
    }

    /**
     * Returns a trimmed attribute value, or an empty string when the attribute is absent.
     *
     * @param {Element} element The element to read.
     * @param {string} attributeName The attribute name.
     * @returns {string} The trimmed value.
     */
    function getTrimmedAttribute(element, attributeName) {
        return (element.getAttribute(attributeName) || "").trim();
    }

    /**
     * Tests whether a value exposes attributes, as every element does.
     *
     * @remarks
     * Compute-only helper for the public lookups, which may be handed any event target (a
     * document, a text node, nothing at all) and must answer with an empty result for those.
     *
     * @param {Element|null|undefined} element The value to test.
     * @returns {boolean} True when attributes can be read from the value.
     */
    function testAttributeReadable(element) {
        // A missing value has no attributes; it is tested first so the next read is safe.
        if (!element) {
            return false;
        }

        return typeof element.getAttribute === "function";
    }

    // Expose the accessibility API on the shared namespace.
    namespace.accessibility = {
        getAccessibleName,
        getElementRole,
        getLabel,
        resolveActionableElement
    };
})(globalThis);
