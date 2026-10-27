using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain.UnitTests.Models
{
    [TestClass]
    [OSCondition(OperatingSystems.Windows)]
    [TestCategory(nameof(SapRecordingMessageModel))]
    [TestCategory("UnitTest")]
    public sealed class SapRecordingMessageModelTests
    {
        // Lists the event members every recorder serializes, in declaration order.
        private static readonly string[] ExpectedEventMembers = ["chain", "event", "machineName", "timestamp", "type", "value"];

        // Lists the keyboard value members in the UIA recorder's order.
        private static readonly string[] ExpectedKeyboardMembers = ["scanCode", "virtualKey", "key", "modifiers", "targetSource"];

        // Mirrors the hub's payload options: camelCase names, nulls omitted.
        private static readonly JsonSerializerOptions HubOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        [TestMethod(DisplayName = "Verify that a message with one stream omits the other stream's key")]
        public void SerializeSingleStreamTest()
        {
            // Arrange: a message with only a user32 event.
            var message = new SapRecordingMessageModel { User32 = NewKeyboardEvent() };

            // Act: serialize it as the hub does.
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(message, HubOptions));

            // Assert: user32 is present and nativeSap is absent.
            Assert.IsTrue(document.RootElement.TryGetProperty("user32", out _));
            Assert.IsFalse(document.RootElement.TryGetProperty("nativeSap", out _));
        }

        [TestMethod(DisplayName = "Verify that a SAP event serializes the shared event members and the UIA keyboard value")]
        public void SerializeEventContractTest()
        {
            // Arrange: a keyboard event.
            var recordingEvent = NewKeyboardEvent();

            // Act: serialize it as the hub does.
            using var document = JsonDocument.Parse(JsonSerializer.Serialize<object>(recordingEvent, HubOptions));

            // Assert: the shared members, and the keyboard value in the UIA order.
            var members = document.RootElement.EnumerateObject().Select(i => i.Name);
            var valueMembers = document.RootElement.GetProperty("value").EnumerateObject().Select(i => i.Name);
            Assert.AreSequenceEqual(ExpectedEventMembers, members);
            Assert.AreSequenceEqual(ExpectedKeyboardMembers, valueMembers);
        }

        // Creates a keyboard event with every member set.
        private static SapEventModel NewKeyboardEvent()
        {
            return new SapEventModel
            {
                Chain = new SapChainModel(),
                Event = "Key Down",
                Timestamp = 1000,
                Type = "Keyboard",
                Value = new SapKeyboardValueModel { Key = "a", ScanCode = 30, TargetSource = "CapturedSnapshot", VirtualKey = 0x41 }
            };
        }
    }
}
