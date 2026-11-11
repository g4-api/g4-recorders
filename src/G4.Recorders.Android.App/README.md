# G4 Android Recorder App

An Android app whose accessibility service records interactions in any app on the device and streams them to the G4
**Android recorder** host (`G4.Recorders.Android`) over SignalR, in the same contract as every other G4 recorder. It is
the Android counterpart of the Chromium recorder extension: a bridge that sends everything it sees, while clients
decide what to keep.

## How it works

```text
Tap, text change, scroll, screen change or Back key in any app
  -> RecorderAccessibilityService receives the accessibility event (main thread)
  -> EventProcessor commits pending text and scroll gestures, maps the chain (NodeMapper, XPathLocatorBuilder,
     ViewContextBuilder) and finishes the contract event
  -> RecorderHubConnection sends it with SendRecordingEvent, in order, on its own thread
  -> the host's AndroidRecorderHub re-broadcasts ReceiveRecordingEvent { value: <event> }
  -> consumers such as the G4 Hub receive the event
```

The host starts the app with the hub address, the package to record and the device serial, then enables the service.
The service connects to `http://localhost:9958/hub/v4/g4/peek`, which `adb reverse` maps to the host, and registers
itself with `RegisterRecorder` after every connect. Each new connection is a new session: the event count on the app's
screen starts again.

## Contract

The app builds plain maps that the SignalR client serializes to the shared C# models in `G4.Recorders.Common.Domain`:

- `RecorderEventModel` — `{ chain, event, machineName, timestamp, type, value }`
- `ChainModel` — `{ locator, path[], point, topWindow, trigger }`, with the path **target first**, like Chromium
- `RecorderNodeModel` — `{ automationId, bounds, className, controlTypeId, controlType, frameworkId, isTopWindow,
  isTriggerElement, machine, name, patterns[], processId, properties, runtimeId[] }`

The event catalog, the node mapping, the locator rules and the view context are described in
[docs/events/android-events.md](../../docs/events/android-events.md).

## Requests from the host

| Message | What the app does |
| --- | --- |
| `ReceivePeekRequest` | Resolves the node at a screen point, the focused node, or the last touched node, and answers with `SendPeekResponse`. |
| `ReceiveFlushRequest` | Sends its pending text and scroll, then answers with `SendFlushResponse` after every event queued before the request. |
| `CloseRecorder` | Sends its pending text and scroll, disconnects and switches the accessibility service off. |

## Project layout

```text
settings.gradle.kts, build.gradle.kts   Gradle build; AGP 9 compiles Kotlin itself
app/build.gradle.kts                    App module; exports ../G4.Recorders.Android.App.Drop/g4-recorder.apk
app/src/main/AndroidManifest.xml        Activities, the accessibility service, permissions
app/src/main/kotlin/com/g4/recorders/android/
  core/                                 Pure Kotlin, no Android API, unit tested on the JVM
    RecorderNode.kt                     Node interface, bounds and tree helpers
    XPathLocatorBuilder.kt              UiAutomator2 XPath locators
    ViewContextBuilder.kt               Base64 XML view context snapshot
    NodeMapper.kt                       Node, chain and event maps in the contract shape
    EventCatalog.kt                     Event names, types and values
    EventProcessor.kt                   Text commit, scroll coalescing, ordering, peek lookup
    RecorderSettings.kt                 Settings the core reads
  AccessibilityNodeAdapter.kt           AccessibilityNodeInfo as a RecorderNode
  RecorderAccessibilityService.kt       Capture: accessibility events to the processor
  RecorderHubConnection.kt              SignalR client, ordered send queue, reconnect
  SettingsStore.kt                      Shared preferences
  RecorderState.kt                      Status shown on the main screen
  MainActivity.kt, SettingsActivity.kt  Status screen and settings screen
app/src/main/res/xml/                   Accessibility service config, preferences, network security config
app/src/test/kotlin/                    JVM unit tests with a fake node tree
```

## Build

You need JDK 17 or later and the Android SDK (platform 36 and build tools).

1. Point Gradle at the SDK with `ANDROID_HOME`, or with `sdk.dir` in `local.properties`.
2. Run the tests and build the release APK.

   ```bash
   ./gradlew test assembleRelease
   ```

3. Find the APK at `../G4.Recorders.Android.App.Drop/g4-recorder.apk`. The `G4.Recorders.Android` host project links
   this app's sources, like the Chromium host links its extension, and copies the drop folder into its output under
   the same name, where the host installs the APK from.

The release build is signed with the build machine's debug key, because the app is installed by adb and never through
a store. A device that holds an app signed on another machine gets it reinstalled by the host.

## Settings

Open **G4 Android Recorder** on the device and tap **Recorder settings**:

- **Hub URL** — the host hub (default `http://localhost:9958/hub/v4/g4/peek`; the host sets it on every start).
- **Recorded app package** — empty records every app; the host sets it from `appium:appPackage`.
- **Connect automatically** — connect as soon as the service starts.
- **Redact passwords** — send `***` for password fields (default off).
- **View context** — the snapshot switch, its ancestor levels (default 2) and its maximum size (default 16 KB).
- **Events to record** — one switch per event; all on by default.

## Manual use

Without the host's launcher, install `G4.Recorders.Android.App.Drop/g4-recorder.apk` with `adb install -r -g`, run
`adb reverse tcp:9958 tcp:9958`, start the host, and switch on **G4 Android Recorder** under the device's
accessibility settings. On an emulator, the hub URL `http://10.0.2.2:9958/hub/v4/g4/peek` reaches the host without
`adb reverse`.
