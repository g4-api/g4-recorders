using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Uia.Domain.Models
{
    /// <summary>
    /// Represents a UI Automation (UIA) event captured by the UiaPeek tool.
    /// Inherits from <see cref="RecorderEventModel{TChain}"/> using <see cref="UiaChainModel"/> 
    /// as the event chain type.
    /// 
    /// This model serves as the strongly-typed event payload used throughout the 
    /// recorder pipeline. It provides structure, metadata, and a chain of actions 
    /// that describe the recorded UI interaction sequence.
    /// 
    /// The class does not add new members — all functionality is inherited — 
    /// but it allows the domain layer to work with UIA-specific event chains
    /// without needing to reference the generic base everywhere.
    /// </summary>
    public class UiaEventModel : RecorderEventModel<UiaChainModel>
    {
        // Intentionally empty — this class acts as a domain-specific alias 
        // so consumers can work with a UiaEventModel rather than a generic type.
        // Extend here in the future if UIA events require custom fields or behavior.
    }
}
