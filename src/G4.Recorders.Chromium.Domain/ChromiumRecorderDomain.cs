namespace G4.Recorders.Chromium.Domain
{
    /// <summary>
    /// Default <see cref="IChromiumRecorderDomain"/>. Receives the individual domain services by
    /// constructor injection and exposes them as properties. Registered as a transient so it
    /// can safely hold the singleton launcher and the transient repository together.
    /// </summary>
    public class ChromiumRecorderDomain(
        IChromiumRecorderLauncher launcher,
        IChromiumRecorderRepository repository) : IChromiumRecorderDomain
    {
        #region *** Properties ***
        /// <inheritdoc />
        public IChromiumRecorderLauncher Launcher { get; set; } = launcher;

        /// <inheritdoc />
        public IChromiumRecorderRepository Repository { get; set; } = repository;
        #endregion
    }
}
