using System.Text.Json.Serialization;

namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Identifies how a recorder resolves the element targeted by a peek request.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecorderPeekMode
    {
        /// <summary>
        /// Resolves an element at explicit recorder-specific coordinates.
        /// </summary>
        Coordinates,

        /// <summary>
        /// Resolves the element that currently owns input focus.
        /// </summary>
        Focused,

        /// <summary>
        /// Resolves the element at the recorder's latest current pointer position.
        /// </summary>
        Current
    }
}
