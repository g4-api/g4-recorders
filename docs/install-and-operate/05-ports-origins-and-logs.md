# Module 5: Ports, origins and log levels

[⬅ Back to overview](README.md) · [⬅ Module 4](04-sap-recorder.md) · [Next: Module 6 ➡](06-verify-with-the-ping-routes.md)

⏱️ **About 5 minutes**

All three recorders read the same kinds of settings from `appsettings.json`: the port, the allowed origins and the log levels. In this module you change each one.

In this module, you will:

- Read the default ports and spot the one that can collide
- Change a port
- Allow a browser origin
- Raise a log level

---

## Step 1: Read the default ports

| Recorder | Default port | Set in |
| --- | --- | --- |
| UIA | 9955 | `Kestrel:Endpoints:Http:Url` in `appsettings.json` |
| Chromium | 9956 | `Kestrel:Endpoints:Http:Url` in `appsettings.json` |
| SAP | 9957 | `Kestrel:Endpoints:Http:Url` in `appsettings.json` |

Each value looks like `http://+:9955`. The `+` means the recorder listens on every network address of the machine.

> **⚠️ Warning:** The G4 Hub Worker also defaults to port 9955, the same port as the UIA recorder. Do not run both on one machine without changing one of them.

If you start a recorder from Visual Studio or with `dotnet run`, the launch profile applies instead. For UIA it is `http://localhost:5031`, for Chromium `http://localhost:5064` and for SAP `http://localhost:9957`.

---

## Step 2: Change a port

1. Close the recorder.
2. Open its `appsettings.json`.
3. Change the number in the `Url` value.

   ```json
   {
       "Kestrel": {
           "Endpoints": {
               "Http": {
                   "Url": "http://+:9965"
               }
           }
       }
   }
   ```

4. Save the file and start the recorder.
5. Update every client that points at the old port. The hub address is `http://localhost:<port>/hub/v4/g4/peek`.

> **📝 Note:** The Chromium extension has a default hub address of `http://localhost:9956/hub/v4/g4/peek`. A browser the recorder launches is pointed at the recorder that launched it, so it keeps working after a port change. A browser you start yourself needs the **Hub URL** set on the extension options page.

---

## Step 3: Allow an origin

A web page or app that talks to a recorder from a browser must come from an allowed origin. The recorder allows:

- Origins that start with `vscode-webview://`
- Origins that start with `chrome-extension://`, on the Chromium recorder only
- Origins you list yourself

Origins are matched exactly, so include the scheme and the port, for example `http://localhost:4200`.

You can list origins in either place. The environment variable wins when it is set and not empty.

| Where | How |
| --- | --- |
| Environment variable | `ORIGINS`, with items separated by `;` |
| `appsettings.json` | An `AllowedOrigins` array at the top level |

1. Choose one place.
2. Add your origin.

   ```powershell
   $env:ORIGINS = "http://localhost:4200;https://studio.example.com"
   .\G4.Recorders.Uia.exe
   ```

3. Start the recorder from the same terminal.

---

## Step 4: Raise a log level

Log levels are in the `Logging:LogLevel` section of `appsettings.json`. The shipped values are:

| Category | UIA | Chromium | SAP |
| --- | --- | --- | --- |
| `Default` | `Error` | `Error` | `Error` |
| `G4.Api` | `Information` | `Information` | `Information` |
| `G4.Recorders` | `Warning` | not set | `Warning` |
| `Microsoft.AspNetCore` | `Warning` | `Warning` | `Warning` |

The recorder code logs under categories that start with `G4.Recorders`. The Chromium file has no `G4.Recorders` entry, so its recorder messages fall under `Default`, which is `Error`.

1. Open the recorder's `appsettings.json`.
2. Set the category you need to `Information`, or to `Debug` while you investigate.

   ```json
   {
       "Logging": {
           "LogLevel": {
               "Default": "Error",
               "G4.Recorders": "Debug"
           }
       }
   }
   ```

3. Save the file and restart the recorder.

> **💡 Tip:** Set the level back to `Warning` when you are done. Debug adds detail that you rarely need after the problem is solved.

---

## ✔ Check your work

- [ ] You know the default ports 9955, 9956 and 9957
- [ ] You know the Hub Worker port can collide with UIA
- [ ] You know the two places to list an origin
- [ ] You can raise the `G4.Recorders` log level

---

**Next:** [Module 6: Verify with the ping routes](06-verify-with-the-ping-routes.md)
