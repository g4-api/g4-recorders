using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using G4.Recorders.Common.Domain.Models;

using G4.Recorders.Uia.Domain.Models;

namespace G4.Recorders.Uia.Domain.UnitTests.Models
{
    [TestClass]
    [TestCategory(nameof(UiaEventModel))]
    [TestCategory("UnitTest")]
    public sealed class RecorderEventModelTests
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        [TestMethod(DisplayName = "Verify that the event no longer carries a top-level offset property")]
        public void RecorderEventHasNoEventOffsetTest()
        {
            // Arrange: create a bare event; offset now lives on the chain, not the event.
            var recordingEvent = new UiaEventModel();

            // Act: serialize through the same camel-case convention used by the SignalR protocol.
            var json = JsonSerializer.Serialize(recordingEvent, _jsonOptions);
            using var document = JsonDocument.Parse(json);

            // Assert: the removed event-level offset property is absent from the payload.
            Assert.IsFalse(document.RootElement.TryGetProperty("offset", out _));
        }

        [TestMethod(DisplayName = "Verify that the chain serializes its mouse offset under mouseOffset")]
        public void ChainMouseOffsetSerializationTest()
        {
            // Arrange: a chain that carries an element-relative pointer offset.
            var chain = new UiaChainModel
            {
                MouseOffset = new RecorderOffsetModel { X = 12, Y = 34 }
            };

            // Act: serialize through the same camel-case convention used by the SignalR protocol.
            var json = JsonSerializer.Serialize(chain, _jsonOptions);
            using var document = JsonDocument.Parse(json);
            var offset = document.RootElement.GetProperty("mouseOffset");

            // Assert: both axes serialize under the consolidated mouseOffset property.
            Assert.AreEqual(12, offset.GetProperty("x").GetInt32());
            Assert.AreEqual(34, offset.GetProperty("y").GetInt32());
        }
    }
}
