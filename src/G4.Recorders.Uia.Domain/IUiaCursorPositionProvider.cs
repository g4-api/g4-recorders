using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Reads the current physical desktop cursor position for UI Automation point resolution.
    /// </summary>
    public interface IUiaCursorPositionProvider
    {
        #region *** Methods      ***
        /// <summary>
        /// Gets the current physical desktop cursor position when Windows makes it available.
        /// </summary>
        /// <param name="point">Receives the current physical cursor position on success.</param>
        /// <returns><c>true</c> when the position was read; otherwise, <c>false</c>.</returns>
        bool GetCurrent(out RecorderPointModel point);
        #endregion
    }
}
