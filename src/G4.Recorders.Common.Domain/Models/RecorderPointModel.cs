using System.Text.Json.Serialization;

namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Represents a screen coordinate point in pixels.
    /// </summary>
    public class RecorderPointModel
    {
        /// <summary>
        /// The horizontal screen coordinate (in pixels).
        /// </summary>
        [JsonPropertyName("X")]
        public int XPos { get; set; }

        /// <summary>
        /// The vertical screen coordinate (in pixels).
        /// </summary>
        [JsonPropertyName("Y")]
        public int YPos { get; set; }
    }
}
