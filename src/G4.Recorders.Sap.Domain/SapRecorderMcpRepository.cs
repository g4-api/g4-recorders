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

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Default <see cref="ISapRecorderMcpRepository"/>: runs the fixed tool catalog against the SAP recorder repository,
    /// with the UIA recorder's tool names, arguments, and screenshot layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tool definitions are embedded JSON resources under <c>Resources.SystemTools</c>, loaded once. Screenshots are
    /// returned as an overview plus native-resolution detail tiles, each preceded by its one-based image number;
    /// <c>g4.ConvertScreenshotPoint</c> maps a pixel on one of those images back to physical desktop pixels from current
    /// metrics, so callers never handle geometry, scaling, or DPI.
    /// </para>
    /// <para>
    /// The image layout is the UIA recorder's, so a point selected on either recorder's screenshot converts the same way.
    /// </para>
    /// </remarks>
    /// <param name="repository">The repository that provides the screenshot, pointer, focus, and grounding operations.</param>
    public sealed class SapRecorderMcpRepository(ISapRecorderRepository repository) : ISapRecorderMcpRepository
    {
        #region *** Constants    ***
        // Names the tool whose result is image content rather than structured text.
        private const string GetScreenshotTool = "g4.GetScreenshot";

        // Bounds the overview and every detail tile below client and model image-size limits.
        private const int ModelImageMaximumDimension = 1_000;

        // Names the MCP protocol version this recorder implements.
        private const string ProtocolVersion = "2025-06-18";

        // Names the recorder in initialize results; clients use it for display only.
        private const string ServerName = "g4-recorders-sap-mcp";

        // Versions the recorder in initialize results.
        private const string ServerVersion = "1.0.0";

        // Holds the fixed tool catalog, loaded once from the embedded resources.
        private static readonly Dictionary<string, McpToolDefinitionModel> Tools = LoadTools();
        #endregion

        #region *** Fields       ***
        // Provides the operations every tool runs.
        private readonly ISapRecorderRepository _repository = repository;
        #endregion

        #region *** Methods      ***
        /// <inheritdoc />
        public McpResponseModel CallTool(JsonElement parameters, object id)
        {
            // Read the MCP tools/call shape: { name, arguments }.
            var isObject = parameters.ValueKind == JsonValueKind.Object;
            var toolName = isObject && parameters.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var arguments = isObject && parameters.TryGetProperty("arguments", out var argumentsElement)
                ? argumentsElement
                : default;

            // Report an unknown tool as a JSON-RPC error rather than an exception.
            if (string.IsNullOrWhiteSpace(toolName) || !Tools.ContainsKey(toolName))
            {
                return NewErrorResponse(id, code: -32601, message: $"Tool '{toolName}' not found.");
            }

            // Run the tool; screenshots become image content blocks, everything else structured text.
            var result = InvokeTool(toolName, arguments);

            return new McpResponseModel
            {
                Id = id,
                Result = toolName == GetScreenshotTool
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
                    Capabilities = new McpCapabilitiesModel { Tools = new McpToolsCapabilityModel { ListChanged = true } },
                    ProtocolVersion = ProtocolVersion,
                    ServerInfo = new McpServerInfoModel { Name = ServerName, Version = ServerVersion }
                }
            };
        }

        // Converts a pixel on one screenshot view into source-image and physical desktop pixels, from current metrics.
        private Dictionary<string, object> ConvertScreenshotPoint(int imageNumber, int x, int y)
        {
            // Read the current desktop metrics; the caller supplies no geometry.
            var screenshot = _repository.GetScreenshot(metricsOnly: true);
            if (screenshot.Width < 1 || screenshot.Height < 1)
            {
                throw new InvalidOperationException("The recorder returned invalid screenshot metrics.");
            }

            // Find the view and check the pixel lies on it.
            var view = NewScreenshotViewDefinitions(screenshot.Width, screenshot.Height)
                .SingleOrDefault(i => i.ImageNumber == imageNumber);
            if (view is null)
            {
                var message = $"Image number {imageNumber} is not valid for the current recorder metrics.";
                throw new ArgumentOutOfRangeException(nameof(imageNumber), message);
            }

            AssertPixel(x, view.ImageSize.Width, imageNumber, nameof(x));
            AssertPixel(y, view.ImageSize.Height, imageNumber, nameof(y));

            // Scale overview pixels to the source; detail tiles are native resolution and only offset.
            var sourceX = view.SourceBounds.X
                + (view.IsOverview ? MapPixel(x, view.ImageSize.Width, view.SourceBounds.Width) : x);
            var sourceY = view.SourceBounds.Y
                + (view.IsOverview ? MapPixel(y, view.ImageSize.Height, view.SourceBounds.Height) : y);

            return new Dictionary<string, object>
            {
                ["sourcePixel"] = NewPoint(sourceX, sourceY),
                ["physicalDesktopPixel"] = NewPoint(
                    checked(screenshot.OriginX + sourceX),
                    checked(screenshot.OriginY + sourceY)),
                ["coordinateSpace"] = "physical-desktop-device-pixels"
            };

            // Rejects a pixel outside the view, naming the valid range.
            static void AssertPixel(int value, int size, int imageNumber, string parameterName)
            {
                if (value < 0 || value >= size)
                {
                    var message = $"Point {parameterName} must be from 0 through {size - 1} for image {imageNumber}.";
                    throw new ArgumentOutOfRangeException(parameterName, message);
                }
            }
        }

        // Encodes one view: a native-resolution crop for detail tiles, a high-quality downscale for the overview.
        private static string EncodeScreenshotView(Bitmap sourceImage, ScreenshotView view)
        {
            // Crop detail tiles without scaling.
            if (!view.IsOverview)
            {
                using var detail = sourceImage.Clone(view.SourceBounds, PixelFormat.Format32bppArgb);
                using var detailStream = new MemoryStream();
                detail.Save(detailStream, ImageFormat.Png);

                return Convert.ToBase64String(detailStream.ToArray());
            }

            // Draw the overview onto its canvas with high-quality resampling.
            using var canvas = new Bitmap(view.ImageSize.Width, view.ImageSize.Height, PixelFormat.Format32bppArgb);
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

        // Reads an optional Boolean argument; anything but a literal true reads as false.
        private static bool GetBooleanOrDefault(JsonElement arguments, string propertyName)
        {
            return arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty(propertyName, out var propertyElement)
                && propertyElement.ValueKind == JsonValueKind.True;
        }

        // Reads a required integer argument, naming the tool and argument when it is missing or not a number.
        private static int GetInt32OrThrow(JsonElement arguments, string propertyName, string toolName)
        {
            // Accept only a JSON number that fits an integer.
            if (arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty(propertyName, out var propertyElement)
                && propertyElement.ValueKind == JsonValueKind.Number
                && propertyElement.TryGetInt32(out var value))
            {
                return value;
            }

            var message = $"Tool '{toolName}' requires a numeric '{propertyName}' argument.";
            throw new ArgumentException(message);
        }

        // Reads an optional string argument; absent or non-string values read as null, which SetWindowFocus skips.
        private static string GetStringOrDefault(JsonElement arguments, string propertyName)
        {
            return arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty(propertyName, out var propertyElement)
                && propertyElement.ValueKind == JsonValueKind.String
                ? propertyElement.GetString()
                : null;
        }

        // Runs the named tool with arguments read from the raw JSON.
        private object InvokeTool(string toolName, JsonElement arguments) => toolName switch
        {
            GetScreenshotTool => _repository.GetScreenshot(GetBooleanOrDefault(arguments, "metricsOnly")),
            "g4.ConvertScreenshotPoint" => ConvertScreenshotPoint(
                GetInt32OrThrow(arguments, "imageNumber", toolName),
                GetInt32OrThrow(arguments, "x", toolName),
                GetInt32OrThrow(arguments, "y", toolName)),
            "g4.MovePointer" => _repository.MovePointer(
                GetInt32OrThrow(arguments, "x", toolName),
                GetInt32OrThrow(arguments, "y", toolName)),
            "g4.ResolveGroundedElement" => _repository.ResolveGroundedElement(
                GetInt32OrThrow(arguments, "x", toolName),
                GetInt32OrThrow(arguments, "y", toolName),
                GetBooleanOrDefault(arguments, "skipOffset")),
            "g4.SetWindowFocus" => _repository.SetWindowFocus(
                GetStringOrDefault(arguments, "windowTitle"),
                GetStringOrDefault(arguments, "processName")),
            _ => throw new InvalidOperationException($"Tool '{toolName}' has no dispatch handler.")
        };

        // Loads the tool catalog from this assembly's embedded JSON resources, cloning each schema out of its document.
        private static Dictionary<string, McpToolDefinitionModel> LoadTools()
        {
            var assembly = typeof(SapRecorderMcpRepository).Assembly;
            var tools = new Dictionary<string, McpToolDefinitionModel>(StringComparer.Ordinal);

            // Read every tool resource; schemas are cloned because the documents are disposed here.
            var resourceNames = assembly
                .GetManifestResourceNames()
                .Where(i => i.Contains(".Resources.SystemTools.", StringComparison.Ordinal))
                .Where(i => i.EndsWith(".json", StringComparison.Ordinal));

            foreach (var resourceName in resourceNames)
            {
                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                using var resourceDocument = JsonDocument.Parse(resourceStream);
                var root = resourceDocument.RootElement;

                var toolDefinition = new McpToolDefinitionModel
                {
                    Description = root.GetProperty("description").GetString(),
                    InputSchema = root.GetProperty("inputSchema").Clone(),
                    Name = root.GetProperty("name").GetString(),
                    OutputSchema = root.TryGetProperty("outputSchema", out var outputSchema) ? outputSchema.Clone() : null,
                    Title = root.GetProperty("title").GetString()
                };

                tools[toolDefinition.Name] = toolDefinition;
            }

            return tools;
        }

        // Maps a pixel between two sizes so the first and last pixels map onto each other.
        private static int MapPixel(int value, int fromSize, int toSize)
        {
            return fromSize == 1 || toSize == 1
                ? 0
                : (int)Math.Round(value / (double)(fromSize - 1) * (toSize - 1), MidpointRounding.AwayFromZero);
        }

        // Builds a JSON-RPC error response without a result.
        private static McpResponseModel NewErrorResponse(object id, int code, string message)
        {
            return new McpResponseModel { Error = new McpErrorModel { Code = code, Message = message }, Id = id };
        }

        // Scales a desktop size down so its longer side fits the model limit; smaller sizes are kept.
        private static Size NewModelCanvasSize(int sourceWidth, int sourceHeight)
        {
            // Keep sizes that already fit.
            if (Math.Max(sourceWidth, sourceHeight) <= ModelImageMaximumDimension)
            {
                return new Size(sourceWidth, sourceHeight);
            }

            var scale = ModelImageMaximumDimension / (double)Math.Max(sourceWidth, sourceHeight);

            return new Size(
                Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero)),
                Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero)));
        }

        // Builds a point dictionary with lowercase members, as the conversion result reports them.
        private static Dictionary<string, object> NewPoint(int x, int y)
        {
            return new Dictionary<string, object> { ["x"] = x, ["y"] = y };
        }

        // Builds the screenshot result: per view, its metadata then its image, followed by the complete metadata.
        private static McpToolCallResultModel NewScreenshotResult(RecorderScreenshotModel screenshot)
        {
            // Encode the views and describe the primary one, which sets the reported width and height.
            var views = NewScreenshotViews(screenshot);
            var primarySize = views.Count > 0 ? views[0].ImageSize : new Size(screenshot.Width, screenshot.Height);
            var metadata = new Dictionary<string, object>
            {
                ["height"] = primarySize.Height,
                ["mimeType"] = screenshot.MimeType,
                ["originX"] = screenshot.OriginX,
                ["originY"] = screenshot.OriginY,
                ["views"] = views.Select(NewScreenshotViewMetadata).ToArray(),
                ["width"] = primarySize.Width
            };

            // Precede each image with its own metadata, so the model reads the image number next to the pixels.
            var content = new List<McpToolCallContentModel>();
            foreach (var view in views)
            {
                var viewMetadata = new Dictionary<string, object> { ["screenshotView"] = NewScreenshotViewMetadata(view) };
                content.Add(new McpToolCallContentModel { Text = JsonSerializer.Serialize(viewMetadata), Type = "text" });
                content.Add(new McpToolCallContentModel
                {
                    Data = view.ImageBase64,
                    MimeType = screenshot.MimeType,
                    Type = "image"
                });
            }

            content.Add(new McpToolCallContentModel { Text = JsonSerializer.Serialize(metadata), Type = "text" });

            return new McpToolCallResultModel { Content = content, StructuredContent = metadata };
        }

        // Describes one view: size, kind, number, and for detail tiles their grid position.
        private static Dictionary<string, object> NewScreenshotViewMetadata(ScreenshotView view)
        {
            var metadata = new Dictionary<string, object>
            {
                ["height"] = view.ImageSize.Height,
                ["kind"] = view.Kind,
                ["imageNumber"] = view.ImageNumber,
                ["width"] = view.ImageSize.Width
            };

            // Only detail tiles sit in the grid.
            if (!view.IsOverview && view.Kind == "detail")
            {
                metadata["column"] = view.Column;
                metadata["row"] = view.Row;
            }

            return metadata;
        }

        // Defines the views of a desktop size: one native view when it fits, otherwise an overview plus detail tiles.
        private static List<ScreenshotView> NewScreenshotViewDefinitions(int sourceWidth, int sourceHeight)
        {
            // Nothing to show for an empty desktop.
            if (sourceWidth < 1 || sourceHeight < 1)
            {
                return [];
            }

            // A desktop that fits is one native view.
            var sourceBounds = new Rectangle(0, 0, sourceWidth, sourceHeight);
            if (Math.Max(sourceWidth, sourceHeight) <= ModelImageMaximumDimension)
            {
                return [new ScreenshotView
                {
                    ImageNumber = 1,
                    ImageSize = sourceBounds.Size,
                    Kind = "native",
                    SourceBounds = sourceBounds
                }];
            }

            // Otherwise an overview first, then tiles row by row.
            var views = new List<ScreenshotView>
            {
                new()
                {
                    ImageNumber = 1,
                    ImageSize = NewModelCanvasSize(sourceWidth, sourceHeight),
                    IsOverview = true,
                    Kind = "overview",
                    SourceBounds = sourceBounds
                }
            };

            var imageNumber = 2;
            for (int top = 0, row = 0; top < sourceHeight; top += ModelImageMaximumDimension, row++)
            {
                for (int left = 0, column = 0; left < sourceWidth; left += ModelImageMaximumDimension, column++)
                {
                    var bounds = new Rectangle(
                        left,
                        top,
                        Math.Min(ModelImageMaximumDimension, sourceWidth - left),
                        Math.Min(ModelImageMaximumDimension, sourceHeight - top));

                    views.Add(new ScreenshotView
                    {
                        Column = column,
                        ImageNumber = imageNumber++,
                        ImageSize = bounds.Size,
                        Kind = "detail",
                        Row = row,
                        SourceBounds = bounds
                    });
                }
            }

            return views;
        }

        // Encodes every view of a capture; a native view reuses the captured PNG as is.
        private static List<ScreenshotView> NewScreenshotViews(RecorderScreenshotModel screenshot)
        {
            // A metrics-only capture has no pixels to show.
            if (string.IsNullOrWhiteSpace(screenshot.ImageBase64))
            {
                return [];
            }

            var views = NewScreenshotViewDefinitions(screenshot.Width, screenshot.Height);
            using var sourceStream = new MemoryStream(Convert.FromBase64String(screenshot.ImageBase64));
            using var sourceImage = new Bitmap(sourceStream);

            foreach (var view in views)
            {
                view.ImageBase64 = view.Kind == "native" ? screenshot.ImageBase64 : EncodeScreenshotView(sourceImage, view);
            }

            return views;
        }

        // Builds the text-plus-structured result every tool except the screenshot returns.
        private static McpToolCallResultModel NewStructuredResult(object result)
        {
            return new McpToolCallResultModel
            {
                Content = [new McpToolCallContentModel { Text = JsonSerializer.Serialize(result), Type = "text" }],
                StructuredContent = result
            };
        }
        #endregion

        #region *** Nested Types ***
        /// <summary>
        /// Describes one screenshot view: its number, kind, size, source area, and grid position.
        /// </summary>
        private sealed class ScreenshotView
        {
            /// <summary>
            /// Gets or sets the tile's column in the detail grid.
            /// </summary>
            public int Column { get; set; }

            /// <summary>
            /// Gets or sets the encoded PNG.
            /// </summary>
            public string ImageBase64 { get; set; }

            /// <summary>
            /// Gets or sets the one-based number callers copy into <c>g4.ConvertScreenshotPoint</c>.
            /// </summary>
            public int ImageNumber { get; set; }

            /// <summary>
            /// Gets or sets the size of the encoded image.
            /// </summary>
            public Size ImageSize { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the view is the scaled overview.
            /// </summary>
            public bool IsOverview { get; set; }

            /// <summary>
            /// Gets or sets the view kind: <c>native</c>, <c>overview</c>, or <c>detail</c>.
            /// </summary>
            public string Kind { get; set; }

            /// <summary>
            /// Gets or sets the tile's row in the detail grid.
            /// </summary>
            public int Row { get; set; }

            /// <summary>
            /// Gets or sets the area of the source capture the view shows.
            /// </summary>
            public Rectangle SourceBounds { get; set; }
        }
        #endregion
    }
}
