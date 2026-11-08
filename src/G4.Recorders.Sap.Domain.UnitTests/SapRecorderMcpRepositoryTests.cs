using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;

using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Common.Domain.Models.Mcp;
using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.UnitTests
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapRecorderMcpRepository))]
    [TestCategory("UnitTest")]
    public sealed class SapRecorderMcpRepositoryTests
    {
        // Lists the tool catalog, which matches the UIA recorder's.
        private static readonly string[] ExpectedTools =
        [
            "g4.ConvertScreenshotPoint",
            "g4.GetScreenshot",
            "g4.MovePointer",
            "g4.ResolveGroundedElement",
            "g4.SetWindowFocus"
        ];

        // Lists the recorder calls of a pointer move followed by a grounding without offset.
        private static readonly string[] ExpectedDispatch = ["MovePointer(10,20)", "ResolveGroundedElement(30,40,True)"];

        [TestMethod(DisplayName = "Verify that the tool catalog matches the UIA recorder's tool names")]
        public void FindToolsTest()
        {
            // Arrange: a repository over a fake recorder.
            var mcp = new SapRecorderMcpRepository(new FakeRecorderRepository());

            // Act: list the tools.
            var result = (McpToolsListResultModel)mcp.FindTools(id: 1).Result;

            // Assert: the same five tools.
            Assert.AreSequenceEqual(ExpectedTools, result.Tools.Select(i => i.Name).Order());
        }

        [TestMethod(DisplayName = "Verify that initialize names the SAP recorder and offers tools")]
        public void InitializeTest()
        {
            // Act: initialize.
            var result = (McpInitializeResultModel)new SapRecorderMcpRepository(new FakeRecorderRepository()).Initialize(id: 1).Result;

            // Assert: the SAP server identity and the tools capability.
            Assert.AreEqual("g4-recorders-sap-mcp", result.ServerInfo.Name);
            Assert.IsNotNull(result.Capabilities.Tools);
        }

        [TestMethod(DisplayName = "Verify that pointer and grounding tools dispatch to the recorder with their arguments")]
        public void CallToolDispatchTest()
        {
            // Arrange: a fake recorder that records its calls.
            var recorder = new FakeRecorderRepository();
            var mcp = new SapRecorderMcpRepository(recorder);

            // Act: move the pointer, then ground with the offset skipped.
            mcp.CallTool(Parse("""{ "name": "g4.MovePointer", "arguments": { "x": 10, "y": 20 } }"""), id: 1);
            var grounded = mcp.CallTool(Parse("""{ "name": "g4.ResolveGroundedElement", "arguments": { "x": 30, "y": 40, "skipOffset": true } }"""), id: 2);

            // Assert: both calls arrived, and the grounded chain is the structured result.
            Assert.AreSequenceEqual(ExpectedDispatch, recorder.Calls);
            Assert.IsInstanceOfType<SapChainModel>(((McpToolCallResultModel)grounded.Result).StructuredContent);
        }

        [TestMethod(DisplayName = "Verify that an unknown tool is a JSON-RPC error, not an exception")]
        public void CallUnknownToolTest()
        {
            // Act: call a tool the catalog does not have.
            var response = new SapRecorderMcpRepository(new FakeRecorderRepository()).CallTool(Parse("""{ "name": "g4.Nope" }"""), id: 1);

            // Assert: method-not-found.
            Assert.AreEqual(-32601, response.Error.Code);
            Assert.IsNull(response.Result);
        }

        [TestMethod(DisplayName = "Verify that a missing required argument names the tool and the argument")]
        public void CallToolMissingArgumentTest()
        {
            // Arrange: a repository over a fake recorder.
            var mcp = new SapRecorderMcpRepository(new FakeRecorderRepository());

            // Act and assert: the error names both.
            var exception = Assert.ThrowsExactly<ArgumentException>(() => mcp.CallTool(Parse("""{ "name": "g4.MovePointer", "arguments": { "x": 10 } }"""), id: 1));
            Assert.Contains("g4.MovePointer", exception.Message);
            Assert.Contains("'y'", exception.Message);
        }

        [TestMethod(DisplayName = "Verify that screenshot points convert from the overview and from detail tiles to physical pixels")]
        #region *** Data Set ***
        [DataRow(1, 999, 499, 1899, 999)]
        [DataRow(3, 5, 5, 905, 5)]
        #endregion
        public void ConvertScreenshotPointTest(int imageNumber, int x, int y, int expectedX, int expectedY)
        {
            // Arrange: a 2000 x 1000 desktop starting at x = -100; image 1 is the overview, image 3 the second tile.
            var recorder = new FakeRecorderRepository { Metrics = new RecorderScreenshotModel { Height = 1000, OriginX = -100, Width = 2000 } };
            var mcp = new SapRecorderMcpRepository(recorder);
            var request = $$"""{ "name": "g4.ConvertScreenshotPoint", "arguments": { "imageNumber": {{imageNumber}}, "x": {{x}}, "y": {{y}} } }""";

            // Act: convert the pixel.
            var result = (Dictionary<string, object>)((McpToolCallResultModel)mcp.CallTool(Parse(request), id: 1).Result).StructuredContent;

            // Assert: the physical desktop pixel.
            var physical = (Dictionary<string, object>)result["physicalDesktopPixel"];
            Assert.AreEqual(expectedX, physical["x"]);
            Assert.AreEqual(expectedY, physical["y"]);
        }

        [TestMethod(DisplayName = "Verify that a screenshot becomes image content, each image preceded by its metadata")]
        public void CallScreenshotTest()
        {
            // Arrange: a fake 2 x 2 capture.
            var recorder = new FakeRecorderRepository { Metrics = NewCapture() };
            var mcp = new SapRecorderMcpRepository(recorder);

            // Act: take the screenshot.
            var result = (McpToolCallResultModel)mcp.CallTool(Parse("""{ "name": "g4.GetScreenshot" }"""), id: 1).Result;
            var content = result.Content.ToList();

            // Assert: one native view - its metadata, its image, then the complete metadata.
            Assert.HasCount(3, content);
            Assert.AreEqual("text", content[0].Type);
            Assert.AreEqual("image", content[1].Type);
            Assert.AreEqual("image/png", content[1].MimeType);
        }

        // Parses a JSON request body.
        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.Clone();
        }

        // Creates a 2 x 2 PNG capture at the desktop origin.
        private static RecorderScreenshotModel NewCapture()
        {
            using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);

            return new RecorderScreenshotModel { Height = 2, ImageBase64 = Convert.ToBase64String(stream.ToArray()), MimeType = "image/png", Width = 2 };
        }

        // Records calls and returns fixed results instead of touching the desktop or SAP GUI.
        private sealed class FakeRecorderRepository : ISapRecorderRepository
        {
            // Gets the recorded calls, such as MovePointer(10,20).
            internal List<string> Calls { get; } = [];

            // Gets or sets the screenshot every capture returns.
            internal RecorderScreenshotModel Metrics { get; set; } = new() { Height = 1, Width = 1 };

            // Returns an empty chain.
            public SapChainModel GetElementChain() => new();

            // Returns an empty chain.
            public SapChainModel GetElementChain(int x, int y) => new();

            // Returns the configured capture.
            public RecorderScreenshotModel GetScreenshot(bool metricsOnly) => Metrics;

            // Records the move and returns the requested point.
            public RecorderPointModel MovePointer(int x, int y)
            {
                Calls.Add($"MovePointer({x},{y})");
                return new RecorderPointModel { XPos = x, YPos = y };
            }

            // Records the grounding and returns an empty chain.
            public SapChainModel ResolveGroundedElement(int x, int y, bool skipOffset)
            {
                Calls.Add($"ResolveGroundedElement({x},{y},{skipOffset})");
                return new SapChainModel();
            }

            // Returns a not-found outcome.
            public RecorderWindowFocusModel SetWindowFocus(string windowTitle, string processName) => new();
        }
    }
}
