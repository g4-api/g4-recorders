using G4.Recorders.Android.Domain.Adb;
using G4.Recorders.Android.Domain.Models;
using G4.Recorders.Common.Domain.Models;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain
{
    /// <summary>
    /// Default <see cref="IAndroidRecorderLauncher"/>: prepares and restores devices through adb.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton so the registry of started recordings persists across requests; only ids in that
    /// registry can be stopped, so a client can never act on a device or process this host did not prepare.
    /// <para>
    /// Start order matters: the recorder app's activity receives the hub URL before its accessibility service is
    /// enabled, so the service's first connection already targets this host.
    /// </para>
    /// </remarks>
    /// <param name="repository">Sends <c>CloseRecorder</c> to the connected recorder app on stop.</param>
    /// <param name="adbRunner">Runs every adb command.</param>
    /// <param name="server">Resolves this host's listening port for <c>adb reverse</c> and the hub URL.</param>
    /// <param name="logger">Records stop failures, which are never thrown.</param>
    public sealed partial class AndroidRecorderLauncher(
        IAndroidRecorderRepository repository,
        IAdbRunner adbRunner,
        IServer server,
        ILogger<AndroidRecorderLauncher> logger) : IAndroidRecorderLauncher
    {
        #region *** Constants    ***
        /// <summary>
        /// The package name of the recorder app.
        /// </summary>
        public const string RecorderPackage = "com.g4.recorders.android";

        /// <summary>
        /// The flattened component name of the recorder's accessibility service, as Android settings store it.
        /// </summary>
        public const string RecorderServiceComponent = RecorderPackage + "/" + RecorderPackage + ".RecorderAccessibilityService";

        // Secure setting that switches accessibility on as a whole.
        private const string AccessibilityEnabledSetting = "accessibility_enabled";

        // File name of the recorder APK the app build exports.
        private const string ApkFileName = "g4-recorder.apk";

        // Folder under the host's base directory that holds the recorder APK; the host project copies the sibling
        // G4.Recorders.Android.App.Drop folder there under the same name.
        private const string AppFolderName = "G4.Recorders.Android.App.Drop";

        // Port the host listens on when its listening address cannot be read.
        private const int DefaultPort = 9958;

        // Secure setting that lists the enabled accessibility services, separated by colons.
        private const string EnabledServicesSetting = "enabled_accessibility_services";

        // Hub route appended to the reversed loopback origin.
        private const string HubPath = "/hub/v4/g4/peek";

        // How many times the launcher reads the started application's pid while it is still starting.
        private const int PidAttempts = 10;

        // Value Android's settings tool prints for an unset setting.
        private const string UnsetSettingValue = "null";

        // How long the recorder app gets to send its last events and close before the device is restored.
        private static readonly TimeSpan CloseGracePeriod = TimeSpan.FromSeconds(5);

        // Time limit of ordinary adb commands.
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

        // Time limit of package installs, which copy and verify a whole APK.
        private static readonly TimeSpan InstallTimeout = TimeSpan.FromSeconds(180);

        // Delay between pid reads.
        private static readonly TimeSpan PidDelay = TimeSpan.FromMilliseconds(300);
        #endregion

        #region *** Fields       ***
        // Runs every adb command; replaced by a scripted fake in tests.
        private readonly IAdbRunner _adbRunner = adbRunner;

        // Serial of the device the most recent recording started on; observe captures it. Null until a start.
        private string _lastSerial;

        // Last negative id handed out for a recording that started no application.
        private int _lastSessionId;

        // Records stop failures, which are never thrown.
        private readonly ILogger<AndroidRecorderLauncher> _logger = logger;

        // Sends CloseRecorder to the connected recorder app.
        private readonly IAndroidRecorderRepository _repository = repository;

        // Resolves this host's listening port.
        private readonly IServer _server = server;

        // Started recordings by the id StartAsync returned; only these can be stopped.
        private readonly ConcurrentDictionary<int, AndroidRecorderSession> _sessions = new();
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Quotes one value for the device shell that <c>adb shell</c> runs the command in.
        /// </summary>
        /// <param name="value">The raw value.</param>
        /// <returns>The value in single quotes, with embedded single quotes escaped.</returns>
        /// <remarks>
        /// adb joins the arguments of <c>adb shell</c> into one command line for the device's shell, so every
        /// caller-supplied value is quoted, and an empty value still arrives as an argument.
        /// </remarks>
        public static string ConvertToShellArgument(string value)
        {
            // Close the quote, write an escaped quote and reopen it, the POSIX way to embed a single quote.
            var escaped = (value ?? string.Empty).Replace("'", "'\\''");

            return "'" + escaped + "'";
        }

        /// <inheritdoc />
        public async Task<RecorderScreenshotModel> GetScreenshotAsync(bool metricsOnly)
        {
            // Capture the device of the most recent recording, else the only connected device.
            var serial = _lastSerial ?? await ResolveSerialAsync(_adbRunner, requestedSerial: null).ConfigureAwait(false);

            // Read the display size; an override (set by wm size) is what apps draw at, so the last match wins.
            var sizeCommand = new AdbCommand(serial, CommandTimeout, ["shell", "wm", "size"]);
            var sizeResult = await RunCheckedAsync(_adbRunner, sizeCommand).ConfigureAwait(false);
            var sizeMatches = DisplaySizePattern().Matches(sizeResult.StandardOutput);

            if (sizeMatches.Count == 0)
            {
                var message = $"Failed to read the screen size of device '{serial}': {sizeResult.StandardOutput}";
                throw new InvalidOperationException(message);
            }

            // Describe the coordinate space; Android screens always start at the origin.
            var sizeMatch = sizeMatches[^1];
            var screenshot = new RecorderScreenshotModel
            {
                Height = int.Parse(sizeMatch.Groups["height"].Value),
                MimeType = "image/png",
                OriginX = 0,
                OriginY = 0,
                Width = int.Parse(sizeMatch.Groups["width"].Value)
            };

            // Skip the pixel capture when only the coordinate space is wanted.
            if (metricsOnly)
            {
                return screenshot;
            }

            // Capture through exec-out, which keeps the PNG bytes intact where shell translates line endings.
            var captureCommand = new AdbCommand(serial, CommandTimeout, ["exec-out", "screencap", "-p"]);
            var captureResult = await RunCheckedAsync(_adbRunner, captureCommand).ConfigureAwait(false);
            screenshot.ImageBase64 = Convert.ToBase64String(captureResult.StandardOutputBytes);

            return screenshot;
        }

        /// <summary>
        /// Adds the recorder's service to a colon-separated list of enabled accessibility services.
        /// </summary>
        /// <param name="enabledServices">The current setting value; empty or <c>null</c> when nothing is enabled.</param>
        /// <returns>The new value, which keeps every other service enabled.</returns>
        public static string NewEnabledServices(string enabledServices)
        {
            // Treat the settings tool's "null" output as an empty list.
            var isUnset = string.IsNullOrWhiteSpace(enabledServices) || enabledServices.Trim() == UnsetSettingValue;
            var services = isUnset
                ? new List<string>()
                : enabledServices.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            // Append the recorder once; another user's screen reader or automation service stays on.
            var isPresent = services.Any(service => string.Equals(service, RecorderServiceComponent, StringComparison.OrdinalIgnoreCase));

            if (!isPresent)
            {
                services.Add(RecorderServiceComponent);
            }

            return string.Join(':', services);
        }

        /// <inheritdoc />
        public async Task<int> StartAsync(DriverParametersModel driverParameters)
        {
            // Fail before touching the device when the host was published without the recorder app.
            var options = driverParameters?.Capabilities?.AlwaysMatch ?? new AlwaysMatchModel();
            var apkPath = Path.Combine(AppContext.BaseDirectory, AppFolderName, ApkFileName);

            if (!File.Exists(apkPath))
            {
                var message = $"Recorder app not found on the recorder host: {apkPath}";
                throw new FileNotFoundException(message, apkPath);
            }

            // Pick the device, then install or update the recorder app on it.
            var serial = await ResolveSerialAsync(_adbRunner, options.Udid).ConfigureAwait(false);
            await InstallRecorderAsync(_adbRunner, serial, apkPath).ConfigureAwait(false);

            // Map the device's loopback port to this host, so the app reaches the hub at localhost.
            var port = ResolveServerPort(_server);
            var portArgument = $"tcp:{port}";
            await RunCheckedAsync(_adbRunner, new AdbCommand(serial, CommandTimeout, ["reverse", portArgument, portArgument])).ConfigureAwait(false);

            // Hand the hub URL, target package and serial to the app before its service starts.
            var targetPackage = (options.AppPackage ?? string.Empty).Trim();
            var hubUrl = $"http://localhost:{port}{HubPath}";
            await StartRecorderActivityAsync(_adbRunner, new RecorderActivityOptions(serial, hubUrl, targetPackage)).ConfigureAwait(false);

            // Remember the accessibility settings so stop restores exactly what the user had.
            var previousServices = await ReadSecureSettingAsync(_adbRunner, serial, EnabledServicesSetting).ConfigureAwait(false);
            var previousEnabled = await ReadSecureSettingAsync(_adbRunner, serial, AccessibilityEnabledSetting).ConfigureAwait(false);

            // Enable the recorder's service next to any service the user already runs.
            var enabledServices = ConvertToShellArgument(NewEnabledServices(previousServices));
            string[] enableServicesArguments = ["shell", "settings", "put", "secure", EnabledServicesSetting, enabledServices];
            string[] enableAccessibilityArguments = ["shell", "settings", "put", "secure", AccessibilityEnabledSetting, "1"];
            await RunCheckedAsync(_adbRunner, new AdbCommand(serial, CommandTimeout, enableServicesArguments)).ConfigureAwait(false);
            await RunCheckedAsync(_adbRunner, new AdbCommand(serial, CommandTimeout, enableAccessibilityArguments)).ConfigureAwait(false);

            // Install and start the application under test when the capabilities name one.
            await InstallApplicationAsync(_adbRunner, serial, options.App).ConfigureAwait(false);
            var launchOptions = new ApplicationLaunchOptions(serial, targetPackage, options.AppActivity);
            var isStarted = await StartApplicationAsync(_adbRunner, launchOptions).ConfigureAwait(false);
            var processId = isStarted ? await ReadProcessIdAsync(_adbRunner, serial, targetPackage).ConfigureAwait(false) : 0;

            // Register the recording under the application's pid, or a negative id when there is none to report.
            var sessionId = processId > 0 ? processId : Interlocked.Decrement(ref _lastSessionId);
            _sessions[sessionId] = new AndroidRecorderSession
            {
                IsApplicationStarted = isStarted,
                Port = port,
                PreviousAccessibilityEnabled = previousEnabled,
                PreviousEnabledServices = previousServices,
                Serial = serial,
                TargetPackage = targetPackage
            };
            _lastSerial = serial;

            return sessionId;
        }

        /// <inheritdoc />
        public async Task<bool> StopAsync(int processId)
        {
            // Only recordings this launcher started can be stopped.
            if (!_sessions.TryRemove(processId, out var session))
            {
                return false;
            }

            // Let the app send what it still holds and switch itself off, bounded so a stuck app cannot block stop.
            await InvokeSafelyAsync(_logger, "close the recorder app", () => _repository.CloseRecordersAsync(CloseGracePeriod)).ConfigureAwait(false);

            // Stop the application under test only when this launcher started it.
            if (session.IsApplicationStarted)
            {
                var stopCommand = new AdbCommand(session.Serial, CommandTimeout, ["shell", "am", "force-stop", session.TargetPackage]);
                await InvokeSafelyAsync(_logger, "stop the application", () => RunCheckedAsync(_adbRunner, stopCommand)).ConfigureAwait(false);
            }

            // Restore the accessibility settings exactly as they were; each step runs even when another failed.
            var services = new SecureSettingValue(EnabledServicesSetting, session.PreviousEnabledServices);
            var accessibility = new SecureSettingValue(AccessibilityEnabledSetting, session.PreviousAccessibilityEnabled);
            await InvokeSafelyAsync(_logger, "restore the accessibility services", () => RestoreSecureSettingAsync(_adbRunner, session.Serial, services)).ConfigureAwait(false);
            await InvokeSafelyAsync(_logger, "restore the accessibility switch", () => RestoreSecureSettingAsync(_adbRunner, session.Serial, accessibility)).ConfigureAwait(false);

            // Remove the port mapping last, so the app still reaches the hub while it closes.
            var removeCommand = new AdbCommand(session.Serial, CommandTimeout, ["reverse", "--remove", $"tcp:{session.Port}"]);
            await InvokeSafelyAsync(_logger, "remove the port mapping", () => RunCheckedAsync(_adbRunner, removeCommand)).ConfigureAwait(false);

            return true;
        }

        // Matches one line of wm size output, for example "Physical size: 1080x2400" or "Override size: 720x1600".
        [GeneratedRegex(@"size:\s*(?<width>\d+)x(?<height>\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex DisplaySizePattern();

        // Installs the application under test when the capabilities name an APK on this host; throws when it is
        // missing or adb refuses it, so the start fails before any recording begins.
        private static async Task InstallApplicationAsync(IAdbRunner adbRunner, string serial, string applicationPath)
        {
            // Nothing to install: the application is already on the device, or the user starts it by hand.
            if (string.IsNullOrWhiteSpace(applicationPath))
            {
                return;
            }

            // Only a file on this host can be installed; Appium's remote URLs are not downloaded.
            if (!File.Exists(applicationPath))
            {
                var message = $"Application not found on the recorder host: {applicationPath}";
                throw new FileNotFoundException(message, applicationPath);
            }

            // Install with -r to keep an older build's data and -g to grant its runtime permissions.
            var result = await RunAsync(adbRunner, new AdbCommand(serial, InstallTimeout, ["install", "-r", "-g", applicationPath])).ConfigureAwait(false);
            TestInstallSucceeded(result, applicationPath);
        }

        // Installs or updates the recorder app; a build signed by another machine is uninstalled and installed again,
        // which resets the app's settings on that device. Throws when the final install fails.
        private static async Task InstallRecorderAsync(IAdbRunner adbRunner, string serial, string apkPath)
        {
            // Update in place and grant the runtime permissions; -r keeps the app's settings.
            var installCommand = new AdbCommand(serial, InstallTimeout, ["install", "-r", "-g", apkPath]);
            var result = await RunAsync(adbRunner, installCommand).ConfigureAwait(false);

            // Each build machine signs with its own debug key, which a device refuses as an update; reinstall then.
            var isSignatureMismatch = result.StandardOutput.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal)
                || result.StandardError.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal);

            if (isSignatureMismatch)
            {
                await RunAsync(adbRunner, new AdbCommand(serial, InstallTimeout, ["uninstall", RecorderPackage])).ConfigureAwait(false);
                result = await RunAsync(adbRunner, installCommand).ConfigureAwait(false);
            }

            // Fail the start with adb's own message when the device still refuses the app.
            TestInstallSucceeded(result, apkPath);
        }

        // Runs one stop step and logs its failure instead of throwing, so one failed step never skips the others.
        private static async Task InvokeSafelyAsync(ILogger logger, string stepName, Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException or IOException)
            {
                // Keep the reason on the host's log; the device may need a manual restore of this one step.
                logger.LogWarning(exception, "The Android recorder failed to {StepName} while stopping.", stepName);
            }
        }

        // Reads the application's pid while it starts; returns 0 when it never appears, which StartAsync turns into a
        // negative session id.
        private static async Task<int> ReadProcessIdAsync(IAdbRunner adbRunner, string serial, string packageName)
        {
            for (var attempt = 0; attempt < PidAttempts; attempt++)
            {
                // pidof prints one pid per process of the package; the first is the main process.
                var command = new AdbCommand(serial, CommandTimeout, ["shell", "pidof", ConvertToShellArgument(packageName)]);
                var result = await RunAsync(adbRunner, command).ConfigureAwait(false);
                var firstPid = result.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                if (int.TryParse(firstPid, out var processId) && processId > 0)
                {
                    return processId;
                }

                // Give the application time to create its process before the next read.
                await Task.Delay(PidDelay).ConfigureAwait(false);
            }

            return 0;
        }

        // Reads one secure setting; returns null when it is unset, which stop restores by deleting the setting.
        private static async Task<string> ReadSecureSettingAsync(IAdbRunner adbRunner, string serial, string name)
        {
            var command = new AdbCommand(serial, CommandTimeout, ["shell", "settings", "get", "secure", name]);
            var result = await RunCheckedAsync(adbRunner, command).ConfigureAwait(false);
            var value = result.StandardOutput;

            return value == UnsetSettingValue || value.Length == 0 ? null : value;
        }

        // Resolves the device: the requested serial when it is connected, else the only connected device. Throws an
        // InvalidOperationException naming the connected serials when the choice is missing or ambiguous.
        private static async Task<string> ResolveSerialAsync(IAdbRunner adbRunner, string requestedSerial)
        {
            // List the devices adb can use; offline and unauthorized devices are skipped.
            var result = await RunCheckedAsync(adbRunner, new AdbCommand(Serial: null, CommandTimeout, ["devices"])).ConfigureAwait(false);
            var serials = result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Skip(1)
                .Select(line => line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length >= 2 && parts[1] == "device")
                .Select(parts => parts[0])
                .ToArray();

            // An explicit serial must be one of them.
            if (!string.IsNullOrWhiteSpace(requestedSerial))
            {
                var serial = requestedSerial.Trim();

                if (serials.Contains(serial, StringComparer.Ordinal))
                {
                    return serial;
                }

                var notFoundMessage = $"Device '{serial}' is not connected. Connected devices: {ConvertToDeviceList(serials)}.";
                throw new InvalidOperationException(notFoundMessage);
            }

            // Without a serial the choice must be unambiguous.
            if (serials.Length == 1)
            {
                return serials[0];
            }

            // Name what is connected, so the operator knows whether to connect a device or set appium:udid.
            var message = serials.Length == 0
                ? "No Android device is connected. Connect a device or start an emulator, and check 'adb devices'."
                : $"More than one Android device is connected ({ConvertToDeviceList(serials)}); set appium:udid to choose one.";
            throw new InvalidOperationException(message);

            // Lists serials for a failure message; owns no state and never throws.
            static string ConvertToDeviceList(string[] serials) => serials.Length == 0 ? "none" : string.Join(", ", serials);
        }

        // Resolves the port this host listens on, normalizing wildcard hosts; falls back to the default port when the
        // server reports no usable address.
        private static int ResolveServerPort(IServer server)
        {
            // Prefer an http address, the one the device reaches through the reversed port.
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var address = addresses?.FirstOrDefault(i => i.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? addresses?.FirstOrDefault();

            if (string.IsNullOrEmpty(address))
            {
                return DefaultPort;
            }

            // Replace wildcard hosts, which are not valid URI hosts, before parsing.
            var normalized = address
                .Replace("://+", "://localhost")
                .Replace("://*", "://localhost")
                .Replace("://[::]", "://localhost")
                .Replace("://0.0.0.0", "://localhost");

            return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri.Port : DefaultPort;
        }

        // Writes a secure setting back to its value before recording, deleting it when it was unset; throws when adb
        // fails, which the stop step logs.
        private static Task<AdbResult> RestoreSecureSettingAsync(IAdbRunner adbRunner, string serial, SecureSettingValue setting)
        {
            // An unset setting is deleted so the device returns to its own default.
            if (setting.Value is null)
            {
                return RunCheckedAsync(adbRunner, new AdbCommand(serial, CommandTimeout, ["shell", "settings", "delete", "secure", setting.Name]));
            }

            // A set value is written back quoted, because it can hold several colon-separated components.
            string[] arguments = ["shell", "settings", "put", "secure", setting.Name, ConvertToShellArgument(setting.Value)];

            return RunCheckedAsync(adbRunner, new AdbCommand(serial, CommandTimeout, arguments));
        }

        // Runs adb against one device; a null serial runs a host command such as "devices".
        private static Task<AdbResult> RunAsync(IAdbRunner adbRunner, AdbCommand command)
        {
            string[] argumentList = command.Serial is null ? command.Arguments : ["-s", command.Serial, .. command.Arguments];

            return adbRunner.RunAsync(argumentList, command.Timeout);
        }

        // Runs adb and throws an InvalidOperationException on a non-zero exit code, naming the command and adb's own
        // message.
        private static async Task<AdbResult> RunCheckedAsync(IAdbRunner adbRunner, AdbCommand command)
        {
            var result = await RunAsync(adbRunner, command).ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                // Prefer standard error, where adb writes its reason; some device tools write it to standard output.
                var detail = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
                var message = $"adb {string.Join(' ', command.Arguments)} failed with exit code {result.ExitCode}: {detail}";
                throw new InvalidOperationException(message);
            }

            return result;
        }

        // Starts the application under test; returns false when no package is named, and throws when the device
        // reports that the package or activity does not exist.
        private static async Task<bool> StartApplicationAsync(IAdbRunner adbRunner, ApplicationLaunchOptions options)
        {
            if (options.PackageName.Length == 0)
            {
                return false;
            }

            // Start the named activity, or the package's launcher activity through monkey when none is named.
            var hasActivity = !string.IsNullOrWhiteSpace(options.Activity);
            var package = ConvertToShellArgument(options.PackageName);
            string[] arguments = hasActivity
                ? ["shell", "am", "start", "-n", ConvertToShellArgument($"{options.PackageName}/{options.Activity.Trim()}")]
                : ["shell", "monkey", "-p", package, "-c", "android.intent.category.LAUNCHER", "1"];
            var result = await RunCheckedAsync(adbRunner, new AdbCommand(options.Serial, CommandTimeout, arguments)).ConfigureAwait(false);

            // am and monkey exit with 0 even when the component does not exist; their output says so.
            var isFailed = result.StandardOutput.Contains("Error", StringComparison.Ordinal)
                || result.StandardOutput.Contains("No activities found", StringComparison.Ordinal);

            if (isFailed)
            {
                var message = $"Failed to start '{options.PackageName}' on device '{options.Serial}': {result.StandardOutput}";
                throw new InvalidOperationException(message);
            }

            return true;
        }

        // Starts the recorder app's activity with the values its service reads; throws when the device cannot start it.
        private static async Task StartRecorderActivityAsync(IAdbRunner adbRunner, RecorderActivityOptions options)
        {
            // Pass every value as a quoted string extra; the activity stores them in the app's preferences.
            var component = $"{RecorderPackage}/.MainActivity";
            string[] arguments =
            [
                "shell", "am", "start", "-n", component,
                "--es", "hubUrl", ConvertToShellArgument(options.HubUrl),
                "--es", "targetPackage", ConvertToShellArgument(options.TargetPackage),
                "--es", "deviceSerial", ConvertToShellArgument(options.Serial)
            ];
            var result = await RunCheckedAsync(adbRunner, new AdbCommand(options.Serial, CommandTimeout, arguments)).ConfigureAwait(false);

            // am exits with 0 even when the activity is missing; its output says so.
            if (result.StandardOutput.Contains("Error", StringComparison.Ordinal))
            {
                var message = $"Failed to start the recorder app on device '{options.Serial}': {result.StandardOutput}";
                throw new InvalidOperationException(message);
            }
        }

        // Throws when adb install did not report success; adb exits with 0 on some install failures.
        private static void TestInstallSucceeded(AdbResult result, string apkPath)
        {
            var isSuccess = result.ExitCode == 0 && result.StandardOutput.Contains("Success", StringComparison.Ordinal);

            if (!isSuccess)
            {
                // Prefer standard error, then the install output, which carries the Failure [CODE] line.
                var detail = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
                var message = $"Installing '{apkPath}' failed: {detail}";
                throw new InvalidOperationException(message);
            }
        }
        #endregion

        #region *** Nested Types ***
        // One adb invocation: the device (null for a host command), the time limit and the argument tokens.
        private sealed record AdbCommand(string Serial, TimeSpan Timeout, string[] Arguments);

        // One recording this launcher started, with everything stop needs to undo it.
        private sealed class AndroidRecorderSession
        {
            // Whether start launched the application under test, which stop then force-stops.
            internal bool IsApplicationStarted { get; init; }

            // The host port mapped with adb reverse.
            internal int Port { get; init; }

            // The accessibility switch before recording; null when it was unset.
            internal string PreviousAccessibilityEnabled { get; init; }

            // The enabled services before recording; null when none were enabled.
            internal string PreviousEnabledServices { get; init; }

            // The device serial.
            internal string Serial { get; init; }

            // The application under test, or empty when every application was recorded.
            internal string TargetPackage { get; init; }
        }

        // Inputs of StartApplicationAsync: the device, the package to start and the optional activity.
        private sealed record ApplicationLaunchOptions(string Serial, string PackageName, string Activity);

        // Inputs of StartRecorderActivityAsync: the device and the values the recorder app stores.
        private sealed record RecorderActivityOptions(string Serial, string HubUrl, string TargetPackage);

        // A secure setting and the value to restore; a null value means the setting was unset.
        private sealed record SecureSettingValue(string Name, string Value);
        #endregion
    }
}
