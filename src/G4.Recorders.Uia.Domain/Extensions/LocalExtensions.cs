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
        #region *** Methods     ***

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
        /// Builds a structured ancestor chain for a given UI Automation element.
        /// </summary>
        /// <param name="automation">The <see cref="CUIAutomation8"/> automation instance used for traversal.</param>
        /// <param name="element">The starting <see cref="IUIAutomationElement"/> from which to build the chain.</param>
        /// <returns>A <see cref="UiaChainModel"/> containing the ancestor path and the top-level window, or <c>null</c> if <paramref name="element"/> is <c>null</c>.</returns>
        public static UiaChainModel NewAncestorChain(this CUIAutomation8 automation, IUIAutomationElement element)
        {
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
                        Automation = automation,
                        Node = node,
                        Parent = parent,
                        Target = current,
                        Walker = walker
                    }
                );

                // Persist the 1-based ranks so locator generation remains deterministic after the COM traversal
                // advances to the next ancestor.
                node.AutomationIdMatchCount = siblingDetails.AutomationIdMatchCount;
                node.AutomationIdMatchIndex = siblingDetails.AutomationIdMatchIndex;
                node.IdentityMatchCount = siblingDetails.IdentityMatchCount;
                node.IdentityMatchIndex = siblingDetails.IdentityMatchIndex;
                node.NameMatchCount = siblingDetails.NameMatchCount;
                node.NameMatchIndex = siblingDetails.NameMatchIndex;
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
        /// Builds the canonical UIA XPath-like locator for a <see cref="UiaChainModel"/>.
        /// </summary>
        /// <param name="chain">The chain containing the ordered UIA ancestor nodes.</param>
        /// <returns>A canonical locator string beginning with <c>/Desktop</c>.</returns>
        public static string ResolveLocator(this UiaChainModel chain)
        {
            return GetCanonicalLocator(chain);
        }

        /// <summary>
        /// Builds the canonical locator for a UIA element by preserving every resolvable ancestor and
        /// positioning any repeated selector relative to the siblings that match that exact selector.
        /// </summary>
        /// <param name="chain">The UIA chain model to format.</param>
        /// <returns>A canonical locator beginning at the desktop element.</returns>
        private static string GetCanonicalLocator(UiaChainModel chain)
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
                var segment = GetCanonicalSegment(node);

                builder.Append(separator).Append(segment);
                isGap = false;
            }

            return builder.ToString();
        }

        // Gets a selector that is unique under the already-resolved parent, adding a 1-based position
        // when multiple siblings match the exact control type and property predicate.
        internal static string GetCanonicalSegment(UiaNodeModel node)
        {
            var controlType = node.ControlType ?? "*";
            var automationId = node.AutomationId;
            var name = node.Name;
            var hasAutomationId = !string.IsNullOrEmpty(automationId) && !TestBrokenIdentifier(automationId);
            var hasName = !string.IsNullOrEmpty(name) && !TestBrokenIdentifier(name);

            if (hasAutomationId && node.AutomationIdMatchCount == 1)
            {
                return $"{controlType}[@AutomationId='{automationId}']";
            }

            if (hasAutomationId && hasName && node.IdentityMatchCount > 0)
            {
                var selector = $"{controlType}[@AutomationId='{automationId}' and @Name='{name}']";

                return AppendPosition(
                    selector: selector,
                    matchCount: node.IdentityMatchCount,
                    matchIndex: node.IdentityMatchIndex
                );
            }

            if (hasName && node.NameMatchCount == 1)
            {
                return $"{controlType}[@Name='{name}']";
            }

            if (hasAutomationId && node.AutomationIdMatchCount > 0)
            {
                var selector = $"{controlType}[@AutomationId='{automationId}']";

                return AppendPosition(
                    selector: selector,
                    matchCount: node.AutomationIdMatchCount,
                    matchIndex: node.AutomationIdMatchIndex
                );
            }

            if (hasName && node.NameMatchCount > 0)
            {
                var selector = $"{controlType}[@Name='{name}']";

                return AppendPosition(
                    selector: selector,
                    matchCount: node.NameMatchCount,
                    matchIndex: node.NameMatchIndex
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

            try
            {
                var child = Safe(
                    getter: () => context.Walker.GetFirstChildElement(context.Parent),
                    fallback: null
                );

                while (child != null)
                {
                    var controlTypeId = Safe(() => child.CurrentControlType);
                    var automationId = Safe(() => child.CurrentAutomationId);
                    var name = Safe(() => child.CurrentName);
                    var hasSameControlType = controlTypeId == context.Node.ControlTypeId;
                    var hasSameAutomationId = hasSameControlType
                        && !string.IsNullOrEmpty(context.Node.AutomationId)
                        && string.Equals(
                            a: automationId,
                            b: context.Node.AutomationId,
                            comparisonType: StringComparison.Ordinal
                        );
                    var hasSameName = hasSameControlType
                        && !string.IsNullOrEmpty(context.Node.Name)
                        && string.Equals(
                            a: name,
                            b: context.Node.Name,
                            comparisonType: StringComparison.Ordinal
                        );
                    var hasSameIdentity = hasSameAutomationId && hasSameName;

                    details.AllCount++;
                    details.AutomationIdMatchCount += hasSameAutomationId ? 1 : 0;
                    details.IdentityMatchCount += hasSameIdentity ? 1 : 0;
                    details.NameMatchCount += hasSameName ? 1 : 0;
                    details.SameControlTypeCount += hasSameControlType ? 1 : 0;

                    if (TestSameElement(context.Automation, child, context.Target))
                    {
                        details.AllIndex = details.AllCount;
                        details.AutomationIdMatchIndex = Math.Max(1, details.AutomationIdMatchCount);
                        details.IdentityMatchIndex = Math.Max(1, details.IdentityMatchCount);
                        details.NameMatchIndex = Math.Max(1, details.NameMatchCount);
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

            public int AutomationIdMatchCount { get; set; }

            public int AutomationIdMatchIndex { get; set; } = 1;

            public int IdentityMatchCount { get; set; }

            public int IdentityMatchIndex { get; set; } = 1;

            public int NameMatchCount { get; set; }

            public int NameMatchIndex { get; set; } = 1;

            public int SameControlTypeCount { get; set; }

            public int SameControlTypeIndex { get; set; } = 1;

            public bool TargetFound { get; set; }
        }

        #endregion
    }
}
