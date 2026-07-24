using G4.Recorders.Chromium.Domain.Hubs;
using G4.Recorders.Chromium.Domain.Models;

using Microsoft.AspNetCore.SignalR;

using System.Threading.Tasks;

namespace G4.Recorders.Chromium.Domain.Middlewares
{
    /// <summary>
    /// Default <see cref="IChromiumEventCaptureService"/>. Fans recorder events out to every
    /// connected consumer through the <see cref="ChromiumRecorderHub"/> hub context.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton: it is stateless and the injected hub context is itself a
    /// singleton. There is deliberately no background queue like the desktop capture service has —
    /// that queue exists only to keep slow UI Automation tree walks off the OS input-hook thread,
    /// a constraint that does not apply here because the extension already resolved the DOM chain
    /// before invoking the hub, and the hub method runs on a normal thread-pool thread.
    /// </remarks>
    /// <param name="hubContext">Hub context used to broadcast events to connected consumers.</param>
    public class ChromiumEventCaptureService(IHubContext<ChromiumRecorderHub> hubContext) : IChromiumEventCaptureService
    {
        // Hub context used to fan recorder events out to every connected consumer.
        private readonly IHubContext<ChromiumRecorderHub> _hubContext = hubContext;

        /// <inheritdoc />
        public Task BroadcastRecordingEventAsync(ChromiumEventModel recordingEvent)
        {
            // Fan the event out to all clients using the same "ReceiveRecordingEvent" message and
            // { Value = event } envelope as the desktop UIA capture service, so a consumer sees an
            // identical shape regardless of whether the event originated from Chromium or UIA.
            return _hubContext.Clients.All.SendAsync(
                method: "ReceiveRecordingEvent",
                arg1: new { Value = recordingEvent });
        }
    }
}
