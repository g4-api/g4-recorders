using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace G4.Recorders.Common.Domain.Extensions
{
    /// <summary>
    /// Provides utility methods for controllers in the G4 application.
    /// </summary>
    public static class ControllerUtilities
    {
        /// <summary>
        /// Writes the G4 ChromiumRecorder ASCII logo to the console, including the specified version number.
        /// </summary>
        /// <param name="version">The version number to display in the logo.</param>
        public static void WriteChromiumAsciiLogo(string version)
        {
            var logo = new string[]
            {
                "   ____ _                         _                 ____           _            ",
                "  / ___| |__  _ __ ___  _ __ ___ (_)_   _ _ __ ___ |  _ \\ ___  ___| | __       ",
                " | |   | '_ \\| '__/ _ \\| '_ ` _ \\| | | | | '_ ` _ \\| |_) / _ \\/ _ \\ |/ /  ",
                " | |___| | | | | | (_) | | | | | | | |_| | | | | | |  __/  __/  __/   <         ",
                "  \\____|_| |_|_|  \\___/|_| |_| |_|_|\\__,_|_| |_| |_|_|   \\___|\\___|_|\\_\\ ",
                "                                                                                ",
                "                                                 G4 - Chromium Recorder        ",
                "                                                                                ",
                "  Version: " + version + "                                                      ",
                "  Project: https://github.com/g4-api/uia-peek                                   ",
                "                                                                                "
             };

            Console.Clear();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(string.Join("\n", logo));
        }

        /// <summary>
        /// Writes the G4 UiaRecorder ASCII logo to the console, including the specified version number.
        /// </summary>
        /// <param name="version">The version number to display in the logo.</param>
        public static void WriteUiaAsciiLogo(string version)
        {
            var logo = new string[]
            {
                "  _   _ _       ____           _              ",
                " | | | (_) __ _|  _ \\ ___  ___| | __         ",
                " | | | | |/ _` | |_) / _ \\/ _ \\ |/ /        ",
                " | |_| | | (_| |  __/  __/  __/   <           ",
                "  \\___/|_|\\__,_|_|   \\___|\\___|_|\\_\\    ",
                "                                              ",
                "            G4 - UIA Recorder                ",
                "            Powered by IUIAutomation          ",
                "                                              ",
                "  Version: " + version + "                    ",
                "  Project: https://github.com/g4-api/uia-peek ",
                "                                              "
             };

            Console.Clear();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(string.Join("\n", logo));
        }

        /// <summary>
        /// Retrieves the local endpoint's IP address.
        /// </summary>
        /// <returns>The IP address of the local endpoint.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the local endpoint IP address is not found.</exception>
        public static string GetLocalEndpoint()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

                if (ip != null && !string.IsNullOrEmpty(ip.ToString()))
                {
                    return ip.ToString();
                }

                throw new KeyNotFoundException("No valid IP address found.");
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
