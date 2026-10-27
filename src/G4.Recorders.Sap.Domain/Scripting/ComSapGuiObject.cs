using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace G4.Recorders.Sap.Domain.Scripting
{
    /// <summary>
    /// Default <see cref="ISapGuiObject"/> that calls a SAP GUI Scripting COM object through <c>IDispatch</c>.
    /// </summary>
    /// <remarks>
    /// Calls go through <see cref="Type.InvokeMember(string, BindingFlags, Binder, object, object[], CultureInfo)"/>,
    /// which resolves names at run time. A property the object does not expose reads as <c>null</c>; every other
    /// failure propagates as the original COM exception, unwrapped from the reflection layer.
    /// </remarks>
    /// <param name="comObject">The scripting COM object to wrap.</param>
    internal sealed class ComSapGuiObject(object comObject) : ISapGuiObject
    {
        #region *** Constants    ***
        // Reported by IDispatch when the object has no member with the requested name.
        private const int DispatchUnknownName = unchecked((int)0x80020006);

        // Reported by IDispatch when the member exists but not for the requested kind of access.
        private const int DispatchMemberNotFound = unchecked((int)0x80020003);
        #endregion

        #region *** Properties   ***
        /// <inheritdoc />
        public object ComObject { get; } = comObject ?? throw new ArgumentNullException(nameof(comObject));
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public object GetProperty(string name)
        {
            try
            {
                // Read the property and wrap object results, so callers never handle raw COM references.
                return ConvertToResult(InvokeDispatch(name, BindingFlags.GetProperty, arguments: []));
            }
            catch (COMException exception) when (exception.HResult is DispatchUnknownName or DispatchMemberNotFound)
            {
                // Control families expose different property subsets; a missing one reads as absent.
                return null;
            }
        }

        /// <inheritdoc />
        public object InvokeMethod(string name, params object[] arguments)
        {
            // Call the method and wrap object results.
            return ConvertToResult(InvokeDispatch(name, BindingFlags.InvokeMethod, arguments ?? []));
        }

        /// <inheritdoc />
        public void SetProperty(string name, object value)
        {
            // Assign the property; failures, including read-only properties, propagate.
            InvokeDispatch(name, BindingFlags.SetProperty, arguments: [value]);
        }

        // Wraps a COM object result; scalars and nulls pass through unchanged.
        private static object ConvertToResult(object result)
        {
            return result is not null && Marshal.IsComObject(result)
                ? new ComSapGuiObject(result)
                : result;
        }

        // Calls one member through IDispatch and rethrows the COM failure instead of the reflection wrapper.
        private object InvokeDispatch(string name, BindingFlags flags, object[] arguments)
        {
            try
            {
                // Resolve and call the member by name with invariant culture, so numbers convert predictably.
                return ComObject
                    .GetType()
                    .InvokeMember(name, flags, binder: null, ComObject, arguments, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                // Surface the SAP failure itself, keeping its HRESULT and stack trace for classification.
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
        #endregion
    }
}
