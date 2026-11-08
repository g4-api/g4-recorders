using System.Threading;

namespace G4.Recorders.Sap.Domain.Hubs
{
    /// <summary>
    /// Tracks active SAP recorder hub connections and identifies each continuous
    /// recording session with a monotonically increasing generation.
    /// </summary>
    /// <remarks>
    /// The event-capture service uses this state to skip SAP lookups when no
    /// recorder is listening and to reject targets retained by an older session.
    /// </remarks>
    internal sealed class RecorderConnectionState
    {
        #region *** Constants    ***
        /// <summary>
        /// The process-wide state shared by the capture services and the recorder hub.
        /// </summary>
        internal static readonly RecorderConnectionState Instance = new();
        #endregion

        #region *** Fields       ***
        // Number of hub clients currently connected; capture work runs only while it is above zero.
        private int _activeConnections;

        // Generation of the current or most recent continuous recording session, advanced by its first client.
        private long _sessionGeneration;

        // Serializes connection transitions so the count and the generation change together.
        private readonly object _syncRoot = new();
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the number of recorder clients currently connected to the SAP hub.
        /// </summary>
        internal int ActiveConnections => Volatile.Read(ref _activeConnections);

        /// <summary>
        /// Gets a value indicating whether at least one recorder client is connected.
        /// </summary>
        internal bool CaptureActive => ActiveConnections > 0;

        /// <summary>
        /// Gets the generation assigned to the current or most recent recording session.
        /// </summary>
        internal long SessionGeneration => Interlocked.Read(ref _sessionGeneration);
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Registers one recorder connection and starts a new generation when it is the first active client.
        /// </summary>
        /// <returns>The generation associated with the registered connection.</returns>
        internal long AddConnection()
        {
            lock (_syncRoot)
            {
                // Advance the generation before publishing the first active connection.
                if (_activeConnections == 0)
                {
                    Interlocked.Increment(ref _sessionGeneration);
                }

                // Publish active capture only after the current generation is visible to hook readers.
                Volatile.Write(ref _activeConnections, _activeConnections + 1);
                return SessionGeneration;
            }
        }

        /// <summary>
        /// Removes one recorder connection without allowing the active count to become negative.
        /// </summary>
        /// <returns>The number of recorder clients that remain connected.</returns>
        internal int RemoveConnection()
        {
            lock (_syncRoot)
            {
                // Ignore duplicate disconnect notifications without allowing a negative count.
                if (_activeConnections == 0)
                {
                    return 0;
                }

                // Publish the remaining active-client count as one synchronized transition.
                var remainingConnections = _activeConnections - 1;
                Volatile.Write(ref _activeConnections, remainingConnections);
                return remainingConnections;
            }
        }
        #endregion
    }
}
