using System;

namespace G4.Recorders.Sap.Domain.Scripting
{
    /// <summary>
    /// Owns the attachment to SAP GUI Scripting and runs every scripting call on one scripting thread.
    /// </summary>
    /// <remarks>
    /// The host attaches lazily through the <c>SAPGUI</c> running object table entry and re-attaches after SAP Logon
    /// restarts. Operations receive the <c>GuiApplication</c> object and run one at a time, interleaved only with the
    /// SAP events the same thread receives.
    /// </remarks>
    public interface ISapScriptingHost : IDisposable
    {
        #region *** Methods      ***
        /// <summary>
        /// Runs an operation against the scripting application and waits for its result.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="operation">The operation, which receives the <c>GuiApplication</c> object.</param>
        /// <returns>The operation's result.</returns>
        /// <exception cref="InvalidOperationException">SAP Logon is not running, or it refuses scripting.</exception>
        T Invoke<T>(Func<ISapGuiObject, T> operation);

        /// <summary>
        /// Queues an operation against the scripting application without waiting for it.
        /// </summary>
        /// <param name="operation">
        /// The operation, which receives the <c>GuiApplication</c> object, or <c>null</c> when SAP Logon is not running
        /// or refuses scripting. The operation handles its own failures.
        /// </param>
        void Post(Action<ISapGuiObject> operation);
        #endregion
    }
}
