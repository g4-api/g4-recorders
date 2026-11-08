using System.Threading.Tasks;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Broadcasts SAP recording messages to every connected hub client.
    /// </summary>
    /// <remarks>
    /// The message is sent as <c>ReceiveRecordingEvent</c> inside the same <c>{ value }</c> envelope the UIA and Chromium
    /// recorders use; the value holds the <c>user32</c> and <c>nativeSap</c> keys.
    /// </remarks>
    public interface ISapRecordingBroadcaster
    {
        #region *** Methods      ***
        /// <summary>
        /// Broadcasts one recording message.
        /// </summary>
        /// <param name="message">The message, with at least one stream key set.</param>
        /// <returns>A task that completes once the message has been handed to the transport.</returns>
        Task SendAsync(SapRecordingMessageModel message);
        #endregion
    }
}
