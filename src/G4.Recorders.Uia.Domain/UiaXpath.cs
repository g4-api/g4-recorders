using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Parses the hierarchy and terminal position supported by recorder UIA locators.
    /// </summary>
    /// <remarks>
    /// All operations are compute-only. Hierarchy parsing preserves quoted predicate values and descendant scope,
    /// while position parsing distinguishes an absent predicate from an invalid supplied collection rank.
    /// </remarks>
    internal static class UiaXpath
    {
        #region *** Constants    ***

        // Recognizes the optional recorder root so hierarchy traversal does not emit Desktop as an executable step.
        private static readonly Regex DesktopPrefixExpression = new(
            pattern: @"^(?:\(+)?/(?:root|desktop)(?=/|$)",
            options: RegexOptions.IgnoreCase
        );

        // Captures only a terminal numeric predicate so digits inside property values remain part of the condition.
        private static readonly Regex PositionExpression = new(
            pattern: @"\[(?<position>\d+)\]\s*$",
            options: RegexOptions.CultureInvariant
        );

        #endregion

        #region *** Methods      ***
        /// <summary>
        /// Splits a locator into UIA hierarchy segments.
        /// </summary>
        /// <param name="xpath">The XPath-like locator to split.</param>
        /// <returns>Segments with descendant scope attached to the applicable segment.</returns>
        /// <remarks>
        /// A blank locator produces an empty collection. The parser removes one optional Desktop or Root prefix,
        /// preserves slashes inside quoted predicates, and marks segments following repeated separators as
        /// descendant-scoped.
        /// </remarks>
        public static string[] GetHierarchy(string xpath)
        {
            // Reject an absent locator before parsing so callers receive the established empty hierarchy.
            if (string.IsNullOrWhiteSpace(value: xpath))
            {
                return [];
            }

            // Remove only the optional recorder root so every returned item represents an executable UIA step.
            var path = DesktopPrefixExpression.Replace(
                input: xpath,
                replacement: string.Empty,
                count: 1
            );

            // Accumulate parsed steps independently from the source text so caller input remains unchanged.
            var hierarchy = new List<string>();
            var index = 0;

            while (index < path.Length)
            {
                // Consume structural separators before scanning the next step and retain descendant intent.
                var separator = GetSeparator(path, index);
                index = separator.Index;

                // Find the next structural boundary without splitting quoted or predicate-contained slash values.
                var startIndex = index;
                index = FindSegmentEnd(path, startIndex);

                // Ignore separator-only tails so a trailing slash does not create an empty executable step.
                if (index == startIndex)
                {
                    continue;
                }

                // Attach descendant scope to its selected step so downstream traversal retains the original intent.
                var segment = path[startIndex..index];
                var outputSegment = separator.IsDescendant ? $"/{segment}" : segment;
                hierarchy.Add(item: outputSegment);
            }

            // Materialize the stable hierarchy once parsing completes so callers cannot mutate internal state.
            return [.. hierarchy];
        }

        /// <summary>
        /// Converts a supported 1-based terminal position into a validated zero-based collection index.
        /// </summary>
        /// <param name="pathSegment">The UIA path segment that may contain a terminal position.</param>
        /// <param name="matchCount">The number of elements matching the segment condition.</param>
        /// <returns>The presence of a position and its validated collection index.</returns>
        /// <remarks>
        /// A missing predicate returns <c>HasPosition=false</c>. A supplied zero, numeric overflow, or out-of-range
        /// value returns <c>HasPosition=true</c> with index <c>-1</c> so callers reject it instead of selecting the
        /// first match.
        /// </remarks>
        public static PositionSelection GetPositionSelection(string pathSegment, int matchCount)
        {
            // Preserve an absent optional predicate so callers retain their existing first-match behavior.
            if (string.IsNullOrEmpty(value: pathSegment))
            {
                return new PositionSelection(HasPosition: false, Index: -1);
            }

            // Match only a terminal numeric predicate so digits elsewhere in the selector cannot affect rank.
            var match = PositionExpression.Match(input: pathSegment);
            if (!match.Success)
            {
                return new PositionSelection(HasPosition: false, Index: -1);
            }

            // Convert the 1-based XPath rank and validate it before exposing a zero-based collection index.
            var value = match.Groups["position"].Value;
            var isParsedPosition = int.TryParse(s: value, result: out var position);
            var index = position - 1;
            var isInRange = isParsedPosition && position > 0 && index < matchCount;

            // Retain predicate presence when invalid so downstream traversal never falls back to its first match.
            return new PositionSelection(HasPosition: true, Index: isInRange ? index : -1);
        }

        // Finds the end of one hierarchy step while treating quoted and predicate-contained slashes as data.
        // The helper is compute-only and returns the path length when a quote or predicate remains unterminated.
        private static int FindSegmentEnd(string path, int startIndex)
        {
            // Initialize parser state at the first character after structural separators.
            var index = startIndex;
            var bracketDepth = 0;
            var quote = '\0';

            // Advance until the path ends or an unquoted slash outside a predicate starts the next step.
            while (index < path.Length)
            {
                var character = path[index];

                // Preserve every character inside a quoted value while releasing state at its matching delimiter.
                if (quote != '\0')
                {
                    quote = character == quote ? '\0' : quote;
                    index++;
                    continue;
                }

                // Enter quoted-value state so structural characters inside the value remain part of this step.
                if (character is '\'' or '"')
                {
                    quote = character;
                    index++;
                    continue;
                }

                // Increase predicate depth so nested bracket content cannot terminate the current hierarchy step.
                if (character == '[')
                {
                    bracketDepth++;
                    index++;
                    continue;
                }

                // Clamp malformed closing brackets at zero to preserve the parser's established tolerance.
                if (character == ']')
                {
                    bracketDepth = Math.Max(val1: 0, val2: bracketDepth - 1);
                    index++;
                    continue;
                }

                // Identify an unquoted structural slash so predicate-contained separators remain part of this step.
                var isStructuralSeparator = character == '/' && bracketDepth == 0;
                if (isStructuralSeparator)
                {
                    break;
                }

                index++;
            }

            // Return the boundary without consuming it so the parent loop can classify the separator sequence.
            return index;
        }

        // Gets the cursor position after contiguous separators and whether they request descendant traversal.
        // The helper reads caller-owned text without mutation and treats two or more slashes as descendant scope.
        private static (int Index, bool IsDescendant) GetSeparator(string path, int index)
        {
            // Count and consume contiguous separators so the next parser stage begins on an element selector.
            var separatorLength = 0;
            while (index < path.Length)
            {
                // Stop at the first selector character so only leading separators determine traversal scope.
                var isSeparator = path[index] == '/';
                if (!isSeparator)
                {
                    break;
                }

                separatorLength++;
                index++;
            }

            // Report both cursor advancement and scope intent without exposing mutable parsing state.
            return (Index: index, IsDescendant: separatorLength > 1);
        }
        #endregion

        #region *** Nested Types ***

        /// <summary>
        /// Represents whether a segment supplied a position and its validated zero-based collection index.
        /// </summary>
        /// <param name="HasPosition">Indicates whether a terminal numeric predicate was supplied.</param>
        /// <param name="Index">The validated zero-based index, or <c>-1</c> when invalid.</param>
        internal readonly record struct PositionSelection(bool HasPosition, int Index);

        #endregion
    }
}
