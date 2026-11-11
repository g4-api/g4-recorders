# Troubleshooting the G4 Recorders

[⬅ Back to the docs index](README.md)

Find your symptom below. Each card shows the message, why it happens, how to check, and what to do. The last line of each card names the source file where the message comes from.

Set-up and start-up problems have their own cards in [Install and Operate: troubleshooting](install-and-operate/troubleshooting.md). Do not look for them twice. This page adds the messages that page does not cover.

## Cards in the install path

These messages are explained in [install-and-operate/troubleshooting.md](install-and-operate/troubleshooting.md).

| Message or symptom | Recorder |
| --- | --- |
| The ping call cannot connect | All |
| The browser blocks a call to the recorder with a CORS error | All |
| Failed to install global keyboard hook | UIA |
| Failed to install global mouse hook | UIA |
| Skipped event; access denied to a higher-privilege UI element | UIA |
| Failed to resolve event | UIA |
| UI Automation focus monitoring failed to start | UIA |
| The current physical cursor position is unavailable | UIA, SAP |
| Recorder extension folder not found | Chromium |
| Browser binary not found on the recorder host | Chromium |
| Failed to start browser process | Chromium |
| No Chromium recorder extension is connected | Chromium |
| More than one Chromium recorder extension is connected; the target is ambiguous | Chromium |
| The Chromium recorder extension did not answer the peek request in time | Chromium |
| SAP GUI Scripting is not available | SAP |
| SAP Logon was not found on the recorder host | SAP |
| adb was not found on the recorder host | Android |
| No Android device is connected | Android |
| More than one Android device is connected; set appium:udid to choose one | Android |
| Recorder app not found on the recorder host | Android |
| Installing g4-recorder.apk failed | Android |
| Failed to start PACKAGE on device SERIAL | Android |
| No Android recorder app is connected | Android |
| The Android recorder failed to STEP while stopping | Android |
| SAP Logon did not start | SAP |
| Failed to install the input hooks; the user32 stream is incomplete | SAP |
| A recording is already running with different values | SAP |
| At least one stream (user32 or nativeSap) must be enabled | SAP |
| An unhandled exception occurred | All |

## UIA recorder

### Recorded keyboard Down without a pre-dispatch focus snapshot

- **Cause:** A key was pressed, but the recorder had no snapshot of the focused element from just before the press. The event is still sent, but without a resolved target. The message lists the `VirtualKey`, `ScanCode` and `Extended` values of the key.
- **Check:** Look at which key is named, and note the time of the step.
- **Fix:** The event is not dropped, but its target may be missing. If a step has no target in the flow, repeat it after clicking the field once.
- **Source:** `src/G4.Recorders.Uia.Domain/Middlewares/UiaEventCaptureService.cs:810`

### SetWindowFocus requires windowTitle and/or processName

- **Cause:** You called the set-focus operation with an empty window title and an empty process name.
- **Check:** Look at the request body. Both values are empty or missing.
- **Fix:** Send at least one of `windowTitle` or `processName`.
- **Source:** `src/G4.Recorders.Uia.Domain/UiaRecorderRepository.cs:237`

### Tool 'NAME' requires a numeric 'ARGUMENT' argument

- **Cause:** An MCP tool call on the recorder left out a required whole-number argument, or sent it as text. `NAME` is the tool and `ARGUMENT` is the argument.
- **Check:** Look at the arguments of your call. The value must be a JSON number such as `3`, not `"3"`.
- **Fix:** Send the argument as a JSON number. The same message exists on the SAP recorder.
- **Source:** `src/G4.Recorders.Uia.Domain/RecorderMcpRepository.cs:122`

### Image number N is not valid for the current recorder metrics

- **Cause:** A screenshot-point conversion named an image number that the current screen layout does not have.
- **Check:** Ask for the screenshot metrics first and read which image numbers exist.
- **Fix:** Use an image number from the current metrics.
- **Source:** `src/G4.Recorders.Uia.Domain/RecorderMcpRepository.cs:232`

### Point x must be from 0 through N for image M

- **Cause:** The `x` pixel is outside the chosen image. The message states the valid range.
- **Check:** Compare your pixel with the range in the message.
- **Fix:** Send a pixel inside the range. The same card applies to `y`: the message then starts with `Point y`.
- **Source:** `src/G4.Recorders.Uia.Domain/RecorderMcpRepository.cs:239`

### The recorder returned invalid screenshot metrics

- **Cause:** The recorder read a screen width or height smaller than 1.
- **Check:** Make sure a monitor is attached and the desktop is available to the recorder.
- **Fix:** Repeat the call once the desktop is available. If it repeats, report it.
- **Source:** `src/G4.Recorders.Uia.Domain/RecorderMcpRepository.cs:224`

### Tool 'NAME' has no dispatch handler

- **Cause:** The MCP call named a tool for which the recorder has no handler.
- **Check:** Compare the tool name with the tool list of the same recorder.
- **Fix:** Use a tool from that list. If a listed tool fails like this, report it.
- **Source:** `src/G4.Recorders.Uia.Domain/RecorderMcpRepository.cs:162`

## Path Finder

### Unsupported control type: NAME

- **Cause:** An XPath segment used a control type name that Path Finder has no mapping for.
- **Check:** Look at the control type named in the message.
- **Fix:** Use a UI Automation control type name.
- **Source:** `src/G4.Recorders.Uia.PathFinder/XpathParser.cs:160`

### Unsupported operator: NAME

- **Cause:** An XPath condition used a logical operator other than `and`, `or` or `not`.
- **Check:** Look at the operator named in the message.
- **Fix:** Rewrite the condition with `and`, `or` or `not`.
- **Source:** `src/G4.Recorders.Uia.PathFinder/XpathParser.cs:199`

### Unsupported property: NAME

- **Cause:** An XPath condition used an element property that Path Finder cannot resolve.
- **Check:** Look at the property named in the message.
- **Fix:** Use a UI Automation property name. A leading `partial` switches the match to a substring match.
- **Source:** `src/G4.Recorders.Uia.PathFinder/XpathParser.cs:229`

## Chromium recorder

### Failed to register the Chromium peek request correlation

- **Cause:** The recorder could not store the id of a peek request. The code treats this as an internal fault.
- **Check:** Repeat the peek once.
- **Fix:** If it repeats, restart the Chromium recorder and report the problem.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderRepository.cs:105`

### GetScreenshot is not implemented for the Chromium recorder surface yet

- **Cause:** The Chromium recorder has no screenshot support. The same type of message appears for `MovePointer`, `ResolveGroundedElementAsync` and `SetWindowFocus`.
- **Check:** Look at which operation the message names.
- **Fix:** Use the UIA recorder for these operations, or skip them. The REST routes map this error to status 501. See [Peek and observe](events/peek-and-observe.md).
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderRepository.cs:152` (also `:160`, `:202`, `:210`)

## SAP recorder

### SAP recording mode was not restored within TIMEOUT

- **Cause:** When the recorder stopped, SAP GUI did not leave recording mode in time. A call into SAP GUI was still busy. The sessions stay in recording mode until SAP Logon exits.
- **Check:** Look at SAP GUI. A dialog may be waiting for an answer.
- **Fix:** Close the dialog, or exit SAP Logon to clear the mode.
- **Source:** `src/G4.Recorders.Sap.Domain/Middlewares/SapActionCaptureService.cs:108`

### Skipped a SAP session synchronization: REASON

- **Cause:** SAP Logon closed or changed while the recorder looked for sessions. The recorder logs the reason and tries again on the next pass.
- **Check:** Check whether SAP Logon was closing or a session was opening.
- **Fix:** Nothing to do if SAP Logon is still open. If the message repeats, restart SAP Logon.
- **Source:** `src/G4.Recorders.Sap.Domain/Middlewares/SapActionCaptureService.cs:157`

### Skipped a recorded input event: REASON

- **Cause:** A click or key could not be matched to a SAP session, because the session was closing or busy. That one record is sent without its values.
- **Check:** Note the time of the missing step.
- **Fix:** Repeat the step if the flow needs it.
- **Source:** `src/G4.Recorders.Sap.Domain/Middlewares/SapInputCaptureService.cs:120`

### SetWindowFocus requires windowTitle and/or processName. (SAP)

- **Cause:** The same as the UIA card of that name: both values are empty.
- **Check:** Look at the request body.
- **Fix:** Send at least one of `windowTitle` or `processName`.
- **Source:** `src/G4.Recorders.Sap.Domain/SapRecorderRepository.cs:130`

### The SAP GUI Scripting object raises no events

- **Cause:** The recorder tried to listen to a SAP GUI Scripting object that offers no events, so it cannot record from it.
- **Check:** Read the SAP setup in [Module 4](install-and-operate/04-sap-recorder.md) and compare it with your set-up.
- **Fix:** Restart SAP Logon and the SAP recorder. If it repeats, report the SAP GUI version.
- **Source:** `src/G4.Recorders.Sap.Domain/Scripting/SapEventSink.cs:85`

### Tool 'NAME' requires a numeric 'ARGUMENT' argument. (SAP)

- **Cause:** The same as the UIA card of that name.
- **Check:** Look at the arguments of the MCP call.
- **Fix:** Send the argument as a JSON number.
- **Source:** `src/G4.Recorders.Sap.Domain/SapRecorderMcpRepository.cs:218`

### Image number N is not valid for the current recorder metrics. (SAP)

- **Cause:** The same as the UIA card of that name.
- **Check:** Read the screenshot metrics first.
- **Fix:** Use an image number from the current metrics. The pixel range message `Point x must be from 0 through N for image M.` is at the same place.
- **Source:** `src/G4.Recorders.Sap.Domain/SapRecorderMcpRepository.cs:130` (range check at `:157`)

### The recorder returned invalid screenshot metrics. (SAP)

- **Cause:** The same as the UIA card of that name.
- **Check:** Make sure the desktop is unlocked.
- **Fix:** Unlock the desktop and repeat the call.
- **Source:** `src/G4.Recorders.Sap.Domain/SapRecorderMcpRepository.cs:121`

### Tool 'NAME' has no dispatch handler. (SAP)

- **Cause:** The same as the UIA card of that name.
- **Check:** Compare the tool name with the tool list of the SAP recorder.
- **Fix:** Use a tool from the list.
- **Source:** `src/G4.Recorders.Sap.Domain/SapRecorderMcpRepository.cs:249`
