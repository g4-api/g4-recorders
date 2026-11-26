# Changelog

All notable user-visible changes to the G4 recorders are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Added

- The Chromium recorder records a middle click (`auxclick` with button 1) as `InvokeMiddleClick`, with its locators
  and DOM context, behind a new **Middle Click** event switch that is on by default. Other `auxclick` buttons are
  ignored: the right button is recorded through `contextmenu`.
- The release pipeline runs the Android, SAP and UIA domain tests and the Chromium locator lab on Windows before it
  creates a release, and stops on a failure. The Android app's Kotlin tests keep running in its build job.
- The Android recorder: a host on port 9958 (`G4.Recorders.Android`) and a recorder app (`G4.Recorders.Android.App`) whose accessibility service sends taps, long presses, field text, scrolls, keyboard closes, activity switches and the Back key (`UndoNavigation`) in the shared contract, with UiAutomator2 XPath locators and a view context snapshot. The host prepares and restores the device over adb, and release assets ship for Windows, Linux and macOS.
- The monitor app (`G4.Recorders.ClientApp`) can watch the Android hub (`Hubs:AndroidEnabled`, `--android`).
- Chromium element events carry a Base64 DOM context of the markup around the target on the trigger node (`domContext`, `domContextLength`), with settings in the extension options.
- Chromium element events carry ranked, verified locators on the trigger node (`locators`, `locatorStrength`, `locatorId`, `shadowPath`, `originalLocator`). The recorder keeps testing them while recording and corrects a delivered event through the `ReceiveLocatorAmendment` broadcast.
- The Chromium recorder host can test any locator against the live browser (`POST recorder/locator`).
- A Chromium target inside a shadow root carries a CSS shadow path (`host >>> inner`) as its first locator, with the `ShadowPath` strategy. Open and closed shadow roots are recorded, including typed text inside a closed root.
- A locator lab page (`recorder-locator-lab.html`) and its automated test (`src/G4.Recorders.Chromium.Extension.Tests`).
- The Chromium recorder records drag and drop as `InvokeDragAndDrop`, for HTML drags and pointer drags, with the drop element's chain in `value.target.chain`, the distance from the dragged element's center, a `dragAndDrop` switch and a `dragThresholdPixels` setting. A drag lab page (`recorder-drag-lab.html`) holds one case for each kind of drag.
- The UIA recorder adds `drag` to a `Left Up` that moved past the drag threshold: the press point, the drop element's chain, and both points measured from the element corners. A press over nothing UI Automation resolved keeps its drag, which replays between screen points.
- The SAP recorder host, runtime and release flow (40f3e8a).
- A screenshot point conversion tool, and an updated `GetScreenshot` (d865e0b).
- OS-level recorder primitives and screenshot support (0254573).
- An MCP JSON-RPC endpoint (d188199).
- An SSE endpoint on the MCP controller for real-time updates (c03e6fd).
- Structured output schemas and image blocks in MCP results (8792e11).
- UIA identity attributes can be set in the settings (fc6028d).

### Changed

- The recorders use `G4.Converters` and `G4.Extensions` 2026.11.25.113. They were at 2026.10.15.82.
- A push that changes only changelogs or workflow files no longer starts a release.
- The Android and recipe docs say that `InvokeLongClick` and `SwitchActivity` now replay, and describe how a recorded
  scroll replays.
- The Android and SAP event pages state that drag and drop is not recorded yet, with links to the feature requests [#34](https://github.com/g4-api/g4-recorders/issues/34) and [#35](https://github.com/g4-api/g4-recorders/issues/35).
- UIA drag and drop maps to `InvokeUser32DragAndDrop` in every mode, `coordinate` included, with element points where a locator exists and screen points otherwise ([#37](https://github.com/g4-api/g4-recorders/issues/37)).
- The `chain.locator` of a Chromium element event is the best user-facing XPath (label, role and name, placeholder, test hook) instead of the absolute path. The absolute path stays in the locator list as the last resort.
- A Chromium click on the inner node of a button, link or field is recorded against that control.
- Chromium paths address SVG elements through `local-name()`, and a target inside a shadow root is recorded as its host with a `shadowPath`.
- Keyboard targeting and queue flushing were refined (d63b631).
- Screenshot metadata and DPI handling were clarified (42deaa2).
- UIA property selection and its schema were simplified (4cdf137).
- UIA locator logic was improved (53d106e).
- The Peek tool was removed from the MCP UIA recorder tools (4a52772).
- The Peek methods were renamed to `GetElementChain` (3962268).
- The peek API is the same for the UIA and Chromium recorders (9e54daa).
- `PingController` returns a detailed JSON payload (5acf53a).
- Regular expressions have a 30 second timeout (0c51610).
