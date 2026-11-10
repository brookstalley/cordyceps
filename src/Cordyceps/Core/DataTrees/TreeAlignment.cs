using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cordyceps.Core.DataTrees
{
    /// <summary>One input's tree as the alignment check sees it: the port's label and its branches.</summary>
    public sealed class LabeledTree
    {
        public LabeledTree(string label, IReadOnlyList<TreeBranch> branches)
        {
            Label = label ?? string.Empty;
            Branches = branches ?? new List<TreeBranch>();
        }

        public string Label { get; }
        public IReadOnlyList<TreeBranch> Branches { get; }
    }

    /// <summary>
    /// The most common data-tree mistake: two inputs of one component arrive with different branch
    /// structures. Compares the inputs that have more than one branch (a single-branch input is
    /// broadcast against anything, so it is never a mismatch) and names the difference.
    /// </summary>
    public static class TreeAlignment
    {
        /// <summary>
        /// A note naming the mismatch, or null when fewer than two inputs have several branches or
        /// the several-branch inputs have the same paths.
        /// </summary>
        public static string Check(IReadOnlyList<LabeledTree> inputs)
        {
            if (inputs == null) return null;
            var inv = CultureInfo.InvariantCulture;
            var multi = inputs.Where(t => t.Branches.Count > 1).ToList();
            if (multi.Count < 2) return null;

            if (multi.Select(t => t.Branches.Count).Distinct().Count() > 1)
            {
                var parts = multi.Select(t => $"{t.Label} has {t.Branches.Count.ToString(inv)} branches");
                return "inputs don't line up: " + string.Join(", ", parts);
            }

            var first = multi[0];
            for (int j = 1; j < multi.Count; j++)
            {
                var other = multi[j];
                for (int k = 0; k < first.Branches.Count; k++)
                {
                    if (first.Branches[k].Path.SequenceEqual(other.Branches[k].Path)) continue;
                    return $"inputs have the same number of branches ({first.Branches.Count.ToString(inv)}) but different paths: " +
                           $"at branch {k.ToString(inv)}, {first.Label} has {first.Branches[k].PathText} and {other.Label} has {other.Branches[k].PathText}";
                }
            }
            return null;
        }
    }
}
