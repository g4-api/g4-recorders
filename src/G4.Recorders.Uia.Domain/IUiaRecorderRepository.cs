using G4.Recorders.Common.Domain;

using G4.Recorders.Uia.Domain.Models;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Represents a repository for accessing UI Automation elements and their ancestor chains.
    /// </summary>
    public interface IUiaRecorderRepository : IRecorderPrimitivesRepository
    {
        #region *** Methods      ***
        /// <summary>
        /// Retrieves the currently focused UI Automation element and constructs
        /// its ancestor chain representation, including an absolute XPath locator.
        /// </summary>
        /// <returns>A <see cref="UiaChainModel"/> representing the focused element and its ancestors, or an empty model if no element is currently focused.</returns>
        UiaChainModel GetElementChain();

        /// <summary>
        /// Retrieves the ancestor chain of the UI Automation element located at the given screen coordinates.
        /// </summary>
        /// <param name="x">The X-coordinate on the screen.</param>
        /// <param name="y">The Y-coordinate on the screen.</param>
        /// <returns>A <see cref="UiaChainModel"/> representing the ancestor chain of the element at the specified point, or <c>null</c> if no element is found.</returns>
        UiaChainModel GetElementChain(int x, int y);

        /// <summary>
        /// Physically moves the cursor to the given coordinates, resolves the UI Automation element at the settled
        /// cursor position, and - unless suppressed - resolves the pointer's pixel offset from that element.
        /// </summary>
        /// <param name="x">The horizontal physical screen coordinate to ground against.</param>
        /// <param name="y">The vertical physical screen coordinate to ground against.</param>
        /// <param name="skipOffset">
        /// When <c>true</c>, skips the offset computation and leaves the returned chain's offset null.
        /// </param>
        /// <returns>
        /// The ancestor chain resolved at the settled cursor position, with its offset populated unless
        /// <paramref name="skipOffset"/> is set.
        /// </returns>
        UiaChainModel ResolveGroundedElement(int x, int y, bool skipOffset);
        #endregion
    }
}
