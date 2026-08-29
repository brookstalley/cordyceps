using System;
using System.Linq;
using System.Threading.Tasks;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="SnapshotStore"/> — the bounded, oldest-first-evicting store behind
    /// gh_document snapshots (GHD-6M2J). The document-serialization halves of snapshot/revert
    /// are host-coupled and verified live in Rhino; these tests pin the bound/eviction/order
    /// contract the tool relies on.
    /// </summary>
    public class SnapshotStoreTests
    {
        private static byte[] Payload(int size = 4) => new byte[size];

        [Fact]
        public void Save_UnderCapacity_NothingEvicted()
        {
            var store = new SnapshotStore(3);
            Assert.Empty(store.Save("a", Payload()));
            Assert.Empty(store.Save("b", Payload()));
            Assert.Equal(2, store.Count);
        }

        [Fact]
        public void Save_BeyondCapacity_EvictsOldestFirst()
        {
            var store = new SnapshotStore(2);
            store.Save("a", Payload());
            store.Save("b", Payload());

            var evicted = store.Save("c", Payload());

            Assert.Equal(new[] { "a" }, evicted);
            Assert.Equal(2, store.Count);
            Assert.Equal(new[] { "b", "c" }, store.List().Select(s => s.Name));
            Assert.False(store.TryGet("a", out _));
        }

        [Fact]
        public void Save_SameName_RefreshesInPlaceWithoutEviction()
        {
            var store = new SnapshotStore(2);
            store.Save("a", Payload(1));
            store.Save("b", Payload(2));

            // Re-saving "a" at capacity must not evict anything — the count is unchanged —
            // and "a" becomes the newest entry (it would now be the LAST to be evicted).
            var evicted = store.Save("a", Payload(9));

            Assert.Empty(evicted);
            Assert.Equal(2, store.Count);
            Assert.Equal(new[] { "b", "a" }, store.List().Select(s => s.Name));
            Assert.True(store.TryGet("a", out var data));
            Assert.Equal(9, data.Length);

            // The refreshed entry aged to newest: the next new-name save evicts "b", not "a".
            Assert.Equal(new[] { "b" }, store.Save("c", Payload()));
        }

        [Fact]
        public void Remove_ExistingAndMissing()
        {
            var store = new SnapshotStore(2);
            store.Save("a", Payload());

            Assert.True(store.Remove("a"));
            Assert.Equal(0, store.Count);
            Assert.False(store.Remove("a"));
            Assert.False(store.Remove(null));
        }

        [Fact]
        public void List_ReportsSizeAndOrder()
        {
            var store = new SnapshotStore(3);
            store.Save("first", Payload(10));
            store.Save("second", Payload(20));

            var list = store.List();

            Assert.Equal(new[] { "first", "second" }, list.Select(s => s.Name));
            Assert.Equal(new[] { 10, 20 }, list.Select(s => s.SizeBytes));
            Assert.All(list, s => Assert.True(s.CreatedAtUtc <= DateTime.UtcNow));
        }

        [Fact]
        public void Save_InvalidArguments_Throw()
        {
            var store = new SnapshotStore(1);
            Assert.Throws<ArgumentException>(() => store.Save(null, Payload()));
            Assert.Throws<ArgumentException>(() => store.Save("", Payload()));
            Assert.Throws<ArgumentNullException>(() => store.Save("a", null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SnapshotStore(0));
        }

        [Fact]
        public async Task ConcurrentSavesAndLists_StayWithinBoundAndDontTear()
        {
            // The tool writes on the UI thread and lists from HTTP worker threads; hammer the
            // store from several tasks and assert the bound and internal consistency hold.
            var store = new SnapshotStore(8);
            var tasks = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    store.Save($"w{worker}_s{i % 12}", Payload());
                    var list = store.List();
                    Assert.True(list.Count <= 8);
                    Assert.Equal(list.Count, list.Select(s => s.Name).Distinct().Count());
                }
            }));

            await Task.WhenAll(tasks);
            Assert.True(store.Count <= 8);
        }
    }
}
