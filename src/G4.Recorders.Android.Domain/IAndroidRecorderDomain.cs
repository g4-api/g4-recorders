namespace G4.Recorders.Android.Domain
{
    /// <summary>
    /// Aggregates the Android recorder domain services behind one injectable facade.
    /// </summary>
    /// <remarks>
    /// Controllers and the hub inject this facade and reach each service through its properties, so a new service is
    /// added here once instead of in every consumer's constructor, as on the Chromium recorder.
    /// </remarks>
    public interface IAndroidRecorderDomain
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets the launcher that prepares and restores devices.
        /// </summary>
        IAndroidRecorderLauncher Launcher { get; }

        /// <summary>
        /// Gets the repository that talks to the connected recorder app.
        /// </summary>
        IAndroidRecorderRepository Repository { get; }
        #endregion
    }
}
