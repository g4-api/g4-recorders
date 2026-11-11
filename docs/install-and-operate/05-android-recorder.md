# Module 5: The Android recorder

[⬅ Back to overview](README.md) · [⬅ Module 4](04-sap-recorder.md) · [Next: Module 6 ➡](06-ports-origins-and-logs.md)

⏱️ **About 8 minutes**

The Android recorder records an app on an Android phone or emulator. Like the Chromium recorder, it captures nothing by itself: a recorder app on the device produces the events and sends them to the recorder. The recorder installs that app for you over adb.

In this module, you will:

- Install adb and connect a device
- Start the Android recorder
- Start a recording on the device and stop it again
- Know what the recorder changes on the device, and what it restores
- Know what a recorded flow needs to replay

---

## Step 1: Install adb and connect a device

The recorder talks to the device through adb, the Android Debug Bridge. adb is part of the Android SDK platform-tools. The G4 sandbox does not ship it.

1. Install the Android SDK platform-tools.
2. Set `ANDROID_HOME` to the SDK folder, or put the `platform-tools` folder on your `PATH`.
3. Connect a phone with USB debugging on, or start an emulator.
4. Run the command below.

   ```bash
   adb devices
   ```

5. Check that your device is listed with the state `device`. A device listed as `unauthorized` needs you to accept the prompt on its screen.

The recorder finds adb in this order: `ANDROID_HOME`, `ANDROID_SDK_ROOT`, `android/platform-tools` under the G4 sandbox, then the `PATH`.

---

## Step 2: Start the recorder

1. Open the folder where you unpacked the Android asset.
2. Start the program.

   - **On Windows:** double-click `G4.Recorders.Android.exe`.
   - **On Linux and macOS:** run `dotnet G4.Recorders.Android.dll` in a terminal.

3. Wait for the console window to show the G4 logo.

The recorder listens on `http://localhost:9958`. It needs no administrator rights.

> **📝 Note:** The unpacked folder must keep the `G4.Recorders.Android.App.Drop` folder next to the program. It holds `g4-recorder.apk`, the app the recorder installs on the device.

---

## Step 3: Start a recording

You start a recording with a REST call. The body uses the same Appium capabilities a G4 `AndroidDriver` flow replays with.

1. Open a second terminal.
2. Run the command below. Change `appium:appPackage` to the app you want to record.

   ```bash
   curl -X POST http://localhost:9958/api/v4/g4/recorders/android/recorder/device \
        -H "Content-Type: application/json" \
        -d '{"capabilities":{"alwaysMatch":{"appium:appPackage":"com.android.settings","appium:appActivity":".Settings"}}}'
   ```

3. Note the number that comes back. It is the process id of the app, or a negative id when you named no app.

What the capabilities do:

| Capability | Effect | When empty |
| --- | --- | --- |
| `appium:udid` | The device to use | The only connected device |
| `appium:appPackage` | The app to start and record | Every app is recorded |
| `appium:appActivity` | The activity to start | The app's launcher activity |
| `appium:app` | An APK on this machine to install first | Nothing is installed |

On start, the recorder:

1. Installs or updates the recorder app, and grants its permissions.
2. Maps port 9958 on the device to this machine with `adb reverse`.
3. Starts the recorder app with this recorder's hub address and the app to record.
4. Switches on the recorder's accessibility service, next to any service you already use.
5. Starts your app.

A client can start the recording over the hub instead. The hub method is `StartRecorder`, and it takes the same body. The G4 Hub does this when a recorder's driver is `AndroidDriver`.

> **💡 Tip:** In Windows PowerShell, type `curl.exe` instead of `curl`, and write the `-d` body on one line.

---

## Step 4: Watch the recorder app

Open **G4 Android Recorder** on the device. Its screen shows whether the service is on, whether it is connected, how many events it sent, and the last error.

Use the app you are recording. Each tap and each field you fill becomes an event. The [Android events](../events/android-events.md) page lists them.

The **Recorder settings** button opens the app's settings: the hub address, password redaction, the view context snapshot, and a switch for each event.

---

## Step 5: Stop the recording

1. Run the command below. Use the id from Step 3.

   ```bash
   curl -X DELETE http://localhost:9958/api/v4/g4/recorders/android/recorder/device/12345
   ```

2. Check that the answer is `true`.

On stop, the recorder asks the recorder app to send what it still holds and switch itself off. Then it stops your app, puts the accessibility settings back exactly as they were, and removes the port mapping. A step that fails is logged, and the other steps still run.

> **⚠️ Pitfall:** Each build machine signs the recorder app with its own key. When a device holds a recorder app from another machine, the recorder uninstalls it and installs its own, so that app's settings are reset.

---

## Step 6: Know what replay needs

The recorder only records. A recorded flow replays through an Appium server with its UiAutomator2 driver, using the same driver parameters you started the recording with:

| Field | Value | Meaning |
| --- | --- | --- |
| `driver` | `AndroidDriver` | The G4 driver that replays the flow. The G4 Hub also uses it to recognize an Android recorder |
| `driverBinaries` | `http://localhost:4723` | The Appium server endpoint. Appium 2 and later serve at the root; Appium 1 needs `/wd/hub` |
| `capabilities.alwaysMatch` and `firstMatch` | Required capabilities, and a list of alternatives | The W3C WebDriver new-session capabilities that G4 sends to Appium. G4 keeps `firstMatch` next to `capabilities`. Every Appium capability carries the `appium:` prefix |

A full set of driver parameters, as the G4 Hub passes them to `StartRecorder` and as the flow replays with:

```json
{
    "driver": "AndroidDriver",
    "driverBinaries": "http://localhost:4723",
    "capabilities": {
        "alwaysMatch": {
            "platformName": "android",
            "appium:automationName": "UiAutomator2",
            "appium:udid": "",
            "appium:appPackage": "com.android.settings",
            "appium:appActivity": ".Settings"
        }
    },
    "firstMatch": [ {} ]
}
```

The recorder reads only `appium:udid`, `appium:appPackage`, `appium:appActivity` and `appium:app`, and ignores the rest. Appium needs `platformName` and `appium:automationName` as well.

To set up Appium:

```bash
npm install -g appium
appium driver install uiautomator2
appium --port 4723
```

Appium also needs Java 17 or later and `ANDROID_HOME`. On Windows, keep `APPIUM_HOME` short, such as `C:\appium`: from a deep folder adb cannot open the helper apps Appium installs.

The [Android events](../events/android-events.md#limits) page lists what does not replay yet.

---

## ✔ Check your work

- [ ] `adb devices` lists your device as `device`
- [ ] The Android recorder shows its console and listens on port 9958
- [ ] You started a recording and got an id back
- [ ] The recorder app on the device shows **connected**
- [ ] You stopped the recording and the answer was `true`
- [ ] You know that replay needs an Appium server at `driverBinaries`

---

**Next:** [Module 6: Ports, origins and log levels](06-ports-origins-and-logs.md)
