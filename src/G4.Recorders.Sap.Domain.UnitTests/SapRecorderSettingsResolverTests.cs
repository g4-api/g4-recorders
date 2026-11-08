using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapRecorderSettingsResolver))]
    [TestCategory("UnitTest")]
    public sealed class SapRecorderSettingsResolverTests
    {
        // Lists both streams, in their canonical order.
        private static readonly string[] BothStreams = ["user32", "nativeSap"];

        // Lists only the SAP action stream.
        private static readonly string[] NativeSapOnly = ["nativeSap"];

        // Lists only the user input stream.
        private static readonly string[] User32Only = ["user32"];

        // Lists two sessions, as a separated environment value yields them.
        private static readonly string[] TwoSessions = ["/app/con[0]/ses[0]", "/app/con[0]/ses[1]"];

        [TestMethod(DisplayName = "Verify that a text value comes from the request, then the environment, then settings, then the default")]
        #region *** Data Set ***
        [DataRow("C:\\request.exe", "C:\\environment.exe", "C:\\settings.exe", "C:\\request.exe", "request")]
        [DataRow(null, "C:\\environment.exe", "C:\\settings.exe", "C:\\environment.exe", "environment")]
        [DataRow(null, null, "C:\\settings.exe", "C:\\settings.exe", "settings")]
        [DataRow(null, null, null, "", "default")]
        #endregion
        public void ResolveTextPrecedenceTest(string requestValue, string environmentValue, string settingsValue, string expected, string expectedSource)
        {
            // Arrange: the SAP Logon path in each source.
            var resolver = NewResolver(
                environment: new() { ["G4_SAP_LOGON_PATH"] = environmentValue },
                settings: new() { ["G4:Sap:Logon:Path"] = settingsValue });
            var request = new SapRecorderParametersModel { SapLogon = new SapLogonParametersModel { Path = requestValue } };

            // Act: resolve.
            var options = resolver.Resolve(request);

            // Assert: the highest source that has a value wins, and is reported.
            Assert.AreEqual(expected, options.SapLogon.Path);
            Assert.AreEqual(expectedSource, options.Sources["sapLogon.path"]);
        }

        [TestMethod(DisplayName = "Verify that a Boolean value comes from the request, then the environment, then settings, then true")]
        #region *** Data Set ***
        [DataRow(false, "true", "true", false, "request")]
        [DataRow(null, "false", "true", false, "environment")]
        [DataRow(null, "not-a-boolean", "false", false, "settings")]
        [DataRow(null, null, null, true, "default")]
        #endregion
        public void ResolveBooleanPrecedenceTest(bool? requestValue, string environmentValue, string settingsValue, bool expected, string expectedSource)
        {
            // Arrange: the record-mode permission in each source; unreadable values count as absent.
            var resolver = NewResolver(
                environment: new() { ["G4_SAP_RECORDER_SET_RECORD"] = environmentValue },
                settings: new() { ["G4:Sap:Recorder:SetRecordMode"] = settingsValue });

            // Act: resolve.
            var options = resolver.Resolve(new SapRecorderParametersModel { SetRecordMode = requestValue });

            // Assert: the value and its source.
            Assert.AreEqual(expected, options.SetRecordMode);
            Assert.AreEqual(expectedSource, options.Sources["setRecordMode"]);
        }

        [TestMethod(DisplayName = "Verify that list values split on semicolons in the environment and read arrays from settings")]
        public void ResolveListFormatsTest()
        {
            // Arrange: sessions in the environment, streams as a settings array.
            var resolver = NewResolver(
                environment: new() { ["G4_SAP_SCOPE_SESSIONS"] = " /app/con[0]/ses[0] ; /app/con[0]/ses[1];" },
                settings: new() { ["G4:Sap:Recorder:Streams:0"] = "NATIVESAP" });

            // Act: resolve without a request.
            var options = resolver.Resolve(request: null);

            // Assert: trimmed items, and stream names in their canonical spelling.
            Assert.AreSequenceEqual(TwoSessions, options.Scope.Sessions);
            Assert.AreSequenceEqual(NativeSapOnly, options.Streams);
            Assert.AreEqual("settings", options.Sources["streams"]);
        }

        [TestMethod(DisplayName = "Verify that streams default to both, and a request value replaces lower sources")]
        public void ResolveStreamsTest()
        {
            // Arrange: settings that would limit the streams.
            var resolver = NewResolver(environment: [], settings: new() { ["G4:Sap:Recorder:Streams"] = "nativeSap" });

            // Act: resolve with and without sources.
            var fromRequest = resolver.Resolve(new SapRecorderParametersModel { Streams = ["user32"] });
            var fromDefault = NewResolver(environment: [], settings: []).Resolve(request: null);

            // Assert: the request wins, and with nothing configured both streams run.
            Assert.AreSequenceEqual(User32Only, fromRequest.Streams);
            Assert.AreSequenceEqual(BothStreams, fromDefault.Streams);
        }

        [TestMethod(DisplayName = "Verify that unknown stream names and an empty stream list are rejected")]
        #region *** Data Set ***
        [DataRow("keyboard")]
        [DataRow("")]
        #endregion
        public void ResolveInvalidStreamsTest(string stream)
        {
            // Arrange: a request with an unknown or no stream.
            var resolver = NewResolver(environment: [], settings: []);
            var request = new SapRecorderParametersModel { Streams = stream.Length == 0 ? [] : [stream] };

            // Act and assert: the request is rejected.
            Assert.ThrowsExactly<ArgumentException>(() => resolver.Resolve(request));
        }

        [TestMethod(DisplayName = "Verify that equivalent options ignore sources and stream name case")]
        public void TestEquivalentTest()
        {
            // Arrange: the same values from different sources.
            var fromRequest = NewResolver(environment: [], settings: []).Resolve(new SapRecorderParametersModel { SetRecordMode = true });
            var fromDefault = NewResolver(environment: [], settings: []).Resolve(request: null);
            var different = NewResolver(environment: [], settings: []).Resolve(new SapRecorderParametersModel { Streams = ["user32"] });

            // Act and assert: equal values compare equal; a different stream list does not.
            Assert.IsTrue(fromRequest.TestEquivalent(fromDefault));
            Assert.IsFalse(fromRequest.TestEquivalent(different));
        }

        // Creates a resolver over an in-memory environment and settings.
        private static SapRecorderSettingsResolver NewResolver(Dictionary<string, string> environment, Dictionary<string, string> settings)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

            return new SapRecorderSettingsResolver(configuration, readEnvironment: name => environment.GetValueOrDefault(name));
        }
    }
}
