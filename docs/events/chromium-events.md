# Chromium Recorder Events

[⬅ Back to the docs index](../README.md)

This page is the event catalog of the Chromium recorder extension. The extension sends each event to the hub method `SendRecordingEvent`, and the hub broadcasts it as `ReceiveRecordingEvent` in the [shared contract](contract.md). The names are defined in `src/G4.Recorders.Chromium.Extension/lib/event-catalog.js`, and the navigation and context events in `js/background.js:115-124`.

## Captured events

Event names follow the G4 action style.

Interaction events:

| `event` | Trigger | `type` |
| --- | --- | --- |
| `InvokeClick` | click | Mouse |
| `InvokeDoubleClick` | double click | Mouse |
| `InvokeContextClick` | right-click / context menu | Mouse |
| `InvokeMiddleClick` | middle-click (`auxclick` with button 1); other `auxclick` buttons are ignored | Mouse |
| `InvokeScroll` | wheel/scroll (direction + notches in `value`) | Mouse |
| `InvokeDragAndDrop` | an element dragged and dropped; see [Drag and drop](#drag-and-drop) | Mouse |
| `SendKeys` | typed-field commit (focus loss, field change, form submission, or page exit); `value.text` = typed text, password-redactable | Keyboard |
| `SubmitForm` | form submit (`value` carries `formId`/`formName`) | Form |
| `MoveMouseCursor` | mouse rests over an element (`value` carries `X`, `Y`, `dwellMilliseconds`); off by default (`hover`) | Mouse |

> Single-key actions (`keydown`/`keyup`) and **key
> combinations** (Ctrl/Alt/Cmd + key, planned as a distinct `SendKeysCombination` event)
> are **deferred** — placeholders marked `// TODO(key-combination)` that emit nothing for
> now; trusted `input` events update one field session, which is captured as one `SendKeys`
> on commit regardless of pauses between keystrokes.

Navigation events (type `Navigation`, top frame, full-page only):

| `event` | Meaning |
| --- | --- |
| `OpenUrl` | Address-bar navigation: a typed URL or search (`from_address_bar` / typed transitions). Link clicks/form submits/redirects are **not** OpenUrl — they are the `InvokeClick` / `SubmitForm` already captured. |
| `UpdatePage` | A reload / refresh. |
| `RedoNavigation` | Forward. |
| `UndoNavigation` | Back. |

## Locators

Every element event carries a ranked list of locators for its target. Each locator was evaluated against the live page when the event was recorded, and only locators that resolve to exactly the target are kept. The events are `InvokeClick`, `InvokeDoubleClick`, `InvokeContextClick`, `InvokeMiddleClick`, `InvokeDragAndDrop`, `SendKeys`, `MoveMouseCursor`, `InvokeScroll` and `SubmitForm`. A drag and drop also carries the locators of its drop element.

The best XPath is the `chain.locator`. The whole list sits on the trigger node, `chain.path[0].properties`:

| Property | Type | Description |
| --- | --- | --- |
| `locators` | array | The ranked locators, best first. Each entry is `{ using, value, strategy, score, verdict }`. |
| `locatorStrength` | string | `Strong`, `Fair` or `Weak`: how stable the `chain.locator` is expected to be. |
| `locatorId` | string | Identifies this event's locators, so a later [locator amendment](peek-and-observe.md#locator-amendments) can correct them. |
| `originalLocator` | string | Only when the event was retargeted: the absolute XPath of the node the pointer landed on. |
| `shadowPath` | array | Only for a target inside a shadow root: the XPath of the outermost host, then one CSS selector per shadow level. See [Shadow DOM](#shadow-dom). |

### How a locator is built

The `using` value is one of the five W3C WebDriver locator strategies, named as G4 names them: `Xpath`, `CssSelector`, `LinkText`, `PartialLinkText` and `TagName`. XPath is the default locator type of a G4 rule, so the best locator is always an XPath and the other strategies are fallbacks.

The XPath candidates are tried in this order:

| Order | Strategy | Built from | Example |
| ---: | --- | --- | --- |
| 1 | `Label` | The `<label>` of a form control. | `//input[@id=//label[normalize-space()='Email']/@for]` |
| 2 | `AriaLabel` | The `aria-label` attribute. | `//button[@aria-label='Close']` |
| 3 | `RoleName` | The role and caption of a button, link, tab or option. | `//button[normalize-space()='Save']` |
| 4 | `Value`, `Placeholder`, `Alt`, `Title` | A naming attribute the user sees or hears. | `//input[@placeholder='Search']` |
| 5 | `TestHook` | An explicit test attribute such as `data-test-id`. | `//*[@data-test-id='checkout']` |
| 6 | `Id`, `NameValue`, `Name`, `Href` | An authored, stable attribute. | `//input[@name='email']` |
| 7 | `Text` | The text of any other element. | `//td[normalize-space()='Total']` |
| 8 | `Structure` | A short indexed path below the nearest ancestor with a stable identity. | `//*[@id='cart']/div[2]/button[1]` |
| 9 | `Absolute` | The absolute path from the document root. It is always present, as the last resort. | `/html[1]/body[1]/div[3]/button[2]` |

A candidate that matches several elements is scoped to an anchor before a position is used. An anchor is a nearby ancestor with its own identity: a test hook, an id, an `aria-label`, a heading that titles it, or, for a repeated row, a text the row contains. The strategy name then starts with `Anchored`:

```text
//tr[.//*[normalize-space()='INV-1042']]//button[normalize-space()='Delete']
```

Only when no anchor works does the recorder fall back to a position among the matches. The strategy name then starts with `Indexed`, for example `(//button[normalize-space()='Save'])[2]`.

Three more rules protect a replay:

- **Generated values are never used.** An id or attribute value that looks generated (a framework counter such as `mat-input-3` or `:r1:`, a hash, a long run of digits) is skipped, because it changes on the next page load.
- **Typed text is never used.** A locator is never built from the text a user typed into a field.
- **The control is recorded, not its inner node.** A click on an icon or a caption inside a button, link or field is recorded against that control. `InvokeScroll` and `SendKeys` are not retargeted.

### Verdicts and hot testing

The recorder keeps testing the locators of `InvokeClick`, `InvokeDoubleClick`, `InvokeContextClick`, `SendKeys` and `MoveMouseCursor` events while the recording runs. The `verdict` of a locator says how much evidence backs it:

| Verdict | Meaning |
| --- | --- |
| `Unique` | The locator resolved to exactly the target when the event was recorded. |
| `Stable` | It still resolved to the same element after the action and the page's reaction to it. |
| `Proven` | It found the same element again on a later load of the same page. |
| `Broken` | On a later load of the same page it no longer found the element, while another locator did. |
| `Unverified` | The target was detached before its locator could be tested. |

A `Broken` locator sorts last and is never the `chain.locator` while a working XPath exists. A locator that stops matching right after its own action is left as it was, not marked `Broken`: the replay uses a locator before the action, so a caption the action itself changed is still the right text to look for.

A changed verdict reaches consumers as a [locator amendment](peek-and-observe.md#locator-amendments), which corrects the event that was already delivered. To test any locator against the live browser yourself, use the [locator test route](peek-and-observe.md#test-a-locator).

The extension options page holds the settings in the **Locators** section: whether test hooks rank above user-facing locators, the list of test hook attributes, retargeting, and hot testing.

### Shadow DOM

No XPath or plain CSS selector reaches inside a shadow root, because neither crosses a shadow boundary. A target inside a shadow root is therefore addressed by a shadow path: one CSS selector per shadow level, joined with `>>>`.

```text
lab-user-card >>> lab-user-menu >>> button[aria-label="Open user menu"]
```

The first segment is found in the document. Every following segment is found inside the shadow root of the element before it. The G4 engine resolves a `CssSelector` rule with this value the same way on replay.

For such a target the event carries three things:

1. The shadow path is the first entry of `locators`, with `using` set to `CssSelector` and `strategy` set to `ShadowPath`. It addresses the target itself.
2. The `chain.locator` and the other `locators` address the outermost host element. A consumer that cannot use a shadow path still has the host to act on.
3. The `shadowPath` property lists the same way as separate steps: the XPath of the outermost host, then one CSS selector per shadow level.

The rules of a shadow path are:

- **The separator counts only outside quoted strings.** In `[title='a >>> b']` the `>>>` is part of the attribute value.
- **Every segment but the last resolves to its first match.** The last segment may match several elements.
- **A selector list is not split per path.** Write one path per locator; `a >>> b, c >>> d` is not two paths.
- **Inside a shadow root only CSS is used.** A segment is built from a test hook, an id, a `name`, an `aria-label`, a `placeholder`, a unique tag, or the position in the shadow tree.
- **The path is as strong as its weakest segment.** The `locatorStrength` of the event describes the shadow path. When no shadow path can be verified, the event falls back to the host and is never above `Fair`.

Open and closed shadow roots are recorded the same way. The page cannot read a closed root, but the extension can, through `chrome.dom.openOrClosedShadowRoot`. A click is followed into a closed root by the pointer position, and typing or a click fired from the keyboard by the focused element. Hot testing and the [locator test route](peek-and-observe.md#test-a-locator) both accept a shadow path.

## Drag and drop

A drag and drop is recorded as one `InvokeDragAndDrop` event. The chain is the dragged element, as for a click, and `value` describes where the drag ended:

```json
{
    "type": "Mouse",
    "event": "InvokeDragAndDrop",
    "chain": { "locator": "//*[@data-test-id='report-card']", "path": [] },
    "value": {
        "kind": "Native",
        "button": 0,
        "X": 612,
        "Y": 310,
        "deltaX": 189,
        "deltaY": 39,
        "durationMilliseconds": 820,
        "target": { "chain": { "locator": "//ul[@aria-label='Done']", "path": [] } }
    }
}
```

| `value` member | Meaning |
| --- | --- |
| `kind` | `Native` for an HTML drag and drop (`dragstart` and `drop`), `Pointer` for a drag the page runs on mouse events. |
| `X`, `Y` | The release point, in viewport coordinates. |
| `deltaX`, `deltaY` | The distance from the center of the dragged element, measured at the press, to the release point. It is the offset the `InvokeDragAndDrop` plugin moves by when the drag has no drop element. |
| `durationMilliseconds` | The time from the press to the release. |
| `target.chain` | The drop element, with its own `locators`, `locatorStrength` and `locatorId`. Left out when the drag ended on nothing distinct. |

The rules are:

- **A press becomes a drag only past a threshold.** The pointer has to move `dragThresholdPixels` (8 by default) on either axis with the button held. A smaller move stays a click.
- **A native drag is recorded on drop.** The browser fires `drop` only on an element that accepted the drag. A drag that ends without a drop, or drops in another frame, records nothing.
- **The drop element is distinct from the dragged element.** It is the first element under the release point that is not the dragged element, one of its ancestors, or one of its descendants. A drag that ends on its own track, such as a slider, has no drop element and is replayed by its distance.
- **The click after a pointer drag is not recorded.** The browser fires one after the release, often on the page body.
- **Text selection is not a drag.** A press inside a text field, a list box, or a media player, a press on a scroll bar, and a release that leaves text selected are not recorded as a drag and drop.
- **Both elements are hot-tested.** The drop element is re-tested and corrected through `ReceiveLocatorAmendment` under its own `locatorId`.

The `dragAndDrop` switch on the options page turns the recording on or off, and it is on by default. The Hub turns the event into an `InvokeDragAndDrop` rule, which drops on the drop element's locator or replays the distance. The drag lab page (`recorder-drag-lab.html`) holds one case for each kind of drag.

## DOM context

Element events carry a snapshot of the markup around their target, so a client can heal or optimise the locator later. The events are `InvokeClick`, `InvokeDoubleClick`, `InvokeContextClick`, `InvokeMiddleClick`, `InvokeDragAndDrop`, `SendKeys` and `MoveMouseCursor`. The snapshot sits on the trigger node, `chain.path[0].properties`:

| Property | Type | Description |
| --- | --- | --- |
| `domContext` | string | Base64 of the UTF-8 HTML snapshot. |
| `domContextLength` | number | The decoded byte count, to check that the whole snapshot was decoded. |

How the snapshot is made:

- It is the outer HTML of the ancestor **Ancestor levels** above the target (default 2). Climbing stops at the document element or a shadow root.
- The target carries `data-g4-target="true"` in the snapshot. The live page is never changed.
- `script`, `style`, `noscript` and `template` elements are removed, SVG drawings keep only their root tag, and `on*` handler attributes are removed.
- The `value` of password inputs and of fields marked `data-sensitive` is blanked, and a sensitive text area loses its text.
- The decoded snapshot is at most **Maximum snapshot size** (default 16 KB). A snapshot that is too large drops one level at a time, then falls back to the target's opening tag. When even that is too large, the event carries no `domContext`.

The extension options page holds the three settings in the **DOM Context** section. The snapshot is on by default.

To read it, decode the Base64 text as UTF-8 and find the element marked `data-g4-target="true"`.

Context events (type `Context`):

| `event` | Trigger | `value` |
| --- | --- | --- |
| `SwitchFrame` | an interaction moves into a frame (deeper or sideways in the frame tree) | `{ frameId, frameUrl, xpath, cssSelector, locators }` — the iframe element locators are resolved for same-origin frames, the same way as for any other element; cross-origin falls back to URL + frameId |
| `SwitchParentFrame` | an interaction moves up to an ancestor frame (e.g. back to the top document) | same shape as `SwitchFrame` for the frame moved into |
| `SwitchWindow` | the active tab changes | `{ tabId, index }` |
| `CloseWindow` | a tab closes | `{ tabId, index }` |

`SwitchWindow` / `CloseWindow` `index` is the 0-based position in the **driver window-handle
list** (creation order, `0` = first/main), matching G4's `SwitchWindow(n)` / `CloseWindow(n)`
semantics — not the Chrome tab-strip position. The recorder tracks this list (seeded from the
open tabs, then updated on tab create/close) and persists it in `chrome.storage.session`.
Closing a tab emits `CloseWindow(index)`, then the browser activating another tab emits the
following `SwitchWindow`. No `tabs` permission is required (`id`/`index` are available without
it).

A switch event is emitted **once per actual frame change** (then subsequent interactions in
the same frame are just the action). Direction is resolved from the frame tree via
`chrome.webNavigation.getAllFrames`, and the active frame per tab is persisted in
`chrome.storage.session` so detection survives a service-worker restart. Expected order
for a frame interaction: `SwitchFrame` → `InvokeClick`/`SendKeys`/…; returning to the
parent: `SwitchParentFrame` → action.

All events can be toggled on the settings page.

## Limits

- A drag is replayed from the center of the dragged element, because the `InvokeDragAndDrop` plugin has no grab offset. A drag that depends on where the element was grabbed, such as drawing on a canvas, starts at a different point on replay.
- A drag from one frame into another is not recorded.

- Browser coordinates are viewport-relative (`clientX` and `clientY`). True screen coordinates are not available to web pages.
- Locators are scoped to each frame document, so cross-frame interactions are recorded with the local path of the frame.
- A `submit` event does not leave a shadow root, so a form submitted inside one is recorded through the click or the typing that caused it, not as `SubmitForm`.
- The candidate work is time-boxed per event. On a very large page the weaker candidates can be skipped; the `Absolute` locator is always present.
- `chain.mouseOffset` is never set by the extension.

## Next

- [Recording event contract](contract.md)
- [Build a flow from events](build-a-flow.md)
