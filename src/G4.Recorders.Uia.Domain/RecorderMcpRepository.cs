using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Common.Domain.Models.Mcp;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Dispatches MCP JSON-RPC requests for the recorder's fixed tool catalog. Mechanical capture and interaction
    /// use the same <see cref="IUiaRecorderRepository"/> as REST; screenshot-point conversion obtains current
    /// recorder metrics directly so callers never transport coordinate metadata.
    /// </summary>
    public class RecorderMcpRepository(IUiaRecorderRepository repository) : IRecorderMcpRepository
    {
        #region *** Constants    ***
        // The MCP protocol version this recorder implements.
        private const string ProtocolVersion = "2025-06-18";

        // The recorder's identity, reported by initialize. Fixed rather than derived from the assembly, since
        // MCP clients only use this for display, not for capability negotiation.
        private const string ServerName = "g4-recorders-uia-mcp";
        private const string ServerVersion = "1.0.0";

        // Bounds the overview and every native detail tile below client and inference resize thresholds.
        private const int ModelImageMaximumDimension = 1_000;
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
            var result = InvokeTool(toolName, arguments);

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

        // Calls the matching recorder primitive or recorder-owned screenshot conversion, extracting typed
        // arguments from the raw JSON element.
        private object InvokeTool(string toolName, JsonElement arguments)
        {
            return toolName switch
            {
                "g4.GetScreenshot" => repository.GetScreenshot(GetBooleanOrDefault(arguments, "metricsOnly")),

                "g4.ConvertScreenshotPoint" => ConvertScreenshotPoint(
                    GetInt32OrThrow(arguments, "imageNumber", toolName),
                    GetInt32OrThrow(arguments, "x", toolName),
                    GetInt32OrThrow(arguments, "y", toolName)),

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

        // Builds one overview plus native-resolution detail views from the same capture. Each one-based image
        // number is emitted immediately before its image, while structured content carries the complete metadata.
        private static McpToolCallResultModel NewScreenshotResult(RecorderScreenshotModel screenshot)
        {
            var views = NewScreenshotViews(screenshot);
            var primarySize = views.Count > 0
                ? views[0].ImageSize
                : new Size(screenshot.Width, screenshot.Height);
            var metadata = new Dictionary<string, object>
            {
                ["height"] = primarySize.Height,
                ["mimeType"] = screenshot.MimeType,
                ["originX"] = screenshot.OriginX,
                ["originY"] = screenshot.OriginY,
                ["views"] = views.Select(NewScreenshotViewMetadata).ToArray(),
                ["width"] = primarySize.Width
            };

            var content = new List<McpToolCallContentModel>();

            foreach (var view in views)
            {
                content.Add(new McpToolCallContentModel
                {
                    Text = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        ["screenshotView"] = NewScreenshotViewMetadata(view)
                    }),
                    Type = "text"
                });
                content.Add(new McpToolCallContentModel
                {
                    Data = view.ImageBase64,
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

        // Converts a pixel from one deterministic screenshot view against current recorder metrics. The caller
        // selects the returned view but supplies no dimensions, origin, transform, scale, or DPI information.
        private Dictionary<string, object> ConvertScreenshotPoint(int imageNumber, int x, int y)
        {
            var screenshot = repository.GetScreenshot(metricsOnly: true);
            if (screenshot.Width < 1 || screenshot.Height < 1)
            {
                throw new InvalidOperationException("The recorder returned invalid screenshot metrics.");
            }

            var view = NewScreenshotViewDefinitions(screenshot.Width, screenshot.Height)
                .SingleOrDefault(item => item.ImageNumber == imageNumber);

            if (view == null)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(imageNumber),
                    $"Image number {imageNumber} is not valid for the current recorder metrics.");
            }

            if (x < 0 || x >= view.ImageSize.Width)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Point x must be from 0 through {view.ImageSize.Width - 1} for image {imageNumber}.");
            }

            if (y < 0 || y >= view.ImageSize.Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(y),
                    $"Point y must be from 0 through {view.ImageSize.Height - 1} for image {imageNumber}.");
            }

            var sourceX = view.SourceBounds.X + (view.IsOverview
                ? MapPixel(x, view.ImageSize.Width, view.SourceBounds.Width)
                : x);
            var sourceY = view.SourceBounds.Y + (view.IsOverview
                ? MapPixel(y, view.ImageSize.Height, view.SourceBounds.Height)
                : y);

            return new Dictionary<string, object>
            {
                ["sourcePixel"] = NewPoint(sourceX, sourceY),
                ["physicalDesktopPixel"] = NewPoint(
                    checked(screenshot.OriginX + sourceX),
                    checked(screenshot.OriginY + sourceY)),
                ["coordinateSpace"] = "physical-desktop-device-pixels"
            };
        }

        private static int MapPixel(int value, int fromSize, int toSize)
        {
            if (fromSize == 1 || toSize == 1)
            {
                return 0;
            }

            return (int)Math.Round(
                value / (double)(fromSize - 1) * (toSize - 1),
                MidpointRounding.AwayFromZero);
        }

        private static Dictionary<string, object> NewPoint(int x, int y)
        {
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["y"] = y
            };
        }

        private static Dictionary<string, object> NewScreenshotViewMetadata(ScreenshotView view)
        {
            var metadata = new Dictionary<string, object>
            {
                ["height"] = view.ImageSize.Height,
                ["kind"] = view.Kind,
                ["imageNumber"] = view.ImageNumber,
                ["width"] = view.ImageSize.Width
            };

            if (!view.IsOverview && view.Kind == "detail")
            {
                metadata["column"] = view.Column;
                metadata["row"] = view.Row;
            }

            return metadata;
        }

        private static List<ScreenshotView> NewScreenshotViews(RecorderScreenshotModel screenshot)
        {
            if (string.IsNullOrWhiteSpace(screenshot.ImageBase64))
            {
                return [];
            }

            var views = NewScreenshotViewDefinitions(screenshot.Width, screenshot.Height);
            using var sourceStream = new MemoryStream(Convert.FromBase64String(screenshot.ImageBase64));
            using var sourceImage = new Bitmap(sourceStream);

            foreach (var view in views)
            {
                view.ImageBase64 = view.Kind == "native"
                    ? screenshot.ImageBase64
                    : EncodeScreenshotView(sourceImage, view);
            }

            return views;
        }

        private static List<ScreenshotView> NewScreenshotViewDefinitions(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth < 1 || sourceHeight < 1)
            {
                return [];
            }

            var sourceBounds = new Rectangle(0, 0, sourceWidth, sourceHeight);
            if (Math.Max(sourceWidth, sourceHeight) <= ModelImageMaximumDimension)
            {
                return
                [
                    new ScreenshotView
                    {
                        ImageSize = sourceBounds.Size,
                        Kind = "native",
                        SourceBounds = sourceBounds,
                        ImageNumber = 1
                    }
                ];
            }

            var views = new List<ScreenshotView>
            {
                new ScreenshotView
                {
                    ImageSize = NewModelCanvasSize(sourceWidth, sourceHeight),
                    IsOverview = true,
                    Kind = "overview",
                    SourceBounds = sourceBounds,
                    ImageNumber = 1
                }
            };

            var imageNumber = 2;
            var row = 0;
            for (var top = 0; top < sourceHeight; top += ModelImageMaximumDimension, row++)
            {
                var column = 0;
                for (var left = 0; left < sourceWidth; left += ModelImageMaximumDimension, column++)
                {
                    var bounds = new Rectangle(
                        left,
                        top,
                        Math.Min(ModelImageMaximumDimension, sourceWidth - left),
                        Math.Min(ModelImageMaximumDimension, sourceHeight - top));

                    views.Add(new ScreenshotView
                    {
                        Column = column,
                        ImageSize = bounds.Size,
                        Kind = "detail",
                        Row = row,
                        SourceBounds = bounds,
                        ImageNumber = imageNumber++
                    });
                }
            }

            return views;
        }

        private static string EncodeScreenshotView(Bitmap sourceImage, ScreenshotView view)
        {
            if (!view.IsOverview)
            {
                using var detail = sourceImage.Clone(view.SourceBounds, PixelFormat.Format32bppArgb);
                using var detailStream = new MemoryStream();
                detail.Save(detailStream, ImageFormat.Png);

                return Convert.ToBase64String(detailStream.ToArray());
            }

            using var canvas = new Bitmap(
                view.ImageSize.Width,
                view.ImageSize.Height,
                PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(
                    sourceImage,
                    new Rectangle(Point.Empty, view.ImageSize),
                    view.SourceBounds,
                    GraphicsUnit.Pixel);
            }

            using var canvasStream = new MemoryStream();
            canvas.Save(canvasStream, ImageFormat.Png);

            return Convert.ToBase64String(canvasStream.ToArray());
        }

        // Applies the same deterministic size policy to a captured image and to a later metrics-only conversion.
        private static Size NewModelCanvasSize(int sourceWidth, int sourceHeight)
        {
            if (Math.Max(sourceWidth, sourceHeight) <= ModelImageMaximumDimension)
            {
                return new Size(sourceWidth, sourceHeight);
            }

            var scale = ModelImageMaximumDimension / (double)Math.Max(sourceWidth, sourceHeight);

            return new Size(
                Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero)));
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

        private sealed class ScreenshotView
        {
            public int Column { get; set; }

            public Size ImageSize { get; set; }

            public string ImageBase64 { get; set; }

            public bool IsOverview { get; set; }

            public string Kind { get; set; }

            public int Row { get; set; }

            public Rectangle SourceBounds { get; set; }

            public int ImageNumber { get; set; }
        }
        #endregion
    }
}
