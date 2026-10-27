# G4 Recorders — UI Automation, Chromium & SAP Recording Tools

[![Build & Release](https://github.com/g4-api/g4-recorders/actions/workflows/release-pipline.yml/badge.svg)](https://github.com/g4-api/g4-recorders/actions/workflows/release-pipline.yml)

## Table of Contents

* [What is this?](#what-is-this)
* [The Tools at a Glance](#the-tools-at-a-glance)
* [Quick Start](#quick-start)
  * [Download and Run](#download-and-run)
  * [Verify the Services](#verify-the-services)
* [Recording Event Contract (shared)](#recording-event-contract-shared)
* [UIA Recorder — Desktop Recorder](#uia-recorder--desktop-recorder)
  * [Command-Line Usage](#command-line-usage)
  * [cURL API Usage](#curl-api-usage)
  * [SignalR Hub Usage](#signalr-hub-usage)
* [Chromium Recorder — Browser Recorder](#chromium-recorder--browser-recorder)
  * [How It Works](#how-it-works)
  * [SignalR Hub Usage (Chromium Recorder)](#signalr-hub-usage-chromium-recorder)
  * [Launching and Stopping a Browser](#launching-and-stopping-a-browser)
  * [Health Check](#health-check)
* [SAP Recorder — SAP GUI Recorder](#sap-recorder--sap-gui-recorder)
  * [How the SAP Recorder Works](#how-the-sap-recorder-works)
  * [SAP Recording Messages](#sap-recording-messages)
  * [Starting the SAP Recorder](#starting-the-sap-recorder)
  * [SAP REST API](#sap-rest-api)
  * [SAP MCP Tools and Command Line](#sap-mcp-tools-and-command-line)
  * [SignalR Hub Usage (SAP Recorder)](#signalr-hub-usage-sap-recorder)
  * [SAP Requirements and Limits](#sap-requirements-and-limits)
  * [SAP Simulator Tests](#sap-simulator-tests)
* [SignalR Client Examples](#signalr-client-examples)
  * [Python Example](#python-example)
  * [JavaScript Example](#javascript-example)
  * [C# Example](#c-example)
* [G4.Recorders.ClientApp — Monitor the Hubs](#g4recordersclientapp--monitor-the-hubs)
* [G4 Recorders Path Finder — Quick Start](#g4-recorders-path-finder--quick-start)
  * [Requirements](#requirements)
  * [Launch](#launch)
  * [Start peeking](#start-peeking)
  * [Adjust update speed](#adjust-update-speed)
  * [Copy the locator](#copy-the-locator)
  * [Stop peeking](#stop-peeking)
  * [Typical workflow](#typical-workflow)
  * [Tips](#tips)
  * [Troubleshooting](#troubleshooting)
  * [Keyboard & accessibility](#keyboard--accessibility)
  * [Exit](#exit)
* [License](#license)

---

## What is this?

G4 Recorders is a **growing suite of recording tools that share one event contract**. It currently ships three recorders, with **Android and iOS recorders planned** to extend the same contract to mobile platforms:

* **UIA Recorder** (`G4.Recorders.Uia`) — a Windows **UI Automation** inspection and recording tool. It peeks at desktop UI elements (returning their **ancestor chain**, like an XPath for Windows UI) and records global keyboard and mouse events enriched with UI context.
* **Chromium Recorder** (`G4.Recorders.Chromium`) — a **browser** recording tool. A bundled Chromium **recorder extension** captures in-page interactions (with a DOM locator chain) and streams them to a hub, which relays them to consumers.
* **SAP Recorder** (`G4.Recorders.Sap`) — a **SAP GUI for Windows** recording tool. It peeks at SAP GUI controls through SAP GUI Scripting and records two streams at once: user input (`user32`) and SAP's own actions (`nativeSap`).

Every recorder in the suite **broadcasts the same `ReceiveRecordingEvent` message over SignalR**, so a consumer can watch interactions across desktop, browser, and (in the future) mobile platforms through a single, uniform contract.

Use cases:

* Debug and inspect desktop applications and web pages to troubleshoot UI behavior.
* Build automated UI tests or robotic process automation (RPA) workflows.
* Record user input with UI context for replay or analysis.
* Monitor UI and user actions in real time.

---

## The Tools at a Glance

| Tool                 | Records                          | Hub port | How recording starts                | REST surface    |
| -------------------- | --------------------------------- | -------- | ------------------------------------ | --------------- |
| **UIA Recorder**     | Windows desktop (global input)   | `9955`   | Automatically (always-on capture)    | `peek` + `ping` |
| **Chromium Recorder** | Chromium browser (via extension) | `9956`   | When a recorder browser is running   | `peek` + `ping` |
| **SAP Recorder**     | SAP GUI (input and SAP actions)  | `9957`   | While a hub client is connected      | UIA surface + `status` + `logon` |

All hubs share the path `/hub/v4/g4/peek` and emit the same `ReceiveRecordingEvent` payload (see [Recording Event Contract](#recording-event-contract-shared)).

---

## Quick Start

> **Requirements:** .NET 10 Runtime/SDK. UIA Recorder requires **Windows** (and Administrator privileges are recommended for global input hooks). Chromium Recorder additionally requires a **Chromium/Chrome** browser with the recorder extension loaded.

### Download and Run

1. Go to **Releases** and download the latest artifact:
   👉 **Releases:** [https://github.com/g4-api/g4-recorders/releases](https://github.com/g4-api/g4-recorders/releases)
2. Unzip the archive to a folder with write permissions (e.g., `C:\Tools\G4.Recorders`).
3. Run the executable(s) from that folder:
   * `G4.Recorders.Uia.exe` — desktop recorder + peek API (listens on `9955`). Run **as administrator** for global hooks.
   * `G4.Recorders.Chromium.exe` — browser recorder hub (listens on `9956`).
   * `G4.Recorders.Sap.exe` — SAP GUI recorder + peek API (listens on `9957`); no administrator rights needed.

### Verify the Services

Each service exposes a Swagger UI and a health-check endpoint:

```bash
# UIA Recorder
curl http://localhost:9955/api/v4/g4/ping
# Output: Pong

# Chromium Recorder
curl http://localhost:9956/api/v4/g4/ping
# Output: Pong
```

* UIA Recorder Swagger: `http://localhost:9955/swagger`
* Chromium Recorder Swagger: `http://localhost:9956/swagger`

> **CORS tip:** If you call from a webview or browser, ensure your origin is allowed via the `ORIGINS` env var or `AllowedOrigins` config. VS Code webviews (`vscode-webview://`) and the Chromium recorder extension (`chrome-extension://`) are already handled.

---

## Recording Event Contract (shared)

All tools broadcast recorded interactions to connected clients using the SignalR message **`ReceiveRecordingEvent`**. The payload is always wrapped in a `{ "value": { ... } }` envelope with this shape:

* `chain` — the element chain (locator + ordered `path` of nodes) where the event occurred.
* `event` — the specific event name (e.g., `Down`, `Click`).
* `machineName` — the machine that recorded the event.
* `offset` — the pointer distance from the target rectangle's top-left corner, defaulting to `{ "x": 0, "y": 0 }`.
* `timestamp` — Unix epoch **milliseconds**.
* `type` — the event category (e.g., `Keyboard`, `Mouse`).
* `value` — the event payload (e.g., the pressed key, or coordinates).

**Sample `ReceiveRecordingEvent` payload (UIA Recorder keyboard event):**

```json
{
    "value": {
        "chain": {
            "locator": "/Desktop/Window[@Name='...']/Document[@Name='Text Area']",
            "path": [
                {
                    "automationId": "Console Window",
                    "className": "ConsoleWindowClass",
                    "controlTypeId": 50032,
                    "controlType": "Window",
                    "isTopWindow": true,
                    "isTriggerElement": false,
                    "name": "...exe",
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
        "event": "Down",
        "offset": { "x": 0, "y": 0 },
        "timestamp": 1757618366611,
        "type": "Keyboard",
        "value": { "scanCode": 30, "virtualKey": 65, "key": "a" }
    }
}
```

> **Note:** The `path` array is ordered **top-down**. The **last element** is always the **target element** (the trigger) and carries more metadata than its ancestors to keep the payload compact. Chromium Recorder events use the same top-level shape; only the chain **node contents** differ (DOM elements instead of UIA elements).

For UIA mouse events, `offset.x` is the captured pointer X coordinate minus the trigger bounds' `X`, and
`offset.y` is the pointer Y coordinate minus the trigger bounds' `Y`. Chromium events currently retain the shared
zero default until DOM-relative offset capture is enabled.

---

## UIA Recorder — Desktop Recorder

UIA Recorder listens on **`http://localhost:9955`**. Recording events flow **automatically** as soon as the service is running — there is no session to start; move the mouse or type and events are broadcast to every connected client.

### Command-Line Usage

```bash
# Peek at focused element
G4.Recorders.Uia peek -f

# Peek at specific coordinates
G4.Recorders.Uia peek -x 100 -y 200
```

### cURL API Usage

The **RecorderController** provides REST endpoints to peek at UI elements.

```bash
# Peek at specific coordinates (x=250, y=300)
curl "http://localhost:9955/api/v4/g4/recorder/element?x=250&y=300"

# Peek at the currently focused element
curl "http://localhost:9955/api/v4/g4/recorder/element?focused=true"

# Peek at the current physical cursor position
curl "http://localhost:9955/api/v4/g4/recorder/element"
```

The response is the ancestor `chain` object (same shape as the `chain` field in a recording event).

### SignalR Hub Usage

**Hub URL:** `http://localhost:9955/hub/v4/g4/peek`

**Client → Server Methods**

* `SendHeartbeat()`
* `SendPeekAt({ xPos, yPos })`
* `SendPeekFocused()`
* `SendPeekCurrent()`

**Server → Client Events**

* `ReceiveHeartbeat` — heartbeat confirmation
* `ReceivePeek` — ancestor chain result
* `ReceiveRecordingEvent` — keyboard/mouse events with UI context (broadcast automatically)

See [SignalR Client Examples](#signalr-client-examples) for Python, JavaScript, and C# clients.

---

## Chromium Recorder — Browser Recorder

Chromium Recorder listens on **`http://localhost:9956`**. Unlike UIA Recorder, it does not capture anything by itself — a **Chromium recorder extension** running in a browser produces the events.

### How It Works

1. A client asks Chromium Recorder to launch a browser (`StartRecorder`), or you start a browser that already has the recorder extension loaded.
2. The extension connects to the hub and pushes in-page interactions via `SendRecordingEvent`.
3. The hub re-broadcasts each interaction to every connected consumer as `ReceiveRecordingEvent` — the **same** message and envelope UIA Recorder uses.
4. On `StopRecorder`, the hub asks the extension to close the browser gracefully (a `CloseBrowser` message), then force-stops the process if it does not exit in time.

> The extension has its own setup and usage guide: **[`src/G4.Recorders.Chromium.Extension/README.md`](src/G4.Recorders.Chromium.Extension/README.md)**.

### SignalR Hub Usage (Chromium Recorder)

**Hub URL:** `http://localhost:9956/hub/v4/g4/peek`

**Client → Server Methods**

* `StartRecorder(driverParameters)` → returns the launched browser **process id**
* `StopRecorder(processId)` → returns `true` if a tracked browser was stopped, `false` if the id is unknown/already closed
* `SendHeartbeat()`
* `SendPeekAt({ xPos, yPos })`
* `SendPeekFocused()`
* `SendPeekCurrent()`
* `SendRecordingEvent(event)` — used by the **extension** (producer); consumers do not call this

**Server → Client Events**

* `ReceiveHeartbeat` — heartbeat confirmation
* `ReceivePeek` — DOM ancestor chain result
* `ReceiveRecordingEvent` — browser interactions with a DOM chain
* `CloseBrowser` — sent to the **extension** to close the browser during a graceful stop

### Chromium Peek API

The REST endpoint delegates DOM resolution to the one connected recorder extension. Chromium coordinates are
relative to the active frame viewport.

```bash
# Peek at viewport coordinates
curl "http://localhost:9956/api/v4/g4/recorder/element?x=250&y=300"

# Peek at the focused element
curl "http://localhost:9956/api/v4/g4/recorder/element?focused=true"

# Peek at the extension's current pointer position
curl "http://localhost:9956/api/v4/g4/recorder/element"
```

Supplying either coordinate selects coordinate mode and defaults the omitted axis to zero. Coordinates take
precedence over `focused=true`; focus is used only when both coordinates are absent. With no coordinates and
`focused=false`, the extension resolves its latest current pointer position.

### Launching and Stopping a Browser

`StartRecorder` takes a W3C-style driver-parameters object; only the Chromium binary and optional arguments are used:

```json
{
    "capabilities": {
        "alwaysMatch": {
            "goog:chromeOptions": {
                "binary": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
                "args": ["--disable-gpu"]
            }
        }
    }
}
```

Each launch gets its own dedicated user-data directory, so it never collides with an already-running browser. Keep the returned process id to stop it later with `StopRecorder(processId)`.

### Health Check

```bash
curl http://localhost:9956/api/v4/g4/ping
# Output: Pong
```

---

## SAP Recorder — SAP GUI Recorder

SAP Recorder listens on **`http://localhost:9957`**. It attaches to SAP Logon through SAP GUI Scripting, the same way the
G4 SAP driver does, and records only while at least one hub client is connected.

### How the SAP Recorder Works

1. A client connects to the hub, and optionally calls `StartRecorder` with its values (see
   [Starting the SAP Recorder](#starting-the-sap-recorder)). The recorder installs global mouse and keyboard hooks and
   subscribes to the `Change` event of every open SAP session in scope.
2. To receive `Change`, the recorder sets each session's `Record` property to `true`, keeping its previous value.
3. Input aimed at SAP GUI (a click on a SAP window, or a key while a SAP window has the foreground) is resolved to the
   SAP control and sent as a `user32` event. Input elsewhere is ignored.
4. Each SAP action that `Change` reports is sent as a `nativeSap` event.
5. When the last client disconnects, the recorder unsubscribes and restores every session's `Record` value. It never
   changes SAP GUI options, the registry, or system settings.

### SAP Recording Messages

SAP messages use the same `ReceiveRecordingEvent` name and `{ "value": ... }` envelope as the other recorders. The
value holds up to two events, one per stream, and each event has exactly the shared event shape. A stream with nothing
to report for the moment is left out, because the streams do not line up one to one: SAP reports typed text only
when the screen is submitted, long after the keystrokes.

```json
{
    "value": {
        "user32": { "chain": {}, "event": "Left Up", "machineName": "", "timestamp": 0, "type": "Mouse", "value": { "x": 0, "y": 0 } },
        "nativeSap": { "chain": {}, "event": "InvokeClick", "machineName": "", "timestamp": 0, "type": "Mouse", "value": {} }
    }
}
```

| Stream | Vocabulary | Source |
| --- | --- | --- |
| `user32` | The UIA recorder's: `Mouse` and `Keyboard`, `Left Down`, `Key Up`, the same `value` members | Global input hooks, resolved through SAP's `findByPosition` or the SAP focus |
| `nativeSap` | The Chromium recorder's style: `event` names the action | SAP's `GuiSession.Change` event |

| SAP reports | `type` | `event` | `value` |
| --- | --- | --- | --- |
| `text` set on a field | `Keyboard` | `SendKeys` | `{ "text" }`; `{ "text": "***", "isRedacted": true }` for passwords |
| `press()`, or `select()` on a menu, tab, or radio button | `Mouse` | `InvokeClick` | `{}` |
| `sendVKey(n)` | `Keyboard` | `SendSapVirtualKey` | `{ "key": n }` |
| Any other method, such as `pressToolbarButton` | `Mouse` | `InvokeSapMethod` | `{ "method", "arguments" }`, the body of the SAP driver's call route |
| Any other property, such as `selectedRows` | `Form` | `SetSapProperty` | `{ "name", "value" }`, the body of the SAP driver's property route |

SAP chains keep the shared node fields. `controlType` holds the SAP type, `className` the shell subtype (or the type),
`automationId` the SAP name, `frameworkId` is `SAPGUI`, and `properties` carries `Id`, `RelativeId`, `SubType`, and
`ContainerType`. The path runs from the window to the trigger, and the `locator` is an XPath on the absolute
scripting identifier, such as `//GuiTextField[@id='/app/con[0]/ses[0]/wnd[0]/usr/txtRSYST-BNAME']`, which the SAP
driver resolves through `findById`. Field contents never appear in node names.

Clients choose the stream by mode: User32 and coordinate modes read `user32`, and the SAP mode reads `nativeSap`.

### Starting the SAP Recorder

A client that only connects records with the recorder's environment and `appsettings.json` values. A client that calls
`StartRecorder(parameters)` sends its own values; each value is resolved on its own, from the first source that has it:

1. The request.
2. The environment variable.
3. `appsettings.json`, under `G4:Sap`.
4. The default.

| Request value | Environment variable | `appsettings.json` key | Default |
| --- | --- | --- | --- |
| `streams` | `G4_SAP_RECORDER_STREAMS` | `G4:Sap:Recorder:Streams` | both: `user32`, `nativeSap` |
| `setRecordMode` | `G4_SAP_RECORDER_SET_RECORD` | `G4:Sap:Recorder:SetRecordMode` | `true` |
| `sapLogon.path` | `G4_SAP_LOGON_PATH` | `G4:Sap:Logon:Path` | none: attach only |
| `sapLogon.arguments` | `G4_SAP_LOGON_ARGUMENTS` | `G4:Sap:Logon:Arguments` | none |
| `sapLogon.connection` | `G4_SAP_LOGON_CONNECTION` | `G4:Sap:Logon:Connection` | none |
| `scope.systemName` | `G4_SAP_SCOPE_SYSTEM` | `G4:Sap:Scope:SystemName` | any system |
| `scope.client` | `G4_SAP_SCOPE_CLIENT` | `G4:Sap:Scope:Client` | any client |
| `scope.sessions` | `G4_SAP_SCOPE_SESSIONS` | `G4:Sap:Scope:Sessions` | any session |

List values in environment variables are separated by `;`, such as `user32;nativeSap`.

```json
{
    "streams": ["nativeSap"],
    "sapLogon": { "path": "C:\\Program Files\\SAP\\FrontEnd\\SAPGUI\\saplogon.exe", "connection": "S4H - Development" },
    "scope": { "systemName": "S4H", "client": "100" }
}
```

1. `StartRecorder` returns the process id of a SAP Logon it started (zero when one was already running), and the
   resolved values with the source of each one, such as `"sapLogon.path": "request"`.
2. A running SAP Logon is never restarted. When SAP GUI is not running and a path resolved, the recorder starts it and
   waits until scripting answers; a named connection is opened when no connection with that description is open.
3. `StopRecorder(processId)` ends a SAP Logon the recorder started. Other processes are never touched.
4. With `setRecordMode: false`, the recorder never switches `Record` on; it records `nativeSap` actions only from
   sessions the user set to recording mode.
5. Sessions outside `scope` are not subscribed, input on their windows is not sent, and the status lists them. Peek
   requests are not scoped.
6. The values apply until the last client disconnects. Another client's `StartRecorder` with different values is
   refused while a recording runs; the same values are accepted.

### SAP REST API

The REST surface matches the UIA recorder's, with the SAP additions at the end.

| Route | Purpose |
| --- | --- |
| `GET /api/v4/g4/recorders/sap/recorder/observe?metricsOnly=` | Capture the virtual desktop as base64 PNG, with its metrics |
| `POST /api/v4/g4/recorders/sap/recorder/point?x=&y=` | Move the physical cursor; returns where it settled |
| `GET /api/v4/g4/recorders/sap/recorder/element?x=&y=&focused=` | Peek: at a point, at the focus, or at the current cursor |
| `POST /api/v4/g4/recorders/sap/recorder/ground?x=&y=&skipOffset=` | Move the cursor, peek there, and add the pointer offset |
| `POST /api/v4/g4/recorders/sap/recorder/focus?windowTitle=&processName=` | Bring a window to the foreground |
| `GET /api/v4/g4/recorders/sap/recorder/status` | Whether each session reports SAP actions, and why not |
| `POST /api/v4/g4/recorders/sap/recorder/logon` | Start or attach to SAP Logon; body `{ path, arguments, connection }`, resolved like `StartRecorder` |
| `DELETE /api/v4/g4/recorders/sap/recorder/logon/{processId}` | Stop a SAP Logon the recorder started |
| `GET`/`POST /api/v4/g4/recorders/sap/mcp` | MCP stream and JSON-RPC endpoint |
| `GET /api/v4/g4/recorders/sap/ping` | Health check |

```bash
curl "http://localhost:9957/api/v4/g4/recorders/sap/recorder/element?focused=true"
```

SAP routes answer `503` when SAP Logon is not running, scripting is disabled, or SAP GUI does not answer in time. The
status list is empty while no hub client is connected, because sessions are attached only while a client listens.

### SAP MCP Tools and Command Line

The MCP endpoint offers the UIA recorder's tool catalog with the same names and arguments: `g4.GetScreenshot`,
`g4.ConvertScreenshotPoint`, `g4.MovePointer`, `g4.ResolveGroundedElement`, and `g4.SetWindowFocus`. Only the grounded
chain differs: it describes SAP GUI controls.

The executable also peeks from the command line, without the host, as the UIA recorder does:

```bash
# Peek at the focused SAP control
G4.Recorders.Sap peek -f

# Peek at specific coordinates
G4.Recorders.Sap peek -x 100 -y 200
```

### SignalR Hub Usage (SAP Recorder)

**Hub URL:** `http://localhost:9957/hub/v4/g4/peek`

**Client → Server Methods**

* `SendHeartbeat()`
* `SendPeekAt({ xPos, yPos })`
* `SendPeekFocused()`
* `SendPeekCurrent()`
* `StartRecorder(parameters)` — applies this client's values; returns `{ processId, isLaunched, options }`
* `StopRecorder(processId)` — stops a SAP Logon `StartRecorder` started; returns `false` for other processes
* `FlushRecorder()` — waits until every event captured so far has been sent; returns `false` after 3 seconds
* `GetRecorderStatus()` — the per-session status, as the status route returns it

**Server → Client Events**

* `ReceiveHeartbeat` — heartbeat confirmation
* `ReceivePeek` — SAP chain result
* `ReceiveRecordingEvent` — `{ user32, nativeSap }` messages while the client is connected

### SAP Requirements and Limits

1. SAP GUI Scripting must be enabled on the client and the server (`sapgui/user_scripting`).
2. A system that sets `sapgui/user_scripting_disable_recording` refuses `Record`. The status reports such sessions, and
   only the `user32` stream records them.
3. When SAP GUI warns that a script is attaching, the user sees that notification once per attach.
4. Values are sent so recordings can be replayed, and they are never written to the recorder's logs. Password text is
   always redacted: `***` in `nativeSap`, and `*` with zero key codes for typed characters in `user32`.
5. `Change` is recognized by its arguments, so the recorder needs no SAP type library. The SAP command codes follow
   SAP's documented recorder format and are verified against the SAP GUI simulator; a check against a live SAP GUI is
   still pending.

### SAP Simulator Tests

The `G4.Recorders.Sap.Domain.UnitTests` project includes end-to-end tests that start the SAP GUI simulator from the
sibling [sap-driver-server](https://github.com/g4-api/sap-driver-server) repository. They are skipped unless enabled.

1. Build the simulator in `sap-driver-server`.
2. Set `G4_SAP_SIMULATOR=true`. Set `G4_SAP_SIMULATOR_PATH` when the simulator is not built next to this repository.
3. Optionally set `G4_SAP_SIMULATOR_INPUT=true` to run the test that clicks the simulator with the real mouse.
4. Run the tests.

    ```powershell
    dotnet test src\G4.Recorders.Sap.Domain.UnitTests --filter TestCategory=EndToEnd
    ```

Each setting comes from the first source that has it: a `.runsettings` test run parameter, then the environment
variable, then the `Environment` section of the test project's `appsettings.json`.

```xml
<RunSettings>
    <TestRunParameters>
        <Parameter name="G4_SAP_SIMULATOR" value="true" />
        <Parameter name="G4_SAP_SIMULATOR_PATH" value="C:\Tools\Sap.DriverServer.Simulator.exe" />
    </TestRunParameters>
</RunSettings>
```

---

## SignalR Client Examples

The **listening** pattern is identical for both hubs — only the URL changes (`9955` for UIA Recorder, `9956` for Chromium Recorder). The Chromium-only `StartRecorder`/`StopRecorder` calls are shown alongside.

### Python Example

Using the community client **signalrcore**.

```bash
pip install signalrcore
```

```python
from signalrcore.hub_connection_builder import HubConnectionBuilder

# Use 9955 for UIA Recorder or 9956 for Chromium Recorder.
hub = (
    HubConnectionBuilder()
    .with_url("http://localhost:9955/hub/v4/g4/peek")
    .with_automatic_reconnect({"type": "raw", "keep_alive_interval": 10, "reconnect_interval": 5})
    .build()
)

def on_heartbeat(payload):
    print("Heartbeat:", payload)

def on_peek(payload):
    print("Peek:", payload)

def on_recording_event(payload):
    event = (payload[0] if isinstance(payload, list) else payload).get("value", {})
    print(f"RecordingEvent: type={event.get('type')} event={event.get('event')} value={event.get('value')}")

hub.on("ReceiveHeartbeat", on_heartbeat)
hub.on("ReceivePeek", on_peek)
hub.on("ReceiveRecordingEvent", on_recording_event)

hub.start()

# Both recorders expose the same optional peek and heartbeat calls.
hub.send("SendHeartbeat", [])
hub.send("SendPeekFocused", [])

# Chromium Recorder only: launch a recorder browser (events start once it is recording).
# hub.send("StartRecorder", [{
#     "capabilities": {"alwaysMatch": {"goog:chromeOptions": {
#         "binary": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", "args": []
#     }}}
# }])

input("Press <Enter> to stop...\n")
hub.stop()
```

### JavaScript Example

Using **@microsoft/signalr** (browser or Node.js).

```bash
npm i @microsoft/signalr
```

```javascript
import * as signalR from "@microsoft/signalr";

// Use 9955 for UIA Recorder or 9956 for Chromium Recorder.
const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:9955/hub/v4/g4/peek")
    .withAutomaticReconnect()
    .build();

connection.on("ReceiveHeartbeat", (payload) => console.log("Heartbeat:", payload));
connection.on("ReceivePeek", (payload) => console.log("Peek chain:", payload?.value));
connection.on("ReceiveRecordingEvent", (payload) => {
    const event = payload?.value || {};
    console.log("RecordingEvent:", event.type, event.event, event.value);
});

async function main() {
    await connection.start();

    // Both recorders expose the same optional peek and heartbeat calls.
    await connection.invoke("SendHeartbeat");
    await connection.invoke("SendPeekFocused");

    // Chromium Recorder only: launch and later stop a recorder browser.
    // const processId = await connection.invoke("StartRecorder", {
    //     capabilities: { alwaysMatch: { "goog:chromeOptions": {
    //         binary: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe", args: []
    //     } } }
    // });
    // await connection.invoke("StopRecorder", processId);
}

main().catch(console.error);
```

### C# Example

Using **Microsoft.AspNetCore.SignalR.Client**.

```bash
dotnet add package Microsoft.AspNetCore.SignalR.Client
```

```csharp
using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;

// Use 9955 for UIA Recorder or 9956 for Chromium Recorder.
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:9955/hub/v4/g4/peek")
    .WithAutomaticReconnect()
    .Build();

connection.On<JsonElement>("ReceiveRecordingEvent", envelope =>
{
    var recordingEvent = envelope.GetProperty("value");
    var type = recordingEvent.GetProperty("type").GetString();
    var name = recordingEvent.GetProperty("event").GetString();
    Console.WriteLine($"RecordingEvent: {type} / {name}");
});

await connection.StartAsync();

// Both recorders expose the same optional peek and heartbeat calls.
await connection.InvokeAsync("SendHeartbeat");
await connection.InvokeAsync("SendPeekFocused");

// Chromium Recorder only: launch and later stop a recorder browser.
// var driverParameters = new
// {
//     capabilities = new
//     {
//         alwaysMatch = new Dictionary<string, object?>
//         {
//             ["goog:chromeOptions"] = new { binary = @"C:\Program Files\Google\Chrome\Application\chrome.exe", args = Array.Empty<string>() }
//         }
//     }
// };
// var processId = await connection.InvokeAsync<int>("StartRecorder", driverParameters);
// var isStopped = await connection.InvokeAsync<bool>("StopRecorder", processId);

Console.WriteLine("Press <Enter> to exit...");
Console.ReadLine();
```

---

## G4.Recorders.ClientApp — Monitor the Hubs

**G4.Recorders.ClientApp** (`src/G4.Recorders.ClientApp`) is a .NET 10 console test harness that connects to the UIA, Chromium, and SAP hubs at once and prints every recorder event to the console. It is the quickest way to watch desktop, browser, and SAP events side by side.

```bash
cd src/G4.Recorders.ClientApp
dotnet run                # compact colored line per event
dotnet run -- --verbose   # full JSON per event
```

* **Choose sources** — toggle each hub in `appsettings.json` (`Hubs:UiaEnabled` / `Hubs:ChromiumEnabled` / `Hubs:SapEnabled`) or at the command line (`--no-uia`, `--no-chromium`, `--no-sap`, `--uia`, `--chromium`, `--sap`).
* **Start SAP** — after connecting, the client sends `StartRecorder` with its `Sap` section (`Logon`, `Recorder`, `Scope`); empty values are left for the recorder to resolve. `--sap-logon <path>` sets the SAP Logon path, and `k` stops a SAP Logon the recorder started for this client.
* **Drive Chromium** — set `Chromium:BinaryPath` in `appsettings.json`, then press `s` to launch a recorder browser and `x` to stop it.
* **Keys** — `s` start Chromium · `x` stop Chromium · `k` stop SAP Logon · `v` toggle verbose · `h` help · `q` quit.

UIA events appear as soon as you move/click/type; Chromium events appear once a recorder browser is running; SAP events appear as `SAP/user32` and `SAP/nativeSap` lines while SAP GUI is open.

---

## G4 Recorders Path Finder — Quick Start

G4 Recorders Path Finder shows the UI-Automation path (an XPath-like locator) for whatever is currently under your mouse pointer. Hover any app control; the locator appears in the text box for copy/paste into your automation or QA tools.

### Requirements

* Windows desktop.
* If you need to peek elevated apps, run Path Finder **as Administrator**.

### Launch

* Open **G4 Recorders Path Finder v1.0** by running `G4.Recorders.Uia.PathFinder.exe`.
* You’ll see a title, a locator text box, a **Start/Stop** button, and a **Faster/Slower** slider.

  ```none
  ╭── G4 Recorders — Title ───────────────────────────────╮
  │                                                       │
  │  Locator                                              │
  │  ┌─────────────────────────────────────────────────┐  │
  │  │                                                 │  │
  │  └─────────────────────────────────────────────────┘  │
  │                                                       │
  │  [ ▶ Start / ⬛ Stop]                                │
  │                                                       │
  │  Faster  ◄───────┈┈┈┈┈●┈┈┈┈────────►  Slower          │
  │                                                       │
  ╰───────────────────────────────────────────────────────╯
  ```

### Start peeking

* Click **▶ Start** (or press **Alt+S**).
* Move your mouse over the target application.
* The locator appears and updates in the text box.

### Adjust update speed

* Use the **Faster ←→ Slower** slider to change refresh rate.
* Faster ≈ \~500 ms; Slower ≈ \~3000 ms.
* Faster feels live; slower reduces CPU and makes copying easier.

### Copy the locator

* Select the locator in the text box and press **Ctrl+C**.
* Paste into your test or automation scripts.
* If the text changes while selecting, press **⬛ Stop** first, then copy.

### Stop peeking

* Click **⬛ Stop** (or **Alt+S**) to pause tracking.

### Typical workflow

* Open the target app.
* Start Path Finder and hover precisely over the desired control (button, textbox, menu item).
* Adjust the slider if updates are too fast or too slow.
* Stop, copy the locator, and paste where needed.

### Tips

* Be precise: tiny mouse movements can change the target element.
* Hover the actionable sub-area (icon/text) if a widget is composite.
* Keep the Path Finder window out of the target area to avoid peeking it.
* Run as Administrator when inspecting admin-level windows.

### Troubleshooting

* **No updates**: ensure you pressed **Start**; try a slower rate; run as Administrator for elevated apps.
* **Hard to copy**: press **Stop**, then copy; or slide toward **Slower**.
* **Unexpected element**: re-hover the exact clickable region; complex controls may have nested elements.

### Keyboard & accessibility

* **Alt+S** toggles Start/Stop.
* **Ctrl+C** copies the locator from the text box.

### Exit

* Close the window (X or Alt+F4). Any active tracking stops automatically.

---

## License

MIT License. See `LICENSE` for details.
