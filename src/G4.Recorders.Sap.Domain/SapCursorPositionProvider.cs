using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Sap.Domain.Native;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Default <see cref="ISapCursorPositionProvider"/> that reads the cursor through <c>GetCursorPos</c>.
    /// </summary>
    /// <remarks>
    /// The process is per-monitor DPI aware, so the position is in physical pixels, the same space SAP GUI Scripting
    /// uses for <c>findByPosition</c>.
    /// </remarks>
    public sealed class SapCursorPositionProvider : ISapCursorPositionProvider
    {
        #region *** Methods      ***
        /// <inheritdoc />
        public bool GetCurrent(out RecorderPointModel point)
        {
            // Read the cursor; a failure, such as a locked desktop, leaves no point.
            if (!NativeMethods.GetCursorPos(out var position))
            {
                point = null;
                return false;
            }

            point = new RecorderPointModel { XPos = position.X, YPos = position.Y };

            return true;
        }
        #endregion
    }
}
