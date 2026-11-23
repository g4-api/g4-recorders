# Peek and Observe

[⬅ Back to the docs index](../README.md)

A peek asks a recorder for the element chain at a place on screen. The answer has the same shape as the `chain` field of a [recording event](contract.md). Each recorder offers peeks over SignalR hub methods and over REST routes. The REST routes also include the "observe" family: screenshot, move pointer, ground and focus. The Chromium recorder can also test a locator against the live page and relay locator amendments.

## Choosing what to peek

All three recorders pick the target the same way (`RecorderPeekRequestResolver.cs`):

1. If `x` or `y` is given, the request is a coordinate peek. A missing axis counts as zero.
2. Otherwise, if `focused` is true, the request peeks the focused element.
3. Otherwise, the request peeks the current pointer position.

## Hub methods

The hub path is `/hub/v4/g4/peek` on each recorder. The answer comes back to the caller only, as the server message `ReceivePeek` with a `{ "value": <chain> }` envelope.

| Hub method | Argument | What it does |
| --- | --- | --- |
| `SendPeekAt` | `{ "X": int, "Y": int }` | Peeks the element at the coordinates. |
| `SendPeekFocused` | none | Peeks the focused element. |
| `SendPeekCurrent` | none | Peeks the element under the current pointer. |
| `SendHeartbeat` | none | Replies with `ReceiveHeartbeat` so a client can check the connection. |

The method names come from the `HubMethodName` attributes in `UiaRecorderHub.cs:76,90,103`, `ChromiumRecorderHub.cs:140,118,164` and `SapRecorderHub.cs:158,168,175`. The argument uses the names `X` and `Y` (`RecorderPointModel.cs`).

| Recorder | Behavior |
| --- | --- |
| UIA | Resolves the chain on the recorder machine. `SendPeekCurrent` throws a hub error "The current physical cursor position is unavailable." when the cursor cannot be read (`UiaRecorderHub.cs:109`). |
| Chromium | Forwards the request to the connected extension and waits for its answer, with a 5-second timeout (`ChromiumRecorderRepository.cs:32`). With no extension, or more than one, the lookup fails with "No Chromium recorder extension is connected." or "More than one Chromium recorder extension is connected; the target is ambiguous." (`ChromiumRecorderRepository.cs`, `GetElementChainAsync`). |
| SAP | Resolves the chain through SAP GUI. `SendPeekCurrent` throws the same cursor message as UIA (`SapRecorderHub.cs:181`). |
| Android | Forwards the request to the connected recorder app and waits for its answer, with a 5-second timeout. Coordinates are screen pixels; `SendPeekCurrent` returns the node of the last interaction. With no app, or more than one, the lookup fails with "No Android recorder app is connected." or "More than one Android recorder app is connected; the target is ambiguous." (`AndroidRecorderRepository.cs`, `GetElementChainAsync`). |

Other hub methods:

| Recorder | Methods |
| --- | --- |
| UIA | `FlushRecorder` |
| Chromium | `RegisterRecorder`, `SendRecordingEvent`, `SendPeekResponse`, `SendLocatorTestResponse`, `SendLocatorAmendment`, `StartRecorder`, `StopRecorder` |
| SAP | `FlushRecorder`, `GetRecorderStatus`, `StartRecorder`, `StopRecorder` |
| Android | `FlushRecorder`, `RegisterRecorder`, `SendRecordingEvent`, `SendPeekResponse`, `SendFlushResponse`, `StartRecorder`, `StopRecorder` |

The extension handles the server message `ReceivePeekRequest` and answers with `SendPeekResponse`. A client never calls these two. The extension also answers `ReceiveLocatorTestRequest` with `SendLocatorTestResponse` and sends `SendLocatorAmendment`; see [Test a locator](#test-a-locator) and [Locator amendments](#locator-amendments). The Android recorder app does the same, and also answers `ReceiveFlushRequest` with `SendFlushResponse` and handles `CloseRecorder`.

## REST routes

The route prefix is `/api/v4/g4/recorders/<recorder>/recorder`, where `<recorder>` is `uia`, `chromium`, `sap` or `android`. The ports are 9955 for UIA, 9956 for Chromium, 9957 for SAP and 9958 for Android.

| Method and route | Query | What it does |
| --- | --- | --- |
| `GET element` | `x`, `y`, `focused` | Peeks and returns the chain. |
| `GET observe` | `metricsOnly` | Captures the virtual desktop as a Base64 PNG with its coordinate metrics. |
| `POST point` | `x`, `y` | Moves the physical cursor and returns the position it settled at. |
| `POST ground` | `x`, `y`, `skipOffset` | Moves the cursor, resolves the element at the settled position, and fills the offset unless `skipOffset` is true. |
| `POST focus` | `windowTitle`, `processName` | Brings a matching window to the foreground and reports `found`, `focused`, `processId`, `processName` and `windowTitle`. |

The observe routes act on the machine that runs the recorder, not on the caller machine.

### Which routes work on which recorder

| Route | UIA | Chromium | SAP | Android |
| --- | --- | --- | --- | --- |
| `GET element` | Works. `500` if the cursor cannot be read. | Works. `503` if no single extension is connected. `504` if the extension does not answer in time. | Works. `500` if the cursor cannot be read. `503` if SAP Logon is not running, scripting is disabled, or SAP GUI does not answer in time. | Works. `503` if no single recorder app is connected. `504` if the app does not answer in time. |
| `GET observe` | Works. | Not implemented. `501`. | Works. | Works, through adb on the device being recorded. `503` if adb or the device is unavailable. |
| `POST point` | Works. | Not implemented. `501`. | Works. | Not implemented. `501`. |
| `POST ground` | Works. | Not implemented. `501`. | Works. `503` for the same reasons as `element`. | Not implemented. `501`. |
| `POST focus` | Works. | Not implemented. `501`. | Works. `400` if neither `windowTitle` nor `processName` is given. | Not implemented. `501`. |

For Chromium, the repository throws `NotImplementedException` for `GetScreenshot`, `MovePointer`, `ResolveGroundedElement` and `SetWindowFocus` (`ChromiumRecorderRepository.cs:196,204,257,265`). The controller turns each one into a `501` problem response (`src/G4.Recorders.Chromium/Controllers/RecorderController.cs:133,165,205,237`).

The SAP recorder also has `GET status`, which returns the recording status of each session (`src/G4.Recorders.Sap/Controllers/RecorderController.cs:274`).

### Test a locator

Only the Chromium recorder has this route. `POST locator` runs a locator against the live page and reports what it matches. The extension evaluates the locator in every frame of the page.

The request body is JSON.

| Field | Type | Meaning |
| --- | --- | --- |
| `using` | string | The locator strategy. Optional. The default is `Xpath`. The value is matched without regard to case. |
| `value` | string | The locator expression. Required. |

The allowed strategies are `Xpath`, `CssSelector`, `LinkText`, `PartialLinkText` and `TagName`. The recorder sends the strategy to the extension in exactly this casing.

The answer is JSON.

| Field | Type | Meaning |
| --- | --- | --- |
| `using` | string | The strategy that was tested, in its exact casing. |
| `value` | string | The expression that was tested. |
| `count` | number | The total number of matches across all frames. |
| `matches` | array | The first matches only, because the extension caps the list. Each match has `frameId`, `frameUrl` and `chain`. The `chain` has the same shape as a peek answer. |

| Status | When |
| --- | --- |
| `200` | The locator was evaluated. `count` is zero and `matches` is empty when nothing matched. |
| `400` | `value` is missing or blank, or `using` is not one of the allowed strategies. |
| `503` | No single extension is connected, the extension reported an error, or the extension disconnected during the test. |
| `504` | The extension did not answer within 5 seconds. |

The recorder forwards the test to the extension as the server message `ReceiveLocatorTestRequest` with `{ "requestId", "using", "value" }`. The extension answers with the hub method `SendLocatorTestResponse` and `{ "requestId", "error", "count", "matches" }`. A client never calls these two (`ChromiumRecorderRepository.cs`, `TestLocatorAsync`).

## Locator amendments

The Chromium extension can correct a locator after it sent that locator to the recorder. It calls the hub method `SendLocatorAmendment`. The recorder then broadcasts the server message `ReceiveLocatorAmendment` to every client, with a `{ "value": <amendment> }` envelope. The message name is separate from `ReceiveRecordingEvent`, so a client that only listens to recording events is not affected.

| Field | Type | Meaning |
| --- | --- | --- |
| `locatorId` | string | The id of the earlier locator that the amendment corrects. Required. The recorder ignores an amendment without it. |
| `locator` | string | The corrected locator. |
| `locators` | array | The corrected alternative locators. Each entry is a free-form object, for example `{ "using": "Xpath", "value": "//button", "strategy": "RoleName", "score": 92, "verdict": "Stable" }`. The recorder relays each entry unchanged. |
| `locatorStrength` | string | The strength of the corrected locator. |
| `reason` | string | The test that changed the verdicts: `Settle` (after the recorded action) or `Revisit` (on a later load of the same page). See [verdicts and hot testing](chromium-events.md#verdicts-and-hot-testing). |
| `timestamp` | number | Unix epoch in milliseconds. |

Null fields are left out of the broadcast (`ChromiumEventCaptureService.cs`, `BroadcastLocatorAmendmentAsync`).

## Examples

Peek the focused element of the UIA recorder.

```bash
curl "http://localhost:9955/api/v4/g4/recorders/uia/recorder/element?focused=true"
```

Peek the element at a point in the browser viewport.

```bash
curl "http://localhost:9956/api/v4/g4/recorders/chromium/recorder/element?x=250&y=300"
```

Ask Chromium for a screenshot and read the `501` answer.

```bash
curl -i "http://localhost:9956/api/v4/g4/recorders/chromium/recorder/observe"
```

Test a CSS selector against the page in the recorder browser.

```bash
curl -X POST "http://localhost:9956/api/v4/g4/recorders/chromium/recorder/locator" \
    -H "Content-Type: application/json" \
    -d '{ "using": "CssSelector", "value": "button[type=submit]" }'
```

## Next

- [Recording event contract](contract.md)
- [Build a flow from events](build-a-flow.md)
