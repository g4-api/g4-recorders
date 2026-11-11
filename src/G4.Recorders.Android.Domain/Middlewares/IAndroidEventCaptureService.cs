using G4.Recorders.Android.Domain.Models;

using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.Middlewares
{
    /// <summary>
    /// Broadcasts Android recorder events to the connected consumers.
    /// </summary>
    /// <remarks>
    /// Like the Chromium capture service it captures nothing: the recorder app builds the whole event, including the
    /// chain, and pushes it to the hub. The service owns the broadcast so the <c>ReceiveRecordingEvent</c> envelope is
    /// built in one place and matches every other recorder.
    /// </remarks>
    public interface IAndroidEventCaptureService
    {
        #region *** Methods      ***
        /// <summary>
        /// Broadcasts one event to every connected consumer as <c>ReceiveRecordingEvent</c> with a <c>value</c> envelope.
        /// </summary>
        /// <param name="recordingEvent">The event pushed by the recorder app.</param>
        /// <returns>A task that completes once the event is handed to the transport.</returns>
        Task BroadcastRecordingEventAsync(AndroidEventModel recordingEvent);
        #endregion
    }
}
