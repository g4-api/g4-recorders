# Module 7: Verify with the ping routes

[⬅ Back to overview](README.md) · [⬅ Module 6](06-ports-origins-and-logs.md)

⏱️ **About 4 minutes**

Each recorder has its own ping route and its own Swagger page. In this module you check all three.

In this module, you will:

- Call the ping route of each recorder
- Open the Swagger pages
- Connect to a hub so that capture starts

---

## Step 1: Call the ping routes

The route includes the recorder name. The shared route `/api/v4/g4/ping` does not exist on the recorders.

1. Start the recorders you want to check.
2. Run the commands below. Skip the ones for recorders you did not start.

   ```bash
   curl http://localhost:9955/api/v4/g4/recorders/uia/ping
   curl http://localhost:9956/api/v4/g4/recorders/chromium/ping
   curl http://localhost:9957/api/v4/g4/recorders/sap/ping
   curl http://localhost:9958/api/v4/g4/recorders/android/ping
   ```

3. Check that each command prints a small JSON object with `"message": "Pong"` and `"status": "OK"`. It also lists the host name, the operating system and the runtime.

![A terminal showing four ping calls that each answer with Pong](images/07-ping-routes.png)

TODO(screenshot): images/07-ping-routes.png, a terminal with the four ping calls.

> **💡 Tip:** In Windows PowerShell, type `curl.exe` instead of `curl`.

If a call fails, go to the [troubleshooting page](troubleshooting.md).

---

## Step 2: Open the Swagger pages

1. Open a browser.
2. Go to the Swagger page of each recorder.

   | Recorder | Address |
   | --- | --- |
   | UIA | `http://localhost:9955/swagger` |
   | Chromium | `http://localhost:9956/swagger` |
   | SAP | `http://localhost:9957/swagger` |
   | Android | `http://localhost:9958/swagger` |

3. Expand a route and use **Try it out** to call it.

---

## Step 3: Connect to a hub

The ping route proves the web service is up. It does not start capture. For that, a client must connect to the recorder hub.

| Recorder | Hub address |
| --- | --- |
| UIA | `http://localhost:9955/hub/v4/g4/peek` |
| Chromium | `http://localhost:9956/hub/v4/g4/peek` |
| SAP | `http://localhost:9957/hub/v4/g4/peek` |
| Android | `http://localhost:9958/hub/v4/g4/peek` |

The simplest client is the monitoring app that ships in the repository as `G4.Recorders.ClientApp`. Any SignalR client works too, and G4 Studio connects the same way.

1. Connect a client to the hub address of the recorder you want.
2. Use your application. For the Chromium recorder, launch a browser first, as you did in [Module 3](03-chromium-recorder.md). For the Android recorder, start a recording first, as you did in [Module 5](05-android-recorder.md).
3. Watch the events arrive as `ReceiveRecordingEvent` messages.

> **💡 Tip:** Call `SendHeartbeat` on the hub to check that the connection is alive. The recorder answers with a `ReceiveHeartbeat` message.

---

## ✔ Check your work

- [ ] Each recorder you started answers with `Pong` on its own ping route
- [ ] You opened a Swagger page
- [ ] A hub client connected and received events

---

Back to the [overview](README.md), or open [Troubleshooting](troubleshooting.md).
