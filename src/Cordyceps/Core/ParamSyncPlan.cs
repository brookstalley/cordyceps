using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// Pure, host-free diff that decides how to reshape one side (inputs or outputs) of a script
    /// component's parameters so its names match a target list, while preserving as many existing
    /// parameters — and therefore their wires — as possible.
    ///
    /// <para>The strategy is a Longest Common Subsequence (LCS) over the parameter <em>names</em>:
    /// parameters whose names appear in both the existing and target lists are kept in place (their
    /// connections survive); the rest are removed, and missing target names are inserted. This is the
    /// single source of truth shared by <c>gh_script(action='set')</c> and
    /// <c>gh_script(action='configure')</c>. <c>configure</c> historically unregistered <em>every</em>
    /// parameter and re-registered it, destroying all wires even on unchanged-name params (GHS-3W9N);
    /// routing it through this plan gives it the same wire preservation <c>set</c> already had.</para>
    ///
    /// <para>Indices are relative to the <em>custom</em> portion of the side — the caller skips any
    /// built-in leading parameter (e.g. a C#/Python script's index-0 <c>out</c> output) before
    /// computing the plan and adds that skip count back when touching the live parameter list.
    /// Kept by the host (not here): capturing the wires of removed params and creating/registering
    /// the inserted ones.</para>
    /// </summary>
    public sealed class ParamSyncPlan
    {
        private ParamSyncPlan(
            IReadOnlyList<int> removeExistingIndicesDescending,
            IReadOnlyList<ParamInsertion> insertions,
            IReadOnlyList<int> keptExistingIndices)
        {
            RemoveExistingIndicesDescending = removeExistingIndicesDescending;
            Insertions = insertions;
            KeptExistingIndices = keptExistingIndices;
        }

        /// <summary>
        /// Existing-list indices to remove, in descending order so the host can unregister them
        /// without shifting the indices of not-yet-removed params. The wires on these params are the
        /// ones reported as lost.
        /// </summary>
        public IReadOnlyList<int> RemoveExistingIndicesDescending { get; }

        /// <summary>
        /// New params to insert, in ascending target order. Applying them left-to-right after the
        /// removals places each at its final position (earlier insertions account for later ones).
        /// </summary>
        public IReadOnlyList<ParamInsertion> Insertions { get; }

        /// <summary>Existing-list indices that are kept in place (the LCS), ascending.</summary>
        public IReadOnlyList<int> KeptExistingIndices { get; }

        /// <summary>True when the existing names already equal the target names — nothing to do.</summary>
        public bool IsNoOp => RemoveExistingIndicesDescending.Count == 0 && Insertions.Count == 0;

        /// <summary>
        /// Compute the keep/remove/insert plan to turn <paramref name="existingNames"/> into
        /// <paramref name="targetNames"/>, maximizing preserved (kept) params via LCS.
        /// </summary>
        public static ParamSyncPlan Compute(IReadOnlyList<string> existingNames, IReadOnlyList<string> targetNames)
        {
            existingNames ??= Array.Empty<string>();
            targetNames ??= Array.Empty<string>();

            var matches = ComputeLcs(existingNames, targetNames);
            var keepExistingIdx = new HashSet<int>(matches.Select(m => m.existIdx));
            var lcsTargetIdx = new HashSet<int>(matches.Select(m => m.targetIdx));

            // Removals: existing indices not in the LCS, descending for shift-safe unregistering.
            var removes = new List<int>();
            for (int i = existingNames.Count - 1; i >= 0; i--)
            {
                if (!keepExistingIdx.Contains(i))
                    removes.Add(i);
            }

            // Insertions: target positions not covered by the LCS, ascending.
            var inserts = new List<ParamInsertion>();
            for (int t = 0; t < targetNames.Count; t++)
            {
                if (!lcsTargetIdx.Contains(t))
                    inserts.Add(new ParamInsertion(t, targetNames[t]));
            }

            var kept = matches.Select(m => m.existIdx).OrderBy(i => i).ToList();

            return new ParamSyncPlan(removes, inserts, kept);
        }

        /// <summary>
        /// Longest Common Subsequence of two name lists, returned as (existing index, target index)
        /// pairs for matched elements in order. Unchanged from the original SyncParamSide algorithm,
        /// so routing the live sync through this plan is behavior-preserving.
        /// </summary>
        private static List<(int existIdx, int targetIdx)> ComputeLcs(IReadOnlyList<string> existing, IReadOnlyList<string> target)
        {
            int m = existing.Count, n = target.Count;
            var dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = existing[i - 1] == target[j - 1]
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);

            var matches = new List<(int, int)>();
            int x = m, y = n;
            while (x > 0 && y > 0)
            {
                if (existing[x - 1] == target[y - 1])
                {
                    matches.Add((x - 1, y - 1));
                    x--; y--;
                }
                else if (dp[x - 1, y] >= dp[x, y - 1])
                    x--;
                else
                    y--;
            }

            matches.Reverse();
            return matches;
        }
    }

    /// <summary>A parameter to insert at <see cref="TargetIndex"/> (relative to the custom portion).</summary>
    public readonly struct ParamInsertion
    {
        public ParamInsertion(int targetIndex, string name)
        {
            TargetIndex = targetIndex;
            Name = name;
        }

        public int TargetIndex { get; }
        public string Name { get; }
    }
}
