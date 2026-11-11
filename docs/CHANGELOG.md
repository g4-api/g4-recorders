# Changelog

All notable user-visible changes to the G4 recorders are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Unreleased

### Added

- The Android recorder: a host on port 9958 (`G4.Recorders.Android`) and a recorder app (`G4.Recorders.Android.App`) whose accessibility service sends taps, long presses, field text, scrolls, keyboard closes, activity switches and the Back key (`UndoNavigation`) in the shared contract, with UiAutomator2 XPath locators and a view context snapshot. The host prepares and restores the device over adb, and release assets ship for Windows, Linux and macOS.
- The monitor app (`G4.Recorders.ClientApp`) can watch the Android hub (`Hubs:AndroidEnabled`, `--android`).
- Chromium element events carry a Base64 DOM context of the markup around the target on the trigger node (`domContext`, `domContextLength`), with settings in the extension options.
- The SAP recorder host, runtime and release flow (40f3e8a).
- A screenshot point conversion tool, and an updated `GetScreenshot` (d865e0b).
- OS-level recorder primitives and screenshot support (0254573).
- An MCP JSON-RPC endpoint (d188199).
- An SSE endpoint on the MCP controller for real-time updates (c03e6fd).
- Structured output schemas and image blocks in MCP results (8792e11).
- UIA identity attributes can be set in the settings (fc6028d).

### Changed

- Keyboard targeting and queue flushing were refined (d63b631).
- Screenshot metadata and DPI handling were clarified (42deaa2).
- UIA property selection and its schema were simplified (4cdf137).
- UIA locator logic was improved (53d106e).
- The Peek tool was removed from the MCP UIA recorder tools (4a52772).
- The Peek methods were renamed to `GetElementChain` (3962268).
- The peek API is the same for the UIA and Chromium recorders (9e54daa).
- `PingController` returns a detailed JSON payload (5acf53a).
- Regular expressions have a 30 second timeout (0c51610).
