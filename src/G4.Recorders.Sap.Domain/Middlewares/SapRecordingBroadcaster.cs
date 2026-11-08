using Microsoft.AspNetCore.SignalR;

using System.Threading.Tasks;

using G4.Recorders.Sap.Domain.Hubs;
using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Default <see cref="ISapRecordingBroadcaster"/> that fans messages out through the SAP recorder hub context.
    /// </summary>
    /// <remarks>
    /// Register it as a singleton: it is stateless and the hub context is itself a singleton.
    /// </remarks>
    /// <param name="hubContext">The hub context used to reach every connected client.</param>
    public sealed class SapRecordingBroadcaster(IHubContext<SapRecorderHub> hubContext) : ISapRecordingBroadcaster
    {
        #region *** Fields       ***
        // Reaches every connected client of the SAP recorder hub.
        private readonly IHubContext<SapRecorderHub> _hubContext = hubContext;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public Task SendAsync(SapRecordingMessageModel message)
        {
            // Use the shared message name and { value } envelope, so clients route SAP messages like the others.
            return _hubContext.Clients.All.SendAsync(method: "ReceiveRecordingEvent", arg1: new { Value = message });
        }
        #endregion
    }
}
