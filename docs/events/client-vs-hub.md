# Client versus Hub

[⬅ Back to the docs index](../README.md)

Two services can turn recorder events into a G4 flow: the standalone `g4-recorders-client`, and the Recordings API of the G4 Hub (`g4-services`). The client is superseded by the Hub (`g4-recorders-client/README.md`, "Status"). This page compares them so you can pick one, or move from the client to the Hub.

## At a glance

| Topic | `g4-recorders-client` | G4 Hub Recordings API |
| --- | --- | --- |
| Status | Superseded. Stays available for standalone use. | Current. |
| Default port | 9957 (`appsettings.json`) | 9944 |
| REST prefix | `/api/v4/g4/recordings` | `/api/v4/g4/recordings` |
| Start a session | `POST /api/v4/g4/recordings` | `POST /api/v4/g4/recordings` |
| Get status | `GET /api/v4/g4/recordings/{id}` | `GET /api/v4/g4/recordings/{id}` |
| Get the flow | `GET /api/v4/g4/recordings/{id}/flow` | `GET /api/v4/g4/recordings/{id}/flow` |
| Stop | `POST /api/v4/g4/recordings/{id}/stop` | `POST /api/v4/g4/recordings/{id}/stop` |
| Suspend a recorder | Not available | `POST /api/v4/g4/recordings/{id}/suspend` |
| Resume a recorder | Not available | `POST /api/v4/g4/recordings/{id}/resume` |
| Drain before stop | No. Stop reads the buffer at once. | Yes. See [Drain on stop](#drain-on-stop). |
| MCP tools | `get_automation`, `get_recording_status`, `start_recording`, `stop_recording` | `g4.GetAutomation`, `g4.GetRecordingStatus`, `g4.StartRecording`, `g4.StopRecording` |
| Supported drivers | `UiaDriver`, `ChromeDriver` | `UiaDriver`, `ChromeDriver`, `AndroidDriver` |
| SAP recorder | Rejected | Rejected |
| Pre and post scripts | Refused ("deferred to a later layer") | Refused (same message) |

Sources: `g4-recorders-client/src/G4.Recorders.Client/Controllers/RecordingsController.cs:16-73`, `RecordingTools.cs:30-75`, `RecordingService.cs:385-425`; `g4-services/src/G4.Services.Hub.Api.V4/Controllers/RecordingsController.cs:20-130`, `G4ToolNames.cs:39,64,123,138`, `Repositories/RecordingsRepository.cs:412-440`.

## Suspend and resume

The Hub can suspend one recorder of a running session and resume it later. While a recorder is suspended its events are dropped, and events that arrived during the suspension stay dropped after resume. The recorder stays connected, so a resume needs no reconnect and no browser relaunch (`IRecordingsRepository.cs:90-104`, `RecorderConnection.cs:244-310`). Both routes take the recorder selection in the body, matched by its base URL (`RecordingsController.cs:68-140`).

The client has no such route. Search `g4-recorders-client/src` for `Suspend`: there is no hit.

## Drain on stop

When you stop a session, the Hub first asks each recorder to drain its capture queue by invoking the hub method `FlushRecorder`, waiting up to 5 seconds. Then it waits for the transport to be quiet, with a quiet period of 300 milliseconds and a ceiling of 1,500 milliseconds. This keeps the last keys and clicks that were still on the queue. Chromium has no `FlushRecorder`, so it uses the quiet wait alone (`RecorderConnection.cs:20-29,175-215`).

The UIA and SAP hubs expose `FlushRecorder` (`UiaRecorderHub.cs:57`, `SapRecorderHub.cs:89`). The client has no flush step: `StopAsync` takes the buffer straight from the connection (`RecordingService.cs:212-254`).

## Supported drivers

The client accepts a recorder only when its driver name is `UiaDriver` (exact match, any case) or contains `chrome`, and refuses any other with "Recorder driver must be ChromeDriver or UiaDriver." (`RecordingService.cs:385-393`). The Hub also accepts `AndroidDriver` (exact match, any case), and refuses any other driver with "Recorder driver must be AndroidDriver, ChromeDriver or UiaDriver." (`RecordingsRepository.cs:410-415`). The Hub starts and stops Android recordings through `StartRecorder` and `StopRecorder`, as for Chromium. The SAP recorder therefore cannot be used with either service. Consume its hub directly. See [SAP messages](sap-message.md).

## Moving from the client to the Hub

1. Change the base address from `http://localhost:9957` to the Hub address, `http://localhost:9944` by default.
2. Keep the recorder endpoints in the request: UIA on 9955 and Chromium on 9956. The Hub also takes Android on 9958.
3. Use the same four routes. Check the Swagger page of the Hub at `/swagger` for the exact request body, because the two services have separate models.
4. For MCP, switch the tool names to the `g4.` form in the table above and point the MCP client at the Hub.

The Hub guide for this API is [Recording Automations](../../../g4-services/docs/recordings-api.md).

## Next

- [Build a flow from events](build-a-flow.md)
- [Peek and observe](peek-and-observe.md)
