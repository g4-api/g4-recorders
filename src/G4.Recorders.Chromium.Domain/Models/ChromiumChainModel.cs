using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Represents a chain of UIA (UI Automation) nodes recorded by UiaPeek.
    /// Uses <see cref="ChromiumNodeModel"/> as the node type.
    ///
    /// This class acts as a strongly-typed alias for <see cref="ChainModel{TNode}"/>,
    /// providing clearer intent within the UIA domain. Extend this class when
    /// UIA-specific chain metadata or behavior is required.
    /// </summary>
    public class ChromiumChainModel : ChainModel<ChromiumNodeModel>
    {
        // Intentionally empty — provides a domain-specific type for UIA chains.
        // Add members here if UIA chains require additional metadata or logic.
    }
}
