using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace G4.Recorders.Uia.Domain
{
    /// <summary>
    /// Parses the hierarchy and terminal position supported by recorder UIA locators.
    /// </summary>
    internal static class UiaXpath
    {
        #region *** Fields       ***

        private static readonly Regex s_desktopPrefix = new(
            pattern: @"^(?:\(+)?/(?:root|desktop)(?=/|$)",
            options: RegexOptions.IgnoreCase
        );

        private static readonly Regex s_position = new(
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
        public static string[] GetHierarchy(string xpath)
        {
            if (string.IsNullOrWhiteSpace(xpath))
            {
                return [];
            }

            var path = s_desktopPrefix.Replace(
                input: xpath,
                replacement: string.Empty,
                count: 1
            );
            var hierarchy = new List<string>();
            var index = 0;

            while (index < path.Length)
            {
                var separatorLength = 0;
                while (index < path.Length && path[index] == '/')
                {
                    separatorLength++;
                    index++;
                }

                var start = index;
                var bracketDepth = 0;
                var quote = '\0';

                while (index < path.Length)
                {
                    var character = path[index];

                    if (quote != '\0')
                    {
                        if (character == quote)
                        {
                            quote = '\0';
                        }
                    }
                    else if (character is '\'' or '"')
                    {
                        quote = character;
                    }
                    else if (character == '[')
                    {
                        bracketDepth++;
                    }
                    else if (character == ']')
                    {
                        bracketDepth = Math.Max(0, bracketDepth - 1);
                    }
                    else if (character == '/' && bracketDepth == 0)
                    {
                        break;
                    }

                    index++;
                }

                if (index == start)
                {
                    continue;
                }

                var segment = path[start..index];
                hierarchy.Add(separatorLength > 1 ? $"/{segment}" : segment);
            }

            return [.. hierarchy];
        }

        /// <summary>
        /// Converts a supported 1-based terminal position into a validated zero-based collection index.
        /// </summary>
        /// <param name="pathSegment">The UIA path segment that may contain a terminal position.</param>
        /// <param name="matchCount">The number of elements matching the segment condition.</param>
        /// <returns>The presence of a position and its validated collection index.</returns>
        public static PositionSelection GetPositionSelection(string pathSegment, int matchCount)
        {
            if (string.IsNullOrEmpty(pathSegment))
            {
                return new PositionSelection(HasPosition: false, Index: -1);
            }

            var match = s_position.Match(pathSegment);
            if (!match.Success)
            {
                return new PositionSelection(HasPosition: false, Index: -1);
            }

            var value = match.Groups["position"].Value;
            var isPosition = int.TryParse(value, out int position);
            var index = position - 1;
            var isInRange = isPosition && position > 0 && index < matchCount;

            return new PositionSelection(
                HasPosition: true,
                Index: isInRange ? index : -1
            );
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
