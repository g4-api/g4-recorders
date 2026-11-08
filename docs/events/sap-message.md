# SAP Recording Messages

[⬅ Back to the docs index](../README.md)

The SAP recorder sends the same `ReceiveRecordingEvent` message as the other recorders, in the same `value` envelope. The value holds up to two events, one for each stream. This page lists the message shape, the stream rules and the mapping from SAP commands to events.

## Message

`SapRecordingMessageModel` (`src/G4.Recorders.Sap.Domain/Models/SapRecordingMessageModel.cs`) has two members: `user32` and `nativeSap`. Each is a normal event with the [shared event shape](contract.md). A stream with nothing to report is left out of the message, because the streams do not line up one to one. SAP reports typed text only when the screen is submitted, long after the keystrokes.

```json
{
    "value": {
        "user32": {
            "chain": {},
            "event": "Left Up",
            "machineName": "WORKSTATION",
            "timestamp": 1757618366611,
            "type": "Mouse",
            "value": { "x": 0, "y": 0 }
        },
        "nativeSap": {
            "chain": {},
            "event": "InvokeClick",
            "machineName": "WORKSTATION",
            "timestamp": 1757618366611,
            "type": "Mouse",
            "value": {}
        }
    }
}
```

The broadcast happens in `SapRecordingBroadcaster.cs:29`. The stream names are in `SapRecordingStreams.cs:12,17`.

## Streams

| Stream | Vocabulary | Source |
| --- | --- | --- |
| `user32` | The UIA recorder names: `Mouse` and `Keyboard`, `Left Down`, `Key Up`, with the same `value` members. | Global input hooks, resolved through SAP `findByPosition` or the SAP focus. |
| `nativeSap` | The Chromium style: `event` names the action. | The SAP `GuiSession.Change` event. |

A client picks the stream by mode. User32 and coordinate modes read `user32`. The SAP mode reads `nativeSap`. The recorder client app checks for these two property names to tell a SAP message from other messages (`G4.Recorders.ClientApp/Program.cs:458-461`).

## nativeSap events

`SapActionMapper.cs` turns each command of a SAP `Change` event into one event. Unknown command kinds are skipped.

| SAP reports | `type` | `event` | `value` |
| --- | --- | --- | --- |
| `text` set on a field | `Keyboard` | `SendKeys` | `{ "text" }`. On a password field: `{ "text": "***", "isRedacted": true }`. |
| `press()`, or `select()` on a menu, tab or radio button | `Mouse` | `InvokeClick` | `{}` |
| `sendVKey(n)` | `Keyboard` | `SendSapVirtualKey` | `{ "key": n }` |
| Any other method, such as `pressToolbarButton` | `Mouse` | `InvokeSapMethod` | `{ "method", "arguments" }` |
| Any other property, such as `selectedRows` | `Form` | `SetSapProperty` | `{ "name", "value" }` |

Values on password fields are redacted. The mapper never reads SAP GUI and never logs values (`SapActionMapper.cs:10-30,120-190`).

## SAP chains

SAP chains keep the shared node fields:

- `controlType` holds the SAP type.
- `className` holds the shell subtype, or the type.
- `automationId` holds the SAP name.
- `frameworkId` is `SAPGUI`.
- `properties` carries `Id`, `RelativeId`, `SubType` and `ContainerType`.
- The `path` runs from the window to the trigger.
- The `locator` is an XPath on the absolute scripting identifier, such as `//GuiTextField[@id='/app/con[0]/ses[0]/wnd[0]/usr/txtRSYST-BNAME']`.

Field contents never appear in node names.

## Start parameters

A client starts the SAP recorder with the hub method `StartRecorder` (`SapRecorderHub.cs:105`). The argument is `SapRecorderParametersModel`.

| Field | Type | Meaning |
| --- | --- | --- |
| `sapLogon` | object | `{ "path", "arguments", "connection" }`. How to start SAP Logon. |
| `scope` | object | `{ "systemName", "client", "sessions" }`. Which SAP sessions to record. |
| `setRecordMode` | boolean | Whether to turn on SAP record mode. |
| `streams` | array | The streams to send: `user32`, `nativeSap`, or both. |

An invalid stream name comes back as a hub error. A second client that asks for different values while a recording runs is refused with the message "A recording is already running with different values. Stop the other client, or start with the same values." (`SapRecorderHub.cs:121-124`).

The hub also offers `GetRecorderStatus`, `StopRecorder` and `FlushRecorder` (`SapRecorderHub.cs:88-142`).

## Next

- [Recording event contract](contract.md)
- [Build a flow from events](build-a-flow.md)
