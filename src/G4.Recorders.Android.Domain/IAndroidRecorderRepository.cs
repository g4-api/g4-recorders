using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Common.Domain;
using G4.Recorders.Common.Domain.Models;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain
{
    /// <summary>
    /// Coordinates requests between server callers and the connected Android recorder app.
    /// </summary>
    /// <remarks>
    /// The app connects to the hub as a client and registers itself; the repository tracks those connections and
    /// correlates the requests the host sends to the app (peek and flush) with the answers that come back.
    /// </remarks>
    public interface IAndroidRecorderRepository : IRecorderPrimitivesRepository
    {
        #region *** Methods      ***
        /// <summary>
        /// Asks every registered recorder app to close, and waits until they disconnect or the wait ends.
        /// </summary>
        /// <param name="timeout">The longest wait for the apps to disconnect.</param>
        /// <returns><c>true</c> when no app was left connected; otherwise, <c>false</c>.</returns>
        Task<bool> CloseRecordersAsync(TimeSpan timeout);

        /// <summary>
        /// Completes a pending peek request with the app's answer.
        /// </summary>
        /// <param name="response">The correlated answer received by the hub.</param>
        /// <param name="connectionId">The connection that sent the answer.</param>
        /// <returns><c>true</c> when a pending request accepted the answer; otherwise, <c>false</c>.</returns>
        bool Complete(AndroidPeekResponseModel response, string connectionId);

        /// <summary>
        /// Completes a pending flush request with the app's acknowledgement.
        /// </summary>
        /// <param name="requestId">The correlation of the flush request.</param>
        /// <param name="connectionId">The connection that sent the acknowledgement.</param>
        /// <returns><c>true</c> when a pending flush accepted the acknowledgement; otherwise, <c>false</c>.</returns>
        bool CompleteFlush(string requestId, string connectionId);

        /// <summary>
        /// Waits until every registered app has sent the events it captured before this call.
        /// </summary>
        /// <param name="timeout">The longest wait for the acknowledgements.</param>
        /// <param name="cancellationToken">Stops waiting when the caller disconnects.</param>
        /// <returns><c>true</c> when every app acknowledged, or none is connected; <c>false</c> on timeout.</returns>
        Task<bool> FlushAsync(TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>
        /// Gets an element chain from the connected recorder app.
        /// </summary>
        /// <param name="request">The normalized coordinate, focus or current-node request.</param>
        /// <param name="cancellationToken">Stops waiting when the caller disconnects or abandons the request.</param>
        /// <returns>The resolved chain, or an empty chain when no node matched.</returns>
        Task<AndroidChainModel> GetElementChainAsync(RecorderPeekRequestModel request, CancellationToken cancellationToken);

        /// <summary>
        /// Registers a SignalR connection as a recorder app eligible to receive requests.
        /// </summary>
        /// <param name="connectionId">The connection identifier owned by the app.</param>
        void Register(string connectionId);

        /// <summary>
        /// Removes a disconnected recorder app and fails the requests that were waiting on it.
        /// </summary>
        /// <param name="connectionId">The disconnected connection identifier.</param>
        void Remove(string connectionId);

        /// <summary>
        /// Moves the pointer, peeks the element there and resolves the pointer offset.
        /// </summary>
        /// <param name="x">The horizontal screen coordinate.</param>
        /// <param name="y">The vertical screen coordinate.</param>
        /// <param name="skipOffset">When <c>true</c>, skips the offset computation.</param>
        /// <param name="cancellationToken">Stops waiting when the caller disconnects.</param>
        /// <returns>The resolved chain.</returns>
        /// <remarks>
        /// Not implemented on the Android surface yet; it always throws <see cref="NotImplementedException"/>.
        /// </remarks>
        Task<AndroidChainModel> ResolveGroundedElementAsync(int x, int y, bool skipOffset, CancellationToken cancellationToken);
        #endregion
    }
}
