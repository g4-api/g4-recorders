using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.Middlewares
{
    /// <summary>
    /// Maps the commands of a SAP <c>Change</c> event into <c>nativeSap</c> events, in the Chromium recorder's style.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SAP GUI reports each change as a command array: an array of commands, each an array of a kind (<c>SP</c> sets a
    /// property, <c>M</c> calls a method), a member name, and the arguments. One command becomes one event:
    /// </para>
    /// <list type="table">
    ///     <item><term><c>SP text</c></term><description><c>Keyboard</c> / <c>SendKeys</c> with <c>{ text }</c>.</description></item>
    ///     <item><term><c>M press</c>, <c>M select</c></term><description><c>Mouse</c> / <c>InvokeClick</c> with <c>{}</c>.</description></item>
    ///     <item><term><c>M sendVKey</c></term><description><c>Keyboard</c> / <c>SendSapVirtualKey</c> with <c>{ key }</c>.</description></item>
    ///     <item><term>other <c>M</c></term><description><c>Mouse</c> / <c>InvokeSapMethod</c> with <c>{ method, arguments }</c>.</description></item>
    ///     <item><term>other <c>SP</c></term><description><c>Form</c> / <c>SetSapProperty</c> with <c>{ name, value }</c>.</description></item>
    /// </list>
    /// <para>
    /// Values on password fields are redacted. Unknown command kinds are skipped. The mapper is pure, so it never reads
    /// SAP GUI and never logs values.
    /// </para>
    /// </remarks>
    internal static class SapActionMapper
    {
        #region *** Constants    ***
        // Names the click action shared with the Chromium recorder.
        private const string InvokeClickEvent = "InvokeClick";

        // Names the generic SAP method action, replayed through the driver's call route.
        private const string InvokeSapMethodEvent = "InvokeSapMethod";

        // Names the keyboard event family shared with the other recorders.
        private const string KeyboardType = "Keyboard";

        // Names the form event family shared with the Chromium recorder.
        private const string FormType = "Form";

        // Identifies a method call in a SAP command.
        private const string MethodKind = "M";

        // Names the mouse event family shared with the other recorders.
        private const string MouseType = "Mouse";

        // Replaces any redacted value, as the Chromium recorder does.
        private const string RedactedText = "***";

        // Names the typed-text action shared with the Chromium recorder.
        private const string SendKeysEvent = "SendKeys";

        // Names the SAP virtual key action, replayed through the driver's vkey route.
        private const string SendSapVirtualKeyEvent = "SendSapVirtualKey";

        // Identifies a property assignment in a SAP command.
        private const string SetPropertyKind = "SP";

        // Names the generic SAP property action, replayed through the driver's property route.
        private const string SetSapPropertyEvent = "SetSapProperty";

        // Lists the methods that are clicks: buttons press, and menus, tabs, and radio buttons select.
        private static readonly HashSet<string> ClickMethods = new(StringComparer.OrdinalIgnoreCase) { "press", "select" };
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Converts a SAP command array into events.
        /// </summary>
        /// <param name="chain">The chain of the control the change happened on.</param>
        /// <param name="commandArray">The command array SAP GUI passed to <c>Change</c>.</param>
        /// <param name="timestamp">The time the change was received, in Unix epoch milliseconds.</param>
        /// <returns>One event per recognized command, in order; empty when nothing was recognized.</returns>
        internal static List<SapEventModel> ConvertToEvents(SapChainModel chain, object commandArray, long timestamp)
        {
            // Read the trigger's type once; it decides redaction for every command.
            var trigger = chain.Path.Count > 0 ? chain.Path[^1] : null;
            var isPassword = trigger?.ControlType == SapGuiTypes.PasswordField;

            return GetCommands(commandArray)
                .Select(i => ConvertToEvent(chain, i, isPassword, timestamp))
                .Where(i => i is not null)
                .ToList();
        }

        /// <summary>
        /// Converts one SAP command into an event.
        /// </summary>
        /// <param name="chain">The chain of the control the change happened on.</param>
        /// <param name="command">The command: kind, member name, then arguments.</param>
        /// <param name="isPassword">Whether the control is a password field, whose values are redacted.</param>
        /// <param name="timestamp">The time the change was received, in Unix epoch milliseconds.</param>
        /// <returns>The event, or <c>null</c> for an unknown kind or a malformed command.</returns>
        internal static SapEventModel ConvertToEvent(SapChainModel chain, object[] command, bool isPassword, long timestamp)
        {
            // Require a kind and a member name; anything shorter is not a command.
            if (command is null || command.Length < 2 || command[0] is not string kind || command[1] is not string name)
            {
                return null;
            }

            var arguments = command.Skip(2).ToArray();
            var (type, eventName, value) = kind.ToUpperInvariant() switch
            {
                SetPropertyKind => NewPropertyAction(name, arguments, isPassword),
                MethodKind => NewMethodAction(name, arguments, isPassword),
                _ => (null, null, null)
            };

            return eventName is null ? null : new SapEventModel
            {
                Chain = chain,
                Event = eventName,
                Timestamp = timestamp,
                Type = type,
                Value = value
            };
        }

        // Reads the commands of a command array; a single flat command is accepted as an array of one.
        private static IEnumerable<object[]> GetCommands(object commandArray)
        {
            // A flat command starts with its kind; a command array holds commands.
            if (commandArray is not object[] items || items.Length == 0)
            {
                return [];
            }

            return items[0] is string
                ? [items]
                : items.OfType<object[]>();
        }

        // Builds the action of a method call.
        private static (string Type, string Event, object Value) NewMethodAction(string name, object[] arguments, bool isPassword)
        {
            // Clicks carry no value.
            if (ClickMethods.Contains(name))
            {
                return (MouseType, InvokeClickEvent, new object());
            }

            // Virtual keys carry the key number.
            if (string.Equals(name, "sendVKey", StringComparison.OrdinalIgnoreCase) && arguments.Length > 0)
            {
                var key = Convert.ToInt32(arguments[0], CultureInfo.InvariantCulture);

                return (KeyboardType, SendSapVirtualKeyEvent, new SapVirtualKeyValueModel { Key = key });
            }

            // Every other method replays through the driver's call route, with its arguments redacted on passwords.
            var value = new SapMethodValueModel
            {
                Arguments = isPassword ? [.. arguments.Select(_ => (object)RedactedText)] : arguments,
                Method = name
            };

            return (MouseType, InvokeSapMethodEvent, value);
        }

        // Builds the action of a property assignment.
        private static (string Type, string Event, object Value) NewPropertyAction(string name, object[] arguments, bool isPassword)
        {
            // Text assignments are typed text, redacted on passwords as the Chromium recorder does.
            var assigned = arguments.Length > 0 ? arguments[0] : null;
            if (string.Equals(name, "text", StringComparison.OrdinalIgnoreCase))
            {
                var text = isPassword ? RedactedText : Convert.ToString(assigned, CultureInfo.InvariantCulture);
                var keys = new SapSendKeysValueModel { IsRedacted = isPassword ? true : null, Text = text };

                return (KeyboardType, SendKeysEvent, keys);
            }

            // Every other property replays through the driver's property route.
            var value = new SapPropertyValueModel
            {
                IsRedacted = isPassword ? true : null,
                Name = name,
                Value = isPassword ? RedactedText : assigned
            };

            return (FormType, SetSapPropertyEvent, value);
        }
        #endregion
    }
}
