using System;
using System.Collections.Generic;
using System.Linq;

namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Holds the resolved recorder values, with where each one came from.
    /// </summary>
    /// <remarks>
    /// Every member is set: a value missing from the request, the environment, and <c>appsettings.json</c> takes its
    /// default. <see cref="Sources"/> names the source of each value, keyed by its JSON path, such as
    /// <c>sapLogon.path</c>, so a client can see why a value applied.
    /// </remarks>
    public class SapRecorderOptionsModel : SapRecorderParametersModel
    {
        #region *** Constants    ***
        /// <summary>
        /// Names a value that took its built-in default.
        /// </summary>
        public const string DefaultSource = "default";

        /// <summary>
        /// Names a value read from an environment variable.
        /// </summary>
        public const string EnvironmentSource = "environment";

        /// <summary>
        /// Names a value sent with the request.
        /// </summary>
        public const string RequestSource = "request";

        /// <summary>
        /// Names a value read from <c>appsettings.json</c>.
        /// </summary>
        public const string SettingsSource = "settings";
        #endregion

        #region *** Properties   ***
        /// <summary>
        /// Gets the source of each value, keyed by its JSON path, such as <c>streams</c> or <c>scope.client</c>.
        /// </summary>
        public Dictionary<string, string> Sources { get; init; } = new(StringComparer.Ordinal);
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Tests whether another set of options records the same way; sources are ignored.
        /// </summary>
        /// <param name="other">The other options.</param>
        /// <returns><c>true</c> when every value is equal; otherwise, <c>false</c>.</returns>
        public bool TestEquivalent(SapRecorderOptionsModel other)
        {
            // Compare every value; list values compare in order and without case for stream names.
            return other is not null
                && SetRecordMode == other.SetRecordMode
                && Streams.SequenceEqual(other.Streams, StringComparer.OrdinalIgnoreCase)
                && SapLogon.Path == other.SapLogon.Path
                && SapLogon.Connection == other.SapLogon.Connection
                && SapLogon.Arguments.SequenceEqual(other.SapLogon.Arguments, StringComparer.Ordinal)
                && Scope.SystemName == other.Scope.SystemName
                && Scope.Client == other.Scope.Client
                && Scope.Sessions.SequenceEqual(other.Scope.Sessions, StringComparer.Ordinal);
        }

        /// <summary>
        /// Tests whether a stream is enabled.
        /// </summary>
        /// <param name="stream">The stream name, <c>user32</c> or <c>nativeSap</c>.</param>
        /// <returns><c>true</c> when the stream is sent; otherwise, <c>false</c>.</returns>
        public bool TestStream(string stream)
        {
            return Streams.Contains(stream, StringComparer.OrdinalIgnoreCase);
        }
        #endregion
    }
}
