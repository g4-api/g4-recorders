using Microsoft.Extensions.Configuration;

using System;
using System.Collections.Generic;
using System.Linq;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Default <see cref="ISapRecorderSettingsResolver"/>: request, then environment variable, then <c>appsettings.json</c>,
    /// then the built-in default, decided separately for every value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A request value counts when it is not <c>null</c>; an environment or settings value counts when it is not empty.
    /// List values are separated by <c>;</c> in environment variables and in settings strings; settings may also use
    /// JSON arrays.
    /// </para>
    /// <list type="table">
    ///     <listheader><term>Value</term><description>Environment variable / settings key / default</description></listheader>
    ///     <item><term><c>streams</c></term><description><c>G4_SAP_RECORDER_STREAMS</c> / <c>G4:Sap:Recorder:Streams</c> / both</description></item>
    ///     <item><term><c>setRecordMode</c></term><description><c>G4_SAP_RECORDER_SET_RECORD</c> / <c>G4:Sap:Recorder:SetRecordMode</c> / <c>true</c></description></item>
    ///     <item><term><c>sapLogon.path</c></term><description><c>G4_SAP_LOGON_PATH</c> / <c>G4:Sap:Logon:Path</c> / none</description></item>
    ///     <item><term><c>sapLogon.arguments</c></term><description><c>G4_SAP_LOGON_ARGUMENTS</c> / <c>G4:Sap:Logon:Arguments</c> / none</description></item>
    ///     <item><term><c>sapLogon.connection</c></term><description><c>G4_SAP_LOGON_CONNECTION</c> / <c>G4:Sap:Logon:Connection</c> / none</description></item>
    ///     <item><term><c>scope.systemName</c></term><description><c>G4_SAP_SCOPE_SYSTEM</c> / <c>G4:Sap:Scope:SystemName</c> / any</description></item>
    ///     <item><term><c>scope.client</c></term><description><c>G4_SAP_SCOPE_CLIENT</c> / <c>G4:Sap:Scope:Client</c> / any</description></item>
    ///     <item><term><c>scope.sessions</c></term><description><c>G4_SAP_SCOPE_SESSIONS</c> / <c>G4:Sap:Scope:Sessions</c> / any</description></item>
    /// </list>
    /// </remarks>
    public sealed class SapRecorderSettingsResolver : ISapRecorderSettingsResolver
    {
        #region *** Constants    ***
        // Separates list items in environment variables and settings strings.
        private const char ListSeparator = ';';

        // Lists the stream names a request may use.
        private static readonly string[] KnownStreams = [SapRecordingStreams.User32, SapRecordingStreams.NativeSap];

        // Names where the SAP Logon arguments is read from.
        private static readonly SettingName LogonArgumentsSetting = new("G4_SAP_LOGON_ARGUMENTS", "G4:Sap:Logon:Arguments", "sapLogon.arguments");

        // Names where the SAP Logon entry to open is read from.
        private static readonly SettingName LogonConnectionSetting = new("G4_SAP_LOGON_CONNECTION", "G4:Sap:Logon:Connection", "sapLogon.connection");

        // Names where the SAP Logon executable is read from.
        private static readonly SettingName LogonPathSetting = new("G4_SAP_LOGON_PATH", "G4:Sap:Logon:Path", "sapLogon.path");

        // Names where the SAP client in scope is read from.
        private static readonly SettingName ScopeClientSetting = new("G4_SAP_SCOPE_CLIENT", "G4:Sap:Scope:Client", "scope.client");

        // Names where the sessions in scope is read from.
        private static readonly SettingName ScopeSessionsSetting = new("G4_SAP_SCOPE_SESSIONS", "G4:Sap:Scope:Sessions", "scope.sessions");

        // Names where the SAP system in scope is read from.
        private static readonly SettingName ScopeSystemSetting = new("G4_SAP_SCOPE_SYSTEM", "G4:Sap:Scope:SystemName", "scope.systemName");

        // Names where the permission to set recording mode is read from.
        private static readonly SettingName SetRecordModeSetting = new("G4_SAP_RECORDER_SET_RECORD", "G4:Sap:Recorder:SetRecordMode", "setRecordMode");

        // Names where the streams to send is read from.
        private static readonly SettingName StreamsSetting = new("G4_SAP_RECORDER_STREAMS", "G4:Sap:Recorder:Streams", "streams");
        #endregion

        #region *** Fields       ***
        // Reads appsettings.json and the other configuration sources of the host.
        private readonly IConfiguration _configuration;

        // Reads one environment variable; replaceable so tests never touch the process environment.
        private readonly Func<string, string> _readEnvironment;
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="SapRecorderSettingsResolver"/> class over the process environment.
        /// </summary>
        /// <param name="configuration">The host configuration, which holds <c>appsettings.json</c>.</param>
        public SapRecorderSettingsResolver(IConfiguration configuration)
            : this(configuration, readEnvironment: Environment.GetEnvironmentVariable)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SapRecorderSettingsResolver"/> class with an explicit environment.
        /// </summary>
        /// <param name="configuration">The host configuration, which holds <c>appsettings.json</c>.</param>
        /// <param name="readEnvironment">Reads one environment variable by name.</param>
        internal SapRecorderSettingsResolver(IConfiguration configuration, Func<string, string> readEnvironment)
        {
            ArgumentNullException.ThrowIfNull(argument: configuration);
            ArgumentNullException.ThrowIfNull(argument: readEnvironment);

            _configuration = configuration;
            _readEnvironment = readEnvironment;
        }
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public SapRecorderOptionsModel Resolve(SapRecorderParametersModel request)
        {
            // Resolve every value on its own, recording where it came from.
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            var options = new SapRecorderOptionsModel
            {
                SapLogon = new SapLogonParametersModel
                {
                    Arguments = ResolveList(request?.SapLogon?.Arguments, LogonArgumentsSetting, sources),
                    Connection = ResolveText(request?.SapLogon?.Connection, LogonConnectionSetting, sources),
                    Path = ResolveText(request?.SapLogon?.Path, LogonPathSetting, sources)
                },
                Scope = new SapScopeParametersModel
                {
                    Client = ResolveText(request?.Scope?.Client, ScopeClientSetting, sources),
                    Sessions = ResolveList(request?.Scope?.Sessions, ScopeSessionsSetting, sources),
                    SystemName = ResolveText(request?.Scope?.SystemName, ScopeSystemSetting, sources)
                },
                SetRecordMode = ResolveBoolean(request?.SetRecordMode, SetRecordModeSetting, sources),
                Sources = sources,
                Streams = ResolveList(request?.Streams, StreamsSetting, sources)
            };

            // Streams default to both, and must name known streams only.
            if (options.Streams.Length == 0 && sources[StreamsSetting.Path] == SapRecorderOptionsModel.DefaultSource)
            {
                options.Streams = KnownStreams;
            }

            options.Streams = NormalizeStreams(options.Streams);

            return options;
        }

        // Splits a list value, trimming items and dropping empty ones.
        private static string[] ConvertToList(string value)
        {
            return value
                .Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        // Checks stream names against the known ones and returns them in their canonical spelling.
        private static string[] NormalizeStreams(string[] streams)
        {
            // Require at least one stream; a recorder that sends nothing is a configuration mistake.
            if (streams.Length == 0)
            {
                throw new ArgumentException("At least one stream (user32 or nativeSap) must be enabled.", nameof(streams));
            }

            // Map each name to its canonical spelling, rejecting unknown names.
            var normalized = new List<string>();
            foreach (var stream in streams)
            {
                var known = Array.Find(KnownStreams, i => string.Equals(i, stream, StringComparison.OrdinalIgnoreCase));
                if (known is null)
                {
                    var message = $"Unknown stream '{stream}'. " +
                        $"Use {SapRecordingStreams.User32} or {SapRecordingStreams.NativeSap}.";
                    throw new ArgumentException(message, nameof(streams));
                }

                if (!normalized.Contains(known))
                {
                    normalized.Add(known);
                }
            }

            return [.. normalized];
        }

        // Resolves a Boolean value; an unreadable environment or settings value is skipped like an empty one.
        private bool ResolveBoolean(bool? requestValue, SettingName setting, Dictionary<string, string> sources)
        {
            // Take the request value first.
            if (requestValue.HasValue)
            {
                sources[setting.Path] = SapRecorderOptionsModel.RequestSource;
                return requestValue.Value;
            }

            // Then the environment, then settings, then the default.
            if (bool.TryParse(_readEnvironment(setting.Variable), out var environmentValue))
            {
                sources[setting.Path] = SapRecorderOptionsModel.EnvironmentSource;
                return environmentValue;
            }

            if (bool.TryParse(_configuration[setting.Key], out var settingsValue))
            {
                sources[setting.Path] = SapRecorderOptionsModel.SettingsSource;
                return settingsValue;
            }

            sources[setting.Path] = SapRecorderOptionsModel.DefaultSource;

            return true;
        }

        // Resolves a list value; settings may hold a JSON array or a separated string.
        private string[] ResolveList(string[] requestValue, SettingName setting, Dictionary<string, string> sources)
        {
            // Take the request value first, even when empty, because an empty list is a deliberate choice.
            if (requestValue is not null)
            {
                sources[setting.Path] = SapRecorderOptionsModel.RequestSource;
                return [.. requestValue.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim())];
            }

            // Then the environment.
            var environmentValue = _readEnvironment(setting.Variable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                sources[setting.Path] = SapRecorderOptionsModel.EnvironmentSource;
                return ConvertToList(environmentValue);
            }

            // Then settings: an array section, or one separated string.
            var section = _configuration.GetSection(setting.Key);
            var settingsArray = section.GetChildren().Select(i => i.Value).Where(i => !string.IsNullOrWhiteSpace(i)).ToArray();
            var settingsValue = settingsArray.Length > 0 ? settingsArray : ConvertToList(section.Value ?? string.Empty);
            if (settingsValue.Length > 0)
            {
                sources[setting.Path] = SapRecorderOptionsModel.SettingsSource;
                return settingsValue;
            }

            sources[setting.Path] = SapRecorderOptionsModel.DefaultSource;

            return [];
        }

        // Resolves a text value; empty request text counts as sent, so a client can clear a configured value.
        private string ResolveText(string requestValue, SettingName setting, Dictionary<string, string> sources)
        {
            // Take the request value first.
            if (requestValue is not null)
            {
                sources[setting.Path] = SapRecorderOptionsModel.RequestSource;
                return requestValue.Trim();
            }

            // Then the environment, then settings, then no value.
            var environmentValue = _readEnvironment(setting.Variable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                sources[setting.Path] = SapRecorderOptionsModel.EnvironmentSource;
                return environmentValue.Trim();
            }

            var settingsValue = _configuration[setting.Key];
            if (!string.IsNullOrWhiteSpace(settingsValue))
            {
                sources[setting.Path] = SapRecorderOptionsModel.SettingsSource;
                return settingsValue.Trim();
            }

            sources[setting.Path] = SapRecorderOptionsModel.DefaultSource;

            return string.Empty;
        }
        #endregion

        #region *** Nested Types ***
        /// <summary>
        /// Names one value's environment variable, settings key, and JSON path.
        /// </summary>
        /// <param name="Variable">The environment variable name.</param>
        /// <param name="Key">The configuration key in appsettings.json.</param>
        /// <param name="Path">The JSON path reported in the sources, such as <c>sapLogon.path</c>.</param>
        private sealed record SettingName(string Variable, string Key, string Path);
        #endregion
    }
}
