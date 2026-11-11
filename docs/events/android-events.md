# Android Recorder Events

[⬅ Back to the docs index](../README.md)

This page is the event catalog of the Android recorder. The recorder app on the device sends each event to the hub method `SendRecordingEvent`, and the host broadcasts it as `ReceiveRecordingEvent` in the [shared contract](contract.md). The names are defined in `src/G4.Recorders.Android.App/app/src/main/kotlin/com/g4/recorders/android/core/EventCatalog.kt`.

## How events are made

The recorder app runs an Android accessibility service. Android tells the service about every tap, text change, scroll and window change in any app, and about hardware key presses. The service turns each one into a contract event and sends it to the host through `adb reverse`, so the device reaches the host at `localhost:9958`.

The app is a bridge: it sends everything it sees. Clients decide what to keep. The VS Code template leaves out `SwitchActivity` and `InvokeLongClick` through `excludeEvents`, because no plugin replays them yet.

## Captured events

Event names follow the G4 action style, like the Chromium recorder's.

| `event` | Trigger | `type` | `value` |
| --- | --- | --- | --- |
| `InvokeClick` | a tap on a view | Mouse | `{}` |
| `InvokeLongClick` | a long press on a view | Mouse | `{ "durationMilliseconds": 500 }` |
| `SendKeys` | the text of one field, sent once when you leave the field | Keyboard | `{ "text" }`, or `{ "text": "***", "isRedacted": true }` for a password when redaction is on |
| `InvokeScroll` | a scroll gesture | Mouse | `{ "direction": "Up", "Down", "Left" or "Right", "notches" }` |
| `HideSoftKeyboard` | the on-screen keyboard closes | Keyboard | `{}` |
| `SwitchActivity` | a new activity comes to the front | Context | `{ "package", "activity" }` |
| `UndoNavigation` | the Back key is released | Navigation | `{}` |

How the app decides when an event is finished:

- **Text.** Every change to one field updates one pending entry. It is sent as one `SendKeys` with the final text when you use another view, the keyboard closes, the activity changes, or the host flushes or stops. A tap on the same field keeps it open. Text is capped at 512 characters.
- **Scroll.** Scroll events on the same view and in the same direction, less than 500 ms apart, become one `InvokeScroll`. `notches` is the number of scroll events in the gesture.
- **Order.** A pending text or scroll is always sent before the next event, so the flow keeps your order.
- **Activities.** Dialogs, menus and popups change the window too, but only a real activity sends `SwitchActivity`.
- **Back.** The Back key becomes `UndoNavigation`, the name the Chromium recorder uses for Back. The G4 plugin of that name navigates back on Appium too, so a recorded flow leaves a screen the way the user did.

`SwitchActivity`, `HideSoftKeyboard` and `UndoNavigation` carry an empty chain: no locator and no path, like the Chromium recorder's context events. The Hub writes no `onElement` for them.

Every event type has a switch on the app's settings screen. All are on by default.

## Chains and nodes

Chains run target first, the Chromium orientation: `chain.path[0]` is the trigger node and the last node is the window root, which is also `chain.topWindow`.

| Contract field | Source on Android |
| --- | --- |
| `chain.locator` | An XPath in the UiAutomator2 page-source vocabulary. See [Locators](#locators). |
| `chain.point` | The center of the target, in screen pixels. |
| `chain.trigger` | The accessibility event, such as `ViewClicked`. |
| `automationId` | The resource id, such as `com.app:id/email`. |
| `className` | The view class, such as `android.widget.EditText`. |
| `controlType` | The short class name, such as `EditText`. |
| `frameworkId` | `Android`. |
| `name` | The text of a label or button, else the content description, else the hint, else the first label inside the node, the way a screen reader names a list row. A scrollable container keeps an empty name. A field's typed content is never the name. |
| `bounds` | The screen rectangle in pixels. |
| `machine.name` / `machine.publicAddress` | The device model / the adb serial the host passed in. |
| `processId` | `0`. Android does not let one app read another app's process id. The host returns the real one from `StartRecorder`. |
| `properties` | `package`, `index`, `checkable`, `checked`, `clickable`, `enabled`, `focused`, `longClickable`, `password`, `scrollable`, `activity`, and on the trigger node of element events `domContext` and `domContextLength`. |

`controlTypeId`, `patterns` and `runtimeId` have no Android equivalent and carry the same empty values as the Chromium recorder's.

## Locators

The locator uses the vocabulary of the Appium UiAutomator2 page source, so a recorded flow replays on the G4 `AndroidDriver` without changes. The class is the tag, and `resource-id`, `content-desc` and `text` are attributes. The recorder tries, in order:

1. A unique resource id: `//android.widget.EditText[@resource-id='com.app:id/email']`.
2. A unique content description: `//android.widget.ImageButton[@content-desc='Share']`.
3. A unique text, only for views that are not editable: `//android.widget.Button[@text='Sign in']`.
4. A unique label inside the node, for a row or card whose title is a child view: `//android.widget.TextView[@text='Network & internet']/ancestor::android.widget.LinearLayout[1]`. The node must be the nearest ancestor of its class above the label. Scrollable containers skip this step, because their children change as they scroll.
5. The absolute path from the root, which always matches one node: `/hierarchy/android.widget.FrameLayout[1]/android.widget.LinearLayout[1]/android.widget.TextView[2]`. The position counts siblings of the same class.

Uniqueness is checked against the window the view is in.

## View context

Element events carry a snapshot of the views around their target, so a client can heal or improve the locator. The events are `InvokeClick`, `InvokeLongClick` and `SendKeys`. The snapshot uses the same property names as the Chromium DOM context, so the Hub and VS Code treat both alike:

| Property | Type | Description |
| --- | --- | --- |
| `domContext` | string | Base64 of the UTF-8 XML snapshot. |
| `domContextLength` | number | The decoded byte count. |

How the snapshot is made:

- It is the XML of the subtree rooted **Ancestor levels** above the target (default 2), in the UiAutomator2 page-source form. You can test a candidate XPath straight against it.
- The target carries `g4-target="true"`.
- Password fields have an empty `text`.
- The decoded snapshot is at most **Maximum snapshot size** (default 16 KB). A snapshot that is too large drops one level at a time, then falls back to the target element alone. When even that is too large, the event carries no `domContext`.

The three settings are in the **View context** section of the app's settings screen.

## Limits

- `InvokeLongClick` and `SwitchActivity` have no replay plugin yet.
- Only real key presses reach accessibility services. Back from a hardware key or the emulator's keys is recorded. Back sent with `adb shell input keyevent`, and the back gesture of gesture navigation, send no key event and are not recorded.
- The G4 `InvokeScroll` plugin reads only its `Left` and `Top` parameters, while the Hub writes `--direction` and `--times`, so a recorded scroll replays as a no-op. This affects Chromium flows the same way.
- `notches` counts scroll events, not distance. A fast fling makes many notches.
- Android 8 reports no scroll distance, so every scroll there is recorded as `Down`.
- Android hides the content of password fields from accessibility services. With redaction off, `SendKeys` carries what Android reports, which is usually empty.
- `HideSoftKeyboard` is recorded whenever the keyboard closes, also when a tap on a button closes it. Replay of `HideSoftKeyboard` with no keyboard showing depends on the plugin.

## Next

- [Recording event contract](contract.md)
- [Build a flow from events](build-a-flow.md)
- [Module 5: The Android recorder](../install-and-operate/05-android-recorder.md)
