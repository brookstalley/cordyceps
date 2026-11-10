using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cordyceps.Core.Canvas
{
    /// <summary>
    /// The separators outline prints around names, in one place, and the rule that decides whether
    /// a nickname can be printed as a name at all. <see cref="OutlineView"/> builds its lines from
    /// these constants and <see cref="DisplayNames"/> refuses any nickname that contains one, so a
    /// printed name can always be cut back out of its line: it ends at the first separator after it.
    /// Host-free.
    /// </summary>
    public static class OutlineGrammar
    {
        /// <summary>Between the fields of a line; the first field of an object line is its name, the second its type.</summary>
        public const string Field = " · ";

        /// <summary>Between the items of a list (sources, names, destinations, group tags).</summary>
        public const string Item = ", ";

        /// <summary>Between the entries of an <c>out:</c> field (each holds its own destination list).</summary>
        public const string Entry = "; ";

        /// <summary>"fed from": <c>PORT ← SOURCE</c>, <c>in ← NAME</c>.</summary>
        public const string FedFrom = " ← ";

        /// <summary>"feeds": <c>NAME (PORT: COUNT) → GROUP</c>.</summary>
        public const string Feeds = " → ";

        /// <summary>Opens a parenthesised detail after a name: a port, counts, modifiers, a child's size.</summary>
        public const string Detail = " (";

        /// <summary>Opens the group tags after a name: <c>NAME [A, B]</c>.</summary>
        public const string Tags = " [";

        /// <summary>Closes the group tags; a group name is printed inside them.</summary>
        public const string TagsClose = "]";

        /// <summary>Before a value: <c>NAME = VALUE</c>.</summary>
        public const string Value = " = ";

        /// <summary>Every sequence outline prints next to a name. A printable name contains none of them.</summary>
        public static readonly IReadOnlyList<string> Separators = new[] { Field, Item, Entry, FedFrom, Feeds, Detail, Tags, Value };

        /// <summary>
        /// Starts a name may not have: <c>[</c> reads as a JSON array where a group is passed back
        /// (<c>group=</c>), and <c>+</c> reads as the <c>+N more</c> line that ends a capped list.
        /// </summary>
        public static readonly IReadOnlyList<string> LeadingMarks = new[] { "[", "+" };

        /// <summary>
        /// True when <paramref name="name"/> can be printed as a name and cut back out of any line:
        /// not empty; no leading or trailing whitespace; no line break or control character; none of
        /// <see cref="Separators"/> inside it, nor formed across its edges with the separator next to
        /// it (a name ending <c>" ·"</c> would make <c>" · "</c> appear one character early); no
        /// <see cref="LeadingMarks"/> at its start. A group name, printed inside group tags, also
        /// contains no <see cref="TagsClose"/>.
        /// </summary>
        public static bool IsPrintable(string name, bool isGroup)
        {
            return IsPrintableCore(name, isGroup);
        }

        /// <summary>What can stand directly beside a printed name: any separator, or one space.</summary>
        private static readonly string[] Neighbours = new[] { Field, Item, Entry, FedFrom, Feeds, Detail, Tags, Value, " " };

        private static bool IsPrintableCore(string name, bool isGroup)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[name.Length - 1])) return false;
            foreach (var c in name)
            {
                if (char.IsControl(c)) return false;
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator) return false;
            }
            foreach (var mark in LeadingMarks)
                if (name.StartsWith(mark, StringComparison.Ordinal)) return false;
            foreach (var sep in Separators)
            {
                if (name.IndexOf(sep, StringComparison.Ordinal) >= 0) return false;
                // Across the edges: whatever stands next to the name (any separator, or the single
                // space before a type or a port), no separator may form that reaches into the name.
                // "a," followed by " (R)" would read as "a" then ", (R)"; "(m)" after "← " would
                // read as a port. Only a separator that starts exactly where the name ends, or ends
                // exactly where it starts, is allowed.
                foreach (var neighbour in Neighbours)
                {
                    var after = name + neighbour;
                    var first = after.IndexOf(sep, StringComparison.Ordinal);
                    if (first >= 0 && first < name.Length) return false;

                    var before = neighbour + name;
                    var last = before.LastIndexOf(sep, StringComparison.Ordinal);
                    if (last >= 0 && last + sep.Length > neighbour.Length) return false;
                }
            }
            if (isGroup && name.IndexOf(TagsClose, StringComparison.Ordinal) >= 0) return false;
            return true;
        }
    }
}
