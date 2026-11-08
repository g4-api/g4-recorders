using G4.Recorders.Sap.Domain.Native;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Reads the desktop state the SAP lookups need: the top-level window at a point and the foreground window.
    /// </summary>
    /// <remarks>
    /// Handles are returned as numbers, matching how SAP GUI Scripting reports a window's <c>Handle</c>, so the two
    /// compare directly. The process is per-monitor DPI aware, so every coordinate is in physical pixels.
    /// </remarks>
    internal static class SapDesktop
    {
        #region *** Methods      ***
        /// <summary>
        /// Gets the top-level handle of the foreground window.
        /// </summary>
        /// <returns>The handle, or zero when no window has the foreground.</returns>
        internal static long GetForegroundRootWindow()
        {
            // Walk from the foreground window to its root, in case a child window holds the foreground.
            return NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), NativeMethods.GaRoot).ToInt64();
        }

        /// <summary>
        /// Gets the top-level handle of the window at a screen point.
        /// </summary>
        /// <param name="x">The horizontal screen coordinate in physical pixels.</param>
        /// <param name="y">The vertical screen coordinate in physical pixels.</param>
        /// <returns>The handle, or zero when no window is at the point.</returns>
        internal static long GetRootWindow(int x, int y)
        {
            // Hit-test the desktop, then walk from the child window under the point to its root.
            var window = NativeMethods.WindowFromPoint(new NativeMethods.Point { X = x, Y = y });

            return NativeMethods.GetAncestor(window, NativeMethods.GaRoot).ToInt64();
        }
        #endregion
    }
}
