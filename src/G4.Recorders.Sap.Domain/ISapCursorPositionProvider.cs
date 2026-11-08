using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Provides the current physical cursor position for current-position peek requests.
    /// </summary>
    public interface ISapCursorPositionProvider
    {
        #region *** Methods      ***
        /// <summary>
        /// Gets the current cursor position in physical screen pixels.
        /// </summary>
        /// <param name="point">The cursor position, or <c>null</c> when it cannot be read.</param>
        /// <returns><c>true</c> when the position was read; otherwise, <c>false</c>.</returns>
        bool GetCurrent(out RecorderPointModel point);
        #endregion
    }
}
