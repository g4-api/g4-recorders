using G4.Recorders.Chromium.Domain.Models;

namespace G4.Recorders.Chromium.Domain
{
    /// <summary>
    /// Represents a repository for accessing UI Automation elements and their ancestor chains.
    /// </summary>
    public class ChromiumRecorderRepository : IChromiumRecorderRepository
    {
        public ChromiumChainModel Peek()
        {
            throw new System.NotImplementedException();
        }

        public ChromiumChainModel Peek(int x, int y)
        {
            throw new System.NotImplementedException();
        }
    }
}
