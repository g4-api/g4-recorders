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
| `InvokeScroll` | wheel/scroll (direction + notches in `value`) | Mouse |
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

Context events (type `Context`):

| `event` | Trigger | `value` |
| --- | --- | --- |
| `SwitchFrame` | an interaction moves into a frame (deeper or sideways in the frame tree) | `{ frameId, frameUrl, xpath, cssSelector }` — the iframe element locator is resolved for same-origin frames; cross-origin falls back to URL + frameId |
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

- Browser coordinates are viewport-relative (`clientX` and `clientY`). True screen coordinates are not available to web pages.
- Locators are scoped to each frame document, so cross-frame interactions are recorded with the local path of the frame.
- `chain.mouseOffset` is never set by the extension.

## Next

- [Recording event contract](contract.md)
- [Build a flow from events](build-a-flow.md)
