namespace G4.Recorders.Android.Domain
{
    /// <summary>
    /// Default <see cref="IAndroidRecorderDomain"/>; exposes the injected services as properties.
    /// </summary>
    /// <remarks>
    /// Registered as a transient over the singleton launcher and repository, so every consumer sees the same state.
    /// </remarks>
    /// <param name="launcher">The device launcher.</param>
    /// <param name="repository">The recorder app repository.</param>
    public sealed class AndroidRecorderDomain(
        IAndroidRecorderLauncher launcher,
        IAndroidRecorderRepository repository) : IAndroidRecorderDomain
    {
        #region *** Properties   ***
        /// <inheritdoc />
        public IAndroidRecorderLauncher Launcher { get; } = launcher;

        /// <inheritdoc />
        public IAndroidRecorderRepository Repository { get; } = repository;
        #endregion
    }
}
