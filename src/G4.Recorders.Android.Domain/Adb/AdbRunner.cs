using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.Adb
{
    /// <summary>
    /// Default <see cref="IAdbRunner"/>: starts the adb executable as a child process.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton. The executable is resolved on the first run and cached, so installing platform-tools
    /// while the recorder runs needs no restart until the first successful lookup. Arguments go through
    /// <see cref="ProcessStartInfo.ArgumentList"/>, so caller values are never re-parsed by a host shell.
    /// </remarks>
    public sealed class AdbRunner : IAdbRunner
    {
        #region *** Fields       ***
        // The resolved adb path, or null until the first successful lookup.
        private string _adbPath;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public async Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout)
        {
            // Require the argument list before resolving adb or starting a process.
            ArgumentNullException.ThrowIfNull(argument: arguments);

            // Resolve adb once; a failed lookup is retried on the next call.
            _adbPath ??= AdbPathResolver.Resolve(new AdbPathOptions(
                getEnvironmentVariable: Environment.GetEnvironmentVariable,
                testFileExists: File.Exists,
                baseDirectory: AppContext.BaseDirectory,
                isWindows: RuntimeInformation.IsOSPlatform(OSPlatform.Windows)));

            // Capture both streams; standard output stays binary for screencap.
            var startInfo = new ProcessStartInfo
            {
                FileName = _adbPath,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            // Start adb; the process is disposed when the call ends, whatever its outcome.
            using var process = Process.Start(startInfo);

            if (process is null)
            {
                var message = $"Failed to start adb at '{_adbPath}'.";
                throw new InvalidOperationException(message);
            }

            // Read both streams while waiting, so a full pipe buffer can never block adb.
            using var timeoutSource = new CancellationTokenSource(timeout);
            using var output = new MemoryStream();
            var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output, timeoutSource.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                return new AdbResult(process.ExitCode, output.ToArray(), error);
            }
            catch (OperationCanceledException exception)
            {
                // Kill a hung adb so it cannot hold the device or the port, then report the timeout.
                process.Kill(entireProcessTree: true);
                var message = $"adb {string.Join(' ', arguments)} did not finish within {timeout.TotalSeconds} seconds.";
                throw new TimeoutException(message, exception);
            }
        }
        #endregion
    }
}
