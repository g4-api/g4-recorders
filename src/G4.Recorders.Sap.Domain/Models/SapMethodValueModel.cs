namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Carries the value of a <c>nativeSap</c> <c>InvokeSapMethod</c> event: a scripting method call.
    /// </summary>
    /// <remarks>
    /// The members match the body of the SAP driver's <c>sap/element/{e}/call</c> route, so a recorded call replays
    /// as one request. Arguments are redacted to <c>***</c> on password fields.
    /// </remarks>
    public class SapMethodValueModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the SAP method name, such as <c>pressToolbarButton</c>.
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// Gets or sets the method arguments, in order.
        /// </summary>
        public object[] Arguments { get; set; } = [];
        #endregion
    }
}
