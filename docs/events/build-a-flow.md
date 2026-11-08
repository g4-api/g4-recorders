# Build a Flow from Recording Events

[⬅ Back to the docs index](../README.md)

A recording client collects the events a recorder broadcasts and turns them into a G4 automation (a flow). This page lists the rules that mapping follows. The code is the `AutomationBuilder.cs` file in the G4 Hub (`g4-services/src/G4.Services.Domain.V4/Recordings/AutomationBuilder.cs`). The older `g4-recorders-client` has its own copy with the same plugin mapping (`g4-recorders-client/src/G4.Recorders.Client/AutomationBuilder.cs`). See [Client versus Hub](client-vs-hub.md) for the differences.

## Shape of the result

- One stage named "Recorded Actions Stage" holds the flow (`AutomationBuilder.cs`, `NewAutomation`).
- Each run of events from one recorder endpoint becomes one job named "Recorded Actions Job (<machine>)".
- When a later run returns to an earlier endpoint, its job reuses the driver of the first job as `Job(<n>)`.
- The last job of each endpoint ends with a `CloseBrowser` action.
- With one recorder, the flow uses that recorder driver. With none, it uses `NoDriver`.
- Every rule carries `context.timestamp`, copied from the event.

## Capture modes

A UIA recorder can run in three modes. Chromium always records in `standard` mode (`RecordingsRepository.cs`, `ResolveRecorder`).

| Mode | Meaning | Rules use |
| --- | --- | --- |
| `standard` | Element-pattern input. | Plugins that act on the element, such as `InvokeClick`, with a locator. |
| `user32` | Physical input with element locators. | `User32` plugins with a locator. Offsets are optional (`useOffset`). |
| `coordinate` | Physical input at absolute positions. | `User32` plugins with no locator. |

The Hub accepts only these three mode names, in lower case, and only the drivers `ChromeDriver` and `UiaDriver`. Anything else is refused with "Recorder mode must be standard, user32, or coordinate." or "Recorder driver must be ChromeDriver or UiaDriver." (`RecordingsRepository.cs:412-415`).

## UIA events to plugins

The builder keeps release events only. Presses (`Down`), standalone modifier keys and clicks without a chain are dropped (`TestUiaEvent`).

### Keyboard

| Event | Mode | Plugin | Argument |
| --- | --- | --- | --- |
| A run of typed characters | `standard` | `SendKeys` | `{{$ --Keys:<text>}}` |
| A run of typed characters | `user32`, `coordinate` | `SendUser32Keys` | `{{$ --Keys:<text>}}` |
| A single special key, such as Enter | `standard` | `SendKeyboardKey` | `{{$ --Key:<key>}}` |
| A single special key | `user32`, `coordinate` | `SendUser32KeyboardKey` | `{{$ --Key:<key>}}` |
| A shortcut, such as Ctrl+A | any | `SendUser32KeyboardKey` | `{{$ --Key:<modifier> --Key:<key> --Sticky}}` |
| A Windows key released alone | any | `SendUser32KeyboardKey` | `{{$ --Key:LWin --Sticky}}` or `RWin` |

Details (`NewUiaRules`, `NewKeyboardRule`, `NewShortcutRule`):

- Consecutive character releases are joined into one text run. A special key, a shortcut, or a mouse event ends the run.
- The word `Space` counts as a character in a run.
- The modifiers of a shortcut come from `value.modifiers` when the recorder sends it. For older recorders the builder tracks held modifier keys across the stream.
- In `coordinate` mode, keyboard rules have no locator and act on whatever has focus.

### Mouse

The button is the first word of the event name. Only the release (`Up`) event produces a rule.

| Event | Mode | Plugin |
| --- | --- | --- |
| `Left Up` | `standard` | `InvokeClick` |
| `Left Up` | `user32`, `coordinate` | `InvokeUser32Click` |
| `Middle Up` | `standard` | `InvokeMiddleClick` |
| `Middle Up` | `user32`, `coordinate` | `InvokeUser32MiddleClick` |
| `Right Up` | `standard` | `InvokeContextClick` |
| `Right Up` | `user32`, `coordinate` | `InvokeUser32ContextClick` |
| Any other button word | any | `None` (a placeholder rule) |

Arguments and locators (`NewMouseRule`):

- In `coordinate` mode the argument is `{{$ --X:<x> --Y:<y>}}`, taken from `value.x` and `value.y`, and the rule has no locator.
- In the other modes the rule gets the `chain.locator`. If it is empty, the builder falls back to `chain.fallbackLocator`.
- With `user32` mode and `useOffset` on, the builder reads `chain.mouseOffset`. If that is empty it falls back to an event-level `offset`, which current recorders do not send. A non-zero offset gives the argument `{{$ --OffsetX:<x> --OffsetY:<y> --Alignment:TopLeft}}`.

Wheel events are not mapped to scroll actions. By the filter and button rules above, `Wheel Up` passes the release test and becomes a `None` placeholder, and the other wheel events are dropped. This is read from the code and was not run.

## Chromium events to plugins

Chromium events arrive with plugin names already in `event`. The builder keeps the name as the plugin and shapes the argument (`NewChromiumRule`).

| Event | Argument | Locator |
| --- | --- | --- |
| `SendKeys` | `{{$ --keys:<value.text>}}` | Yes |
| `OpenUrl` | `<value.url>` | No |
| `UpdatePage`, `UndoNavigation`, `RedoNavigation` | none | No |
| `SwitchWindow`, `CloseWindow` | `<value.index>`, or `0` | No |
| `InvokeScroll` | `{{$ --direction:<direction> --times:<notches>}}` (defaults: `Down`, `1`) | Yes |
| Any other name, such as `InvokeClick` | none | Yes |

Unknown event names pass through as element actions, so new extension plugins keep their locator. Events without an `event` name are skipped. The full list of names is in [Chromium events](chromium-events.md).

## Think time

When think time is on, the builder adds a `WaitFlow` action between two rules when the gap between their timestamps is larger than the minimum. The wait is the gap, capped at the maximum, in the argument `{{$ --Timeout:<ms>}}`, with the display name "Think Time (<seconds> seconds)" (`NewTimedRules`). The Hub refuses think-time values that are negative or where the maximum is below the minimum (`RecordingsRepository.cs`).

## What is not mapped

- SAP recorders. The Hub accepts only `ChromeDriver` and `UiaDriver`, and the builder has no code for the `user32` and `nativeSap` streams (a search for `nativeSap` in the Hub `Recordings` folder finds nothing). A SAP recording has to be consumed from the hub directly. See [SAP messages](sap-message.md).
- Pre and post scripts. The Hub refuses them with "Pre/post scripts are deferred to a later layer; disable their flags." (`RecordingsRepository.cs`).

## Next

- [Client versus Hub](client-vs-hub.md)
- [Recording event contract](contract.md)
