using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.UnitTests.Support
{
    /// <summary>
    /// In-memory <see cref="ISapGuiObject"/> that stands in for a SAP GUI Scripting object in unit tests.
    /// </summary>
    /// <remarks>
    /// Properties are held in a case-insensitive dictionary, methods are delegates, and every method call and property
    /// assignment is recorded in <see cref="Invocations"/>. Children added through <see cref="AddChild"/> get their
    /// <c>Parent</c>, <c>Id</c>, and the container's <c>Children</c> collection maintained.
    /// </remarks>
    internal sealed class FakeSapGuiObject : ISapGuiObject
    {
        #region *** Fields       ***
        // Holds the children in insertion order, exposed through the Children collection.
        private readonly List<FakeSapGuiObject> _children = [];
        #endregion

        #region *** Constructors ***
        /// <summary>
        /// Initializes a new instance of the <see cref="FakeSapGuiObject"/> class.
        /// </summary>
        /// <param name="id">The absolute scripting identifier, such as <c>/app/con[0]/ses[0]/wnd[0]</c>.</param>
        /// <param name="type">The SAP type, such as <c>GuiMainWindow</c>.</param>
        internal FakeSapGuiObject(string id, string type)
        {
            // Seed the identity properties every scripting object exposes, and the Children collection.
            Properties["Id"] = id;
            Properties["Type"] = type;
            Properties["Children"] = new FakeSapGuiCollection(() => _children.Cast<object>().ToList());
        }
        #endregion

        #region *** Properties   ***
        /// <inheritdoc />
        public object ComObject => null;

        /// <summary>
        /// Gets the recorded method calls and property assignments, such as <c>findById(x,False)</c> or <c>set Record=True</c>.
        /// </summary>
        internal List<string> Invocations { get; } = [];

        /// <summary>
        /// Gets the method implementations by name; a call to a missing method throws.
        /// </summary>
        internal Dictionary<string, Func<object[], object>> Methods { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets or sets the exception thrown by property assignments, to simulate SAP GUI refusing them; <c>null</c> to accept.
        /// </summary>
        internal Exception SetPropertyFailure { get; set; }

        /// <summary>
        /// Gets the property values by name; a missing property reads as <c>null</c>.
        /// </summary>
        internal Dictionary<string, object> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Adds a child and links it back to this object as its parent.
        /// </summary>
        /// <param name="child">The child to add.</param>
        /// <returns>The child, so tree setup can be chained.</returns>
        internal FakeSapGuiObject AddChild(FakeSapGuiObject child)
        {
            _children.Add(child);
            child.Properties["Parent"] = this;

            return child;
        }

        /// <inheritdoc />
        public object GetProperty(string name)
        {
            return Properties.TryGetValue(name, out var value) ? value : null;
        }

        /// <inheritdoc />
        public object InvokeMethod(string name, params object[] arguments)
        {
            // Record the call with invariant formatting, then run its implementation.
            var formatted = string.Join(',', arguments.Select(i => Convert.ToString(i, CultureInfo.InvariantCulture)));
            Invocations.Add($"{name}({formatted})");

            if (!Methods.TryGetValue(name, out var method))
            {
                throw new MissingMethodException(Properties["Type"] as string, name);
            }

            return method(arguments);
        }

        /// <inheritdoc />
        public void SetProperty(string name, object value)
        {
            // Record the assignment, refuse it when configured to, then store it.
            Invocations.Add(string.Create(CultureInfo.InvariantCulture, $"set {name}={value}"));
            if (SetPropertyFailure is not null)
            {
                throw SetPropertyFailure;
            }

            Properties[name] = value;
        }

        /// <summary>
        /// Sets a property and returns this object, for fluent setup.
        /// </summary>
        /// <param name="name">The property name.</param>
        /// <param name="value">The property value.</param>
        /// <returns>This object.</returns>
        internal FakeSapGuiObject With(string name, object value)
        {
            Properties[name] = value;

            return this;
        }
        #endregion

        #region *** Nested Types ***
        /// <summary>
        /// In-memory SAP GUI collection exposing <c>Count</c> and <c>ElementAt</c>.
        /// </summary>
        /// <param name="source">Reads the current items, so later additions appear.</param>
        internal sealed class FakeSapGuiCollection(Func<IReadOnlyList<object>> source) : ISapGuiObject
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="FakeSapGuiCollection"/> class over fixed items.
            /// </summary>
            /// <param name="items">The items, in order.</param>
            internal FakeSapGuiCollection(params object[] items)
                : this(() => items)
            { }

            /// <inheritdoc />
            public object ComObject => null;

            /// <inheritdoc />
            public object GetProperty(string name)
            {
                return string.Equals(name, "Count", StringComparison.OrdinalIgnoreCase) ? source().Count : null;
            }

            /// <inheritdoc />
            public object InvokeMethod(string name, params object[] arguments)
            {
                return source()[Convert.ToInt32(arguments[0], CultureInfo.InvariantCulture)];
            }

            /// <inheritdoc />
            public void SetProperty(string name, object value)
            {
                throw new NotSupportedException("Collections have no settable properties.");
            }
        }
        #endregion
    }
}
