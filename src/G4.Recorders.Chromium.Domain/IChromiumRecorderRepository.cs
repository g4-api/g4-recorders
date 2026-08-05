using G4.Recorders.Common.Domain;
using G4.Recorders.Common.Domain.Models;

using G4.Recorders.Chromium.Domain.Models;

using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Chromium.Domain
{
    /// <summary>
    /// Coordinates correlated Chromium element queries between server callers and connected recorder extensions.
    /// </summary>
    public interface IChromiumRecorderRepository : IRecorderPrimitivesRepository
    {
        #region *** Methods      ***
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

        /// <summary>
        /// Physically moves the pointer to the given coordinates, peeks the DOM element found there, and - unless
        /// suppressed - resolves the pointer's pixel offset from that element's top-left corner.
        /// </summary>
        /// <param name="x">The horizontal viewport coordinate to ground against.</param>
        /// <param name="y">The vertical viewport coordinate to ground against.</param>
        /// <param name="skipOffset">
        /// When <c>true</c>, skips the offset computation and leaves the returned chain's offset null.
        /// </param>
        /// <param name="cancellationToken">Stops waiting when the caller disconnects or abandons the request.</param>
        /// <returns>The resolved ancestor chain, with its offset populated unless <paramref name="skipOffset"/> is set.</returns>
        /// <remarks>
        /// Not implemented on the Chromium surface yet - always throws <see cref="System.NotImplementedException"/>
        /// until this surface ships pointer and offset support alongside its UIA counterpart.
        /// </remarks>
        Task<ChromiumChainModel> ResolveGroundedElementAsync(int x, int y, bool skipOffset, CancellationToken cancellationToken);
        #endregion
    }
}
