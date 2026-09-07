using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Common.Domain.Models.Mcp;

using G4.Recorders.Uia.Domain.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using UIAutomationClient;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace G4.Recorders.Uia.Domain.UnitTests
{
    [TestClass]
    [TestCategory(nameof(RecorderMcpRepository))]
    [TestCategory("UnitTest")]
    public sealed class RecorderMcpRepositoryTests
    {
        [TestMethod(DisplayName = "Verify that the MCP catalog excludes the redundant Peek tool")]
        public void FindToolsExcludesPeekTest()
        {
            // Arrange: create the MCP repository over a recorder stub with no operating-system dependencies.
            var recorderRepository = new StubRecorderRepository();
            var subject = new RecorderMcpRepository(recorderRepository);

            // Act: request the complete embedded MCP tool catalog.
            var response = subject.FindTools(id: 1);
            var result = (McpToolsListResultModel)response.Result;
            var actualToolNames = result.Tools
                .Select(tool => tool.Name)
                .OrderBy(toolName => toolName, StringComparer.Ordinal)
                .ToArray();

            // Assert: the fixed catalog exposes recorder-owned screenshot conversion and excludes Peek.
            string[] expectedToolNames =
            [
                "g4.ConvertScreenshotPoint",
                "g4.GetScreenshot",
                "g4.MovePointer",
                "g4.ResolveGroundedElement",
                "g4.SetWindowFocus"
            ];
            Assert.AreSequenceEqual(expectedToolNames, actualToolNames);
        }

        [TestMethod(DisplayName = "Verify that initialize advertises the structured-output MCP protocol version")]
        public void InitializeAdvertisesStructuredOutputProtocolVersionTest()
        {
            // Arrange: create the MCP dispatcher over an operating-system-independent recorder stub.
            var subject = new RecorderMcpRepository(new StubRecorderRepository());

            // Act: initialize the MCP server.
            var response = subject.Initialize(id: 2);
            var result = (McpInitializeResultModel)response.Result;

            // Assert: outputSchema and structuredContent are advertised under their defining protocol revision.
            Assert.IsNull(response.Error);
            Assert.AreEqual("2025-06-18", result.ProtocolVersion);
        }

        [TestMethod(DisplayName = "Verify that GetScreenshot publishes its structured metadata output schema")]
        public void FindToolsPublishesScreenshotOutputSchemaTest()
        {
            // Arrange: create the MCP dispatcher over an operating-system-independent recorder stub.
            var subject = new RecorderMcpRepository(new StubRecorderRepository());

            // Act: inspect the advertised screenshot tool definition.
            var response = subject.FindTools(id: 3);
            var result = (McpToolsListResultModel)response.Result;
            var screenshotTool = result.Tools.Single(tool => tool.Name == "g4.GetScreenshot");
            var outputSchema = screenshotTool.OutputSchema.Value;
            var properties = outputSchema.GetProperty("properties");
            var required = outputSchema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();

            // Assert: the exact structuredContent fields are declared, with no undeclared additions allowed.
            Assert.AreEqual("object", outputSchema.GetProperty("type").GetString());
            Assert.AreEqual("integer", properties.GetProperty("width").GetProperty("type").GetString());
            Assert.AreEqual("integer", properties.GetProperty("height").GetProperty("type").GetString());
            Assert.AreEqual("integer", properties.GetProperty("originX").GetProperty("type").GetString());
            Assert.AreEqual("integer", properties.GetProperty("originY").GetProperty("type").GetString());
            Assert.AreEqual("image/png", properties.GetProperty("mimeType").GetProperty("const").GetString());
            Assert.AreEqual("array", properties.GetProperty("views").GetProperty("type").GetString());
            Assert.IsFalse(outputSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.AreSequenceEqual(
                new[] { "height", "mimeType", "originX", "originY", "views", "width" },
                required);
            StringAssert.Contains(
                properties.GetProperty("width").GetProperty("description").GetString(),
                "overview");
            StringAssert.Contains(
                properties.GetProperty("originX").GetProperty("description").GetString(),
                "Informational only");
        }

        [TestMethod(DisplayName = "Verify that ConvertScreenshotPoint accepts only an image number and raw point")]
        public void FindToolsPublishesRecorderOwnedPointConversionSchemaTest()
        {
            // Arrange: inspect the fixed recorder catalog through its normal MCP dispatcher.
            var subject = new RecorderMcpRepository(new StubRecorderRepository());

            // Act: read the screenshot-point converter definition.
            var result = (McpToolsListResultModel)subject.FindTools(id: 31).Result;
            var converter = result.Tools.Single(tool => tool.Name == "g4.ConvertScreenshotPoint");
            var inputSchema = converter.InputSchema;
            var propertyNames = inputSchema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var requiredNames = inputSchema.GetProperty("required")
                .EnumerateArray()
                .Select(item => item.GetString())
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // Assert: dimensions, origins, scaling, and DPI information cannot be supplied by the caller.
            Assert.AreSequenceEqual(new[] { "imageNumber", "x", "y" }, propertyNames);
            Assert.AreSequenceEqual(new[] { "imageNumber", "x", "y" }, requiredNames);
            Assert.IsFalse(inputSchema.GetProperty("additionalProperties").GetBoolean());
        }

        [TestMethod(DisplayName = "Verify that ResolveGroundedElement publishes its structured output schema")]
        public void FindToolsPublishesGroundingOutputSchemaTest()
        {
            // Arrange: create the MCP dispatcher over an operating-system-independent recorder stub.
            var subject = new RecorderMcpRepository(new StubRecorderRepository());

            // Act: inspect the advertised grounding tool definition.
            var result = (McpToolsListResultModel)subject.FindTools(id: 4).Result;
            var groundingTool = result.Tools.Single(tool => tool.Name == "g4.ResolveGroundedElement");
            var outputSchema = groundingTool.OutputSchema.Value;
            var properties = outputSchema.GetProperty("properties");

            // Assert: the reusable locator, ancestry, physical point, and optional offset are declared.
            Assert.AreEqual("object", outputSchema.GetProperty("type").GetString());
            Assert.AreEqual("string", properties.GetProperty("locator").GetProperty("type").GetString());
            Assert.AreEqual("array", properties.GetProperty("path").GetProperty("type").GetString());
            Assert.AreEqual("#/$defs/point", properties.GetProperty("point").GetProperty("$ref").GetString());
            Assert.AreEqual("#/$defs/offset", properties.GetProperty("mouseOffset").GetProperty("$ref").GetString());
            Assert.IsFalse(outputSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.IsFalse(
                outputSchema.GetProperty("$defs").GetProperty("node").GetProperty("properties")
                    .TryGetProperty("patterns", out _));
            Assert.IsFalse(outputSchema.GetProperty("$defs").TryGetProperty("pattern", out _));
        }

        [TestMethod(DisplayName = "Verify that UIA provider calls use bounded native timeouts")]
        public void CreateAutomationClientAppliesBoundedTimeoutsTest()
        {
            // Act: create the same native client used by focus and coordinate chain resolution.
            var automation = UiaRecorderRepository.CreateAutomationClient();
            var settings = (IUIAutomation2)automation;

            // Assert: a faulty provider cannot retain a recorder request for the UIA platform defaults.
            Assert.AreEqual(
                UiaRecorderRepository.UiaConnectionTimeoutMilliseconds,
                settings.ConnectionTimeout);
            Assert.AreEqual(
                UiaRecorderRepository.UiaTransactionTimeoutMilliseconds,
                settings.TransactionTimeout);
        }

        [TestMethod(DisplayName = "Verify that a Peek invocation is rejected as an unknown MCP tool")]
        public void CallToolRejectsPeekTest()
        {
            // Arrange: prepare a tools/call payload for the removed public tool.
            var recorderRepository = new StubRecorderRepository();
            var subject = new RecorderMcpRepository(recorderRepository);
            using var parametersDocument = JsonDocument.Parse("{\"name\":\"g4.Peek\",\"arguments\":{}}");

            // Act: dispatch the removed tool name through the normal MCP entry point.
            var response = subject.CallTool(parametersDocument.RootElement, id: 2);

            // Assert: callers receive the standard JSON-RPC unknown-tool error without touching the recorder.
            Assert.IsNull(response.Result);
            Assert.IsNotNull(response.Error);
            Assert.AreEqual(-32601, response.Error.Code);
            Assert.AreEqual(0, recorderRepository.ResolveGroundedElementCallCount);
        }

        [TestMethod(DisplayName = "Verify that grounded resolution is dispatched as one atomic repository call")]
        public void CallToolDispatchesAtomicGroundingTest()
        {
            // Arrange: prepare one grounded-resolution request at a known physical coordinate.
            var recorderRepository = new StubRecorderRepository();
            var subject = new RecorderMcpRepository(recorderRepository);
            using var parametersDocument = JsonDocument.Parse(
                "{\"name\":\"g4.ResolveGroundedElement\",\"arguments\":{\"x\":12,\"y\":34,\"skipOffset\":false}}");

            // Act: dispatch the request through the same MCP call path used by connected agents.
            var response = subject.CallTool(parametersDocument.RootElement, id: 3);

            // Assert: MCP delegates movement and lookup to the single atomic grounding operation.
            Assert.IsNull(response.Error);
            Assert.IsNotNull(response.Result);
            Assert.AreEqual(1, recorderRepository.ResolveGroundedElementCallCount);
            Assert.AreEqual(0, recorderRepository.MovePointerCallCount);
            Assert.AreEqual(12, recorderRepository.LastGroundedX);
            Assert.AreEqual(34, recorderRepository.LastGroundedY);
            Assert.IsFalse(recorderRepository.LastSkipOffset);
        }

        [TestMethod(DisplayName = "Verify that GetScreenshot returns an MCP image block and separate metadata")]
        public void CallToolReturnsScreenshotImageContentTest()
        {
            // Arrange: return a deterministic valid PNG without touching the operating system.
            using var bitmap = new Bitmap(width: 4, height: 2);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var imageBase64 = Convert.ToBase64String(stream.ToArray());
            var recorderRepository = new StubRecorderRepository
            {
                Screenshot = new RecorderScreenshotModel
                {
                    Height = bitmap.Height,
                    ImageBase64 = imageBase64,
                    MimeType = "image/png",
                    OriginX = -1920,
                    OriginY = 0,
                    Width = bitmap.Width
                }
            };
            var subject = new RecorderMcpRepository(recorderRepository);
            using var parametersDocument = JsonDocument.Parse(
                "{\"name\":\"g4.GetScreenshot\",\"arguments\":{\"metricsOnly\":false}}");

            // Act: call the screenshot tool through its public MCP dispatcher.
            var response = subject.CallTool(parametersDocument.RootElement, id: 4);
            var result = (McpToolCallResultModel)response.Result;
            var content = result.Content.ToArray();
            var metadata = (Dictionary<string, object>)result.StructuredContent;

            // Assert: pixels are model-visible image content and are not duplicated in structured metadata.
            Assert.IsNull(response.Error);
            Assert.AreEqual(3, content.Length);
            Assert.AreEqual("text", content[0].Type);
            using (var labelDocument = JsonDocument.Parse(content[0].Text))
            {
                var label = labelDocument.RootElement.GetProperty("screenshotView");
                Assert.AreEqual(1, label.GetProperty("imageNumber").GetInt32());
                Assert.AreEqual("native", label.GetProperty("kind").GetString());
            }
            Assert.AreEqual("image", content[1].Type);
            Assert.AreEqual(imageBase64, content[1].Data);
            Assert.AreEqual("image/png", content[1].MimeType);
            Assert.IsNull(content[1].Text);
            Assert.AreEqual("text", content[2].Type);
            Assert.IsFalse(content[2].Text.Contains(imageBase64, StringComparison.Ordinal));
            Assert.AreEqual(4, metadata["width"]);
            Assert.AreEqual(2, metadata["height"]);
            Assert.AreEqual(-1920, metadata["originX"]);
            Assert.AreEqual(0, metadata["originY"]);
            Assert.AreEqual("image/png", metadata["mimeType"]);
            var views = (Dictionary<string, object>[])metadata["views"];
            Assert.AreEqual(1, views.Length);
            Assert.AreEqual(1, views[0]["imageNumber"]);
            Assert.AreEqual("native", views[0]["kind"]);
            Assert.AreEqual(4, views[0]["width"]);
            Assert.AreEqual(2, views[0]["height"]);
            Assert.IsFalse(metadata.ContainsKey("imageBase64"));
            Assert.IsFalse(metadata.ContainsKey("coordinateTransform"));

            var screenshotDefinition = ((McpToolsListResultModel)subject.FindTools(id: 5).Result)
                .Tools
                .Single(tool => tool.Name == "g4.GetScreenshot");
            var declaredFields = screenshotDefinition.OutputSchema.Value
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var returnedFields = metadata.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

            Assert.AreSequenceEqual(declaredFields, returnedFields);

            // Assert the actual HTTP JSON shape as configured by the UIA host: irrelevant null members disappear,
            // property names are MCP-compatible camel case, and the image payload still occurs exactly once.
            var wireJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            using var wireDocument = JsonDocument.Parse(wireJson);
            var wireResult = wireDocument.RootElement.GetProperty("result");
            var viewLabel = wireResult.GetProperty("content")[0];
            var imageContent = wireResult.GetProperty("content")[1];
            var structuredContent = wireResult.GetProperty("structuredContent");
            using var wireLabelDocument = JsonDocument.Parse(viewLabel.GetProperty("text").GetString());

            Assert.AreEqual("text", viewLabel.GetProperty("type").GetString());
            Assert.AreEqual(
                1,
                wireLabelDocument.RootElement
                    .GetProperty("screenshotView").GetProperty("imageNumber").GetInt32());
            Assert.AreEqual("image", imageContent.GetProperty("type").GetString());
            Assert.AreEqual(imageBase64, imageContent.GetProperty("data").GetString());
            Assert.AreEqual("image/png", imageContent.GetProperty("mimeType").GetString());
            Assert.IsFalse(imageContent.TryGetProperty("text", out _));
            Assert.IsFalse(structuredContent.TryGetProperty("imageBase64", out _));
            Assert.AreEqual(1, wireJson.Split(imageBase64, StringSplitOptions.None).Length - 1);
        }

        [TestMethod(DisplayName = "Verify that screenshot metadata dimensions match the returned PNG")]
        public void CallToolReturnsMatchingScreenshotDimensionsTest()
        {
            // Arrange: build a real PNG with deliberately non-square dimensions.
            using var bitmap = new Bitmap(width: 7, height: 3);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var recorderRepository = new StubRecorderRepository
            {
                Screenshot = new RecorderScreenshotModel
                {
                    Height = bitmap.Height,
                    ImageBase64 = Convert.ToBase64String(stream.ToArray()),
                    MimeType = "image/png",
                    OriginX = -10,
                    OriginY = 20,
                    Width = bitmap.Width
                }
            };
            var subject = new RecorderMcpRepository(recorderRepository);
            using var parametersDocument = JsonDocument.Parse(
                "{\"name\":\"g4.GetScreenshot\",\"arguments\":{\"metricsOnly\":false}}");

            // Act: dispatch through MCP and decode exactly the image block the model receives.
            var response = subject.CallTool(parametersDocument.RootElement, id: 6);
            var result = (McpToolCallResultModel)response.Result;
            var imageContent = result.Content.Single(item => item.Type == "image");
            var metadata = (Dictionary<string, object>)result.StructuredContent;
            using var decodedStream = new MemoryStream(Convert.FromBase64String(imageContent.Data));
            using var decodedImage = Image.FromStream(decodedStream);

            // Assert: the MCP metadata describes the original payload, not a client-rendered preview.
            Assert.AreEqual(decodedImage.Width, metadata["width"]);
            Assert.AreEqual(decodedImage.Height, metadata["height"]);
            Assert.AreEqual(7, decodedImage.Width);
            Assert.AreEqual(3, decodedImage.Height);
        }

        [TestMethod(DisplayName = "Verify that screenshot conversion uses current recorder metrics")]
        public void CallToolConvertsScreenshotPointUsingRecorderMetricsTest()
        {
            // Arrange: use the same 3000x1920 desktop shape observed in the multi-monitor grounding failure.
            using var bitmap = new Bitmap(width: 3000, height: 1920);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var recorderRepository = new StubRecorderRepository
            {
                Screenshot = new RecorderScreenshotModel
                {
                    Height = bitmap.Height,
                    ImageBase64 = Convert.ToBase64String(stream.ToArray()),
                    MimeType = "image/png",
                    OriginX = -1080,
                    OriginY = 0,
                    Width = bitmap.Width
                }
            };
            var screenshotSubject = new RecorderMcpRepository(recorderRepository);
            using var screenshotParameters = JsonDocument.Parse(
                "{\"name\":\"g4.GetScreenshot\",\"arguments\":{\"metricsOnly\":false}}");

            // Act: capture the overview and native detail views, then convert a point on the last detail view
            // while supplying no dimensions or transform.
            var screenshotResponse = screenshotSubject.CallTool(screenshotParameters.RootElement, id: 7);
            var screenshotResult = (McpToolCallResultModel)screenshotResponse.Result;
            var screenshotMetadata = (Dictionary<string, object>)screenshotResult.StructuredContent;
            var screenshotViews = (Dictionary<string, object>[])screenshotMetadata["views"];
            var imageContent = screenshotResult.Content.Where(item => item.Type == "image").ToArray();
            var conversionRequest = JsonSerializer.Serialize(new
            {
                name = "g4.ConvertScreenshotPoint",
                arguments = new { imageNumber = 7, x = 999, y = 919 }
            });
            using var conversionParameters = JsonDocument.Parse(conversionRequest);
            var conversionResponse = screenshotSubject.CallTool(conversionParameters.RootElement, id: 8);
            var conversionResult = (McpToolCallResultModel)conversionResponse.Result;
            var conversion = (Dictionary<string, object>)conversionResult.StructuredContent;
            var sourcePixel = (Dictionary<string, object>)conversion["sourcePixel"];
            var physicalPixel = (Dictionary<string, object>)conversion["physicalDesktopPixel"];

            // Assert: one overview is followed by six native-resolution row-major detail views. The final pixel
            // of the final detail view maps exactly to the final source and physical desktop pixel.
            Assert.AreEqual(1000, screenshotMetadata["width"]);
            Assert.AreEqual(640, screenshotMetadata["height"]);
            Assert.AreEqual(7, screenshotViews.Length);
            Assert.AreEqual(7, imageContent.Length);
            for (var index = 0; index < screenshotViews.Length; index++)
            {
                var labelContent = screenshotResult.Content.ElementAt(index * 2);
                var pairedImageContent = screenshotResult.Content.ElementAt(index * 2 + 1);
                using var labelDocument = JsonDocument.Parse(labelContent.Text);

                Assert.AreEqual("text", labelContent.Type);
                Assert.AreEqual(
                    index + 1,
                    labelDocument.RootElement.GetProperty("screenshotView")
                        .GetProperty("imageNumber").GetInt32());
                Assert.AreEqual("image", pairedImageContent.Type);
                Assert.AreEqual(index + 1, screenshotViews[index]["imageNumber"]);
            }
            Assert.AreEqual("overview", screenshotViews[0]["kind"]);
            Assert.AreEqual("detail", screenshotViews[1]["kind"]);
            Assert.AreEqual(0, screenshotViews[1]["column"]);
            Assert.AreEqual(0, screenshotViews[1]["row"]);
            Assert.AreEqual(1000, screenshotViews[1]["width"]);
            Assert.AreEqual(1000, screenshotViews[1]["height"]);
            Assert.AreEqual(2, screenshotViews[6]["column"]);
            Assert.AreEqual(1, screenshotViews[6]["row"]);
            Assert.AreEqual(1000, screenshotViews[6]["width"]);
            Assert.AreEqual(920, screenshotViews[6]["height"]);
            Assert.AreEqual(2999, sourcePixel["x"]);
            Assert.AreEqual(1919, sourcePixel["y"]);
            Assert.AreEqual(1919, physicalPixel["x"]);
            Assert.AreEqual(1919, physicalPixel["y"]);
            Assert.AreEqual("physical-desktop-device-pixels", conversion["coordinateSpace"]);
            Assert.IsTrue(recorderRepository.LastScreenshotMetricsOnly);
        }

        [TestMethod(DisplayName = "Verify that the fourth presented image maps to image number four")]
        public void CallToolMapsPresentedImageNumberWithoutOrdinalDriftTest()
        {
            // Arrange: reproduce the desktop geometry and local pixel from the multi-image grounding failure.
            var subject = new RecorderMcpRepository(new StubRecorderRepository
            {
                Screenshot = new RecorderScreenshotModel
                {
                    Height = 1920,
                    MimeType = "image/png",
                    OriginX = -1080,
                    OriginY = 0,
                    Width = 3000
                }
            });
            using var parameters = JsonDocument.Parse(
                "{\"name\":\"g4.ConvertScreenshotPoint\",\"arguments\":{\"imageNumber\":4,\"x\":120,\"y\":261}}");

            // Act: convert the point selected on the fourth image presented to the model.
            var response = subject.CallTool(parameters.RootElement, id: 9);
            var result = (McpToolCallResultModel)response.Result;
            var conversion = (Dictionary<string, object>)result.StructuredContent;
            var sourcePixel = (Dictionary<string, object>)conversion["sourcePixel"];
            var physicalPixel = (Dictionary<string, object>)conversion["physicalDesktopPixel"];

            // Assert: image four is the top-right detail image, not the fifth image on the lower-left monitor.
            Assert.AreEqual(2120, sourcePixel["x"]);
            Assert.AreEqual(261, sourcePixel["y"]);
            Assert.AreEqual(1040, physicalPixel["x"]);
            Assert.AreEqual(261, physicalPixel["y"]);
        }

        [TestMethod(DisplayName = "Verify that screenshot conversion rejects an unknown image number")]
        public void CallToolRejectsUnknownScreenshotImageNumberTest()
        {
            // Arrange: expose metrics that deterministically produce image numbers 1-7.
            var subject = new RecorderMcpRepository(new StubRecorderRepository
            {
                Screenshot = new RecorderScreenshotModel
                {
                    Height = 1920,
                    MimeType = "image/png",
                    OriginX = -1080,
                    OriginY = 0,
                    Width = 3000
                }
            });
            using var parameters = JsonDocument.Parse(
                "{\"name\":\"g4.ConvertScreenshotPoint\",\"arguments\":{\"imageNumber\":8,\"x\":0,\"y\":0}}");

            // Act and assert: the converter cannot silently reinterpret a view selected from another layout.
            try
            {
                subject.CallTool(parameters.RootElement, id: 9);
                Assert.Fail("Expected an unknown screenshot image number to be rejected.");
            }
            catch (ArgumentOutOfRangeException exception)
            {
                StringAssert.Contains(exception.Message, "Image number 8");
            }
        }

        [TestMethod(DisplayName = "Verify that metrics-only GetScreenshot omits image content")]
        public void CallToolReturnsScreenshotMetricsOnlyTest()
        {
            // Arrange: the repository represents a metrics-only capture with no image payload.
            var recorderRepository = new StubRecorderRepository
            {
                Screenshot = new RecorderScreenshotModel
                {
                    Height = 1080,
                    MimeType = "image/png",
                    OriginX = 0,
                    OriginY = 0,
                    Width = 1920
                }
            };
            var subject = new RecorderMcpRepository(recorderRepository);
            using var parametersDocument = JsonDocument.Parse(
                "{\"name\":\"g4.GetScreenshot\",\"arguments\":{\"metricsOnly\":true}}");

            // Act: request only coordinate-space metrics.
            var response = subject.CallTool(parametersDocument.RootElement, id: 5);
            var result = (McpToolCallResultModel)response.Result;
            var content = result.Content.ToArray();

            // Assert: no synthetic or empty image block is emitted.
            Assert.IsNull(response.Error);
            Assert.AreEqual(1, content.Length);
            Assert.AreEqual("text", content[0].Type);
            Assert.IsTrue(content.All(item => item.Type != "image"));
        }

        private sealed class StubRecorderRepository : IUiaRecorderRepository
        {
            public RecorderScreenshotModel Screenshot { get; set; } = new RecorderScreenshotModel();

            public int LastGroundedX { get; private set; }

            public int LastGroundedY { get; private set; }

            public bool LastSkipOffset { get; private set; }

            public int MovePointerCallCount { get; private set; }

            public int ResolveGroundedElementCallCount { get; private set; }

            public bool LastScreenshotMetricsOnly { get; private set; }

            public UiaChainModel GetElementChain()
            {
                return new UiaChainModel();
            }

            public UiaChainModel GetElementChain(int x, int y)
            {
                return new UiaChainModel();
            }

            public RecorderScreenshotModel GetScreenshot(bool metricsOnly)
            {
                LastScreenshotMetricsOnly = metricsOnly;

                return Screenshot;
            }

            public RecorderPointModel MovePointer(int x, int y)
            {
                MovePointerCallCount++;

                return new RecorderPointModel { XPos = x, YPos = y };
            }

            public UiaChainModel ResolveGroundedElement(int x, int y, bool skipOffset)
            {
                ResolveGroundedElementCallCount++;
                LastGroundedX = x;
                LastGroundedY = y;
                LastSkipOffset = skipOffset;

                return new UiaChainModel();
            }

            public RecorderWindowFocusModel SetWindowFocus(string windowTitle, string processName)
            {
                return new RecorderWindowFocusModel();
            }
        }
    }
}
