// Adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core.Naming
{
    /// <summary>
    /// "Did you mean" helpers shared by the strict name rules. Everything here produces
    /// suggestions to show a caller; nothing here is ever used to pick what a call acts on.
    /// </summary>
    public static class NameSuggestions
    {
        /// <summary>
        /// The candidate closest to <paramref name="name"/> by edit distance, ignoring case, within
        /// a budget of 2 edits or a third of the length (whichever is larger); null when none is
        /// that close.
        /// </summary>
        public static string Closest(string name, IEnumerable<string> candidates)
        {
            if (string.IsNullOrEmpty(name) || candidates == null) return null;

            var lower = name.ToLowerInvariant();
            int limit = Math.Max(2, name.Length / 3);
            string best = null;
            int bestDistance = int.MaxValue;

            foreach (var c in candidates.Where(c => !string.IsNullOrEmpty(c)).Distinct())
            {
                if (Math.Abs(c.Length - name.Length) > limit) continue;
                int d = EditDistance(lower, c.ToLowerInvariant());
                if (d <= limit && d < bestDistance)
                {
                    best = c;
                    bestDistance = d;
                }
            }

            return best;
        }

        /// <summary>
        /// Whether either string contains the other, ignoring case. The contained string must be at
        /// least <paramref name="minLength"/> characters, so a one-letter nickname is not "inside"
        /// every name that happens to contain that letter.
        /// </summary>
        public static bool Overlaps(string a, string b, int minLength = 2)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return (b.Length >= minLength && a.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)
                || (a.Length >= minLength && b.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Levenshtein distance; inputs are short names, so the two-row table is fine.</summary>
        public static int EditDistance(string a, string b)
        {
            a ??= string.Empty;
            b ??= string.Empty;
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                var t = prev; prev = cur; cur = t;
            }

            return prev[b.Length];
        }
    }
}
