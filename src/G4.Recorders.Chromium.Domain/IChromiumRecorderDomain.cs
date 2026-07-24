namespace G4.Recorders.Chromium.Domain
{
    /// <summary>
    /// Aggregates the ChromiumPeek domain services behind a single injectable facade.
    /// Consumers (controllers, hubs) inject this domain and reach the individual services
    /// through its properties, so a new dependency is added once here rather than in every
    /// consumer's constructor.
    /// </summary>
    public interface IChromiumRecorderDomain
    {
        #region *** Properties ***
        /// <summary>
        /// Gets or sets the launcher used to start and stop peek browsers.
        /// </summary>
        IChromiumRecorderLauncher Launcher { get; set; }

        /// <summary>
        /// Gets or sets the repository used to peek UI Automation elements.
        /// </summary>
        IChromiumRecorderRepository Repository { get; set; }
        #endregion
    }
}
