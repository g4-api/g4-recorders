using CommandBridge;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace G4.Recorders.Uia.Domain.Commands
{
    /// <summary>
    /// A command that inspects a screen coordinate and prints the UI Automation
    /// ancestor chain of the element found at that point.
    /// </summary>
    [Command(
        name: "peek",
        description: "Retrieve the ancestor chain of a UI Automation element at the given screen coordinates, " +
            "or the currently focused element if coordinates are not provided.")]
    public class UiaRecorderCommand() : CommandBase(s_commands)
    {
        // Defines the command schema and parameter metadata.
        private static readonly Dictionary<string, IDictionary<string, CommandData>> s_commands =
            new(StringComparer.Ordinal)
            {
                ["peek"] = new Dictionary<string, CommandData>(StringComparer.Ordinal)
                {
                    ["f"] = new()
                    {
                        Name = "focused",
                        Description = "Peek the currently focused element instead of using coordinates.",
                        Mandatory = false
                    },
                    ["x"] = new()
                    {
                        Name = "xpos",
                        Description = "X-coordinate on the screen.",
                        Mandatory = false
                    },
                    ["y"] = new()
                    {
                        Name = "ypos",
                        Description = "Y-coordinate on the screen.",
                        Mandatory = false
                    }
                }
            };

        // JSON serialization options used for output.
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <inheritdoc />
        protected override void OnInvoke(Dictionary<string, string> parameters)
        {
            // Exit early if parameters are missing or insufficient.
            if (parameters == null || parameters.Count < 1)
            {
                // Not enough parameters — return empty JSON.
                Console.WriteLine("{}");
                return;
            }

            // Validate presence and format of X and Y coordinates.
            var isX = parameters.TryGetValue("xpos", out var xOut) && Regex.IsMatch(xOut, @"^(-)?\d+$");
            var isY = parameters.TryGetValue("ypos", out var yOut) && Regex.IsMatch(xOut, @"^(-)?\d+$");
            var isFocused = parameters.ContainsKey("focused");

            // Parse X coordinate (defaults to 0 if missing or invalid).
            var x = isX && int.TryParse(xOut, out var xValue)
                ? xValue
                : 0;

            // Parse Y coordinate (defaults to 0 if missing or invalid).
            var y = isY && int.TryParse(yOut, out var yValue)
                ? yValue
                : 0;

            // Construct the repository directly (this command runs outside the ASP.NET host's DI container) with
            // the same cursor-position provider the host itself registers and the configured identity attributes.
            var repository = new UiaRecorderRepository(
                cursorPositionProvider: new UiaCursorPositionProvider(),
                identityAttributes: ReadIdentityAttributes());

            // Retrieve the ancestor chain based on the provided coordinates
            // or focused element if no coordinates.
            var chain = (!isX || !isY) && isFocused
                ? repository.GetElementChain()
                : repository.GetElementChain(x, y);

            // Serialize the result to JSON and write to console.
            var json = JsonSerializer.Serialize(chain, s_jsonOptions);

            // Output the JSON result to the console.
            Console.WriteLine(json);
        }

        // Reads the ordered identity attributes from the appsettings.json copied next to the executable, mirroring the
        // host's G4:Uia:IdentityAttributes section without taking a configuration-package dependency. Any missing file,
        // section, or read error returns null so the repository falls back to its default attribute order.
        private static IReadOnlyList<string> ReadIdentityAttributes()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

                if (!File.Exists(path))
                {
                    return null;
                }

                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);

                var hasSection = document.RootElement.TryGetProperty("G4", out var g4)
                    && g4.TryGetProperty("Uia", out var uia)
                    && uia.TryGetProperty("IdentityAttributes", out var attributes)
                    && attributes.ValueKind == JsonValueKind.Array;

                if (!hasSection)
                {
                    return null;
                }

                // Re-read the array here so definite assignment is unambiguous after the short-circuited guard above.
                var identityAttributes = g4.GetProperty("Uia").GetProperty("IdentityAttributes");
                var values = new List<string>();

                foreach (var attribute in identityAttributes.EnumerateArray())
                {
                    if (attribute.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var value = attribute.GetString();

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }

                return values.Count > 0 ? values : null;
            }
            catch (Exception)
            {
                // A missing or malformed settings file must not break the standalone peek command.
                return null;
            }
        }
    }
}
