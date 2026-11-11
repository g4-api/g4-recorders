# Recorder recipes

[⬅ Back to the docs index](README.md)

Short, step-by-step jobs. Each recipe lists the steps, how to check that it worked, and where to read more.

In Windows PowerShell, type `curl.exe` instead of `curl`. The recipes use files for request bodies, so you do not need to escape quotes.

## Recipes

### Record a desktop application from start to finish

**Steps:**

1. Start the Hub. See [Install and Run the G4 Hub](../../g4-services/docs/install-and-run/README.md). It listens on `http://localhost:9944` by default.
2. Start the UIA recorder as administrator, as in [Module 2](install-and-operate/02-uia-recorder.md). It listens on `http://localhost:9955`.
3. Save this request body as `start-uia.json`.

   ```json
   {
       "authentication": {
           "token": ""
       },
       "excludedProcessIds": [],
       "excludedWindowTitles": [],
       "recorders": [
           {
               "enabled": true,
               "baseUrl": "http://localhost:9955",
               "driverParameters": {
                   "driver": "UiaDriver"
               }
           }
       ]
   }
   ```

4. Start the session and copy the `id` from the answer.

   ```bash
   curl -X POST http://localhost:9944/api/v4/g4/recordings -H "Content-Type: application/json" -d @start-uia.json
   ```

5. Use the application: click, type and move between windows.
6. Stop the session. Replace `<id>` with the id from step 4.

   ```bash
   curl -X POST http://localhost:9944/api/v4/g4/recordings/<id>/stop -o flow.json
   ```

**Verify:**

- The stop call answers `200` and `flow.json` holds a G4 automation. An empty recording answers `204 No Content`.
- `GET /api/v4/g4/recordings/<id>` shows the event count of the recorder while the session runs.

**Related:**

- [Recording Automations](../../g4-services/docs/recordings-api.md)
- [Build a flow from events](events/build-a-flow.md)

### Record a web application in a browser

**Steps:**

1. Start the Hub as in the recipe above.
2. Start the Chromium recorder, as in [Module 3](install-and-operate/03-chromium-recorder.md). It listens on `http://localhost:9956`. Keep the `G4.Recorders.Chromium.Extension` folder next to the program.
3. Save this request body as `start-chromium.json`. The Hub opens the browser for you, so you do not launch it yourself.

   ```json
   {
       "authentication": {
           "token": ""
       },
       "excludedProcessIds": [],
       "excludedWindowTitles": [],
       "recorders": [
           {
               "enabled": true,
               "baseUrl": "http://localhost:9956",
               "driverParameters": {
                   "driver": "ChromeDriver"
               }
           }
       ]
   }
   ```

4. If the recorder cannot find Chrome, add your own Chrome path under `capabilities.alwaysMatch."goog:chromeOptions".binary` in `driverParameters`.
5. Start the session.

   ```bash
   curl -X POST http://localhost:9944/api/v4/g4/recordings -H "Content-Type: application/json" -d @start-chromium.json
   ```

6. Work in the browser window that opens.
7. Stop the session with the `id` from the start answer.

   ```bash
   curl -X POST http://localhost:9944/api/v4/g4/recordings/<id>/stop -o flow.json
   ```

**Verify:**

- A Chrome window with the recorder extension opened after step 5.
- `flow.json` holds the recorded automation.

**Related:**

- [Chromium events](events/chromium-events.md)
- [Troubleshooting](troubleshooting.md)

### Record an Android app and replay it

**Steps:**

1. Start the Hub as in the first recipe. It must be a Hub that accepts `AndroidDriver`.
2. Connect a device with USB debugging on, or start an emulator, and check that `adb devices` lists it as `device`.
3. Start the Android recorder, as in [Module 5](install-and-operate/05-android-recorder.md). It listens on `http://localhost:9958`. Keep the `G4.Recorders.Android.App.Drop` folder next to the program.
4. Save this request body as `start-android.json`. The `driverParameters` are the ones the flow replays with: `driver` is `AndroidDriver`, `driverBinaries` is the Appium endpoint, and `capabilities.alwaysMatch` with `firstMatch` are the W3C WebDriver new-session capabilities. Put your G4 license token in `authentication.token`, so the flow can replay.

   ```json
   {
       "authentication": {
           "token": "<your G4 token>"
       },
       "recorders": [
           {
               "enabled": true,
               "baseUrl": "http://localhost:9958",
               "driverParameters": {
                   "driver": "AndroidDriver",
                   "driverBinaries": "http://localhost:4723",
                   "capabilities": {
                       "alwaysMatch": {
                           "platformName": "android",
                           "appium:automationName": "UiAutomator2",
                           "appium:appPackage": "com.android.settings",
                           "appium:appActivity": ".Settings"
                       }
                   },
                   "firstMatch": [ {} ]
               }
           }
       ]
   }
   ```

5. Start the session. The Hub asks the recorder to install the recorder app, switch on its accessibility service, and start Settings on the device.

   ```bash
   curl -X POST http://localhost:9944/api/v4/g4/recordings -H "Content-Type: application/json" -d @start-android.json
   ```

6. Use the app on the device: tap, type, and press Back.
7. Stop the session with the `id` from the start answer.

   ```bash
   curl -X POST http://localhost:9944/api/v4/g4/recordings/<id>/stop -o flow.json
   ```

8. Start an Appium server with the UiAutomator2 driver, as in [Module 5, Step 6](install-and-operate/05-android-recorder.md#step-6-know-what-replay-needs).
9. Remove the `SwitchActivity` and `InvokeLongClick` actions from `flow.json`. No plugin replays them yet; the VS Code extension removes them for you through `excludeEvents`.
10. Replay the flow.

    ```bash
    curl -X POST http://localhost:9944/api/v4/g4/automation/invoke -H "Content-Type: application/json" -d @flow.json
    ```

**Verify:**

- The device showed Settings after step 5, and the recorder app's status screen showed **connected**.
- `flow.json` holds `InvokeClick` actions with XPath locators such as `//android.widget.TextView[@text='Network & internet']/ancestor::android.widget.LinearLayout[1]`, and ends with `CloseBrowser`.
- After step 7, `adb shell settings get secure enabled_accessibility_services` no longer lists the recorder.
- During step 10, the Appium log shows the taps and the Back of your session.

**Related:**

- [Android events](events/android-events.md)
- [Troubleshooting](troubleshooting.md)

### Watch the raw events from a recorder

**Steps:**

1. Start the recorder you want to watch. For UIA, start it as administrator.
2. Open a terminal in the repository and go to the monitoring app.

   ```bash
   cd src/G4.Recorders.ClientApp
   ```

3. Run it with full JSON per event. Choose the sources with the switches `--uia`, `--chromium` and `--sap`, or `--no-uia`, `--no-chromium` and `--no-sap`.

   ```bash
   dotnet run -- --verbose --uia --no-chromium
   ```

4. Use the application. A line appears for each event.
5. Press `q` to quit.

**Verify:**

- Each click or key press prints a line. UIA events start as soon as you move, click or type. Chromium events start once a recorder browser runs.

**Related:**

- [The event contract](events/contract.md)
- [Verify with the ping routes](install-and-operate/07-verify-with-the-ping-routes.md)

### Build the recorders on your computer

**Steps:**

1. Open a terminal in the `src` folder of the repository.
2. Publish the UIA recorder with MSBuild. It uses COM references, so `dotnet build` does not work for it. Use the MSBuild that comes with Visual Studio.

   ```bash
   msbuild G4.Recorders.Uia/G4.Recorders.Uia.csproj /t:Publish /p:Configuration=Release /p:PublishDir=../artifacts/uia/
   ```

3. Publish the Chromium recorder with `dotnet`.

   ```bash
   dotnet publish G4.Recorders.Chromium/G4.Recorders.Chromium.csproj --configuration Release --self-contained false --output ../artifacts/chromium
   ```

4. Publish the SAP recorder with `dotnet`.

   ```bash
   dotnet publish G4.Recorders.Sap/G4.Recorders.Sap.csproj -c Release -o ../artifacts/sap
   ```

5. Build the Android recorder app with its Gradle wrapper, which needs JDK 17 or later and the Android SDK (`ANDROID_HOME`). It writes `G4.Recorders.Android.App.Drop/g4-recorder.apk`.

   ```bash
   cd G4.Recorders.Android.App
   ./gradlew test assembleRelease
   cd ..
   ```

6. Publish the Android recorder with `dotnet`. The publish copies the drop folder next to the program.

   ```bash
   dotnet publish G4.Recorders.Android/G4.Recorders.Android.csproj --configuration Release --self-contained false --output ../artifacts/android
   ```

**Verify:**

- Each output folder holds the recorder program. The Chromium folder also holds `G4.Recorders.Chromium.Extension`, and the Android folder holds `G4.Recorders.Android.App.Drop`.
- The commands above match the release workflow. It publishes the Path Finder with MSBuild as well.

**Related:**

- [Release assets and runtime](install-and-operate/01-release-assets-and-runtime.md)
