namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Normalizes optional coordinates and focus intent into one deterministic recorder peek mode.
    /// </summary>
    public static class RecorderPeekRequestResolver
    {
        #region *** Methods      ***
        /// <summary>
        /// Resolves coordinates before focus, defaults a missing coordinate axis to zero, and otherwise selects
        /// focused or current-pointer lookup.
        /// </summary>
        /// <param name="x">The optional horizontal coordinate.</param>
        /// <param name="y">The optional vertical coordinate.</param>
        /// <param name="focused">Whether an omitted coordinate pair selects the focused element.</param>
        /// <returns>The normalized peek request consumed by recorder-specific implementations.</returns>
        public static RecorderPeekRequestModel Resolve(int? x, int? y, bool focused)
        {
            // Give any supplied coordinate precedence so an explicit point cannot be hidden by the focus flag.
            var isCoordinateRequest = x.HasValue || y.HasValue;

            if (isCoordinateRequest)
            {
                return new RecorderPeekRequestModel
                {
                    Mode = RecorderPeekMode.Coordinates,
                    X = x ?? 0,
                    Y = y ?? 0
                };
            }

            // Select focus only when both coordinates are absent so the remaining fallback represents current position.
            if (focused)
            {
                return new RecorderPeekRequestModel { Mode = RecorderPeekMode.Focused };
            }

            return new RecorderPeekRequestModel { Mode = RecorderPeekMode.Current };
        }
        #endregion
    }
}
