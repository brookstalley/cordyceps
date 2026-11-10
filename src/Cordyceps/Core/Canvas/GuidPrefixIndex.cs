// Adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core.Canvas
{
    /// <summary>
    /// Shortest unique GUID prefixes (at least <see cref="MinimumLength"/> characters) and prefix
    /// lookup, over the lowercase D form (<c>9ac1d04b-0000-…</c>) of a fixed set of ids. Built once
    /// per snapshot, over every object a caller may name (Cordyceps infrastructure is left out by the
    /// caller, so a short id never has to grow to avoid an object nobody can see).
    /// </summary>
    public sealed class GuidPrefixIndex
    {
        public const int MinimumLength = 8;

        private readonly string[] _sorted;

        public GuidPrefixIndex(IEnumerable<Guid> ids)
        {
            _sorted = (ids ?? Enumerable.Empty<Guid>())
                .Select(g => g.ToString("D"))
                .Distinct()
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
        }

        public int Count => _sorted.Length;

        /// <summary>
        /// The shortest prefix of <paramref name="id"/>'s D form, at least <see cref="MinimumLength"/>
        /// characters, that no other indexed id shares. Works for an id that is not in the index too.
        /// </summary>
        public string Shortest(Guid id)
        {
            var s = id.ToString("D");
            int i = Array.BinarySearch(_sorted, s, StringComparer.Ordinal);
            int below, above;
            if (i >= 0)
            {
                below = i - 1;
                above = i + 1;
            }
            else
            {
                int insert = ~i;
                below = insert - 1;
                above = insert;
            }

            // In sorted order the longest shared prefix is always with an immediate neighbour.
            int common = 0;
            if (below >= 0) common = Math.Max(common, CommonPrefix(s, _sorted[below]));
            if (above < _sorted.Length) common = Math.Max(common, CommonPrefix(s, _sorted[above]));
            return s.Substring(0, Math.Min(s.Length, Math.Max(MinimumLength, common + 1)));
        }

        /// <summary>
        /// Every indexed id whose D form starts with <paramref name="prefix"/>, ignoring case, in
        /// sorted order. The prefix is not trimmed: a stray space matches nothing.
        /// </summary>
        public List<Guid> StartingWith(string prefix)
        {
            var result = new List<Guid>();
            if (string.IsNullOrEmpty(prefix)) return result;

            var p = prefix.ToLowerInvariant();
            int i = Array.BinarySearch(_sorted, p, StringComparer.Ordinal);
            if (i < 0) i = ~i;
            for (; i < _sorted.Length && _sorted[i].StartsWith(p, StringComparison.Ordinal); i++)
                result.Add(Guid.Parse(_sorted[i]));
            return result;
        }

        private static int CommonPrefix(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length), k = 0;
            while (k < n && a[k] == b[k]) k++;
            return k;
        }
    }
}
