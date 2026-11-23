using G4.Recorders.Chromium.Domain.Models;

using System.Threading.Tasks;

namespace G4.Recorders.Chromium.Domain.Middlewares
{
    /// <summary>
    /// Broadcasts Chromium recorder events to connected SignalR consumers.
    /// </summary>
    /// <remarks>
    /// This is the Chromium counterpart to the desktop <c>UiaEventCaptureService</c>. Unlike the
    /// UIA service it does not capture input: the browser extension already builds the full event
    /// (including the DOM chain) and pushes it to the hub, so this service only owns the broadcast
    /// step. It exists so the "ReceiveRecordingEvent" envelope is produced in one place and stays
    /// byte-shaped identically to the UIA broadcast, letting consumers treat both sources uniformly.
    /// </remarks>
    public interface IChromiumEventCaptureService
    {
        #region *** Methods      ***
        /// <summary>
        /// Broadcasts a locator amendment to every connected consumer using the dedicated
        /// "ReceiveLocatorAmendment" message and the same <c>{ Value = payload }</c> envelope as recording events.
        /// </summary>
        /// <remarks>
        /// The amendment travels under its own message name, so a consumer that only listens to
        /// "ReceiveRecordingEvent" never receives it. A null amendment, or one without a locator identifier,
        /// is ignored and the returned task is already complete.
        /// </remarks>
        /// <param name="amendment">The locator correction pushed by the recorder extension.</param>
        /// <returns>A task that completes once the amendment has been handed to the transport.</returns>
        Task BroadcastLocatorAmendmentAsync(ChromiumLocatorAmendmentModel amendment);

        /// <summary>
        /// Broadcasts a recorder event to every connected consumer using the shared
        /// "ReceiveRecordingEvent" message and envelope.
        /// </summary>
        /// <param name="recordingEvent">The event pushed by the recorder extension.</param>
        /// <returns>A task that completes once the event has been handed to the transport.</returns>
        Task BroadcastRecordingEventAsync(ChromiumEventModel recordingEvent);
        #endregion
    }
}
