using G4.Recorders.Sap.Domain.Models;

namespace G4.Recorders.Sap.Domain
{
    /// <summary>
    /// Resolves the recorder's values from a request, the environment, and <c>appsettings.json</c>, in that order.
    /// </summary>
    public interface ISapRecorderSettingsResolver
    {
        #region *** Methods      ***
        /// <summary>
        /// Resolves every recorder value, field by field.
        /// </summary>
        /// <param name="request">The client's values, or <c>null</c> to resolve from the environment and settings only.</param>
        /// <returns>The resolved values, with the source of each one.</returns>
        /// <exception cref="System.ArgumentException">A stream name is unknown, or no stream is enabled.</exception>
        SapRecorderOptionsModel Resolve(SapRecorderParametersModel request);
        #endregion
    }
}
