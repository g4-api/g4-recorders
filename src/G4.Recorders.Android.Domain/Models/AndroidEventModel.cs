using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Represents one recording event pushed by the Android recorder app.
    /// </summary>
    /// <remarks>
    /// The class adds no members: it binds the shared <see cref="RecorderEventModel{T}"/> to the Android chain type, so
    /// the hub relays an event with exactly the contract every other recorder broadcasts.
    /// </remarks>
    public class AndroidEventModel : RecorderEventModel<AndroidChainModel>
    {
    }
}
