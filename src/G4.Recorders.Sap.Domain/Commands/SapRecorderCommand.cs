using CommandBridge;

using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using G4.Recorders.Sap.Domain.Models;
using G4.Recorders.Sap.Domain.Scripting;

namespace G4.Recorders.Sap.Domain.Commands
{
    /// <summary>
    /// A command that prints the chain of the SAP GUI control at a screen point, or of the focused control, as JSON.
    /// </summary>
    /// <remarks>
    /// The command mirrors the UIA recorder's <c>peek</c>: <c>-x</c> and <c>-y</c> select a point, and <c>-f</c> the
    /// focused control when no point is given. It attaches to SAP GUI itself, without the host, and prints <c>{}</c>
    /// when no argument is given or SAP GUI is not available.
    /// </remarks>
    [Command(
        name: "peek",
        description: "Retrieve the chain of the SAP GUI control at the given screen coordinates, " +
            "or of the focused control if coordinates are not provided.")]
    public class SapRecorderCommand() : CommandBase(CommandSchema)
    {
        #region *** Constants    ***
        // Defines the command schema and parameter metadata, matching the UIA recorder's peek command.
        private static readonly Dictionary<string, IDictionary<string, CommandData>> CommandSchema = new(StringComparer.Ordinal)
        {
            ["peek"] = new Dictionary<string, CommandData>(StringComparer.Ordinal)
            {
                ["f"] = new()
                {
                    Name = "focused",
                    Description = "Peek the focused SAP control instead of using coordinates.",
                    Mandatory = false
                },
                ["x"] = new() { Name = "xpos", Description = "X-coordinate on the screen, in physical pixels.", Mandatory = false },
                ["y"] = new() { Name = "ypos", Description = "Y-coordinate on the screen, in physical pixels.", Mandatory = false }
            }
        };

        // Serializes the chain as the hub and REST routes do: camelCase, nulls omitted.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        protected override void OnInvoke(Dictionary<string, string> parameters)
        {
            // Print an empty object when no argument was given.
            if (parameters is null || parameters.Count < 1)
            {
                Console.WriteLine("{}");
                return;
            }

            // Read the point; a coordinate that is missing or not a number counts as absent.
            var hasX = parameters.TryGetValue("xpos", out var xText) && int.TryParse(xText, out _);
            var hasY = parameters.TryGetValue("ypos", out var yText) && int.TryParse(yText, out _);
            var isFocused = parameters.ContainsKey("focused");
            var x = hasX ? int.Parse(xText, System.Globalization.CultureInfo.InvariantCulture) : 0;
            var y = hasY ? int.Parse(yText, System.Globalization.CultureInfo.InvariantCulture) : 0;

            // Attach to SAP GUI for this one lookup; the host's thread and attachment are not running here.
            using var scriptingHost = new SapScriptingHost();
            var repository = new SapRecorderRepository(scriptingHost, new SapCursorPositionProvider());

            try
            {
                // Peek focus when asked and no complete point was given; otherwise the point.
                var chain = (!hasX || !hasY) && isFocused
                    ? repository.GetElementChain()
                    : repository.GetElementChain(x, y);

                Console.WriteLine(JsonSerializer.Serialize(chain, JsonOptions));
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                // SAP GUI is not available; print an empty object, as for a point without a control.
                Console.WriteLine(JsonSerializer.Serialize(new SapChainModel(), JsonOptions));
            }
        }
        #endregion
    }
}
