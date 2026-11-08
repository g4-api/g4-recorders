using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Represents a chain of SAP GUI Scripting nodes, from the top window down to the trigger control.
    /// </summary>
    /// <remarks>
    /// The chain keeps the shared <see cref="ChainModel{T}"/> contract unchanged. The path runs root-first, like UIA
    /// chains, and the locator is an XPath on the trigger's absolute scripting identifier, which the SAP driver
    /// resolves through <c>findById</c>.
    /// </remarks>
    public class SapChainModel : ChainModel<SapNodeModel>
    {
        // Intentionally empty: SAP chains add no members, keeping one contract across recorders.
    }
}
