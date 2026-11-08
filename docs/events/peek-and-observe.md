# Peek and Observe

[⬅ Back to the docs index](../README.md)

A peek asks a recorder for the element chain at a place on screen. The answer has the same shape as the `chain` field of a [recording event](contract.md). Each recorder offers peeks over SignalR hub methods and over REST routes. The REST routes also include the "observe" family: screenshot, move pointer, ground and focus.

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

The method names come from the `HubMethodName` attributes in `UiaRecorderHub.cs:76,90,103`, `ChromiumRecorderHub.cs:74,98,119` and `SapRecorderHub.cs:158,168,175`. The argument uses the names `X` and `Y` (`RecorderPointModel.cs`).

| Recorder | Behavior |
| --- | --- |
| UIA | Resolves the chain on the recorder machine. `SendPeekCurrent` throws a hub error "The current physical cursor position is unavailable." when the cursor cannot be read (`UiaRecorderHub.cs:109`). |
| Chromium | Forwards the request to the connected extension and waits for its answer, with a 5-second timeout (`ChromiumRecorderRepository.cs:29`). With no extension, or more than one, the lookup fails with "No Chromium recorder extension is connected." or "More than one Chromium recorder extension is connected; the target is ambiguous." (`ChromiumRecorderRepository.cs`, `GetElementChainAsync`). |
| SAP | Resolves the chain through SAP GUI. `SendPeekCurrent` throws the same cursor message as UIA (`SapRecorderHub.cs:181`). |

Other hub methods:

| Recorder | Methods |
| --- | --- |
| UIA | `FlushRecorder` |
| Chromium | `RegisterRecorder`, `SendRecordingEvent`, `SendPeekResponse`, `StartRecorder`, `StopRecorder` |
| SAP | `FlushRecorder`, `GetRecorderStatus`, `StartRecorder`, `StopRecorder` |

The extension handles the server message `ReceivePeekRequest` and answers with `SendPeekResponse`. A client never calls these two.

## REST routes

The route prefix is `/api/v4/g4/recorders/<recorder>/recorder`, where `<recorder>` is `uia`, `chromium` or `sap`. The ports are 9955 for UIA, 9956 for Chromium and 9957 for SAP.

| Method and route | Query | What it does |
| --- | --- | --- |
| `GET element` | `x`, `y`, `focused` | Peeks and returns the chain. |
| `GET observe` | `metricsOnly` | Captures the virtual desktop as a Base64 PNG with its coordinate metrics. |
| `POST point` | `x`, `y` | Moves the physical cursor and returns the position it settled at. |
| `POST ground` | `x`, `y`, `skipOffset` | Moves the cursor, resolves the element at the settled position, and fills the offset unless `skipOffset` is true. |
| `POST focus` | `windowTitle`, `processName` | Brings a matching window to the foreground and reports `found`, `focused`, `processId`, `processName` and `windowTitle`. |

The observe routes act on the machine that runs the recorder, not on the caller machine.

### Which routes work on which recorder

| Route | UIA | Chromium | SAP |
| --- | --- | --- | --- |
| `GET element` | Works. `500` if the cursor cannot be read. | Works. `503` if no single extension is connected. `504` if the extension does not answer in time. | Works. `500` if the cursor cannot be read. `503` if SAP Logon is not running, scripting is disabled, or SAP GUI does not answer in time. |
| `GET observe` | Works. | Not implemented. `501`. | Works. |
| `POST point` | Works. | Not implemented. `501`. | Works. |
| `POST ground` | Works. | Not implemented. `501`. | Works. `503` for the same reasons as `element`. |
| `POST focus` | Works. | Not implemented. `501`. | Works. `400` if neither `windowTitle` nor `processName` is given. |

For Chromium, the repository throws `NotImplementedException` for `GetScreenshot`, `MovePointer`, `ResolveGroundedElement` and `SetWindowFocus` (`ChromiumRecorderRepository.cs:152,160,202,210`). The controller turns each one into a `501` problem response (`src/G4.Recorders.Chromium/Controllers/RecorderController.cs:105,140,245,280`).

The SAP recorder also has `GET status`, which returns the recording status of each session (`src/G4.Recorders.Sap/Controllers/RecorderController.cs:274`).

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

## Next

- [Recording event contract](contract.md)
- [Build a flow from events](build-a-flow.md)
