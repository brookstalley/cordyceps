using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Cordyceps.Core.Canvas;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Many ids in one call: every entry resolved before anything changes, repeats acted on once
    /// with a note pointing at the first entry, and the all-or-nothing message naming every entry
    /// that failed. The resolver is the real one over a host-free canvas, so a nickname and a GUID
    /// prefix for the same object count as one object.
    /// </summary>
    public class IdBatchTests
    {
        private static readonly Guid A = TestCanvas.Id(1), B = TestCanvas.Id(2), C = TestCanvas.Id(3);
        private static readonly Guid Bridge = TestCanvas.Id(0xb0b);

        private static CanvasSnapshot Canvas() => TestCanvas.Snap(
            new[]
            {
                TestCanvas.Node(A, "COUNT", "Series"),
                TestCanvas.Node(B, "Pt", "Point", isDefault: true),
                TestCanvas.Node(C, "Pt", "Point", isDefault: true),
                TestCanvas.Node(Bridge, "MCP", "Cordyceps")
            },
            infra: new[] { Bridge });

        private static Func<string, (Guid? Id, string Error)> Resolver(IObjectIndex index) => text =>
        {
            var r = ObjectResolver.Resolve(index, text, ResolveScope.Objects);
            return r.IsResolved ? (r.Entry.Id, (string)null) : ((Guid?)null, r.Error);
        };

        private static IReadOnlyList<IdOutcome> Collect(params string[] texts) =>
            IdBatch.Collect(texts, Resolver(Canvas()));

        [Fact]
        public void AllResolved_ActOnEveryEntry_InOrder_WithNoError()
        {
            var outcomes = Collect("COUNT", B.ToString());

            Assert.All(outcomes, o => Assert.True(o.ActOn));
            Assert.Equal(new[] { A, B }, IdBatch.DistinctIds(outcomes));
            Assert.Null(IdBatch.AllOrNothingError(outcomes, "ids"));
            Assert.Empty(IdBatch.DuplicateNotes(outcomes, "ids"));
        }

        [Fact]
        public void SameObjectByNicknameAndByPrefix_IsActedOnOnce_TheLaterEntryCarriesTheNote()
        {
            var outcomes = Collect("COUNT", "00000001", A.ToString("D"));

            Assert.Equal(new[] { A }, IdBatch.DistinctIds(outcomes));
            Assert.True(outcomes[0].ActOn);
            Assert.Null(outcomes[0].Note);
            Assert.Equal(0, outcomes[1].DuplicateOf);
            Assert.Equal("same object as entry 0", outcomes[1].Note);
            Assert.False(outcomes[1].ActOn);
            Assert.True(outcomes[1].IsResolved);
            Assert.Equal(A, outcomes[2].Id);
            Assert.Equal(new[] { "ids[1]: same object as entry 0", "ids[2]: same object as entry 0" },
                IdBatch.DuplicateNotes(outcomes, "ids"));
        }

        [Fact]
        public void AnyFailure_AllOrNothingNamesEveryFailedEntry_ByPositionAndText()
        {
            var outcomes = Collect("COUNT", "Pt", "nope", Bridge.ToString());

            var error = IdBatch.AllOrNothingError(outcomes, "ids");
            Assert.Equal(
                "Nothing was changed: 3 of 4 ids did not resolve. "
                + "ids[1] 'Pt': 'Pt' is the nickname of 2 objects: 00000002 Point, 00000003 Point; name one by its id; "
                + "ids[2] 'nope': No object named 'nope'.; "
                + $"ids[3] '{Bridge}': Component not found: {Bridge:D}",
                error);
        }

        [Fact]
        public void FailedEntry_KeepsTheCallersText_AndHasNoId()
        {
            var outcomes = Collect("nope");

            Assert.False(outcomes[0].IsResolved);
            Assert.False(outcomes[0].ActOn);
            Assert.Equal("nope", outcomes[0].Text);
            Assert.Null(outcomes[0].Id);
            Assert.Empty(IdBatch.DistinctIds(outcomes));
        }

        [Fact]
        public void InfrastructureNickname_IsNotFound_LikeAnyMissingName()
        {
            var outcomes = Collect("MCP");

            Assert.Equal("No object named 'MCP'.", outcomes[0].Error);
        }

        [Fact]
        public void EmptyList_HasNothingToDo_AndNoError()
        {
            var outcomes = IdBatch.Collect(Array.Empty<string>(), Resolver(Canvas()));

            Assert.Empty(outcomes);
            Assert.Null(IdBatch.AllOrNothingError(outcomes, "ids"));
        }

        [Fact]
        public void NullEntry_IsAnError_NotSkipped()
        {
            var outcomes = IdBatch.Collect(new string[] { null }, Resolver(Canvas()));

            Assert.Equal("Component ID is required", outcomes[0].Error);
            Assert.NotNull(IdBatch.AllOrNothingError(outcomes, "ids"));
        }

        [Fact]
        public void ResolverReturningNeitherIdNorError_IsTreatedAsNotFound()
        {
            var outcomes = IdBatch.Collect(new[] { "x" }, _ => ((Guid?)null, (string)null));

            Assert.False(outcomes[0].IsResolved);
            Assert.Equal("Component not found", outcomes[0].Error);
        }

        [Fact]
        public void ResolveRunsOncePerEntry_AllBeforeTheCallerActs()
        {
            var seen = new List<string>();
            IdBatch.Collect(new[] { "a", "b", "c" }, t => { seen.Add(t); return (Guid.NewGuid(), null); });

            Assert.Equal(new[] { "a", "b", "c" }, seen);
        }
    }
}
