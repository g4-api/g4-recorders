# Module 3: The Chromium recorder

[⬅ Back to overview](README.md) · [⬅ Module 2](02-uia-recorder.md) · [Next: Module 4 ➡](04-sap-recorder.md)

⏱️ **About 8 minutes**

The Chromium recorder does not capture anything by itself. A recorder extension inside a Chrome window produces the events and sends them to the recorder. The recorder launches that browser for you.

In this module, you will:

- Start the Chromium recorder
- Tell it where Chrome is
- Launch a recorder browser and stop it again
- Know which hosts the extension may talk to

---

## Step 1: Start the recorder

1. Open the folder where you unpacked the Chromium asset.
2. Start the program.

   - **On Windows:** double-click `G4.Recorders.Chromium.exe`.
   - **On Linux:** run `dotnet G4.Recorders.Chromium.dll` in a terminal.

3. Wait for the console window to show the G4 logo.

The recorder listens on `http://localhost:9956`. It needs no administrator rights.

![The Chromium recorder console after start-up](images/03-chromium-console.png)

TODO(screenshot): images/03-chromium-console.png, the Chromium recorder console window.

> **📝 Note:** The unpacked folder must keep the `G4.Recorders.Chromium.Extension` folder next to the program. The launcher loads the extension from there.

---

## Step 2: Tell the recorder where Chrome is

The recorder looks for a Chrome binary in this order:

1. The `binary` value in the launch request, if you send one. The file must exist.
2. `browsers\chrome\chrome.exe` (`browsers/chrome/chrome` on Linux) under the folder named by the `G4_SANDBOX` environment variable.
3. The same path under the folder two levels above the program, which is the G4 sandbox layout.

If none of these exists, the launch fails and tells you to provide the binary or install Chrome under the G4 sandbox.

To use your own Chrome, pass its path in the launch request in the next step.

---

## Step 3: Launch a recorder browser

You launch a browser with a REST call. A W3C-style driver-parameters body names the Chrome binary and any arguments.

1. Open a second terminal.
2. Run the command below. Change the `binary` path to your Chrome.

   ```bash
   curl -X POST http://localhost:9956/api/v4/g4/recorders/chromium/recorder/browser \
        -H "Content-Type: application/json" \
        -d '{"capabilities":{"alwaysMatch":{"goog:chromeOptions":{"binary":"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe","args":[]}}}}'
   ```

3. Note the number that comes back. It is the process id of the browser.

   ![A Chrome window opened by the recorder, showing the G4 Chromium Recorder page](images/03-recorder-browser.png)

   TODO(screenshot): images/03-recorder-browser.png, the browser the recorder launched.

Each launch:

- Gets its own free remote-debugging port.
- Loads the recorder extension.
- Uses a new temporary user-data folder, unless you pass your own `--user-data-dir`.
- Opens the recorder's own bootstrap page first. That page tells the extension to connect back to the recorder that launched it.

> **💡 Tip:** In Windows PowerShell, `curl` is an alias for another command. Type `curl.exe` instead, and write the `-d` body on one line without the line breaks.

A client can start the browser over the hub instead. The hub method is `StartRecorder`, and it takes the same body.

---

## Step 4: Stop the browser

1. Run the command below. Use the process id from the last step.

   ```bash
   curl -X DELETE http://localhost:9956/api/v4/g4/recorders/chromium/recorder/browser/12345
   ```

2. Watch the browser close.

The recorder first asks the extension to close the browser. If the process is still running after 5 seconds, it stops the process tree.

---

## Step 5: Know the host rules

The extension may talk only to `localhost` and `127.0.0.1`, over `http` and `ws`. These are its `host_permissions`. A recorder on another machine is not reachable from the extension.

On the recorder side, the CORS policy already allows origins that start with `chrome-extension://` and `vscode-webview://`. Other origins need the setting in [Module 6](06-ports-origins-and-logs.md).

---

## ✔ Check your work

- [ ] The Chromium recorder shows its console and listens on port 9956
- [ ] You launched a browser and got a process id back
- [ ] The browser opened on the G4 Chromium Recorder page
- [ ] You stopped the browser with the process id

---

**Next:** [Module 4: The SAP recorder](04-sap-recorder.md)
