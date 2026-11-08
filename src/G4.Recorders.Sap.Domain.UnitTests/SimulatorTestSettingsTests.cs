using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

using G4.Recorders.Sap.Domain.UnitTests.Support;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SimulatorTestSettings))]
    [TestCategory("UnitTest")]
    public sealed class SimulatorTestSettingsTests
    {
        [TestMethod(DisplayName = "Verify that simulator settings come from run parameters, then the environment, then appsettings.json")]
        #region *** Data Set ***
        [DataRow("run", "environment", "settings", "run")]
        [DataRow(null, "environment", "settings", "environment")]
        [DataRow("", "", "settings", "settings")]
        [DataRow(null, null, null, null)]
        #endregion
        public void ResolvePrecedenceTest(string runValue, string environmentValue, string settingsValue, string expected)
        {
            // Arrange: the same key in every source.
            var runParameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["G4_SAP_SIMULATOR_PATH"] = runValue };
            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["Environment:G4_SAP_SIMULATOR_PATH"] = settingsValue })
                .Build();

            // Act: resolve.
            var value = SimulatorTestSettings.Resolve("G4_SAP_SIMULATOR_PATH", runParameters, _ => environmentValue, settings);

            // Assert: the first non-empty source wins.
            Assert.AreEqual(expected, value);
        }
    }
}
