using System;

namespace G4.Recorders.Common.Domain.Models
{
    /// <summary>
    /// Represents the ping payload returned by recorder hosts to report health and host runtime details.
    /// </summary>
    public class RecorderPingModel
    {
        #region *** Properties   ***
        /// <summary>
        /// Gets or sets the service status.
        /// </summary>
        public string Status { get; set; } = "OK";

        /// <summary>
        /// Gets or sets the service heartbeat message.
        /// </summary>
        public string Message { get; set; } = "Pong";

        /// <summary>
        /// Gets or sets the local machine name.
        /// </summary>
        public string MachineName { get; set; }

        /// <summary>
        /// Gets or sets the host name reported by the operating system.
        /// </summary>
        public string HostName { get; set; }

        /// <summary>
        /// Gets or sets the preferred IPv4 local endpoint address.
        /// </summary>
        public string IpAddress { get; set; }

        /// <summary>
        /// Gets or sets the operating-system description.
        /// </summary>
        public string OperatingSystem { get; set; }

        /// <summary>
        /// Gets or sets the operating-system architecture.
        /// </summary>
        public string OsArchitecture { get; set; }

        /// <summary>
        /// Gets or sets the current process architecture.
        /// </summary>
        public string ProcessArchitecture { get; set; }

        /// <summary>
        /// Gets or sets the current .NET runtime description.
        /// </summary>
        public string FrameworkDescription { get; set; }

        /// <summary>
        /// Gets or sets the UTC timestamp when the payload was produced.
        /// </summary>
        public DateTime UtcTimestamp { get; set; } = DateTime.UtcNow;
        #endregion
    }
}
