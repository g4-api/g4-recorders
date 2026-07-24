using G4.Recorders.Common.Domain.Models;

namespace G4.Recorders.Chromium.Domain.Models
{
    /// <summary>
    /// Represents a single UI Automation (UIA) node within a recorded chain.
    /// Wraps an <see cref="IUIAutomationElement"/> as the underlying UI element.
    /// 
    /// This class is a strongly-typed alias for <see cref="RecorderNodeModel{TElement}"/>,
    /// allowing the recorder pipeline to work specifically with UIA elements.
    /// Extend this class when UIA nodes require additional metadata, properties,
    /// or domain-specific behavior.
    /// </remarks>
    public class ChromiumNodeModel : RecorderNodeModel<object>
    {
        // Intentionally empty — provides a domain-specific node type for UIA recordings.
        // Add UIA-specific fields or logic here if needed in future.
    }
}
