using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

using G4.Recorders.Sap.Domain.Native;

namespace G4.Recorders.Sap.Domain.Scripting
{
    /// <summary>
    /// Reads entries from the Windows running object table, where SAP Logon registers its <c>SAPGUI</c> entry.
    /// </summary>
    /// <remarks>
    /// Each call enumerates the table, compares moniker display names, and returns the first match. Every moniker,
    /// enumerator, bind context, and table reference is released before the call returns; only the returned object is
    /// left to the caller. Entries whose display name cannot be read are skipped. Call it on the scripting thread.
    /// </remarks>
    internal static class RunningObjectTableReader
    {
        #region *** Constants    ***
        // Separates the item name from the delimiter in an item moniker display name, such as !SAPGUI.
        private const char ItemDelimiter = '!';
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Gets the running object registered under an item name.
        /// </summary>
        /// <param name="itemName">The item name, such as <c>SAPGUI</c>, matched case-insensitively with or without <c>!</c>.</param>
        /// <returns>The registered object, or <c>null</c> when no entry matches.</returns>
        internal static object GetObject(string itemName)
        {
            // Require a name; an empty name matches no entry.
            ArgumentException.ThrowIfNullOrWhiteSpace(argument: itemName);

            // Open the table and a bind context, then enumerate the registered monikers.
            NativeMethods.OpenRunningObjectTable(out var table, out var bindContext);
            IEnumMoniker enumerator = null;

            try
            {
                table.EnumRunning(out enumerator);

                // Walk the entries one at a time and return the first one whose display name matches.
                var monikers = new IMoniker[1];
                while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var entry = GetMatchingObject(table, bindContext, monikers[0], itemName);
                    if (entry is not null)
                    {
                        return entry;
                    }
                }

                return null;
            }
            finally
            {
                // Release the enumeration objects; the returned entry holds its own reference.
                ReleaseComObject(enumerator);
                ReleaseComObject(bindContext);
                ReleaseComObject(table);
            }
        }

        // Returns the object behind one moniker when its display name matches the item name; otherwise null.
        // The moniker is always released; unreadable entries, such as ones from exiting processes, are skipped.
        private static object GetMatchingObject(IRunningObjectTable table, IBindCtx bindContext, IMoniker moniker, string itemName)
        {
            try
            {
                // Compare the display name without the item delimiter, so !SAPGUI and SAPGUI both match.
                moniker.GetDisplayName(bindContext, pmkToLeft: null, out var displayName);
                var isMatch = string.Equals(
                    displayName?.TrimStart(ItemDelimiter),
                    itemName.TrimStart(ItemDelimiter),
                    StringComparison.OrdinalIgnoreCase);

                if (!isMatch)
                {
                    return null;
                }

                // Bind to the running object; a failure here means the entry exists but is not reachable.
                Marshal.ThrowExceptionForHR(table.GetObject(moniker, out var entry));

                return entry;
            }
            catch (COMException)
            {
                // Skip entries that vanished or refuse a display name.
                return null;
            }
            finally
            {
                // Release the moniker; the table keeps its own registration.
                ReleaseComObject(moniker);
            }
        }

        // Releases one runtime callable wrapper reference, ignoring null values.
        private static void ReleaseComObject(object comObject)
        {
            // Skip values that hold no COM reference.
            if (comObject is not null && Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
        #endregion
    }
}
