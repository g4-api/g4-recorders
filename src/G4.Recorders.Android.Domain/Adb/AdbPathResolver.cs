using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace G4.Recorders.Android.Domain.Adb
{
    /// <summary>
    /// Finds the adb executable on the recorder host.
    /// </summary>
    /// <remarks>
    /// The lookup order is the Android SDK named by <c>ANDROID_HOME</c>, then <c>ANDROID_SDK_ROOT</c>, then the G4
    /// sandbox (<c>G4_SANDBOX</c>, or two folders above the recorder, the sandbox's <c>bot-utilities</c> layout), then
    /// the <c>PATH</c>. The resolver reads only the inputs it is given, so tests supply their own environment.
    /// </remarks>
    public static class AdbPathResolver
    {
        #region *** Methods      ***
        /// <summary>
        /// Returns the candidate adb paths in lookup order.
        /// </summary>
        /// <param name="options">The environment, base directory and platform to resolve against.</param>
        /// <returns>The candidate full paths; they may not exist.</returns>
        public static IReadOnlyList<string> GetCandidatePaths(AdbPathOptions options)
        {
            // Require the inputs before any lookup reads them.
            ArgumentNullException.ThrowIfNull(argument: options);

            // Name the executable for the platform; Windows adds the extension.
            var executableName = options.IsWindows ? "adb.exe" : "adb";
            var candidates = new List<string>();

            // Prefer an explicitly configured SDK, the way Android Studio and Appium find adb.
            foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
            {
                var sdkPath = options.GetEnvironmentVariable(variable);

                if (!string.IsNullOrWhiteSpace(sdkPath))
                {
                    candidates.Add(Path.Combine(sdkPath, "platform-tools", executableName));
                }
            }

            // Then the sandbox, from its marker or from the recorder's own bot-utilities location.
            var sandboxPath = options.GetEnvironmentVariable("G4_SANDBOX");

            if (string.IsNullOrWhiteSpace(sandboxPath))
            {
                sandboxPath = Path.GetFullPath(Path.Combine(options.BaseDirectory, "..", ".."));
            }

            candidates.Add(Path.Combine(sandboxPath, "android", "platform-tools", executableName));

            // Last, every PATH entry, which covers a platform-tools folder the user added by hand.
            var pathVariable = options.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathEntries = pathVariable
                .Split(separator: Path.PathSeparator, options: StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(entry => Path.Combine(entry, executableName));
            candidates.AddRange(pathEntries);

            return candidates;
        }

        /// <summary>
        /// Returns the first existing adb executable.
        /// </summary>
        /// <param name="options">The environment, base directory and platform to resolve against.</param>
        /// <returns>The full path of adb.</returns>
        /// <exception cref="FileNotFoundException">No candidate exists; the message lists every probed path.</exception>
        public static string Resolve(AdbPathOptions options)
        {
            // Require the inputs before any lookup reads them.
            ArgumentNullException.ThrowIfNull(argument: options);

            // Return the first candidate that exists, in lookup order.
            var candidates = GetCandidatePaths(options);
            var adbPath = candidates.FirstOrDefault(options.TestFileExists);

            if (adbPath is not null)
            {
                return adbPath;
            }

            // Name every probed location so the operator knows where to install platform-tools.
            var message = "adb was not found on the recorder host. Install the Android SDK platform-tools and set " +
                "ANDROID_HOME, or put adb on the PATH. Probed: " + string.Join("; ", candidates);
            throw new FileNotFoundException(message);
        }
        #endregion
    }
}
