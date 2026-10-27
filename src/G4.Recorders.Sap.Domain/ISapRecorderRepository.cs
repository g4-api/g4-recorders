using G4.Recorders.Common.Domain;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Represents a repository for resolving SAP GUI controls and their chains.
    /// </summary>
    /// <remarks>
    /// The desktop primitives inherited from <see cref="IRecorderPrimitivesRepository"/> work as on the UIA surface, so
    /// the SAP recorder exposes the same REST, MCP, and command-line operations as the UIA recorder.
    /// </remarks>
    public interface ISapRecorderRepository : IRecorderPrimitivesRepository
    {
        #region *** Methods      ***
        /// <summary>
        /// Gets the chain of the focused SAP control: in the foreground SAP window, or in the first session's active
        /// window when the foreground window is not a SAP window.
        /// </summary>
        /// <returns>The chain, or an empty chain when no SAP control has the focus.</returns>
        /// <exception cref="System.InvalidOperationException">SAP GUI Scripting is not available.</exception>
        SapChainModel GetElementChain();

        /// <summary>
        /// Gets the chain of the SAP control at a screen point.
        /// </summary>
        /// <param name="x">The horizontal screen coordinate in physical pixels.</param>
        /// <param name="y">The vertical screen coordinate in physical pixels.</param>
        /// <returns>The chain, or an empty chain when the point is not on a SAP control.</returns>
        /// <exception cref="System.InvalidOperationException">SAP GUI Scripting is not available.</exception>
        SapChainModel GetElementChain(int x, int y);

        /// <summary>
        /// Physically moves the cursor to the given coordinates, resolves the SAP control at the settled cursor
        /// position, and - unless suppressed - resolves the pointer's pixel offset from that control.
        /// </summary>
        /// <param name="x">The horizontal physical screen coordinate to ground against.</param>
        /// <param name="y">The vertical physical screen coordinate to ground against.</param>
        /// <param name="skipOffset">When <c>true</c>, leaves the returned chain's offset null.</param>
        /// <returns>The chain at the settled cursor position, with its offset unless <paramref name="skipOffset"/> is set.</returns>
        /// <exception cref="System.InvalidOperationException">SAP GUI Scripting is not available.</exception>
        SapChainModel ResolveGroundedElement(int x, int y, bool skipOffset);
        #endregion
    }
}
