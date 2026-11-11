# Module 2: The UIA recorder

[⬅ Back to overview](README.md) · [⬅ Module 1](01-release-assets-and-runtime.md) · [Next: Module 3 ➡](03-chromium-recorder.md)

⏱️ **About 6 minutes**

The UIA recorder watches Windows desktop applications. It installs global keyboard and mouse hooks and reads the UI Automation tree of the control you touch.

In this module, you will:

- Start the UIA recorder as administrator
- Learn why it records only while a client is connected
- Choose which attributes build a locator

---

## Step 1: Start the recorder as administrator

The UIA program asks Windows for administrator rights when it starts. Its application manifest says `requireAdministrator`.

1. Open the folder where you unpacked the UIA asset.
2. Double-click `G4.Recorders.Uia.exe`.
3. Click **Yes** on the Windows prompt.

   ![The Windows prompt asking to allow the UIA recorder to make changes](images/02-uia-uac-prompt.png)

   TODO(screenshot): images/02-uia-uac-prompt.png, the UAC prompt for `G4.Recorders.Uia.exe`.

4. Wait for the console window to show the G4 logo.

The recorder now listens on `http://localhost:9955`.

> **⚠️ Warning:** Without administrator rights the recorder cannot read windows that run at a higher privilege. It skips those events and writes a warning. You will see this in the [troubleshooting page](troubleshooting.md).

---

## Step 2: Understand when it records

The hooks are installed when the recorder starts, but events are captured only while at least one client is connected to the hub at `/hub/v4/g4/peek`. The recorder counts connected clients, and capture is active when the count is above zero.

- No client connected: the recorder skips the capture work.
- One or more clients connected: events are captured and sent.

So a quiet console does not mean something is wrong. Connect a client, as you do in [Module 7](07-verify-with-the-ping-routes.md), and then use an application.

---

## Step 3: Choose the locator attributes

The recorder builds a locator for each control from an ordered list of attributes. The list is in `appsettings.json`, next to the program.

1. Open `appsettings.json` in a text editor.
2. Find the `G4:Uia:IdentityAttributes` section.

   ```json
   {
       "G4": {
           "Uia": {
               "IdentityAttributes": [
                   "AutomationId",
                   "Name"
               ]
           }
       }
   }
   ```

3. Change the order or the entries if you need to.
4. Save the file and restart the recorder.

If the section is missing, the recorder still works. It falls back to a built-in list, `Name` first and then `AutomationId`.

> **💡 Tip:** Put the attribute that is most stable in your application first. For many applications that is `AutomationId`.

---

## ✔ Check your work

- [ ] You started `G4.Recorders.Uia.exe` and accepted the administrator prompt
- [ ] The console shows the G4 logo
- [ ] You know that events are captured only while a client is connected
- [ ] You know where `IdentityAttributes` lives

---

**Next:** [Module 3: The Chromium recorder](03-chromium-recorder.md)
