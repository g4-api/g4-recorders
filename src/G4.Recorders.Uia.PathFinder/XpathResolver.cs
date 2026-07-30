using G4.Recorders.Uia.Domain;

using UIAutomationClient;

namespace G4.Recorders.Uia.PathFinder
{
    /// <summary>
    /// Resolves the recorder's XPath-like UIA syntax one hierarchy segment at a time.
    /// </summary>
    internal static class XpathResolver
    {
        /// <summary>
        /// Resolves an XPath from the supplied UIA root.
        /// </summary>
        /// <param name="rootElement">The element from which resolution begins.</param>
        /// <param name="xpath">The XPath-like locator to resolve.</param>
        /// <returns>The resolved element, or <c>null</c> when any segment cannot be resolved.</returns>
        public static IUIAutomationElement Resolve(IUIAutomationElement rootElement, string xpath)
        {
            var hierarchy = UiaXpath.GetHierarchy(xpath);
            var outputElement = rootElement;

            foreach (var pathSegment in hierarchy)
            {
                outputElement = FindElement(outputElement, pathSegment);
                if (outputElement == null)
                {
                    return null;
                }
            }

            return outputElement;
        }

        // Finds one segment, applying a position to the exact condition result when supplied.
        private static IUIAutomationElement FindElement(
            IUIAutomationElement rootElement,
            string pathSegment)
        {
            if (rootElement == null)
            {
                return null;
            }

            var condition = XpathParser.ConvertToCondition(pathSegment);
            if (condition == null)
            {
                return null;
            }

            var scope = pathSegment.StartsWith('/')
                ? TreeScope.TreeScope_Descendants
                : TreeScope.TreeScope_Children;
            var position = UiaXpath.GetPositionSelection(
                pathSegment: pathSegment,
                matchCount: int.MaxValue
            );

            if (!position.HasPosition)
            {
                return rootElement.FindFirst(scope, condition);
            }

            if (position.Index < 0)
            {
                return null;
            }

            var elements = rootElement.FindAll(scope, condition);
            position = UiaXpath.GetPositionSelection(
                pathSegment: pathSegment,
                matchCount: elements?.Length ?? 0
            );

            return position.Index < 0
                ? null
                : elements.GetElement(position.Index);
        }
    }
}
