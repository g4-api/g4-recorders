using G4.Recorders.Common.Domain.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

using G4.Recorders.Uia.Domain.Models;

using UIAutomationClient;

namespace G4.Recorders.Uia.Domain.Extensions
{
    /// <summary>
    /// Local extension methods for UI Automation.
    /// </summary>
    internal static class LocalExtensions
    {
        #region *** Fields       ***
        // The ordered identity attributes used when a caller does not supply an explicit collection. The order
        // matches the appsettings G4:Uia:IdentityAttributes seed, so the primary attribute is Name.
        private static readonly string[] s_defaultIdentityAttributes = ["Name", "AutomationId"];

        // Maps a supported identity attribute name to the reader that extracts its value from a UIA element. Add an
        // entry here to make a new attribute available to locator generation; unlisted names resolve to null (absent).
        private static readonly Dictionary<string, Func<IUIAutomationElement, string>> s_attributeReaders =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["AcceleratorKey"] = element => Safe(() => element.CurrentAcceleratorKey),
                ["AccessKey"] = element => Safe(() => element.CurrentAccessKey),
                ["AutomationId"] = element => Safe(() => element.CurrentAutomationId),
                ["ClassName"] = element => Safe(() => element.CurrentClassName),
                ["FrameworkId"] = element => Safe(() => element.CurrentFrameworkId),
                ["HelpText"] = element => Safe(() => element.CurrentHelpText),
                ["ItemStatus"] = element => Safe(() => element.CurrentItemStatus),
                ["ItemType"] = element => Safe(() => element.CurrentItemType),
                ["Name"] = element => Safe(() => element.CurrentName)
            };
        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Converts an <see cref="IUIAutomationElement"/> into a <see cref="UiaNodeModel"/> representation.
        /// </summary>
        /// <param name="element">The UI Automation element to convert.</param>
        /// <returns>A <see cref="UiaNodeModel"/> containing extracted details such as name, control type, class name, automation ID, process ID, runtime ID, and bounding rectangle.</returns>
        public static UiaNodeModel ConvertToNode(this IUIAutomationElement element)
        {
            return Convert(element, metadata: false);
        }

        /// <summary>
        /// Builds a structured ancestor chain for a given UI Automation element using the default identity attributes.
        /// </summary>
        /// <param name="automation">The <see cref="CUIAutomation8"/> automation instance used for traversal.</param>
        /// <param name="element">The starting <see cref="IUIAutomationElement"/> from which to build the chain.</param>
        /// <returns>A <see cref="UiaChainModel"/> containing the ancestor path and the top-level window, or <c>null</c> if <paramref name="element"/> is <c>null</c>.</returns>
        /// <remarks>Delegates to the attribute-aware overload with <c>Name</c> and <c>AutomationId</c> as the ordered identity attributes.</remarks>
        public static UiaChainModel NewAncestorChain(this CUIAutomation8 automation, IUIAutomationElement element)
        {
            return NewAncestorChain(automation, element, attributes: s_defaultIdentityAttributes);
        }

        /// <summary>
        /// Builds a structured ancestor chain for a given UI Automation element using a caller-supplied identity attribute order.
        /// </summary>
        /// <param name="automation">The <see cref="CUIAutomation8"/> automation instance used for traversal.</param>
        /// <param name="element">The starting <see cref="IUIAutomationElement"/> from which to build the chain.</param>
        /// <param name="attributes">The ordered identity attributes; the first entry is primary and the second is secondary. Missing entries fall back to the defaults.</param>
        /// <returns>A <see cref="UiaChainModel"/> containing the ancestor path and the top-level window, or <c>null</c> if <paramref name="element"/> is <c>null</c>.</returns>
        /// <remarks>
        /// The two configured attributes drive selector-relative sibling counting exactly as the previous hardcoded
        /// automation-id and name pair did; only which properties are read changes, never how uniqueness is measured.
        /// </remarks>
        public static UiaChainModel NewAncestorChain(
            this CUIAutomation8 automation,
            IUIAutomationElement element,
            IReadOnlyList<string> attributes)
        {
            // Resolve the ordered attribute pair once so every node reads and counts the same two properties.
            var (attribute1, attribute2) = NormalizeAttributes(attributes);

            if (element == null)
            {
                // No element provided — cannot build a chain.
                return null;
            }

            // Collects ancestor nodes (including the starting element).
            var nodes = new List<UiaNodeModel>();

            // Tree walker used to navigate the UI Automation hierarchy.
            var walker = automation.RawViewWalker;

            // The desktop root element (absolute root of the UIA tree).
            var root = automation.GetRootElement();

            // Start traversal from the provided element.
            var current = element;

            // Flag to control whether to include full details (false) or metadata-only (true).
            // The starting element includes full details; ancestors only include metadata.
            var metadataOnly = false;

            // Tracks whether we are processing the first (starting) element.
            var isFirst = true;

            while (current != null)
            {
                // Convert the current element to a node model and add it to the chain.
                var node = Convert(current, metadataOnly);
                nodes.Add(node);

                // Resolve the two configured attribute values from the live element so locator formatting keys off
                // the same properties that sibling counting measures below, including attributes Convert does not map.
                node.Attribute1Value = ResolveAttributeValue(current, attribute1);
                node.Attribute2Value = ResolveAttributeValue(current, attribute2);

                // Mark the first element as the trigger element.
                if (isFirst)
                {
                    node.IsTriggerElement = true;
                    isFirst = false;
                }

                // Attempt to retrieve the parent element, handling COM issues safely.
                var parent = Safe(() => walker.GetParentElement(current), fallback: null);

                if (parent == null)
                {
                    // No parent found — this is the top of the chain.
                    break;
                }

                // Capture every selector-relative sibling rank while the live parent and target are available so
                // later locator formatting can add a position that matches the driver's exact predicate scope.
                var siblingDetails = GetSiblingDetails(
                    context: new SiblingContext
                    {
                        Attribute1 = attribute1,
                        Attribute2 = attribute2,
                        Automation = automation,
                        Node = node,
                        Parent = parent,
                        Target = current,
                        Walker = walker
                    }
                );

                // Persist the 1-based ranks so locator generation remains deterministic after the COM traversal
                // advances to the next ancestor.
                node.Attribute1MatchCount = siblingDetails.Attribute1MatchCount;
                node.Attribute1MatchIndex = siblingDetails.Attribute1MatchIndex;
                node.Attribute2MatchCount = siblingDetails.Attribute2MatchCount;
                node.Attribute2MatchIndex = siblingDetails.Attribute2MatchIndex;
                node.IdentityMatchCount = siblingDetails.IdentityMatchCount;
                node.IdentityMatchIndex = siblingDetails.IdentityMatchIndex;
                node.SiblingIndex = siblingDetails.AllIndex;
                node.SiblingIndexOfSameControlType = siblingDetails.SameControlTypeIndex;

                // Stop climbing further if the parent is the root desktop element.
                try
                {
                    if (automation.CompareElements(parent, root) == 1)
                    {
                        break;
                    }
                }
                catch (COMException)
                {
                    // If comparison fails due to a COM error, continue upward.
                }

                // After processing the first element, only metadata is needed for ancestors.
                metadataOnly = true;

                // Move upward in the hierarchy.
                current = parent;
            }

            // Reverse the collected nodes to have the trigger element last.
            nodes.Reverse();

            // Mark the top-level window in the chain.
            var topWindow = nodes.FirstOrDefault();

            // The top window is the first node in the reversed list.
            topWindow?.IsTopWindow = true;

            // Return the structured chain model.
            return new UiaChainModel
            {
                Path = nodes,
                TopWindow = topWindow
            };
        }

        /// <summary>
        /// Builds the canonical UIA XPath-like locator for a <see cref="UiaChainModel"/> using the default identity attributes.
        /// </summary>
        /// <param name="chain">The chain containing the ordered UIA ancestor nodes.</param>
        /// <returns>A canonical locator string beginning with <c>/Desktop</c>.</returns>
        /// <remarks>Delegates to the attribute-aware overload with <c>Name</c> and <c>AutomationId</c> as the ordered identity attributes.</remarks>
        public static string ResolveLocator(this UiaChainModel chain)
        {
            return ResolveLocator(chain, attributes: s_defaultIdentityAttributes);
        }

        /// <summary>
        /// Builds the canonical UIA XPath-like locator for a <see cref="UiaChainModel"/> using a caller-supplied identity attribute order.
        /// </summary>
        /// <param name="chain">The chain containing the ordered UIA ancestor nodes.</param>
        /// <param name="attributes">The ordered identity attributes; the first entry is primary and the second is secondary. Missing entries fall back to the defaults.</param>
        /// <returns>A canonical locator string beginning with <c>/Desktop</c>.</returns>
        /// <remarks>
        /// The attribute order must match the one supplied to <see cref="NewAncestorChain(CUIAutomation8, IUIAutomationElement, IReadOnlyList{string})"/>,
        /// because the sibling ranks stored on each node are positional to that same pair.
        /// </remarks>
        public static string ResolveLocator(this UiaChainModel chain, IReadOnlyList<string> attributes)
        {
            var (attribute1, attribute2) = NormalizeAttributes(attributes);

            return GetCanonicalLocator(chain, attribute1, attribute2);
        }

        /// <summary>
        /// Builds the canonical locator for a UIA element by preserving every resolvable ancestor and
        /// positioning any repeated selector relative to the siblings that match that exact selector.
        /// </summary>
        /// <param name="chain">The UIA chain model to format.</param>
        /// <param name="attribute1">The primary identity attribute name used for the highest-priority predicate.</param>
        /// <param name="attribute2">The secondary identity attribute name used for the lower-priority predicate.</param>
        /// <returns>A canonical locator beginning at the desktop element.</returns>
        private static string GetCanonicalLocator(UiaChainModel chain, string attribute1, string attribute2)
        {
            var nodes = chain?.Path ?? [];
            var builder = new StringBuilder("/Desktop");
            var isGap = false;

            foreach (var node in nodes)
            {
                var isUwp = node.ClassName?.Equals(
                    value: "Windows.UI.Core.CoreWindow",
                    comparisonType: StringComparison.OrdinalIgnoreCase
                ) == true;

                if (isUwp)
                {
                    isGap = true;
                    continue;
                }

                var separator = isGap ? "//" : "/";
                var segment = GetCanonicalSegment(node, attribute1, attribute2);

                builder.Append(separator).Append(segment);
                isGap = false;
            }

            return builder.ToString();
        }

        // Gets a selector that is unique under the already-resolved parent, adding a 1-based position
        // when multiple siblings match the exact control type and property predicate. The branch order,
        // uniqueness conditions, and positioning are unchanged; only the two predicate attributes are configurable.
        internal static string GetCanonicalSegment(UiaNodeModel node, string attribute1, string attribute2)
        {
            var controlType = node.ControlType ?? "*";
            var value1 = node.Attribute1Value;
            var value2 = node.Attribute2Value;
            var hasAttribute1 = !string.IsNullOrEmpty(value1) && !TestBrokenIdentifier(value1);
            var hasAttribute2 = !string.IsNullOrEmpty(value2) && !TestBrokenIdentifier(value2);

            if (hasAttribute1 && node.Attribute1MatchCount == 1)
            {
                return $"{controlType}[@{attribute1}='{value1}']";
            }

            if (hasAttribute1 && hasAttribute2 && node.IdentityMatchCount > 0)
            {
                var selector = $"{controlType}[@{attribute1}='{value1}' and @{attribute2}='{value2}']";

                return AppendPosition(
                    selector: selector,
                    matchCount: node.IdentityMatchCount,
                    matchIndex: node.IdentityMatchIndex
                );
            }

            if (hasAttribute2 && node.Attribute2MatchCount == 1)
            {
                return $"{controlType}[@{attribute2}='{value2}']";
            }

            if (hasAttribute1 && node.Attribute1MatchCount > 0)
            {
                var selector = $"{controlType}[@{attribute1}='{value1}']";

                return AppendPosition(
                    selector: selector,
                    matchCount: node.Attribute1MatchCount,
                    matchIndex: node.Attribute1MatchIndex
                );
            }

            if (hasAttribute2 && node.Attribute2MatchCount > 0)
            {
                var selector = $"{controlType}[@{attribute2}='{value2}']";

                return AppendPosition(
                    selector: selector,
                    matchCount: node.Attribute2MatchCount,
                    matchIndex: node.Attribute2MatchIndex
                );
            }

            return $"{controlType}[{Math.Max(1, node.SiblingIndexOfSameControlType)}]";
        }

        // Adds an XPath position only when more than one sibling matches the exact selector.
        private static string AppendPosition(string selector, int matchCount, int matchIndex)
        {
            return matchCount > 1
                ? $"{selector}[{Math.Max(1, matchIndex)}]"
                : selector;
        }

        // Tests whether an identifier contains a quote that cannot be embedded in the supported syntax.
        private static bool TestBrokenIdentifier(string input)
        {
            return input.Contains('\'') || input.Contains('"');
        }

        // Resolves the ordered attribute collection into the two positional attributes the flow consumes, falling
        // back to the defaults for any missing or blank entry so callers can pass null, one, or two attributes.
        private static (string Attribute1, string Attribute2) NormalizeAttributes(IReadOnlyList<string> attributes)
        {
            var list = attributes ?? [];
            var attribute1 = list.Count > 0 && !string.IsNullOrWhiteSpace(list[0])
                ? list[0]
                : s_defaultIdentityAttributes[0];
            var attribute2 = list.Count > 1 && !string.IsNullOrWhiteSpace(list[1])
                ? list[1]
                : s_defaultIdentityAttributes[1];

            return (attribute1, attribute2);
        }

        // Reads one supported attribute value from a UIA element, returning null for an unlisted attribute or a
        // blank value so downstream comparisons treat both as an absent identifier.
        private static string ResolveAttributeValue(IUIAutomationElement element, string attribute)
        {
            if (element == null || string.IsNullOrEmpty(attribute) || !s_attributeReaders.TryGetValue(attribute, out var reader))
            {
                return null;
            }

            var value = reader(element);

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        // TODO: Export all properties that can be safely retrieved from the element, such as IsContentElement, IsControlElement, IsEnabled, etc.
        // Converts an IUIAutomationElement into a UiaNodeModel representation.
        private static UiaNodeModel Convert(IUIAutomationElement element, bool metadata)
        {
            // Extract common properties from the UIA element
            var automationId = Safe(() => element.CurrentAutomationId);
            var className = Safe(() => element.CurrentClassName);
            var controlTypeId = Safe(() => element.CurrentControlType);
            var name = Safe(() => element.CurrentName);
            var pid = Safe(() => element.CurrentProcessId);

            // Resolve the control type name using the cache, defaulting to "*"
            var controlType = Cache.ControlTypeNames.GetValueOrDefault(
                key: controlTypeId,
                defaultValue: "*"
            );

            // If only metadata is requested, return a simplified model
            if (metadata)
            {
                return new UiaNodeModel
                {
                    AutomationId = string.IsNullOrWhiteSpace(automationId) ? null : automationId,
                    ClassName = string.IsNullOrWhiteSpace(className) ? null : className,
                    ControlType = string.IsNullOrWhiteSpace(controlType) ? null : controlType,
                    ControlTypeId = controlTypeId,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name,
                    ProcessId = pid
                };
            }

            // Initialize runtimeId to an empty array
            int[] runtimeId = [];

            try
            {
                // Attempt to get the runtime ID (may fail for certain elements)
                runtimeId = (int[])element.GetRuntimeId() ?? [];
            }
            catch
            {
                // Ignore errors when retrieving runtime ID
            }

            // Extract the element bounding rectangle
            var rectangle = Safe(() => element.CurrentBoundingRectangle);

            // Extract supported patterns (if any)
            var patterns = GetPatterns(element);

            // Extract mchine information
            var machine = new UiaNodeModel.MachineDataModel
            {
                Name = Environment.MachineName,
                PublicAddress = ControllerUtilities.GetLocalEndpoint()
            };

            // Build and return the node model
            return new UiaNodeModel
            {
                AutomationId = string.IsNullOrWhiteSpace(automationId) ? null : automationId,
                Bounds = new UiaNodeModel.BoundsRectangle
                {
                    Left = rectangle.left,
                    Top = rectangle.top,
                    Width = Math.Max(0, rectangle.right - rectangle.left),
                    Height = Math.Max(0, rectangle.bottom - rectangle.top)
                },
                ClassName = string.IsNullOrWhiteSpace(className) ? null : className,
                ControlType = string.IsNullOrWhiteSpace(controlType) ? null : controlType,
                ControlTypeId = controlTypeId,
                Element = element,
                FrameworkId = Safe(() => element.CurrentFrameworkId),
                Machine = machine,
                Name = string.IsNullOrWhiteSpace(name) ? null : name,
                Patterns = [.. patterns],
                ProcessId = pid,
                RuntimeId = runtimeId.Length > 0 ? runtimeId : null
            };
        }

        // Retrieves the list of supported UI Automation patterns for a given element.
        private static List<UiaNodeModel.PatternDataModel> GetPatterns(IUIAutomationElement element)
        {
            // Resolves all supported UI Automation patterns for a given element.
            static List<UiaNodeModel.PatternDataModel> ResolvePatterns(IUIAutomationElement element)
            {
                // Holds all supported pattern metadata for the element.
                var list = new List<UiaNodeModel.PatternDataModel>();

                // Iterate through all known UI Automation pattern IDs and names.
                foreach (var (id, name) in Cache.PatternNames)
                {
                    // Attempt to retrieve the current pattern for this ID.
                    // Uses Safe<T> to handle COM-related exceptions gracefully.
                    var patternObj = Safe(() => element.GetCurrentPattern(id), fallback: null);

                    // If the pattern is not supported, skip to the next.
                    if (patternObj == null)
                    {
                        continue;
                    }

                    // Add the supported pattern metadata to the result list.
                    list.Add(new UiaNodeModel.PatternDataModel
                    {
                        Id = id,
                        Name = name
                    });
                }

                // Return all supported patterns for the element.
                return list;
            }

            // Initialize an empty list to hold pattern data.
            var list = new List<UiaNodeModel.PatternDataModel>();

            try
            {
                // Attempt to resolve supported patterns.
                return ResolvePatterns(element);
            }
            catch (COMException)
            {
                // Ignore; element may be stale or provider buggy.
            }
            catch (InvalidComObjectException)
            {
                // Ignore; element may have been released.
            }

            // Return an empty list if exceptions occurred.
            return list;
        }

        // Captures selector-relative match counts and positions for the target under its direct parent.
        private static SiblingDetails GetSiblingDetails(SiblingContext context)
        {
            var details = new SiblingDetails();
            var nodeValue1 = context.Node.Attribute1Value;
            var nodeValue2 = context.Node.Attribute2Value;

            try
            {
                var child = Safe(
                    getter: () => context.Walker.GetFirstChildElement(context.Parent),
                    fallback: null
                );

                while (child != null)
                {
                    var controlTypeId = Safe(() => child.CurrentControlType);
                    var hasSameControlType = controlTypeId == context.Node.ControlTypeId;
                    var childValue1 = hasSameControlType ? ResolveAttributeValue(child, context.Attribute1) : null;
                    var childValue2 = hasSameControlType ? ResolveAttributeValue(child, context.Attribute2) : null;
                    var hasSameAttribute1 = hasSameControlType
                        && !string.IsNullOrEmpty(nodeValue1)
                        && string.Equals(
                            a: childValue1,
                            b: nodeValue1,
                            comparisonType: StringComparison.Ordinal
                        );
                    var hasSameAttribute2 = hasSameControlType
                        && !string.IsNullOrEmpty(nodeValue2)
                        && string.Equals(
                            a: childValue2,
                            b: nodeValue2,
                            comparisonType: StringComparison.Ordinal
                        );
                    var hasSameIdentity = hasSameAttribute1 && hasSameAttribute2;

                    details.AllCount++;
                    details.Attribute1MatchCount += hasSameAttribute1 ? 1 : 0;
                    details.Attribute2MatchCount += hasSameAttribute2 ? 1 : 0;
                    details.IdentityMatchCount += hasSameIdentity ? 1 : 0;
                    details.SameControlTypeCount += hasSameControlType ? 1 : 0;

                    if (TestSameElement(context.Automation, child, context.Target))
                    {
                        details.AllIndex = details.AllCount;
                        details.Attribute1MatchIndex = Math.Max(1, details.Attribute1MatchCount);
                        details.Attribute2MatchIndex = Math.Max(1, details.Attribute2MatchCount);
                        details.IdentityMatchIndex = Math.Max(1, details.IdentityMatchCount);
                        details.SameControlTypeIndex = Math.Max(1, details.SameControlTypeCount);
                        details.TargetFound = true;
                    }

                    child = Safe(
                        getter: () => context.Walker.GetNextSiblingElement(child),
                        fallback: null
                    );
                }
            }
            catch (COMException)
            {
                // A stale UIA provider cannot contribute reliable sibling metadata.
            }
            catch (InvalidComObjectException)
            {
                // A released UIA element cannot contribute reliable sibling metadata.
            }

            return details.TargetFound
                ? details
                : new SiblingDetails();
        }

        // Tests element identity while isolating failures from stale or faulty UIA providers.
        private static bool TestSameElement(
            CUIAutomation8 automation,
            IUIAutomationElement candidate,
            IUIAutomationElement target)
        {
            try
            {
                return automation.CompareElements(candidate, target) == 1;
            }
            catch (COMException)
            {
                return false;
            }
            catch (InvalidComObjectException)
            {
                return false;
            }
        }

        // Safely executes a function that retrieves a COM-related value,
        // returning a fallback value if a known exception occurs.
        private static T Safe<T>(Func<T> getter, T fallback = default)
        {
            try
            {
                // Attempt to execute the provided function.
                return getter();
            }
            catch (COMException)
            {
                // COM object is not available or failed; return fallback.
                return fallback;
            }
            catch (InvalidComObjectException)
            {
                // The COM object has been released or is invalid; return fallback.
                return fallback;
            }
            catch (Exception)
            {
                // Getter referenced a null object; return fallback.
                return fallback;
            }
        }
        #endregion

        #region *** Nested Types ***
        // Carries the live UIA objects needed to enumerate the selected element's siblings.
        private sealed class SiblingContext
        {
            public string Attribute1 { get; init; }

            public string Attribute2 { get; init; }

            public CUIAutomation8 Automation { get; init; }

            public UiaNodeModel Node { get; init; }

            public IUIAutomationElement Parent { get; init; }

            public IUIAutomationElement Target { get; init; }

            public IUIAutomationTreeWalker Walker { get; init; }
        }

        // Stores 1-based ranks and total match counts for every selector the recorder can emit.
        private sealed class SiblingDetails
        {
            public int AllCount { get; set; }

            public int AllIndex { get; set; } = 1;

            public int Attribute1MatchCount { get; set; }

            public int Attribute1MatchIndex { get; set; } = 1;

            public int Attribute2MatchCount { get; set; }

            public int Attribute2MatchIndex { get; set; } = 1;

            public int IdentityMatchCount { get; set; }

            public int IdentityMatchIndex { get; set; } = 1;

            public int SameControlTypeCount { get; set; }

            public int SameControlTypeIndex { get; set; } = 1;

            public bool TargetFound { get; set; }
        }
        #endregion
    }
}
