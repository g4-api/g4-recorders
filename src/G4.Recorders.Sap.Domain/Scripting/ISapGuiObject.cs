namespace G4.Recorders.Sap.Domain.Scripting
{
    /// <summary>
    /// Represents one SAP GUI Scripting object, such as the application, a session, a window, or a control.
    /// </summary>
    /// <remarks>
    /// Members are reached by name, late-bound, so the recorder needs no SAP type library. Results that are themselves
    /// scripting objects come back as <see cref="ISapGuiObject"/>; scalar results come back unchanged. Every member must
    /// be called on the scripting thread that owns the object.
    /// </remarks>
    public interface ISapGuiObject
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets the underlying COM object, used to subscribe to its events; <c>null</c> for objects without one.
        /// </summary>
        object ComObject { get; }
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Reads a property.
        /// </summary>
        /// <param name="name">The SAP property name, such as <c>Id</c> or <c>ScreenLeft</c>.</param>
        /// <returns>The value, a wrapped scripting object, or <c>null</c> when the object does not expose the property.</returns>
        object GetProperty(string name);

        /// <summary>
        /// Calls a method.
        /// </summary>
        /// <param name="name">The SAP method name, such as <c>findById</c>.</param>
        /// <param name="arguments">The method arguments, in order.</param>
        /// <returns>The result, a wrapped scripting object, or <c>null</c> for methods without a result.</returns>
        object InvokeMethod(string name, params object[] arguments);

        /// <summary>
        /// Assigns a property.
        /// </summary>
        /// <param name="name">The SAP property name, such as <c>Record</c>.</param>
        /// <param name="value">The value to assign.</param>
        void SetProperty(string name, object value);
        #endregion
    }
}
