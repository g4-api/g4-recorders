namespace G4.Recorders.Sap.Domain.Models
{
    /// <summary>
    /// Limits recording to some SAP sessions; every member that is set must match.
    /// </summary>
    /// <remarks>
    /// Out-of-scope sessions are not subscribed for <c>nativeSap</c>, input on their windows is not sent as
    /// <c>user32</c>, and the status lists them with the reason. Peek requests are not scoped.
    /// </remarks>
    public class SapScopeParametersModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the SAP client to record, such as <c>001</c>; <c>null</c> or empty for any client.
        /// </summary>
        public string Client { get; set; }

        /// <summary>
        /// Gets or sets the absolute session identifiers to record, such as <c>/app/con[0]/ses[0]</c>; empty for any session.
        /// </summary>
        public string[] Sessions { get; set; }

        /// <summary>
        /// Gets or sets the SAP system to record, such as <c>S4H</c>; <c>null</c> or empty for any system.
        /// </summary>
        public string SystemName { get; set; }
        #endregion
    }
}
