using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Common.Domain.Models.Mcp;

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Dispatches MCP JSON-RPC requests for the recorder's fixed 4-tool catalog, calling straight into the same
    /// <see cref="IUiaRecorderRepository"/> the REST controller uses, so MCP and REST can never diverge.
    /// </summary>
    /// <param name="repository">The repository both this MCP dispatcher and the REST controller call into.</param>
    public class RecorderMcpRepository(IUiaRecorderRepository repository) : IRecorderMcpRepository
    {
        #region *** Constants    ***
        // The MCP protocol version this recorder implements.
        private const string ProtocolVersion = "2025-06-18";

        // The recorder's identity, reported by initialize. Fixed rather than derived from the assembly, since
        // MCP clients only use this for display, not for capability negotiation.
        private const string ServerName = "g4-recorders-uia-mcp";
        private const string ServerVersion = "1.0.0";
        #endregion

        #region *** Fields       ***
        // Loads the fixed tool catalog once from embedded JSON resources, matching ToolsRepository's own
        // static-field caching convention in G4.Services - this catalog never changes at runtime.
        private static readonly Dictionary<string, McpToolDefinitionModel> Tools = LoadTools();
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public McpResponseModel CallTool(JsonElement parameters, object id)
        {
            // Read the real MCP tools/call shape ({name, arguments}), not G4's own rule-execution envelope -
            // this recorder has no G4 rule model to convert into.
            var toolName = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;

            var arguments = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement
                : default;

            // Report an unknown tool name as a structured JSON-RPC error rather than throwing, so the caller
            // receives a normal response instead of an unhandled exception.
            if (string.IsNullOrWhiteSpace(toolName) || !Tools.ContainsKey(toolName))
            {
                return NewErrorResponse(id, code: -32601, message: $"Tool '{toolName}' not found.");
            }

            // Dispatch to the matching repository method. Screenshots need an MCP image content block so clients
            // deliver pixels to the model; the remaining tools keep the normal text/structured result shape.
            var result = InvokeTool(repository, toolName, arguments);

            return new McpResponseModel
            {
                Id = id,
                Result = toolName == "g4.GetScreenshot"
                    ? NewScreenshotResult((RecorderScreenshotModel)result)
                    : NewStructuredResult(result)
            };
        }

        /// <inheritdoc />
        public McpResponseModel FindTools(object id)
        {
            return new McpResponseModel
            {
                Id = id,
                Result = new McpToolsListResultModel { Tools = Tools.Values }
            };
        }

        /// <inheritdoc />
        public McpResponseModel Initialize(object id)
        {
            return new McpResponseModel
            {
                Id = id,
                Result = new McpInitializeResultModel
                {
                    ProtocolVersion = ProtocolVersion,
                    Capabilities = new McpCapabilitiesModel
                    {
                        Tools = new McpToolsCapabilityModel { ListChanged = true }
                    },
                    ServerInfo = new McpServerInfoModel { Name = ServerName, Version = ServerVersion }
                }
            };
        }

        // Reads an optional boolean argument, treating anything other than a literal JSON "true" (missing,
        // wrong type, or explicit "false") as false.
        private static bool GetBooleanOrDefault(JsonElement arguments, string propertyName)
        {
            return arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty(propertyName, out var propertyElement)
                && propertyElement.ValueKind == JsonValueKind.True;
        }

        // Reads a required integer argument, throwing a clear, tool-scoped message when it is missing or
        // non-numeric, since a malformed required argument cannot be defaulted silently.
        private static int GetInt32OrThrow(JsonElement arguments, string propertyName, string toolName)
        {
            if (TryGetInt32(arguments, propertyName, out var value))
            {
                return value;
            }

            var message = $"Tool '{toolName}' requires a numeric '{propertyName}' argument.";
            throw new ArgumentException(message);
        }

        // Reads an optional string argument, returning null when absent or the wrong type - matching
        // SetWindowFocus's own optional-string contract, which treats null as "skip this identifier."
        private static string GetStringOrDefault(JsonElement arguments, string propertyName)
        {
            return arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty(propertyName, out var propertyElement)
                && propertyElement.ValueKind == JsonValueKind.String
                ? propertyElement.GetString()
                : null;
        }

        // Calls the matching repository method for the given tool name, extracting typed arguments from the raw
        // JSON element. Mirrors the same methods the REST controller calls, so behavior cannot diverge by
        // transport.
        private static object InvokeTool(
            IUiaRecorderRepository repository,
            string toolName,
            JsonElement arguments)
        {
            return toolName switch
            {
                "g4.GetScreenshot" => repository.GetScreenshot(GetBooleanOrDefault(arguments, "metricsOnly")),

                "g4.MovePointer" => repository.MovePointer(
                    GetInt32OrThrow(arguments, "x", toolName),
                    GetInt32OrThrow(arguments, "y", toolName)),

                "g4.ResolveGroundedElement" => repository.ResolveGroundedElement(
                    GetInt32OrThrow(arguments, "x", toolName),
                    GetInt32OrThrow(arguments, "y", toolName),
                    GetBooleanOrDefault(arguments, "skipOffset")),

                "g4.SetWindowFocus" => repository.SetWindowFocus(
                    GetStringOrDefault(arguments, "windowTitle"),
                    GetStringOrDefault(arguments, "processName")),

                _ => throw new InvalidOperationException($"Tool '{toolName}' has no dispatch handler.")
            };
        }

        // Builds a screenshot result whose PNG is represented exactly once as an MCP image content block. The
        // coordinate-space fields remain separately available as structured metadata and as compact JSON text.
        private static McpToolCallResultModel NewScreenshotResult(RecorderScreenshotModel screenshot)
        {
            var metadata = new Dictionary<string, object>
            {
                ["height"] = screenshot.Height,
                ["mimeType"] = screenshot.MimeType,
                ["originX"] = screenshot.OriginX,
                ["originY"] = screenshot.OriginY,
                ["width"] = screenshot.Width
            };

            var content = new List<McpToolCallContentModel>();

            if (!string.IsNullOrWhiteSpace(screenshot.ImageBase64))
            {
                content.Add(new McpToolCallContentModel
                {
                    Data = screenshot.ImageBase64,
                    MimeType = screenshot.MimeType,
                    Type = "image"
                });
            }

            content.Add(new McpToolCallContentModel
            {
                Text = JsonSerializer.Serialize(metadata),
                Type = "text"
            });

            return new McpToolCallResultModel
            {
                Content = content,
                StructuredContent = metadata
            };
        }

        // Builds the existing text-plus-structured shape used by every non-screenshot recorder tool.
        private static McpToolCallResultModel NewStructuredResult(object result)
        {
            return new McpToolCallResultModel
            {
                Content =
                [
                    new McpToolCallContentModel { Type = "text", Text = JsonSerializer.Serialize(result) }
                ],
                StructuredContent = result
            };
        }

        // Loads the fixed tool catalog from this assembly's embedded JSON resources exactly once. Clones each
        // inputSchema element because the source JsonDocument is disposed at the end of this method, and an
        // un-cloned JsonElement would become invalid once its owning document is gone.
        private static Dictionary<string, McpToolDefinitionModel> LoadTools()
        {
            var assembly = typeof(RecorderMcpRepository).Assembly;
            var tools = new Dictionary<string, McpToolDefinitionModel>(StringComparer.Ordinal);

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                var isToolResource = resourceName.Contains(".Resources.SystemTools.", StringComparison.Ordinal)
                    && resourceName.EndsWith(".json", StringComparison.Ordinal);

                if (!isToolResource)
                {
                    continue;
                }

                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                using var resourceDocument = JsonDocument.Parse(resourceStream);

                var toolDefinition = new McpToolDefinitionModel
                {
                    Name = resourceDocument.RootElement.GetProperty("name").GetString(),
                    Title = resourceDocument.RootElement.GetProperty("title").GetString(),
                    Description = resourceDocument.RootElement.GetProperty("description").GetString(),
                    InputSchema = resourceDocument.RootElement.GetProperty("inputSchema").Clone(),
                    OutputSchema = resourceDocument.RootElement.TryGetProperty("outputSchema", out var outputSchema)
                        ? outputSchema.Clone()
                        : null
                };

                tools[toolDefinition.Name] = toolDefinition;
            }

            return tools;
        }

        // Builds a JSON-RPC error response, keeping the successful-response shape free of a spurious null Result.
        private static McpResponseModel NewErrorResponse(object id, int code, string message)
        {
            return new McpResponseModel
            {
                Id = id,
                Error = new McpErrorModel { Code = code, Message = message }
            };
        }

        // Reads an optional integer argument, returning false when absent or non-numeric rather than throwing,
        // so callers can distinguish "not supplied" from "supplied as zero."
        private static bool TryGetInt32(JsonElement arguments, string propertyName, out int value)
        {
            value = default;

            return arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty(propertyName, out var propertyElement)
                && propertyElement.ValueKind == JsonValueKind.Number
                && propertyElement.TryGetInt32(out value);
        }
        #endregion
    }
}
