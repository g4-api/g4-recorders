using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Represents one recorded SAP event, either user input or a SAP action.
    /// </summary>
    /// <remarks>
    /// The event keeps the shared <see cref="RecorderEventModel{T}"/> contract unchanged. User input uses the UIA
    /// vocabulary (<c>Mouse</c>/<c>Keyboard</c>, <c>Left Down</c>, <c>Key Up</c>); SAP actions use the Chromium style,
    /// where <c>Event</c> names the action, such as <c>SendKeys</c> or <c>InvokeClick</c>.
    /// </remarks>
    public class SapEventModel : RecorderEventModel<SapChainModel>
    {
        // Intentionally empty: SAP events add no members, keeping one contract across recorders.
    }
}
