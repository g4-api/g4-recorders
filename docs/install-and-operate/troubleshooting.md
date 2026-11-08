# Troubleshooting: Install and Operate the Recorders

[⬅ Back to overview](README.md)

Find your symptom below. Each card shows the message you see, why it happens, how to check, and what to do. The last line of each card names the source file where the message comes from.

Recorder messages are written to the console window of the recorder. Messages from hub and REST calls are also returned to the client that made the call.

---

## Start-up and connection

### The ping call cannot connect

- **Cause:** The recorder is not running, it listens on another port, or another program already holds the port. The G4 Hub Worker also defaults to port 9955, the UIA recorder port.
- **Check:** Look at the recorder console window. Check the `Url` value in the recorder's `appsettings.json`. Run `netstat -ano | findstr :9955` to see who holds a port.
- **Fix:** Start the recorder, or change its port as in [Module 5](05-ports-origins-and-logs.md).
- **Source:** `src/G4.Recorders.Uia/appsettings.json:14`

### The browser blocks a call to the recorder with a CORS error

- **Cause:** The origin of your page is not in the allowed list. Origins are matched exactly, including the scheme and the port.
- **Check:** Compare the origin shown in the browser error with the entries of `ORIGINS` or `AllowedOrigins`.
- **Fix:** Add the origin as in [Module 5](05-ports-origins-and-logs.md), then restart the recorder.
- **Source:** `src/G4.Recorders.Uia/Program.cs:134-146`

---

## UIA recorder

### Failed to install global keyboard hook

- **Cause:** Windows refused the low-level keyboard hook. The recorder is not in an interactive session, or it lacks the rights. The message ends with the Win32 error code.
- **Check:** Make sure the console window belongs to your signed-in desktop session, not a service or a non-interactive remote session. Check that you accepted the administrator prompt.
- **Fix:** Close the recorder and start it again as administrator in your desktop session.
- **Source:** `src/G4.Recorders.Uia.Domain/Middlewares/UiaEventCaptureService.cs:281`

### Failed to install global mouse hook

- **Cause:** Windows refused the low-level mouse hook. Both hooks are tried first, then the keyboard hook is checked. If the keyboard hook failed, you see the keyboard message instead.
- **Check:** Read the Win32 error code at the end of the message.
- **Fix:** Start the recorder again as administrator in your desktop session.
- **Source:** `src/G4.Recorders.Uia.Domain/Middlewares/UiaEventCaptureService.cs:289`

### Skipped event; access denied to a higher-privilege UI element

- **Cause:** You clicked or typed in a window that runs at a higher integrity level than the recorder, such as an application started as administrator. The recorder skips that event.
- **Check:** Check whether the target application was started as administrator.
- **Fix:** Run the recorder as administrator. The UIA program asks for this when it starts, so accept the prompt.
- **Source:** `src/G4.Recorders.Uia.Domain/Middlewares/UiaEventCaptureService.cs:1485`

### Failed to resolve event

- **Cause:** One event could not be turned into a UI Automation chain. The recorder logs it and goes on with the next event, so one bad event does not stop the recording.
- **Check:** Read the exception that follows the message. Note the event type and the timestamp.
- **Fix:** Repeat the action. If it keeps failing on the same control, note the control's details and report them.
- **Source:** `src/G4.Recorders.Uia.Domain/Middlewares/UiaEventCaptureService.cs:1503`

### UI Automation focus monitoring failed to start

- **Cause:** The recorder could not register for focus changes. Keyboard and mouse recording still work.
- **Check:** Read the exception that follows the message.
- **Fix:** Restart the recorder. Focus-based peeks may be less accurate until you do.
- **Source:** `src/G4.Recorders.Uia.Domain/Middlewares/UiaEventCaptureService.cs:531`

### The current physical cursor position is unavailable

- **Cause:** The recorder could not read the real mouse position. This happens on a peek that asks for the current position. The UIA hub, the UIA REST route, the SAP hub and the SAP REST route all use this message.
- **Check:** Make sure the recorder runs in an interactive desktop session and that the screen is not locked.
- **Fix:** Unlock the screen and retry. Or pass `x` and `y`, or ask for the focused element instead.
- **Source:** `src/G4.Recorders.Uia.Domain/Hubs/UiaRecorderHub.cs:109`

---

## Chromium recorder

### Recorder extension folder not found

- **Cause:** The `G4.Recorders.Chromium.Extension` folder is not next to the program. The message ends with the path that was checked.
- **Check:** Open the folder of the program and look for `G4.Recorders.Chromium.Extension`.
- **Fix:** Unpack the full release asset again. Do not copy only the program file.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderLauncher.cs:74`

### Browser binary not found on the recorder host

- **Cause:** The launch request named a binary that does not exist, or it named none and no Chrome was found in the G4 sandbox layout.
- **Check:** Check the `binary` value in your request. Check the `G4_SANDBOX` environment variable and look for `browsers\chrome\chrome.exe` under it.
- **Fix:** Send the full path of your Chrome in `capabilities.alwaysMatch."goog:chromeOptions".binary`, or install Chrome under the sandbox. The path must exist on the machine where the recorder runs, not on the client.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderLauncher.cs:85`

### Failed to start browser process

- **Cause:** On Linux, the operating system returned no process for the browser.
- **Check:** Run the same Chrome path with the same arguments by hand in a terminal.
- **Fix:** Fix the path or the arguments that fail by hand, then launch again.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderLauncher.cs:345`

### Failed to start the browser process (Win32 error

- **Cause:** On Windows, `CreateProcess` failed. The message ends with the Win32 error code.
- **Check:** Look up the error code. Check that the binary path is a real `chrome.exe`.
- **Fix:** Correct the path or the arguments, then launch again.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderLauncher.cs:386`

### No Chromium recorder extension is connected

- **Cause:** A peek call arrived, but no browser with the recorder extension is connected to the hub. REST callers see this as `503`.
- **Check:** Make sure you launched the browser through the recorder, and that it is still open.
- **Fix:** Launch a recorder browser as in [Module 3](03-chromium-recorder.md), then repeat the call.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderRepository.cs:92`

### More than one Chromium recorder extension is connected; the target is ambiguous

- **Cause:** Two recorder browsers are connected at once. A peek call must pick exactly one.
- **Check:** Count the recorder browsers that are open.
- **Fix:** Stop one browser with the `DELETE` call and its process id, then repeat the call.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderRepository.cs:93`

### The Chromium recorder extension did not answer the peek request in time

- **Cause:** The extension did not return an element within 5 seconds. REST callers see this as `504`. The page may be busy, hidden or still loading.
- **Check:** Look at the recorder browser. Make sure the tab is open and has finished loading.
- **Fix:** Repeat the call. If it keeps timing out, reload the tab so the content script is injected again.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderRepository.cs:138`

### The Chromium recorder extension disconnected during the peek request

- **Cause:** The browser or the extension closed while a peek was waiting for its answer.
- **Check:** Check whether the browser was closed, crashed or stopped by another client.
- **Fix:** Launch a recorder browser again and repeat the call.
- **Source:** `src/G4.Recorders.Chromium.Domain/ChromiumRecorderRepository.cs:193`

---

## SAP recorder

### SAP GUI Scripting is not available: SAP Logon is not running, or scripting is disabled

- **Cause:** The recorder could not attach to SAP Logon. SAP Logon is closed, or SAP GUI Scripting is turned off. REST callers see this as `503`.
- **Check:** Check that SAP Logon is running. In SAP GUI options, check that scripting is enabled. Ask your SAP team whether `sapgui/user_scripting` is set on the server.
- **Fix:** Start SAP Logon and enable scripting, then repeat the call.
- **Source:** `src/G4.Recorders.Sap.Domain/Scripting/SapScriptingHost.cs:53`

### SAP Logon was not found on the recorder host

- **Cause:** The `sapLogon.path` value points to a file that does not exist on the machine where the recorder runs. The message ends with that path.
- **Check:** Check the path in the request, in `G4_SAP_LOGON_PATH` and in `G4:Sap:Logon:Path`. Remember the order: request, environment, `appsettings.json`.
- **Fix:** Correct the path to the real `saplogon.exe`.
- **Source:** `src/G4.Recorders.Sap.Domain/SapLogonLauncher.cs:157`

### SAP Logon did not start

- **Cause:** The operating system returned no process for `saplogon.exe`.
- **Check:** Run the same path with the same arguments by hand.
- **Fix:** Fix the path or the arguments, then start the recording again.
- **Source:** `src/G4.Recorders.Sap.Domain/SapLogonLauncher.cs:168`

### started, but SAP GUI Scripting did not become available

- **Cause:** The full message begins with `SAP Logon (process <id>)`. The recorder started SAP Logon and waited 60 seconds, but scripting never answered.
- **Check:** Open SAP Logon by hand and check that scripting is enabled in its options.
- **Fix:** Enable scripting, then start the recording again.
- **Source:** `src/G4.Recorders.Sap.Domain/SapLogonLauncher.cs:202`

### The SAP GUI Scripting operation did not complete within

- **Cause:** SAP GUI took too long to answer a scripting call. The message ends with the time limit.
- **Check:** Check whether SAP GUI shows a dialog that waits for you, such as a scripting warning.
- **Fix:** Answer the dialog and repeat the call.
- **Source:** `src/G4.Recorders.Sap.Domain/Scripting/SapComThread.cs:139`

### Failed to install the input hooks; the user32 stream is incomplete

- **Cause:** Windows refused one of the keyboard or mouse hooks. The `nativeSap` stream keeps working, but the `user32` stream misses input.
- **Check:** Make sure the recorder runs in your signed-in desktop session.
- **Fix:** Close the recorder and start it again in that session.
- **Source:** `src/G4.Recorders.Sap.Domain/Middlewares/SapInputCaptureService.cs:227`

### SAP GUI refused recording mode

- **Cause:** The SAP system sets `sapgui/user_scripting_disable_recording`, so the session cannot switch to recording mode. The status list gives this reason for that session.
- **Check:** Call `GET /api/v4/g4/recorders/sap/recorder/status` while a client is connected.
- **Fix:** Use the `user32` stream for that session, or ask your SAP team to allow recording on the system.
- **Source:** `src/G4.Recorders.Sap.Domain/Middlewares/SapSessionSubscriptions.cs:38`

### A recording is already running with different values

- **Cause:** Another client started a recording with other values, and the recorder keeps one set of values per recording.
- **Check:** Find the other client that is connected to the SAP hub.
- **Fix:** Stop the other client, or start with the same values.
- **Source:** `src/G4.Recorders.Sap.Domain/Hubs/SapRecorderHub.cs:122`

### At least one stream (user32 or nativeSap) must be enabled

- **Cause:** The resolved `streams` list is empty.
- **Check:** Check `G4_SAP_RECORDER_STREAMS` and `G4:Sap:Recorder:Streams` for an empty value.
- **Fix:** Name `user32`, `nativeSap`, or both, separated by `;`.
- **Source:** `src/G4.Recorders.Sap.Domain/SapRecorderSettingsResolver.cs:149`

### Unknown stream

- **Cause:** A stream name is not `user32` or `nativeSap`. The message names the stream it did not recognize.
- **Check:** Look for a typo in the request, the environment variable or `appsettings.json`.
- **Fix:** Use only `user32` and `nativeSap`.
- **Source:** `src/G4.Recorders.Sap.Domain/SapRecorderSettingsResolver.cs:159`

---

## Any recorder

### An unhandled exception occurred

- **Cause:** A REST call failed in a way the recorder did not expect. The recorder logs the exception and answers with an error.
- **Check:** Read the exception in the console. Raise the log level as in [Module 5](05-ports-origins-and-logs.md) if the console shows too little.
- **Fix:** Fix the cause named by the exception, then repeat the call.
- **Source:** `src/G4.Recorders.Common.Domain/Middlewares/ErrorHandlingMiddleware.cs:117`
