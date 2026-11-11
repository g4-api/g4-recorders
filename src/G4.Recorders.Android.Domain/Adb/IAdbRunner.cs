using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace G4.Recorders.Android.Domain.Adb
{
    /// <summary>
    /// Runs the Android Debug Bridge on the recorder host.
    /// </summary>
    /// <remarks>
    /// Every device operation of the Android recorder goes through this interface, so the launcher's command sequences
    /// can be tested with a scripted fake and no device or SDK.
    /// </remarks>
    public interface IAdbRunner
    {
        #region *** Methods      ***
        /// <summary>
        /// Runs adb with an argument list and waits for it to exit.
        /// </summary>
        /// <param name="arguments">The arguments, one token per entry; they are never re-parsed by a host shell.</param>
        /// <param name="timeout">How long adb may run before it is killed.</param>
        /// <returns>The exit code and the captured output.</returns>
        /// <exception cref="System.IO.FileNotFoundException">adb was not found on the recorder host.</exception>
        /// <exception cref="TimeoutException">adb did not exit within <paramref name="timeout"/>.</exception>
        Task<AdbResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout);
        #endregion
    }
}
