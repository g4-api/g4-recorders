# Recording Event Contract

[⬅ Back to the docs index](../README.md)

Every recorder broadcasts the SignalR message `ReceiveRecordingEvent`. The payload is always wrapped in a `value` envelope. This page lists every field of that payload and every event name each recorder can send.

The field names below are the JSON names on the wire. The serializer writes camelCase and leaves out null properties (`src/G4.Recorders.Uia/Program.cs:93,102`, `src/G4.Recorders.Sap/Program.cs:92`).

## Envelope

| Sender | Where the envelope is built | Shape |
| --- | --- | --- |
| UIA recorder | `UiaEventCaptureService.cs` | `{ "value": <event> }` |
| Chromium recorder | `ChromiumEventCaptureService.cs:34` | `{ "value": <event> }` |
| SAP recorder | `SapRecordingBroadcaster.cs:29` | `{ "value": { "user32": <event>, "nativeSap": <event> } }` (see [SAP messages](sap-message.md)) |
| Android recorder | `AndroidEventCaptureService.cs` | `{ "value": <event> }` |

## Event

`RecorderEventModel` (`src/G4.Recorders.Common.Domain/Models/RecorderEventModel.cs`)

| Field | Type | Meaning |
| --- | --- | --- |
| `chain` | object | The element chain. See the next table. |
| `event` | string | The event name. See [Event names](#event-names). |
| `machineName` | string | The machine that recorded the event. Defaults to the machine name of the recorder. |
| `timestamp` | number | Unix epoch in milliseconds. |
| `type` | string | The event category. See [Event types](#event-types). |
| `value` | object | The event payload. Its fields depend on the event name. |

There is no event-level `offset`. The pointer offset lives in `chain.mouseOffset`.

## Chain

`ChainModel` (`RecorderChainModel.cs`)

| Field | Type | Meaning |
| --- | --- | --- |
| `locator` | string | A locator for the trigger element. UIA and SAP use an XPath. Chromium uses an absolute XPath. Android uses an XPath in the UiAutomator2 page-source vocabulary. |
| `path` | array | The nodes between the top window and the trigger element. UIA and SAP list them from the window down, so the last node is the trigger. Chromium and Android list them from the trigger up, so the first node is the trigger. |
| `point` | object | The pointer position as `{ "X", "Y" }`, when known. |
| `mouseOffset` | object | The pointer position inside the trigger element as `{ "x", "y" }`. Set for UIA mouse and wheel events. Chromium never sets it. |
| `topWindow` | node | The top-level window node, when known. |
| `trigger` | string | What caused the chain, for example `Focus`. |

## Node

`RecorderNodeModel` (`RecorderNodeModel.cs`)

| Field | Type | Meaning |
| --- | --- | --- |
| `automationId` | string | The automation id. Chromium puts the element `id` here. SAP puts the SAP name here. Android puts the resource id here. |
| `bounds` | object | `{ "height", "X", "Y", "width" }`. `X` is the left edge and `Y` is the top edge. |
| `className` | string | The class name. |
| `controlTypeId` | number | The UIA control type id. Zero where there is no equivalent. |
| `controlType` | string | The control type. Chromium puts the ARIA role or tag name here. SAP puts the SAP type here. Android puts the short view class here. |
| `frameworkId` | string | The UI framework. SAP uses `SAPGUI`, and Android uses `Android`. |
| `isTopWindow` | boolean | True for the top-level window. |
| `isTriggerElement` | boolean | True for the element that caused the event. |
| `machine` | object | `{ "name", "publicAddress" }`. |
| `name` | string | The accessible name. |
| `patterns` | array | UIA patterns as `{ "id", "name" }`. |
| `processId` | number | The process id of the owning process. |
| `properties` | object | Extra properties. Chromium adds `cssSelector`, and on the trigger node of element events `domContext` and `domContextLength` (see [Chromium events](chromium-events.md#dom-context)). SAP adds `Id`, `RelativeId`, `SubType` and `ContainerType`. Android adds the view state and, on the trigger node of element events, `domContext` and `domContextLength` (see [Android events](android-events.md#view-context)). |
| `runtimeId` | array | The UIA runtime id. |

The model also holds an `Element` member, but it is marked `JsonIgnore` and is never sent.

`RecorderPointModel` uses the JSON names `X` and `Y` (`RecorderPointModel.cs`). `RecorderOffsetModel` uses `x` and `y` (`RecorderOffsetModel.cs`).

## Event types

| Type | Sent by | Used for |
| --- | --- | --- |
| `Keyboard` | UIA, Chromium, SAP, Android | Key presses, typed text, SAP virtual keys, the Android keyboard closing. |
| `Mouse` | UIA, Chromium, SAP, Android | Clicks, taps, long presses, wheel, scroll, SAP method calls. |
| `Form` | Chromium, SAP | Form submits, SAP property assignments. |
| `Navigation` | Chromium, Android | Address-bar navigation, reload, back and forward; the Android Back key. |
| `Context` | Chromium, Android | Frame and window switches, Android activity switches. |

## Event names

### UIA recorder

| Type | Event names | `value` fields | Source |
| --- | --- | --- | --- |
| Keyboard | `Key Down`, `Key Up` | `scanCode`, `virtualKey`, `key`, `modifiers`, `targetSource` | `UiaEventCaptureService.cs:843-852` |
| Mouse button | `Left Down`, `Left Up`, `Right Down`, `Right Up`, `Middle Down`, `Middle Up` | `x`, `y` | `UiaEventCaptureService.cs:1194-1203` |
| Mouse wheel | `Wheel Up`, `Wheel Down`, `Wheel Left`, `Wheel Right` | `notches`, `x`, `y` | `UiaEventCaptureService.cs:908-947` |

`modifiers` is a list of `Ctrl`, `Alt` and `Shift` (`KeyboardModifierState.cs:19,24,29`).

`targetSource` says how the target element was found. It is one of `CapturedSnapshot`, `FocusFallback`, `MissingSnapshot`, `OrphanFallback`, `PairedRelease` or `RepeatedPress` (`KeyboardTargetResolver.cs:221-228`).

### Chromium recorder

The Chromium names follow the G4 action style. The full list, with triggers and `value` fields, is in [Chromium events](chromium-events.md).

### Android recorder

The Android names follow the G4 action style too: `InvokeClick`, `InvokeLongClick`, `SendKeys`, `InvokeScroll`, `HideSoftKeyboard`, `SwitchActivity` and `UndoNavigation`. The full list, with triggers and `value` fields, is in [Android events](android-events.md).

### SAP recorder

The `user32` stream uses the UIA names above. The `nativeSap` stream uses `SendKeys`, `InvokeClick`, `SendSapVirtualKey`, `InvokeSapMethod` and `SetSapProperty`. See [SAP messages](sap-message.md).

## Sample

A UIA keyboard event as a client receives it.

```json
{
    "value": {
        "chain": {
            "locator": "/Desktop/Window[@Name='Console']/Document[@Name='Text Area']",
            "path": [
                {
                    "automationId": "Console Window",
                    "className": "ConsoleWindowClass",
                    "controlTypeId": 50032,
                    "controlType": "Window",
                    "isTopWindow": true,
                    "isTriggerElement": false,
                    "name": "Console",
                    "processId": 31036
                },
                {
                    "automationId": "Text Area",
                    "bounds": { "height": 519, "X": 104, "Y": 104, "width": 993 },
                    "controlTypeId": 50030,
                    "controlType": "Document",
                    "isTopWindow": false,
                    "isTriggerElement": true,
                    "name": "Text Area",
                    "processId": 31036
                }
            ],
            "trigger": "Focus"
        },
        "event": "Key Down",
        "machineName": "WORKSTATION",
        "timestamp": 1757618366611,
        "type": "Keyboard",
        "value": {
            "scanCode": 30,
            "virtualKey": 65,
            "key": "a",
            "modifiers": [],
            "targetSource": "CapturedSnapshot"
        }
    }
}
```

## Next

- [Chromium events](chromium-events.md)
- [SAP messages](sap-message.md)
- [Android events](android-events.md)
- [Build a flow from events](build-a-flow.md)
