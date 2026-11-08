# G4 Chromium Recorder

A vanilla-JavaScript Manifest V3 browser extension that records interactions in the
browser and streams them to the G4 **Chromium recorder** SignalR hub (`ChromiumRecorderHub`) using the same response
contract as the **UIA** desktop recorder. A consumer client connected to the hub
receives each interaction as a `ReceiveRecordingEvent` broadcast whose payload is shaped
like a UIA recorder event.

The extension is fully offline: the official Microsoft SignalR client is vendored into
the project and no script is ever fetched from a network at runtime.

## How it works

```text
DOM event (any frame)
  -> content-script.js builds a RecorderEventModel (XPath locator + CSS, ARIA mapping)
  -> chrome.runtime message to the background service worker
  -> background.js invokes SendRecordingEvent on the hub (SignalR over WebSocket)
  -> C# ChromiumRecorderHub re-broadcasts ReceiveRecordingEvent { value: <event> }
  -> consumer clients receive the event
```

Recording is **always-on while connected**. Each successful connect (including an
automatic reconnect) starts a new session and clears the captured-event stack shown in
the popup.

## Contract

The payload matches the shared recorder C# models in `G4.Recorders.Common.Domain` (camelCase on the wire):

- `RecorderEventModel` — `{ chain, event, machineName, timestamp, type, value }`
- `ChainModel` — `{ locator, path[], point, mouseOffset, topWindow, trigger }`
- `RecorderNodeModel` — `{ automationId, bounds, className, controlTypeId, controlType,
  frameworkId, isTopWindow, isTriggerElement, machine, name, patterns[], processId,
  properties, runtimeId[] }`
- `BoundsRectangle` — `{ height, width, X, Y }` (X = Left, Y = Top)
- `RecorderPointModel` — `{ X, Y }`
- `RecorderOffsetModel` — `{ x, y }`, the shape of `chain.mouseOffset`. The extension does not set it today.

The extension's contract builder (`lib/recorder-contract.js`) still adds an event-level `offset`. The hub models have no such property, so it is not part of the contract and is not forwarded to consumers.

### DOM → contract mapping

| Contract field | Source in the browser |
| --- | --- |
| `locator` | absolute XPath of the trigger element |
| `properties.cssSelector` | unique CSS selector of the element |
| `controlType` | ARIA `role`, else the tag name |
| `automationId` | element `id`, else `data-test-id` |
| `name` | accessible name (aria-label, aria-labelledby, alt/title/placeholder, text) |
| `className` | the element `class` attribute |
| `bounds` | `getBoundingClientRect()` |
| `machine.name` / `machine.publicAddress` | page hostname / page origin |

UI Automation-only fields (`controlTypeId`, `frameworkId`, `patterns`, `processId`,
`runtimeId`) have no DOM equivalent and are emitted as empty/zero defaults so the JSON
shape stays identical.

### Captured events

The event catalog moved to [docs/events/chromium-events.md](../../docs/events/chromium-events.md). It lists every `event` name, its trigger, its `type` and its `value`.

## Project layout

```text
manifest.json
js/
  signalr.min.js        vendored official @microsoft/signalr 8.0.7 (offline)
  background.js         classic service worker: connection, session, stack, relay
  content-script.js     DOM capture in every frame
lib/
  constants.js          shared config, channels, defaults
  settings-store.js     chrome.storage.sync wrapper
  recorder-contract.js  contract builders
  dom-locator.js        absolute XPath + CSS selector
  dom-mapper.js         element -> node, chain builder
  event-catalog.js      DOM event -> { type, event, value }
  signalr-connection.js wrapper over the official SignalR client
  form-controls.js      on/off switch + themed number field enhancers
css/                    parameters / layout / visual split per component (+ fonts.css,
                        form-controls.*), palette matched to the G4 designer
fonts/                  Inter (body/UI) + Space Grotesk (titles), self-hosted (offline)
components/
  popup/                popup.html / popup.js (+ layout, parameters, visual CSS)
  options/              options.html / options.js (+ layout, parameters, visual CSS)
  form-controls/        form-controls.js (+ layout, parameters, visual CSS)
icons/
```

## Location & loading

The extension lives in the `src\G4.Recorders.Chromium.Extension` folder of this repository
and is linked into the `G4.Recorders.Chromium` host project. On build,
`G4.Recorders.Chromium.csproj` copies it into the output under
`G4.Recorders.Chromium.Extension`, and `ChromiumRecorderLauncher` launches
Chrome with the browser load-extension argument pointing at that output folder. So
running `G4.Recorders.Chromium` auto-loads the extension — no manual step required.

### Manual load (optional)

1. Build/start the C# `G4.Recorders.Chromium` hub so it listens on `http://localhost:9956`.
2. Open `chrome://extensions` in a Chromium browser.
3. Enable **Developer mode**.
4. Click **Load unpacked** and select the `src\G4.Recorders.Chromium.Extension` folder
   of this repository.
5. Open the popup to confirm the status shows **Connected**.

## Settings

Open the popup and click **Settings** (or use the browser's extension options):

- **Hub URL** — the SignalR hub endpoint (default `http://localhost:9956/hub/v4/g4/peek`).
- **Connect automatically** — connect on browser start.
- **Reconnect delays** — backoff for automatic reconnect.
- **Redact passwords** — mask values from password/sensitive fields (default off).
- **Maximum events per session** — popup stack cap (default 200).
- **Events to capture** — per-event-type toggles.

> Changing the hub host or port may require updating `host_permissions` in
> `manifest.json`, because extension host permissions are static.

## Server-side requirement

The C# `G4.Recorders.Chromium` hub exposes a relay method the extension invokes:

```csharp
[HubMethodName(name: nameof(SendRecordingEvent))]
public Task SendRecordingEvent(ChromiumEventModel recordingEvent)
{
    return _eventCapture.BroadcastRecordingEventAsync(recordingEvent);
}

// ChromiumEventCaptureService.BroadcastRecordingEventAsync
return _hubContext.Clients.All.SendAsync(
    method: "ReceiveRecordingEvent",
    arg1: new { Value = recordingEvent });
```

The hub's CORS origin check must also allow the extension origin
(`chrome-extension://...`) so the WebSocket upgrade passes SignalR origin validation.

## Notes & limitations

- The connection uses `skipNegotiation` with the WebSockets transport so the official
  SignalR client runs inside a service worker (no `XMLHttpRequest`/`document`).
- Browser coordinates are viewport-relative (`clientX`/`clientY`); true screen
  coordinates are not available to web pages.
- Locators are scoped to each frame's own document; cross-frame interactions are
  recorded with the frame's local path.
