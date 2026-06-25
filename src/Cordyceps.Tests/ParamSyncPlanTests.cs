using System.Linq;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ParamSyncPlan"/>, the host-free LCS diff that lets both
    /// <c>gh_script(action='set')</c> and <c>gh_script(action='configure')</c> reshape a component's
    /// parameters while preserving wires on name-matched params. The defining regression (GHS-3W9N)
    /// is that <c>configure</c> used to remove EVERY param — even unchanged-name ones — losing all
    /// wires; <see cref="Compute_UnchangedNames_KeepsAll_NoRemovals"/> and
    /// <see cref="Compute_PreservesMatchedNames_RemovesOnlyChanged"/> pin the fixed behavior.
    /// </summary>
    public class ParamSyncPlanTests
    {
        private static string[] NamesAt(ParamSyncPlan plan, string[] existing, System.Collections.Generic.IReadOnlyList<int> indices)
            => indices.Select(i => existing[i]).ToArray();

        [Fact]
        public void Compute_UnchangedNames_KeepsAll_NoRemovals()
        {
            var existing = new[] { "a", "b", "c" };
            var plan = ParamSyncPlan.Compute(existing, new[] { "a", "b", "c" });

            Assert.True(plan.IsNoOp);
            Assert.Empty(plan.RemoveExistingIndicesDescending);
            Assert.Empty(plan.Insertions);
            Assert.Equal(new[] { 0, 1, 2 }, plan.KeptExistingIndices);
        }

        [Fact]
        public void Compute_PreservesMatchedNames_RemovesOnlyChanged()
        {
            // existing [a,b,c] -> target [a,c]: 'b' removed, 'a' and 'c' keep their wires.
            var existing = new[] { "a", "b", "c" };
            var plan = ParamSyncPlan.Compute(existing, new[] { "a", "c" });

            Assert.False(plan.IsNoOp);
            Assert.Equal(new[] { "b" }, NamesAt(plan, existing, plan.RemoveExistingIndicesDescending));
            Assert.Empty(plan.Insertions);
            // a (0) and c (2) are kept in place — their connections are preserved.
            Assert.Equal(new[] { 0, 2 }, plan.KeptExistingIndices);
        }

        [Fact]
        public void Compute_AddsNewParam_InsertsAtTargetPosition()
        {
            // existing [a,b] -> target [a,b,c]: insert 'c' at index 2; nothing removed.
            var plan = ParamSyncPlan.Compute(new[] { "a", "b" }, new[] { "a", "b", "c" });

            Assert.Empty(plan.RemoveExistingIndicesDescending);
            var insert = Assert.Single(plan.Insertions);
            Assert.Equal(2, insert.TargetIndex);
            Assert.Equal("c", insert.Name);
        }

        [Fact]
        public void Compute_InsertInMiddle_HasCorrectTargetIndex()
        {
            // existing [a,c] -> target [a,b,c]: insert 'b' at index 1; a and c kept.
            var plan = ParamSyncPlan.Compute(new[] { "a", "c" }, new[] { "a", "b", "c" });

            Assert.Empty(plan.RemoveExistingIndicesDescending);
            var insert = Assert.Single(plan.Insertions);
            Assert.Equal(1, insert.TargetIndex);
            Assert.Equal("b", insert.Name);
        }

        [Fact]
        public void Compute_Rename_RemovesOldInsertsNew()
        {
            // A rename has no name in common: 'a' removed, 'x' inserted — the wire on 'a' is lost.
            var existing = new[] { "a" };
            var plan = ParamSyncPlan.Compute(existing, new[] { "x" });

            Assert.Equal(new[] { "a" }, NamesAt(plan, existing, plan.RemoveExistingIndicesDescending));
            var insert = Assert.Single(plan.Insertions);
            Assert.Equal(0, insert.TargetIndex);
            Assert.Equal("x", insert.Name);
            Assert.Empty(plan.KeptExistingIndices);
        }

        [Fact]
        public void Compute_ClearAll_RemovesEverything_DescendingOrder()
        {
            // target empty (the "empty list -> clear that side" case): all removed, descending.
            var plan = ParamSyncPlan.Compute(new[] { "a", "b", "c" }, new string[0]);

            Assert.Equal(new[] { 2, 1, 0 }, plan.RemoveExistingIndicesDescending);
            Assert.Empty(plan.Insertions);
            Assert.Empty(plan.KeptExistingIndices);
        }

        [Fact]
        public void Compute_FromEmpty_InsertsAllAscending()
        {
            var plan = ParamSyncPlan.Compute(new string[0], new[] { "a", "b" });

            Assert.Empty(plan.RemoveExistingIndicesDescending);
            Assert.Equal(new[] { 0, 1 }, plan.Insertions.Select(i => i.TargetIndex).ToArray());
            Assert.Equal(new[] { "a", "b" }, plan.Insertions.Select(i => i.Name).ToArray());
        }

        [Fact]
        public void Compute_RemovalIndices_AreStrictlyDescending()
        {
            // Descending order is the contract that lets the host unregister without index shifts.
            var plan = ParamSyncPlan.Compute(new[] { "a", "b", "c", "d", "e" }, new[] { "a", "d" });

            var removes = plan.RemoveExistingIndicesDescending;
            for (int i = 1; i < removes.Count; i++)
                Assert.True(removes[i] < removes[i - 1], "remove indices must be strictly descending");
        }

        [Fact]
        public void Compute_PartialOverlap_KeepsCommonSubsequence()
        {
            // existing [a,b,c,d] -> target [b,d,e]: keep b,d (wires preserved); remove a,c; insert e.
            var existing = new[] { "a", "b", "c", "d" };
            var plan = ParamSyncPlan.Compute(existing, new[] { "b", "d", "e" });

            Assert.Equal(new[] { "c", "a" }, NamesAt(plan, existing, plan.RemoveExistingIndicesDescending));
            Assert.Equal(new[] { 1, 3 }, plan.KeptExistingIndices); // b, d
            var insert = Assert.Single(plan.Insertions);
            Assert.Equal(2, insert.TargetIndex);
            Assert.Equal("e", insert.Name);
        }

        [Fact]
        public void Compute_NullLists_TreatedAsEmpty()
        {
            var plan = ParamSyncPlan.Compute(null, null);
            Assert.True(plan.IsNoOp);

            var insertOnly = ParamSyncPlan.Compute(null, new[] { "a" });
            Assert.Single(insertOnly.Insertions);

            var removeOnly = ParamSyncPlan.Compute(new[] { "a" }, null);
            Assert.Equal(new[] { 0 }, removeOnly.RemoveExistingIndicesDescending);
        }
    }
}
