using G4.Recorders.Common.Domain.Models;

using System.Runtime.InteropServices;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Reads physical cursor coordinates from Windows for current-position UIA peek requests.
    /// </summary>
    public class UiaCursorPositionProvider : IUiaCursorPositionProvider
    {
        #region *** Methods      ***
        /// <inheritdoc />
        public bool GetCurrent(out RecorderPointModel point)
        {
            // Read physical coordinates so the result uses the same DPI-aware space as UI Automation.
            var isPositionAvailable = GetPhysicalCursorPos(out var nativePoint);

            // Publish a stable model even on failure so callers never observe partially initialized coordinates.
            point = isPositionAvailable
                ? new RecorderPointModel { XPos = nativePoint.X, YPos = nativePoint.Y }
                : new RecorderPointModel();

            return isPositionAvailable;
        }

        // Imports the Windows physical-cursor API behind this provider so consumers remain testable and interop-free.
        [DllImport("user32.dll")]
        private static extern bool GetPhysicalCursorPos(out NativePoint point);
        #endregion

        #region *** Nested Types ***
        // Mirrors the Win32 POINT layout used only at the native boundary before conversion to the shared model.
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            // Carries the physical horizontal desktop coordinate returned by Windows.
            internal int X;

            // Carries the physical vertical desktop coordinate returned by Windows.
            internal int Y;
        }
        #endregion
    }
}
