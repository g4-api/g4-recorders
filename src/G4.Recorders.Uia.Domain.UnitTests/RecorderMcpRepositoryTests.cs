using G4.Recorders.Common.Domain.Models;
using G4.Recorders.Common.Domain.Models.Mcp;

using G4.Recorders.Uia.Domain.Models;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;
using System.Text.Json;

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

        private sealed class StubRecorderRepository : IUiaRecorderRepository
        {
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
                return new RecorderScreenshotModel();
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
