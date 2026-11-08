using System;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Holds the recorder values that apply to the current recording.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Until a client calls <c>StartRecorder</c>, the values come from the environment and <c>appsettings.json</c>, so a
    /// client that only connects still records. The first <c>StartRecorder</c> of a recording replaces them; a later call
    /// with different values is refused, so one client cannot change what another one is recording. When the last client
    /// disconnects, <see cref="Reset"/> returns to the environment and settings values, read again.
    /// </para>
    /// <para>
    /// Register it as a singleton; capture threads read <see cref="Current"/> without locking.
    /// </para>
    /// </remarks>
    /// <param name="resolver">The resolver for the environment and settings values.</param>
    public sealed class SapRecorderOptionsStore(ISapRecorderSettingsResolver resolver)
    {
        #region *** Fields       ***
        // Resolves the environment and settings values when no request applies.
        private readonly ISapRecorderSettingsResolver _resolver = resolver;

        // Serializes Apply and Reset, so a conflict check and the replacement it guards happen together.
        private readonly object _syncRoot = new();

        // Holds the values in force; replaced as a whole, so readers always see one consistent set.
        private volatile SapRecorderOptionsModel _current;

        // Records whether the values in force came from a StartRecorder request.
        private bool _isRequested;
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the values in force for the current recording.
        /// </summary>
        public SapRecorderOptionsModel Current => _current ?? ResolveDefaults();
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Applies a request's resolved values to the current recording.
        /// </summary>
        /// <param name="options">The resolved values.</param>
        /// <returns>
        /// <c>true</c> when the values now apply; <c>false</c> when another request set different values for this
        /// recording, which then stay in force.
        /// </returns>
        public bool Apply(SapRecorderOptionsModel options)
        {
            ArgumentNullException.ThrowIfNull(argument: options);

            lock (_syncRoot)
            {
                // Refuse a second, different request; an equivalent one is accepted unchanged.
                if (_isRequested && _current is not null && !_current.TestEquivalent(options))
                {
                    return false;
                }

                _current = options;
                _isRequested = true;

                return true;
            }
        }

        /// <summary>
        /// Ends the current recording's values; the next reader gets the environment and settings values, read again.
        /// </summary>
        public void Reset()
        {
            lock (_syncRoot)
            {
                _current = null;
                _isRequested = false;
            }
        }

        // Resolves and keeps the environment and settings values, unless a request applied meanwhile.
        private SapRecorderOptionsModel ResolveDefaults()
        {
            lock (_syncRoot)
            {
                _current ??= _resolver.Resolve(request: null);

                return _current;
            }
        }
        #endregion
    }
}
