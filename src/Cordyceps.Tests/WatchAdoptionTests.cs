using System;
using System.Collections.Generic;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="WatchAdoption"/>: the bound on documents the solution watcher holds
    /// only because the solve coordinator asked (reverted documents the document server never saw).
    /// </summary>
    public class WatchAdoptionTests
    {
        /// <summary>Stands in for a document: identity is reference identity, as for GH_Document.</summary>
        private sealed class Doc
        {
            public Doc(string name) => Name = name;
            public string Name { get; }
            public override string ToString() => Name;
        }

        private static readonly IEqualityComparer<Doc> ByReference = ReferenceEqualityComparer.Instance;

        [Fact]
        public void ToRelease_KeepsTheRequestedAndTheActiveDocument_ReleasesTheRest()
        {
            var first = new Doc("first revert");
            var second = new Doc("second revert");
            var onCanvas = new Doc("on canvas");

            var release = WatchAdoption.ToRelease(new[] { first, second, onCanvas }, new[] { second, onCanvas }, ByReference);

            Assert.Equal(new[] { first }, release);
        }

        [Fact]
        public void ToRelease_RepeatedReverts_HoldOnlyTheLatest()
        {
            // Revert puts each document on the canvas and then schedules it, so the one requested
            // is also the active one; the previous revert is neither and is let go each time.
            var adopted = new List<Doc>();
            for (int i = 0; i < 10; i++)
            {
                var reverted = new Doc($"revert {i}");
                var active = reverted;
                foreach (var stale in WatchAdoption.ToRelease(adopted, new[] { reverted, active }, ByReference))
                    adopted.Remove(stale);
                adopted.Add(reverted);
                Assert.Single(adopted);
            }
        }

        [Fact]
        public void ToRelease_ComparesByIdentity_NotByEquality()
        {
            var adopted = new Doc("same name");
            var lookalike = new Doc("same name");

            var release = WatchAdoption.ToRelease(new[] { adopted }, new[] { lookalike }, ByReference);

            Assert.Equal(new[] { adopted }, release);
        }

        [Fact]
        public void ToRelease_IgnoresNullsAndEmptyInputs()
        {
            var adopted = new Doc("a");

            Assert.Empty(WatchAdoption.ToRelease<Doc>(null, null, ByReference));
            Assert.Equal(new[] { adopted }, WatchAdoption.ToRelease(new[] { adopted, null }, new Doc[] { null }, ByReference));
            Assert.Empty(WatchAdoption.ToRelease(new[] { adopted }, new[] { null, adopted }, ByReference));
        }

        [Fact]
        public void ShouldForget_OnlyWhenNoOtherWatchedDocumentSharesTheId()
        {
            var id = Guid.NewGuid();

            Assert.True(WatchAdoption.ShouldForget(id, new[] { Guid.NewGuid() }));
            Assert.True(WatchAdoption.ShouldForget(id, null));
            Assert.False(WatchAdoption.ShouldForget(id, new[] { Guid.NewGuid(), id }));
        }
    }
}
