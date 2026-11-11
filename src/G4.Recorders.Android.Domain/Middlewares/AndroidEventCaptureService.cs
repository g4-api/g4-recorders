using G4.Recorders.Android.Domain.Hubs;
using G4.Recorders.Android.Domain.Models;

using Microsoft.AspNetCore.SignalR;

using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.Middlewares
{
    /// <summary>
    /// Default <see cref="IAndroidEventCaptureService"/>: fans events out through the hub context.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton; it holds no state. There is no background queue: the app already queues events on its
    /// own sender thread and delivers them in order, and the hub method runs on a thread-pool thread.
    /// </remarks>
    /// <param name="hubContext">Broadcasts to every connected consumer.</param>
    public sealed class AndroidEventCaptureService(IHubContext<AndroidRecorderHub> hubContext) : IAndroidEventCaptureService
    {
        #region *** Fields       ***
        // Broadcasts to every connected consumer.
        private readonly IHubContext<AndroidRecorderHub> _hubContext = hubContext;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public Task BroadcastRecordingEventAsync(AndroidEventModel recordingEvent)
        {
            // Use the shared message name and { value } envelope, so consumers treat every recorder the same way.
            return _hubContext.Clients.All.SendAsync(method: "ReceiveRecordingEvent", arg1: new { Value = recordingEvent });
        }
        #endregion
    }
}
