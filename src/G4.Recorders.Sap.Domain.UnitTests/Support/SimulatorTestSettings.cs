using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.IO;

namespace G4.Recorders.Sap.Domain.UnitTests.Support
{
    /// <summary>
    /// Resolves the SAP GUI simulator test settings: <c>.runsettings</c> test run parameters, then environment variables,
    /// then the <c>Environment</c> section of the test project's <c>appsettings.json</c>.
    /// </summary>
    /// <remarks>
    /// The keys are the same in every source: <c>G4_SAP_SIMULATOR</c>, <c>G4_SAP_SIMULATOR_PATH</c>, and
    /// <c>G4_SAP_SIMULATOR_INPUT</c>. A source counts when its value is not empty. The run parameters are read from the
    /// test context captured by <see cref="Initialize"/> at assembly start.
    /// </remarks>
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    public static class SimulatorTestSettings
    {
        #region *** Fields       ***
        // Holds the test run parameters of the current run; empty until the assembly initializes.
        private static IDictionary<string, object> s_runParameters = new Dictionary<string, object>(StringComparer.Ordinal);

        // Holds the test project's appsettings.json, read once.
        private static readonly IConfiguration Settings = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Captures the test run parameters at assembly start.
        /// </summary>
        /// <param name="context">The assembly's test context, which carries the <c>.runsettings</c> parameters.</param>
        [AssemblyInitialize]
        public static void Initialize(TestContext context)
        {
            s_runParameters = context?.Properties ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Gets a setting from the run parameters, then the environment, then appsettings.json.
        /// </summary>
        /// <param name="key">The key, such as <c>G4_SAP_SIMULATOR</c>.</param>
        /// <returns>The first value found, or <c>null</c>.</returns>
        internal static string Get(string key)
        {
            return Resolve(key, s_runParameters, Environment.GetEnvironmentVariable, Settings);
        }

        /// <summary>
        /// Resolves a setting from explicit sources, in precedence order.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="runParameters">The test run parameters.</param>
        /// <param name="readEnvironment">Reads one environment variable.</param>
        /// <param name="settings">The configuration that holds the <c>Environment</c> section.</param>
        /// <returns>The first non-empty value, or <c>null</c>.</returns>
        internal static string Resolve(
            string key,
            IDictionary<string, object> runParameters,
            Func<string, string> readEnvironment,
            IConfiguration settings)
        {
            // Take the run parameter first, then the environment, then the settings file.
            var runValue = runParameters.TryGetValue(key, out var value) ? value as string : null;
            if (!string.IsNullOrWhiteSpace(runValue))
            {
                return runValue;
            }

            var environmentValue = readEnvironment(key);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }

            var settingsValue = settings[$"Environment:{key}"];

            return string.IsNullOrWhiteSpace(settingsValue) ? null : settingsValue;
        }

        /// <summary>
        /// Tests whether a switch setting is <c>true</c>.
        /// </summary>
        /// <param name="key">The key, such as <c>G4_SAP_SIMULATOR</c>.</param>
        /// <returns><c>true</c> only for the value <c>true</c>, ignoring case.</returns>
        internal static bool TestEnabled(string key)
        {
            return string.Equals(Get(key), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the settings file path, for messages that tell the user where to switch the tests on.
        /// </summary>
        /// <returns>The path of the appsettings.json next to the tests.</returns>
        internal static string GetSettingsPath()
        {
            return Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        }
        #endregion
    }
}
