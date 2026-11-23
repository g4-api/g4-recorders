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
        /// Completes a pending locator test from a response returned by the recorder extension.
        /// </summary>
        /// <remarks>
        /// A response is accepted only from the connection that received the request. A malformed, unknown, or
        /// late response is ignored. A response that carries an error fails the waiting test instead of
        /// returning a result.
        /// </remarks>
        /// <param name="response">The correlated response received by the SignalR hub.</param>
        /// <param name="connectionId">The transport connection that submitted the response.</param>
        /// <returns><c>true</c> when a pending test accepted the response; otherwise, <c>false</c>.</returns>
        bool Complete(ChromiumLocatorTestResponseModel response, string connectionId);

        /// <summary>
        /// Gets a Chromium element chain by sending one normalized request to the connected recorder extension.
        /// </summary>
        /// <param name="request">The normalized coordinate, focus, or current-pointer request.</param>
        /// <param name="cancellationToken">Stops waiting when the caller disconnects or abandons the request.</param>
        /// <returns>The resolved chain, or an empty chain when no accessible element matched.</returns>
        Task<ChromiumChainModel> GetElementChainAsync(RecorderPeekRequestModel request, CancellationToken cancellationToken);

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

        /// <summary>
        /// Tests a locator against the live browser by sending one correlated request to the connected recorder
        /// extension.
        /// </summary>
        /// <remarks>
        /// The extension evaluates the locator in every frame of the page and answers within five seconds. The
        /// correlation is removed on every terminal path, so a late answer is ignored.
        /// </remarks>
        /// <param name="using">
        /// The locator strategy in its exact G4 casing: <c>Xpath</c>, <c>CssSelector</c>, <c>LinkText</c>,
        /// <c>PartialLinkText</c> or <c>TagName</c>. The repository sends it to the extension unchanged.
        /// </param>
        /// <param name="value">The locator expression to evaluate with the selected strategy.</param>
        /// <param name="cancellationToken">Stops waiting when the caller disconnects or abandons the request.</param>
        /// <returns>The tested locator, the total match count across all frames, and the first matched elements.</returns>
        /// <exception cref="System.ArgumentException">The strategy or the expression is null or blank.</exception>
        /// <exception cref="System.InvalidOperationException">
        /// No extension or more than one extension is connected, the extension reported a failure, or the extension
        /// disconnected during the test.
        /// </exception>
        /// <exception cref="System.TimeoutException">The extension did not answer before the timeout.</exception>
        Task<ChromiumLocatorTestResultModel> TestLocatorAsync(string @using, string value, CancellationToken cancellationToken);
        #endregion
    }
}
