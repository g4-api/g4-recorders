using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Provides typed property reads and session, window, and control lookups over <see cref="ISapGuiObject"/>.
    /// </summary>
    /// <remarks>
    /// Every method runs on the scripting thread. Lookups take window handles as arguments instead of reading the
    /// desktop themselves, so callers decide which window is current and tests can drive the lookups with fakes.
    /// Property readers treat members the object does not expose as absent instead of failing.
    /// </remarks>
    internal static class SapGuiNavigator
    {
        #region *** Methods      ***
        /// <summary>
        /// Finds the control under a screen point through the session that owns the window at that point.
        /// </summary>
        /// <param name="application">The <c>GuiApplication</c> object.</param>
        /// <param name="rootWindow">The top-level window handle at the point.</param>
        /// <param name="x">The horizontal screen coordinate in physical pixels.</param>
        /// <param name="y">The vertical screen coordinate in physical pixels.</param>
        /// <returns>The control, or <c>null</c> when the point is outside SAP GUI or on window chrome.</returns>
        internal static ISapGuiObject FindByPosition(ISapGuiObject application, long rootWindow, int x, int y)
        {
            // Require the application; the scripting host supplies it.
            ArgumentNullException.ThrowIfNull(argument: application);

            // Hit-test only the session that owns the window, so overlapping sessions never answer for each other.
            var (session, _) = FindWindow(application, rootWindow);
            if (session is null)
            {
                return null;
            }

            try
            {
                // SAP returns the control identifier first; raise=false keeps an empty hit from throwing.
                var items = GetItems(session.InvokeMethod("findByPosition", x, y, false) as ISapGuiObject);
                var hitId = items.Count > 0 ? Convert.ToString(items[0], CultureInfo.InvariantCulture) : string.Empty;
                if (string.IsNullOrEmpty(hitId))
                {
                    return null;
                }

                // Resolve the identifier in its form: absolute from the application, relative from the session.
                var owner = hitId.StartsWith("/app", StringComparison.Ordinal) ? application : session;

                return owner.InvokeMethod("findById", hitId, false) as ISapGuiObject;
            }
            catch (COMException)
            {
                // A busy or closing session cannot hit-test; report no control rather than failing the caller.
                return null;
            }
        }

        /// <summary>
        /// Finds the session and window whose top-level handle matches a window handle.
        /// </summary>
        /// <param name="application">The <c>GuiApplication</c> object.</param>
        /// <param name="rootWindow">The top-level window handle to match.</param>
        /// <returns>The session and window, or two <c>null</c> values when no SAP window has the handle.</returns>
        internal static (ISapGuiObject Session, ISapGuiObject Window) FindWindow(ISapGuiObject application, long rootWindow)
        {
            // A zero handle matches nothing; SAP windows always have one.
            if (rootWindow == 0)
            {
                return (null, null);
            }

            // Compare each session window's handle: wnd[0] and any open popups.
            foreach (var session in GetSessions(application))
            {
                foreach (var window in GetChildren(session))
                {
                    if (GetLong(window, propertyName: "Handle") == rootWindow)
                    {
                        return (session, window);
                    }
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Gets the focused control of the SAP window that has a handle, or of the first session's active window.
        /// </summary>
        /// <param name="application">The <c>GuiApplication</c> object.</param>
        /// <param name="foregroundWindow">The top-level handle of the foreground window.</param>
        /// <returns>The focused control, or <c>null</c> when no session is open.</returns>
        /// <remarks>
        /// The fallback serves callers outside SAP GUI, such as a REST client in a browser, where the foreground window
        /// is not a SAP window but the user still expects the SAP focus.
        /// </remarks>
        internal static ISapGuiObject GetFocused(ISapGuiObject application, long foregroundWindow)
        {
            // Prefer the foreground SAP window; otherwise use the active window of the first session.
            var (_, window) = FindWindow(application, foregroundWindow);
            if (window is null)
            {
                var sessions = GetSessions(application);
                window = sessions.Count > 0 ? GetObject(sessions[0], propertyName: "ActiveWindow") : null;
            }

            return window is null ? null : GetObject(window, propertyName: "GuiFocus");
        }

        /// <summary>
        /// Reads a Boolean property, treating an absent or non-Boolean value as <c>false</c>.
        /// </summary>
        /// <param name="target">The scripting object.</param>
        /// <param name="propertyName">The SAP property name.</param>
        /// <returns>The property value.</returns>
        internal static bool GetBoolean(ISapGuiObject target, string propertyName)
        {
            return target.GetProperty(propertyName) is true;
        }

        /// <summary>
        /// Gets the child objects of a container through its <c>Children</c> collection.
        /// </summary>
        /// <param name="container">The container, such as the application, a connection, or a session.</param>
        /// <returns>The children in SAP order, or an empty list when the object is not a container.</returns>
        internal static List<ISapGuiObject> GetChildren(ISapGuiObject container)
        {
            // Read the collection and keep only scripting objects.
            var children = new List<ISapGuiObject>();
            foreach (var item in GetItems(container?.GetProperty("Children") as ISapGuiObject))
            {
                if (item is ISapGuiObject child)
                {
                    children.Add(child);
                }
            }

            return children;
        }

        /// <summary>
        /// Reads the items of a SAP GUI collection through <c>Count</c> and <c>ElementAt</c>.
        /// </summary>
        /// <param name="collection">The collection, or <c>null</c>.</param>
        /// <returns>The items in order; empty for a null collection.</returns>
        internal static List<object> GetItems(ISapGuiObject collection)
        {
            // Treat a missing collection as empty, so callers need no null checks.
            var items = new List<object>();
            if (collection is null)
            {
                return items;
            }

            // Read every item by position; SAP collections are zero-based.
            var count = Convert.ToInt32(collection.GetProperty("Count") ?? 0, CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                items.Add(collection.InvokeMethod("ElementAt", index));
            }

            return items;
        }

        /// <summary>
        /// Reads a whole-number property, treating an absent value as zero.
        /// </summary>
        /// <param name="target">The scripting object.</param>
        /// <param name="propertyName">The SAP property name.</param>
        /// <returns>The property value.</returns>
        internal static long GetLong(ISapGuiObject target, string propertyName)
        {
            // SAP returns integers or handles of varying widths; convert them all.
            var value = target.GetProperty(propertyName);

            return value is null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a property that holds a scripting object.
        /// </summary>
        /// <param name="target">The scripting object.</param>
        /// <param name="propertyName">The SAP property name, such as <c>Parent</c>.</param>
        /// <returns>The object, or <c>null</c> when absent.</returns>
        internal static ISapGuiObject GetObject(ISapGuiObject target, string propertyName)
        {
            return target.GetProperty(propertyName) as ISapGuiObject;
        }

        /// <summary>
        /// Gets every session of every connection.
        /// </summary>
        /// <param name="application">The <c>GuiApplication</c> object.</param>
        /// <returns>The sessions in SAP order.</returns>
        internal static List<ISapGuiObject> GetSessions(ISapGuiObject application)
        {
            // Walk application > connections > sessions.
            var sessions = new List<ISapGuiObject>();
            foreach (var connection in GetChildren(application))
            {
                sessions.AddRange(GetChildren(connection));
            }

            return sessions;
        }

        /// <summary>
        /// Reads a property as text, treating an absent value as an empty string.
        /// </summary>
        /// <param name="target">The scripting object.</param>
        /// <param name="propertyName">The SAP property name.</param>
        /// <returns>The property value as invariant text.</returns>
        internal static string GetString(ISapGuiObject target, string propertyName)
        {
            return Convert.ToString(target.GetProperty(propertyName), CultureInfo.InvariantCulture) ?? string.Empty;
        }
        #endregion
    }
}
