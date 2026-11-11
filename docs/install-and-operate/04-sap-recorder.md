# Module 4: The SAP recorder

[⬅ Back to overview](README.md) · [⬅ Module 3](03-chromium-recorder.md) · [Next: Module 5 ➡](05-android-recorder.md)

⏱️ **About 7 minutes**

The SAP recorder attaches to SAP Logon through SAP GUI Scripting. It records two streams at the same time: your mouse and keyboard input (`user32`) and the actions SAP itself reports (`nativeSap`).

In this module, you will:

- Check the SAP prerequisites
- Start the SAP recorder
- Choose its settings with the request, environment or `appsettings.json`
- Read the status of your SAP sessions

---

## Step 1: Check the prerequisites

1. SAP GUI for Windows is installed.
2. SAP GUI Scripting is enabled on the client and on the server (`sapgui/user_scripting`).
3. SAP Logon is running, or you can give the recorder the path to `saplogon.exe`.

> **📝 Note:** A system that sets `sapgui/user_scripting_disable_recording` refuses recording mode. The recorder still records those sessions on the `user32` stream only.

---

## Step 2: Start the recorder

1. Open the folder where you unpacked the SAP asset.
2. Double-click `G4.Recorders.Sap.exe`. It needs no administrator rights.
3. Wait for the console window to show the G4 logo.

The recorder listens on `http://localhost:9957`.

![The SAP recorder console after start-up](images/04-sap-console.png)

TODO(screenshot): images/04-sap-console.png, the SAP recorder console window.

Like the UIA recorder, it records only while at least one hub client is connected. When the last client leaves, the values of that recording are cleared, and the next recording starts from the environment and settings again.

---

## Step 3: Choose the settings

Each setting is resolved on its own, from the first source that has a value:

1. The request a client sends with `StartRecorder`.
2. The environment variable.
3. `appsettings.json`, under `G4:Sap`.
4. The built-in default.

| Request value | Environment variable | `appsettings.json` key | Default |
| --- | --- | --- | --- |
| `streams` | `G4_SAP_RECORDER_STREAMS` | `G4:Sap:Recorder:Streams` | both: `user32`, `nativeSap` |
| `setRecordMode` | `G4_SAP_RECORDER_SET_RECORD` | `G4:Sap:Recorder:SetRecordMode` | `true` |
| `sapLogon.path` | `G4_SAP_LOGON_PATH` | `G4:Sap:Logon:Path` | none, attach only |
| `sapLogon.arguments` | `G4_SAP_LOGON_ARGUMENTS` | `G4:Sap:Logon:Arguments` | none |
| `sapLogon.connection` | `G4_SAP_LOGON_CONNECTION` | `G4:Sap:Logon:Connection` | none |
| `scope.systemName` | `G4_SAP_SCOPE_SYSTEM` | `G4:Sap:Scope:SystemName` | any system |
| `scope.client` | `G4_SAP_SCOPE_CLIENT` | `G4:Sap:Scope:Client` | any client |
| `scope.sessions` | `G4_SAP_SCOPE_SESSIONS` | `G4:Sap:Scope:Sessions` | any session |

In an environment variable or a settings string, separate list items with `;`, for example `user32;nativeSap`.

To keep the settings with the recorder, add a `G4:Sap` section to `appsettings.json`:

```json
{
    "G4": {
        "Sap": {
            "Recorder": {
                "Streams": "nativeSap"
            },
            "Logon": {
                "Path": "C:\\Program Files\\SAP\\FrontEnd\\SAPGUI\\saplogon.exe",
                "Connection": "S4H - Development"
            },
            "Scope": {
                "SystemName": "S4H",
                "Client": "100"
            }
        }
    }
}
```

Stream names must be `user32` or `nativeSap`. At least one must be enabled.

> **💡 Tip:** A running SAP Logon is never restarted. The recorder starts SAP Logon only when SAP GUI is not running and a path was resolved. It then waits up to 60 seconds for scripting to answer.

---

## Step 4: Read the session status

1. Connect a hub client, as you do in [Module 7](07-verify-with-the-ping-routes.md).
2. Run the command below.

   ```bash
   curl http://localhost:9957/api/v4/g4/recorders/sap/recorder/status
   ```

3. Read the list. It says, for each SAP session, whether it reports SAP actions and why not.

The list is empty while no hub client is connected, because sessions are attached only while a client listens. SAP routes answer `503` when SAP Logon is not running, scripting is disabled, or SAP GUI does not answer in time.

---

## ✔ Check your work

- [ ] SAP GUI Scripting is enabled on the client and the server
- [ ] `G4.Recorders.Sap.exe` is running and listens on port 9957
- [ ] You know the order: request, environment, `appsettings.json`, default
- [ ] You can read the status list while a client is connected

---

**Next:** [Module 5: The Android recorder](05-android-recorder.md)
