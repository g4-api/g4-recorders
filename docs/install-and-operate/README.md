# Install and Operate the Recorders

Welcome! 👋 This path shows you how to install the four G4 recorders, start them, and check that each one answers.

The recorders are small web services. A client such as G4 Studio or the Hub connects to a recorder, and the recorder streams back what you do on screen. Nothing is recorded until a client connects.

Plan for about **43 minutes** the first time through.

---

## Who is this for?

Anyone who needs to run the recorders on their own machine without the G4 sandbox: testers, developers and build engineers. If you can unzip a file and run a program, you can finish this.

## What you'll do

By the end you will have:

- Picked the right release asset for your machine
- Started the UIA recorder as administrator and seen why it needs that
- Started the Chromium recorder and launched a recorder browser
- Started the SAP recorder and understood how its settings are chosen
- Started the Android recorder and recorded an app on a device
- Opened the right ports and allowed the right origins
- Checked every recorder with its ping route

## What you need

- A Windows machine for the UIA and SAP recorders. The Chromium recorder also runs on Linux.
- The .NET 10 runtime.
- Administrator rights for the UIA recorder.
- SAP GUI for Windows with scripting enabled, if you want to record SAP.
- A Chrome binary, if you want to record a browser.
- The Android SDK platform-tools (adb) and a device or emulator, if you want to record Android.

> **💡 Tip:** The G4 sandbox installs all of this for you. Use this path when you run the recorders by hand.

---

## Modules

Work through these in order. Each one builds on the previous.

| # | Module | What you'll do | Time |
| --- | --- | --- | --- |
| 1 | [Release assets and runtime](01-release-assets-and-runtime.md) | Choose and unpack the right download | ~5 min |
| 2 | [The UIA recorder](02-uia-recorder.md) | Start the desktop recorder as administrator | ~6 min |
| 3 | [The Chromium recorder](03-chromium-recorder.md) | Start the browser recorder and launch a browser | ~8 min |
| 4 | [The SAP recorder](04-sap-recorder.md) | Start the SAP recorder and choose its settings | ~7 min |
| 5 | [The Android recorder](05-android-recorder.md) | Start the Android recorder and record an app | ~8 min |
| 6 | [Ports, origins and log levels](06-ports-origins-and-logs.md) | Change a port, allow an origin, raise a log level | ~5 min |
| 7 | [Verify with the ping routes](07-verify-with-the-ping-routes.md) | Check that each recorder answers | ~4 min |

**Stuck along the way?** See [Troubleshooting](troubleshooting.md).

Back to the [Recorders documentation index](../README.md).
