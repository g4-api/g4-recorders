using G4.Recorders.Common.Domain.Models;

using G4.Recorders.Uia.Domain.Extensions;
using G4.Recorders.Uia.Domain.Models;

using UIAutomationClient;

namespace G4.Recorders.Uia.Domain
{
    /// <inheritdoc />
    public class UiaRecorderRepository : IUiaRecorderRepository
    {
        /// <inheritdoc />
        public UiaChainModel Peek(int x, int y)
        {
            // Initialize the UI Automation engine.
            var automation = new CUIAutomation8();

            // Get the element directly under the given screen coordinates.
            var element = automation.ElementFromPoint(pt: new tagPOINT { x = x, y = y });

            // If an element was found, build and return its ancestor chain; otherwise return null.
            var chain = automation.NewAncestorChain(element) ?? new UiaChainModel();

            // Set the point information in the chain model if it exists.
            chain.Point = new RecorderPointModel { XPos = x, YPos = y };

            // Keep the canonical absolute path as the executable locator because removing ancestors can
            // reintroduce ambiguity when identical branches exist elsewhere below the same window.
            chain.FallbackLocator = chain.ResolveLocator();
            chain.Locator = chain.FallbackLocator;

            // Indicate that this chain was triggered by a hover action.
            chain.Trigger = "Hover";

            // Return the constructed ancestor chain.
            return chain;
        }

        /// <inheritdoc />
        public UiaChainModel Peek()
        {
            // Create a new instance of the UI Automation engine.
            var automation = new CUIAutomation8();

            // Get the element that currently has keyboard focus.
            var element = automation.GetFocusedElement();

            // Build the ancestor chain for the focused element,
            // or return a new empty model if no element was found.
            var chain = automation.NewAncestorChain(element) ?? new UiaChainModel();

            // Keep the canonical absolute path as the executable locator because removing ancestors can
            // reintroduce ambiguity when identical branches exist elsewhere below the same window.
            chain.FallbackLocator = chain.ResolveLocator();
            chain.Locator = chain.FallbackLocator;

            // Indicate that this chain was triggered by a focus action.
            chain.Trigger = "Focus";

            // Return the ancestor chain model.
            return chain;
        }
    }
}
