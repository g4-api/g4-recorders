using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Android.Domain.Models
{
    /// <summary>
    /// Represents the element chain of an Android event, from the trigger node up to the window root.
    /// </summary>
    /// <remarks>
    /// The class adds no members. Android chains run target first, the Chromium orientation, because the G4 Hub
    /// converts Android events with its Chromium rule builder.
    /// </remarks>
    public class AndroidChainModel : ChainModel<AndroidNodeModel>
    {
    }
}
