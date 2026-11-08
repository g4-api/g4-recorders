using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Represents one SAP GUI Scripting object within a recorded chain.
    /// </summary>
    /// <remarks>
    /// The node keeps the shared <see cref="RecorderNodeModel{T}"/> contract unchanged, so SAP chains serialize like UIA
    /// and Chromium chains. SAP values map into the shared fields: <c>ControlType</c> holds the SAP type,
    /// <c>ClassName</c> the shell subtype (or the type), <c>AutomationId</c> the SAP name, <c>FrameworkId</c> is
    /// <c>SAPGUI</c>, and <c>Properties</c> carries the scripting identifiers.
    /// </remarks>
    public class SapNodeModel : RecorderNodeModel<object>
    {
        // Intentionally empty: SAP data maps into the shared node fields, keeping one contract across recorders.
    }
}
