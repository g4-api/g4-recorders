using System;

namespace G4.Recorders.Android.Domain.Adb
{
    /// <summary>
    /// Holds the inputs of <see cref="AdbPathResolver"/>.
    /// </summary>
    /// <param name="getEnvironmentVariable">Returns an environment variable, or null when it is not set.</param>
    /// <param name="testFileExists">Tests whether a file exists.</param>
    /// <param name="baseDirectory">The recorder's base directory, used to derive the sandbox root.</param>
    /// <param name="isWindows">Whether the host runs Windows, which names the executable <c>adb.exe</c>.</param>
    public sealed class AdbPathOptions(
        Func<string, string> getEnvironmentVariable,
        Func<string, bool> testFileExists,
        string baseDirectory,
        bool isWindows)
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets the recorder's base directory.
        /// </summary>
        public string BaseDirectory { get; } = baseDirectory;

        /// <summary>
        /// Gets the environment variable reader.
        /// </summary>
        public Func<string, string> GetEnvironmentVariable { get; } = getEnvironmentVariable;

        /// <summary>
        /// Gets whether the host runs Windows.
        /// </summary>
        public bool IsWindows { get; } = isWindows;

        /// <summary>
        /// Gets the file existence test.
        /// </summary>
        public Func<string, bool> TestFileExists { get; } = testFileExists;
        #endregion
    }
}
