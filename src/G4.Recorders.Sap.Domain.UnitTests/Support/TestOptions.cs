using Microsoft.Extensions.Configuration;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.UnitTests.Support
{
    /// <summary>
    /// Creates resolved recorder values for tests, without reading the process environment or any settings file.
    /// </summary>
    internal static class TestOptions
    {
        #region *** Methods      ***
        /// <summary>
        /// Resolves a request against an empty environment and empty settings.
        /// </summary>
        /// <param name="request">The request, or <c>null</c> for the defaults.</param>
        /// <returns>The resolved values.</returns>
        internal static SapRecorderOptionsModel Resolve(SapRecorderParametersModel request)
        {
            return NewResolver().Resolve(request);
        }

        /// <summary>
        /// Creates a resolver over an empty environment and empty settings.
        /// </summary>
        /// <returns>The resolver.</returns>
        internal static SapRecorderSettingsResolver NewResolver()
        {
            return new SapRecorderSettingsResolver(new ConfigurationBuilder().Build(), readEnvironment: _ => null);
        }
        #endregion
    }
}
