using System.Collections.Generic;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Holds the latest per-session recording status published by the SAP action capture.
    /// </summary>
    /// <remarks>
    /// Register it as a singleton. The capture service replaces the whole status after each synchronization, and
    /// readers receive a copy, so neither side sees a partly updated list.
    /// </remarks>
    public sealed class SapRecordingStatusStore
    {
        #region *** Fields       ***
        // Guards the status list between the scripting thread and request threads.
        private readonly object _syncRoot = new();

        // Holds the latest status, one entry per attached or refused session.
        private List<SapSessionStatusModel> _sessions = [];
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Gets a copy of the latest per-session status.
        /// </summary>
        /// <returns>The sessions, in SAP order; empty while no client is connected or SAP GUI is unavailable.</returns>
        public List<SapSessionStatusModel> GetSessions()
        {
            lock (_syncRoot)
            {
                return [.. _sessions];
            }
        }

        /// <summary>
        /// Replaces the per-session status.
        /// </summary>
        /// <param name="sessions">The new status list.</param>
        internal void SetSessions(List<SapSessionStatusModel> sessions)
        {
            lock (_syncRoot)
            {
                _sessions = sessions ?? [];
            }
        }
        #endregion
    }
}
