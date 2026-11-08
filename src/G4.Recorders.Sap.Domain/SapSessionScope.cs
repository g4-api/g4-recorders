using System;
using System.Linq;

using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Decides whether a SAP session is in the recording scope.
    /// </summary>
    /// <remarks>
    /// Every scope member that is set must match: the session identifier must be listed, and the system name and client
    /// must equal the session's <c>Info</c> values, ignoring case. An empty scope matches every session. Runs on the
    /// scripting thread, because it reads the session.
    /// </remarks>
    internal static class SapSessionScope
    {
        #region *** Constants    ***
        /// <summary>
        /// Reports a session outside the recording scope.
        /// </summary>
        internal const string OutOfScopeReason =
            "The session is outside the recording scope (scope.systemName, scope.client, or scope.sessions).";
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Tests whether a session is in scope.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="scope">The scope; <c>null</c> matches every session.</param>
        /// <returns><c>true</c> when every set scope member matches; otherwise, <c>false</c>.</returns>
        internal static bool TestInScope(ISapGuiObject session, SapScopeParametersModel scope)
        {
            // Treat a missing or empty scope as everything, without reading the session.
            if (session is null || TestEmpty(scope))
            {
                return session is not null;
            }

            // Match the identifier list first; it needs no Info read.
            var sessions = scope.Sessions ?? [];
            var id = SapGuiNavigator.GetString(session, propertyName: "Id");
            if (sessions.Length > 0 && !sessions.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            // Then the system and client from the session information.
            var info = SapGuiNavigator.GetObject(session, propertyName: "Info");
            var systemName = info is null ? string.Empty : SapGuiNavigator.GetString(info, propertyName: "SystemName");
            var client = info is null ? string.Empty : SapGuiNavigator.GetString(info, propertyName: "Client");

            return TestValue(scope.SystemName, systemName) && TestValue(scope.Client, client);
        }

        // Tests whether a scope sets no member.
        private static bool TestEmpty(SapScopeParametersModel scope)
        {
            return scope is null
                || (string.IsNullOrEmpty(scope.SystemName)
                    && string.IsNullOrEmpty(scope.Client)
                    && (scope.Sessions?.Length ?? 0) == 0);
        }

        // Tests one scope value: an unset value matches anything; a set one must equal the actual value, ignoring case.
        private static bool TestValue(string expected, string actual)
        {
            return string.IsNullOrEmpty(expected) || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
        #endregion
    }
}
