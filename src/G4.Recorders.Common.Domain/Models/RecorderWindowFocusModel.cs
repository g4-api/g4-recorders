namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Represents the outcome of a best-effort attempt to bring a target window to the foreground before a
    /// screen-dependent operation runs.
    /// </summary>
    /// <remarks>
    /// Never represents a fatal condition by itself. A caller-supplied window identifier can legitimately match
    /// zero or more than one running process; both are reported as <see cref="Found"/> equal to <c>false</c>
    /// rather than guessing which process the caller meant.
    /// </remarks>
    public class RecorderWindowFocusModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets a value indicating whether exactly one running process matched the supplied identifier.
        /// </summary>
        /// <remarks>
        /// False both when no process matched and when more than one process matched ambiguously - this type does
        /// not distinguish the two, matching the "don't guess" behavior of the focus attempt itself.
        /// </remarks>
        public bool Found { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the matched window was confirmed as the foreground window after
        /// the focus attempt.
        /// </summary>
        /// <remarks>
        /// Verified independently through the current foreground window rather than trusting the focus API's own
        /// return value, because Windows can silently deny a foreground-focus request depending on which process
        /// currently owns it.
        /// </remarks>
        public bool Focused { get; set; }

        /// <summary>
        /// Gets or sets the process identifier of the matched window, or <c>null</c> when no window matched.
        /// </summary>
        public int? ProcessId { get; set; }

        /// <summary>
        /// Gets or sets the process name of the matched window, or <c>null</c> when no window matched.
        /// </summary>
        public string ProcessName { get; set; }

        /// <summary>
        /// Gets or sets the title of the matched window, or <c>null</c> when no window matched.
        /// </summary>
        public string WindowTitle { get; set; }
        #endregion
    }
}
