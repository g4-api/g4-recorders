using System;

using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.UnitTests.Support
{
    /// <summary>
    /// In-memory <see cref="ISapScriptingHost"/> that runs operations inline against a fake application.
    /// </summary>
    /// <remarks>
    /// While <see cref="Application"/> is <c>null</c>, <see cref="Invoke{T}"/> fails as the real host does when SAP Logon
    /// is not running, and <see cref="Post"/> passes <c>null</c>.
    /// </remarks>
    internal sealed class FakeScriptingHost : ISapScriptingHost
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the application operations receive; <c>null</c> stands for SAP GUI not running.
        /// </summary>
        internal ISapGuiObject Application { get; set; }
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to release; the fake owns no thread.
        }

        /// <inheritdoc />
        public T Invoke<T>(Func<ISapGuiObject, T> operation)
        {
            // Fail like the real host while SAP GUI is unavailable.
            if (Application is null)
            {
                throw new InvalidOperationException("SAP GUI Scripting is not available.");
            }

            return operation(Application);
        }

        /// <inheritdoc />
        public void Post(Action<ISapGuiObject> operation)
        {
            operation(Application);
        }
        #endregion
    }
}
