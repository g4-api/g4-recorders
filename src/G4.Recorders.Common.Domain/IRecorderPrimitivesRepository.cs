using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Common.Domain
{
    /// <summary>
    /// Represents the OS-level recorder primitives shared identically across every recorder surface, independent
    /// of any surface-specific element or DOM model.
    /// </summary>
    /// <remarks>
    /// Grounding a coordinate against a surface's own element tree stays on each surface's typed repository
    /// interface (<c>IUiaRecorderRepository</c>/<c>IChromiumRecorderRepository</c>) instead of here, because its
    /// return type carries that surface's own chain model.
    /// </remarks>
    public interface IRecorderPrimitivesRepository
    {
        #region *** Methods      ***
        /// <summary>
        /// Captures the full virtual desktop, or only its coordinate-space metrics.
        /// </summary>
        /// <param name="metricsOnly">
        /// When <c>true</c>, skips the actual pixel capture and returns only the virtual-desktop bounds.
        /// </param>
        /// <returns>The captured image and its coordinate-space metadata.</returns>
        RecorderScreenshotModel GetScreenshot(bool metricsOnly);

        /// <summary>
        /// Physically moves the desktop cursor to the given physical screen coordinates.
        /// </summary>
        /// <param name="x">The horizontal physical screen coordinate to move the cursor to.</param>
        /// <param name="y">The vertical physical screen coordinate to move the cursor to.</param>
        /// <returns>The cursor position read back after the move, confirming where it actually settled.</returns>
        RecorderPointModel MovePointer(int x, int y);

        /// <summary>
        /// Brings a running process's top-level window to the foreground on a best-effort basis.
        /// </summary>
        /// <param name="windowTitle">
        /// A case-insensitive substring to match against a running process's window title, or <c>null</c> to skip
        /// title matching.
        /// </param>
        /// <param name="processName">
        /// An exact process name to match, or <c>null</c> to skip process-name matching. Combined with
        /// <paramref name="windowTitle"/> as an AND when both are supplied.
        /// </param>
        /// <returns>
        /// The match and focus outcome. Never throws for "not found," "ambiguous," or "found but could not be
        /// focused" - those are legitimate, expected outcomes reported structurally.
        /// </returns>
        RecorderWindowFocusModel SetWindowFocus(string windowTitle, string processName);
        #endregion
    }
}
