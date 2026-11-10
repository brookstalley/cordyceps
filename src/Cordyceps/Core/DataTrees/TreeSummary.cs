// Adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core.DataTrees
{
    /// <summary>
    /// One branch of a data tree as the summary sees it: its path indices and item counts.
    /// <see cref="Count"/> includes nulls and invalid items; <see cref="Nulls"/> and
    /// <see cref="Invalid"/> say how many of those it holds.
    /// </summary>
    public readonly struct TreeBranch
    {
        public TreeBranch(int[] path, int count, int nulls = 0, int invalid = 0)
        {
            Path = path ?? Array.Empty<int>();
            Count = count;
            Nulls = nulls;
            Invalid = invalid;
        }

        public int[] Path { get; }
        public int Count { get; }

        /// <summary>Items that are null.</summary>
        public int Nulls { get; }

        /// <summary>Non-null items whose <c>IsValid</c> is false.</summary>
        public int Invalid { get; }

        public string PathText => "{" + string.Join(";", Path.Select(i => i.ToString(CultureInfo.InvariantCulture))) + "}";
    }

    /// <summary>
    /// A compact description of one data tree, for gh_inspect(action='tree'): totals, path depth,
    /// items per branch, and the paths themselves grouped into runs (consecutive branches that
    /// differ only in their last index, by one each time, and hold the same number of items), so
    /// ten grafted branches read as <c>{0;0}..{0;9}: 1 each</c>, not ten lines. Host-free so the
    /// counting, grouping and notes can be unit-tested.
    /// </summary>
    public static class TreeSummary
    {
        public const int DefaultMaxRuns = 20;
        public const int MaxRunsCeiling = 500;

        /// <summary>
        /// Describe <paramref name="branches"/>. <paramref name="maxRuns"/> is honoured exactly: a
        /// value outside 1-<see cref="MaxRunsCeiling"/> is a caller bug (the action range-checks the
        /// user's value and reports it), never something to adjust silently.
        /// </summary>
        public static JObject Describe(IReadOnlyList<TreeBranch> branches, int maxRuns = DefaultMaxRuns)
        {
            if (maxRuns < 1 || maxRuns > MaxRunsCeiling)
                throw new ArgumentOutOfRangeException(nameof(maxRuns), maxRuns, $"maxRuns must be 1-{MaxRunsCeiling}");

            int items = branches.Sum(b => b.Count);
            int nulls = branches.Sum(b => b.Nulls);
            int invalid = branches.Sum(b => b.Invalid);
            int empty = branches.Count(b => b.Count == 0);

            var o = new JObject
            {
                ["branches"] = branches.Count,
                ["items"] = items
            };
            if (nulls > 0) o["nulls"] = nulls;
            if (invalid > 0) o["invalid"] = invalid;
            if (empty > 0) o["emptyBranches"] = empty;

            if (branches.Count == 0)
                return o;

            int minDepth = branches.Min(b => b.Path.Length);
            int maxDepth = branches.Max(b => b.Path.Length);
            o["depth"] = minDepth == maxDepth ? (JToken)minDepth : new JObject { ["min"] = minDepth, ["max"] = maxDepth };

            int minCount = branches.Min(b => b.Count);
            int maxCount = branches.Max(b => b.Count);
            o["perBranch"] = minCount == maxCount ? (JToken)minCount : new JObject { ["min"] = minCount, ["max"] = maxCount };

            var runs = Runs(branches);
            var list = new JArray();
            foreach (var run in runs.Take(maxRuns))
                list.Add(run);
            o["paths"] = list;
            if (runs.Count > maxRuns) o["morePathGroups"] = runs.Count - maxRuns;

            var notes = Notes(branches, minDepth, maxDepth, nulls, invalid, empty);
            if (notes.Count > 0) o["notes"] = new JArray(notes);
            return o;
        }

        /// <summary>Append <paramref name="note"/> to the tree's <c>notes</c> array, creating it if needed.</summary>
        public static void AddNote(JObject tree, string note)
        {
            if (string.IsNullOrEmpty(note)) return;
            if (tree["notes"] is JArray existing) existing.Add(note);
            else tree["notes"] = new JArray(note);
        }

        /// <summary>
        /// Group consecutive branches into runs: same item count, same path except the last index,
        /// and that index going up by one. Each run is <c>"{0;0}..{0;9}: 1 each"</c> or
        /// <c>"{0;3}: 4"</c>.
        /// </summary>
        public static List<string> Runs(IReadOnlyList<TreeBranch> branches)
        {
            var runs = new List<string>();
            int i = 0;
            while (i < branches.Count)
            {
                int j = i;
                while (j + 1 < branches.Count && Continues(branches[j], branches[j + 1]))
                    j++;

                var first = branches[i];
                var count = first.Count.ToString(CultureInfo.InvariantCulture);
                runs.Add(j == i
                    ? $"{first.PathText}: {count}"
                    : $"{first.PathText}..{branches[j].PathText}: {count} each");
                i = j + 1;
            }
            return runs;
        }

        private static bool Continues(TreeBranch a, TreeBranch b)
        {
            if (a.Count != b.Count || a.Path.Length != b.Path.Length || a.Path.Length == 0) return false;
            int last = a.Path.Length - 1;
            for (int k = 0; k < last; k++)
                if (a.Path[k] != b.Path[k]) return false;
            return b.Path[last] == a.Path[last] + 1;
        }

        /// <summary>
        /// The data-tree situations that most often explain a wrong result. Each note states only
        /// what the branches show; none promises what a fix would do.
        /// </summary>
        private static List<string> Notes(IReadOnlyList<TreeBranch> branches, int minDepth, int maxDepth, int nulls, int invalid, int empty)
        {
            var inv = CultureInfo.InvariantCulture;
            var notes = new List<string>();
            if (minDepth != maxDepth)
                notes.Add($"paths have different depths ({minDepth.ToString(inv)} to {maxDepth.ToString(inv)}); branches from different sources won't line up");
            if (branches.Count > 1 && branches.All(b => b.Count == 1))
                notes.Add("one item per branch (grafted shape)");
            if (empty > 0)
                notes.Add($"{empty.ToString(inv)} empty branch{(empty == 1 ? "" : "es")}");
            if (nulls > 0)
                notes.Add($"{nulls.ToString(inv)} null item{(nulls == 1 ? "" : "s")}");
            if (invalid > 0)
                notes.Add($"{invalid.ToString(inv)} invalid item{(invalid == 1 ? "" : "s")} (not null, but IsValid is false)");

            // Leading indices shared by every branch add depth without separating anything. A single
            // branch has nothing to share them with, so this needs at least two.
            if (branches.Count > 1 && minDepth == maxDepth && minDepth > 2)
            {
                int shared = 0;
                while (shared < minDepth - 1 && branches.All(b => b.Path[shared] == branches[0].Path[shared]))
                    shared++;
                if (shared > 0)
                    notes.Add($"the first {shared.ToString(inv)} {(shared == 1 ? "path index is" : "path indices are")} the same in every branch, so {(shared == 1 ? "it adds" : "they add")} depth without separating anything; Shift Paths is the usual way to remove leading indices (see the data trees guide)");
            }
            return notes;
        }
    }
}
