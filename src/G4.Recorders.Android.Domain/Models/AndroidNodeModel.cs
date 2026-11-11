using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Represents one accessibility node of an Android chain.
    /// </summary>
    /// <remarks>
    /// The class adds no members. The host never holds a live Android element, so the ignored element slot is a
    /// plain <see cref="object"/>, as on the Chromium recorder.
    /// </remarks>
    public class AndroidNodeModel : RecorderNodeModel<object>
    {
    }
}
