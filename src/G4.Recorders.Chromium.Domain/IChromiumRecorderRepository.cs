using G4.Recorders.Common.Domain.Models;

using G4.Recorders.Chromium.Domain.Models;

using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Chromium.Domain
{
    /// <summary>
    /// Coordinates correlated Chromium element queries between server callers and connected recorder extensions.
    /// </summary>
    public interface IChromiumRecorderRepository
    {
        /// <summary>
        /// Completes a pending peek request from a response returned by the recorder extension.
        /// </summary>
        /// <param name="response">The correlated response received by the SignalR hub.</param>
        /// <param name="connectionId">The transport connection that submitted the response.</param>
        /// <returns><c>true</c> when a pending request accepted the response; otherwise, <c>false</c>.</returns>
        bool Complete(ChromiumPeekResponseModel response, string connectionId);

        /// <summary>
        /// Gets a Chromium element chain by sending one normalized request to the connected recorder extension.
        /// </summary>
        /// <param name="request">The normalized coordinate, focus, or current-pointer request.</param>
        /// <param name="cancellationToken">Stops waiting when the caller disconnects or abandons the request.</param>
        /// <returns>The resolved chain, or an empty chain when no accessible element matched.</returns>
        Task<ChromiumChainModel> GetAsync(RecorderPeekRequestModel request, CancellationToken cancellationToken);

        /// <summary>
        /// Registers a SignalR connection as a recorder extension eligible to receive element queries.
        /// </summary>
        /// <param name="connectionId">The SignalR connection identifier owned by the extension.</param>
        void Register(string connectionId);

        /// <summary>
        /// Removes a disconnected recorder extension and fails requests that were waiting on that connection.
        /// </summary>
        /// <param name="connectionId">The disconnected SignalR connection identifier.</param>
        void Remove(string connectionId);
    }
}
