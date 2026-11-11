using System;
using System.Text;

namespace G4.Recorders.Android.Domain.Adb
{
    /// <summary>
    /// Holds the outcome of one adb invocation.
    /// </summary>
    /// <remarks>
    /// The raw standard output is kept as bytes because <c>adb exec-out screencap -p</c> writes a PNG; text commands
    /// read <see cref="StandardOutput"/>, the same bytes decoded as UTF-8.
    /// </remarks>
    /// <param name="exitCode">The adb process exit code.</param>
    /// <param name="standardOutputBytes">Everything adb wrote to standard output.</param>
    /// <param name="standardError">Everything adb wrote to standard error.</param>
    public sealed class AdbResult(int exitCode, byte[] standardOutputBytes, string standardError)
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets the adb process exit code; zero means adb itself succeeded, not that the device command did.
        /// </summary>
        public int ExitCode { get; } = exitCode;

        /// <summary>
        /// Gets the standard error text, trimmed.
        /// </summary>
        public string StandardError { get; } = (standardError ?? string.Empty).Trim();

        /// <summary>
        /// Gets the standard output decoded as UTF-8 and trimmed.
        /// </summary>
        public string StandardOutput => Encoding.UTF8.GetString(StandardOutputBytes).Trim();

        /// <summary>
        /// Gets the raw standard output.
        /// </summary>
        public byte[] StandardOutputBytes { get; } = standardOutputBytes ?? Array.Empty<byte>();
        #endregion
    }
}
