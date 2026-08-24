using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Common.Domain.Models.Mcp;

using G4.Recorders.Uia.Domain.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using UIAutomationClient;

using System;
using System.Collections.Generic;
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

            // Assert: the atomic grounding tool replaces Peek in the four-tool public contract.
            string[] expectedToolNames =
            [
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
            Assert.IsFalse(outputSchema.GetProperty("additionalProperties").GetBoolean());
            Assert.AreSequenceEqual(
                new[] { "height", "mimeType", "originX", "originY", "width" },
                required);
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
            // Arrange: return a deterministic capture without touching the operating system.
            var recorderRepository = new StubRecorderRepository
            {
                Screenshot = new RecorderScreenshotModel
                {
                    Height = 1080,
                    ImageBase64 = "AQIDBA==",
                    MimeType = "image/png",
                    OriginX = -1920,
                    OriginY = 0,
                    Width = 3840
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
            Assert.AreEqual(2, content.Length);
            Assert.AreEqual("image", content[0].Type);
            Assert.AreEqual("AQIDBA==", content[0].Data);
            Assert.AreEqual("image/png", content[0].MimeType);
            Assert.IsNull(content[0].Text);
            Assert.AreEqual("text", content[1].Type);
            Assert.IsFalse(content[1].Text.Contains("AQIDBA==", StringComparison.Ordinal));
            Assert.AreEqual(3840, metadata["width"]);
            Assert.AreEqual(1080, metadata["height"]);
            Assert.AreEqual(-1920, metadata["originX"]);
            Assert.AreEqual(0, metadata["originY"]);
            Assert.AreEqual("image/png", metadata["mimeType"]);
            Assert.IsFalse(metadata.ContainsKey("imageBase64"));

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
            var imageContent = wireResult.GetProperty("content")[0];
            var structuredContent = wireResult.GetProperty("structuredContent");

            Assert.AreEqual("image", imageContent.GetProperty("type").GetString());
            Assert.AreEqual("AQIDBA==", imageContent.GetProperty("data").GetString());
            Assert.AreEqual("image/png", imageContent.GetProperty("mimeType").GetString());
            Assert.IsFalse(imageContent.TryGetProperty("text", out _));
            Assert.IsFalse(structuredContent.TryGetProperty("imageBase64", out _));
            Assert.AreEqual(1, wireJson.Split("AQIDBA==", StringSplitOptions.None).Length - 1);
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
