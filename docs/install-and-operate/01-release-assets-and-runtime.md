# Module 1: Release assets and runtime

[⬅ Back to overview](README.md) · [Next: Module 2 ➡](02-uia-recorder.md)

⏱️ **About 5 minutes**

Every push to `main` builds the recorders and publishes a GitHub release. In this module you pick the asset you need and unpack it.

In this module, you will:

- Read the release asset names
- Choose the asset for your recorder
- Unpack it and find the program inside

---

## Step 1: Read the asset names

Open the releases page of the `g4-recorders` repository on GitHub. Each release is tagged `v<version>`, and the version has the form `yyyy.MM.dd.<run>`, for example `2026.11.08.42`.

A release holds these files:

| Asset | What it holds | Runs on |
| --- | --- | --- |
| `uia-recorder.<version>-win-x64.zip` | The UIA desktop recorder | Windows |
| `uia-path-finder.<version>-win-x64.zip` | Path Finder, a small window that shows the locator under your pointer | Windows |
| `sap-recorder.<version>-win-x64.zip` | The SAP GUI recorder | Windows |
| `chromium-recorder.<version>-win-x64.zip` | The Chromium recorder, with the recorder extension | Windows |
| `chromium-recorder.<version>-linux-x64.tar.gz` | The Chromium recorder, with the recorder extension | Linux |

![The assets list of a recorders release](images/01-release-assets.png)

TODO(screenshot): images/01-release-assets.png, the assets list of a release on GitHub.

> **📝 Note:** Only the Chromium recorder has a Linux build. The UIA and SAP recorders target Windows only.

---

## Step 2: Install the runtime

The Chromium recorder is published as a framework-dependent app, so the .NET 10 runtime must be installed on the machine. The UIA and SAP projects target `net10.0-windows`, and the repository README lists ".NET 10 Runtime/SDK" as the requirement for all of them.

1. Open a terminal.
2. Run the command below.

   ```powershell
   dotnet --list-runtimes
   ```

3. Look for a line that starts with `Microsoft.AspNetCore.App 10.`

If there is no such line, install the .NET 10 runtime before you go on.

---

## Step 3: Unpack the asset

1. Download the asset for your recorder.
2. Unzip it to a folder you can write to, for example `C:\Tools\G4.Recorders\uia`.
3. Find the program in the folder.

   | Recorder | Program |
   | --- | --- |
   | UIA | `G4.Recorders.Uia.exe` |
   | Path Finder | `G4.Recorders.Uia.PathFinder.exe` |
   | SAP | `G4.Recorders.Sap.exe` |
   | Chromium on Windows | `G4.Recorders.Chromium.exe` |
   | Chromium on Linux | `G4.Recorders.Chromium.dll`, started with `dotnet` |

> **💡 Tip:** Unpack each recorder to its own folder. Each one has its own `appsettings.json`, and you will edit them separately in later modules.

---

## ✔ Check your work

- [ ] You know which asset matches your recorder and your operating system
- [ ] `dotnet --list-runtimes` shows a .NET 10 ASP.NET Core runtime
- [ ] The program file is in the folder you unpacked

---

**Next:** [Module 2: The UIA recorder](02-uia-recorder.md)
