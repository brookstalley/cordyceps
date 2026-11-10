using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core.Canvas;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// The ambiguity matrix for <see cref="ObjectResolver"/>: every reading (GUID, GUID prefix,
    /// nickname, group name) on its own, every way two readings collide, both scopes, and the
    /// display names that must always resolve back. Nothing here is ever guessed: each failing case
    /// pins the whole message, because agents act on its candidate ids.
    /// </summary>
    public class ObjectResolverTests
    {
        private static readonly Guid A = TestCanvas.Id(1), B = TestCanvas.Id(2), C = TestCanvas.Id(3);
        private static readonly Guid Hexy = Guid.Parse("abcdef01-2345-4678-9abc-def012345678");
        private static readonly Guid Posts = TestCanvas.Id(0x10);
        private static readonly Guid Unnamed = TestCanvas.Id(0x11);
        private static readonly Guid Bridge = TestCanvas.Id(0xb0b);

        /// <summary>A small canvas: COUNT, a shared default "Pt", a hex-id object, a POSTS group, an unnamed group, the bridge.</summary>
        private static CanvasSnapshot Canvas() => TestCanvas.Snap(
            new[]
            {
                TestCanvas.Node(A, "COUNT", "Series"),
                TestCanvas.Node(B, "Pt", "Point", isDefault: true),
                TestCanvas.Node(C, "Pt", "Point", isDefault: true),
                TestCanvas.Node(Hexy, "Circle", "Circle", isDefault: true),
                TestCanvas.Node(Bridge, "MCP", "Cordyceps")
            },
            new[] { TestCanvas.Group(Posts, "POSTS", new[] { A }), TestCanvas.Group(Unnamed, "", new[] { B }) },
            infra: new[] { Bridge });

        private static Resolution Obj(IObjectIndex index, string text) => ObjectResolver.Resolve(index, text, ResolveScope.Objects);
        private static Resolution Grp(IObjectIndex index, string text) => ObjectResolver.Resolve(index, text, ResolveScope.Groups);

        private static void AssertResolved(Resolution r, Guid id, MatchedBy by)
        {
            Assert.True(r.IsResolved, r.Error);
            Assert.Equal(id, r.Entry.Id);
            Assert.Equal(by, r.MatchedBy);
            Assert.Null(r.Error);
        }

        private static void AssertFailed(Resolution r, string message)
        {
            Assert.False(r.IsResolved);
            Assert.Null(r.Entry);
            Assert.Equal(message, r.Error);
        }

        // ---- full GUID ----

        [Fact]
        public void FullGuid_Present_Resolves()
        {
            AssertResolved(Obj(Canvas(), Hexy.ToString()), Hexy, MatchedBy.Guid);
        }

        [Fact]
        public void FullGuid_WithBraces_Resolves()
        {
            AssertResolved(Obj(Canvas(), "{" + Hexy + "}"), Hexy, MatchedBy.Guid);
        }

        [Fact]
        public void FullGuid_Uppercase_Resolves()
        {
            AssertResolved(Obj(Canvas(), Hexy.ToString().ToUpperInvariant()), Hexy, MatchedBy.Guid);
        }

        [Fact]
        public void FullGuid_Absent_GivesTodaysText()
        {
            var absent = Guid.Parse("12345678-0000-4000-8000-000000000000");
            AssertFailed(Obj(Canvas(), absent.ToString().ToUpperInvariant()), $"Component not found: {absent:D}");
        }

        [Fact]
        public void FullGuid_OfInfrastructure_GivesTheSameTextAsAbsent()
        {
            AssertFailed(Obj(Canvas(), Bridge.ToString()), $"Component not found: {Bridge:D}");
        }

        [Fact]
        public void FullGuid_IsReadOnlyAsAGuid_ANicknameEqualToItIsIgnored()
        {
            var absent = Guid.Parse("12345678-0000-4000-8000-000000000000");
            var snap = TestCanvas.Snap(new[]
            {
                TestCanvas.Node(A, B.ToString()),       // A's nickname is B's GUID text
                TestCanvas.Node(B, "b"),
                TestCanvas.Node(C, absent.ToString())   // C's nickname is a GUID nobody has
            });

            AssertResolved(Obj(snap, B.ToString()), B, MatchedBy.Guid);
            AssertFailed(Obj(snap, absent.ToString()), $"Component not found: {absent:D}");
        }

        // ---- nicknames ----

        [Fact]
        public void Nickname_Unique_Resolves()
        {
            AssertResolved(Obj(Canvas(), "COUNT"), A, MatchedBy.Nickname);
        }

        [Fact]
        public void Nickname_CaseDiffers_IsNotFound_AndSuggestsTheRightCase()
        {
            AssertFailed(Obj(Canvas(), "count"), "No object named 'count'. Did you mean 'COUNT'?");
        }

        [Fact]
        public void Nickname_Untrimmed_IsNotFound_AndSuggestsTheTrimmedName()
        {
            AssertFailed(Obj(Canvas(), " COUNT"), "No object named ' COUNT'. Did you mean 'COUNT'?");
        }

        [Fact]
        public void Nickname_SharedByTwo_ListsBothWithIdsAndTypes()
        {
            AssertFailed(Obj(Canvas(), "Pt"),
                "'Pt' is the nickname of 2 objects: 00000002 Point, 00000003 Point; name one by its id");
        }

        [Fact]
        public void Nickname_SharedByTwelve_ListsTenThenCountsTheRest()
        {
            var nodes = Enumerable.Range(1, 12).Select(i => TestCanvas.Node(TestCanvas.Id(i), "Pt", "Point", isDefault: true));
            var listed = string.Join(", ", Enumerable.Range(1, 10).Select(i => $"{i:x8} Point"));
            AssertFailed(Obj(TestCanvas.Snap(nodes), "Pt"),
                $"'Pt' is the nickname of 12 objects: {listed}, and 2 more; name one by its id");
        }

        [Fact]
        public void Nickname_UniqueDefault_Resolves()
        {
            AssertResolved(Obj(Canvas(), "Circle"), Hexy, MatchedBy.Nickname);
        }

        [Fact]
        public void Nickname_OfInfrastructure_IsNotFound_WithTheSameTextAsAnUnknownName()
        {
            var withoutBridge = TestCanvas.Snap(new[] { TestCanvas.Node(A, "COUNT") });
            var withBridge = TestCanvas.Snap(new[] { TestCanvas.Node(A, "COUNT"), TestCanvas.Node(Bridge, "MCP") }, infra: new[] { Bridge });

            AssertFailed(Obj(withoutBridge, "MCP"), "No object named 'MCP'.");
            AssertFailed(Obj(withBridge, "MCP"), "No object named 'MCP'.");
        }

        [Fact]
        public void Infrastructure_StaysHidden_EvenWhenAnIndexForgetsToFilterIt()
        {
            var leaky = new LeakyIndex(Canvas(), new ObjectEntry(Bridge, "MCP", false, "Cordyceps", false));

            AssertFailed(Obj(leaky, "MCP"), "No object named 'MCP'.");
            AssertFailed(Obj(leaky, Bridge.ToString()), $"Component not found: {Bridge:D}");
            AssertFailed(Obj(leaky, "00000b0b"), "No object named '00000b0b'.");
            AssertFailed(Grp(leaky, Bridge.ToString()), $"Group not found: {Bridge}");
        }

        // ---- prefixes ----

        [Fact]
        public void Prefix_EightHex_Resolves()
        {
            AssertResolved(Obj(Canvas(), "abcdef01"), Hexy, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Prefix_Uppercase_Resolves()
        {
            AssertResolved(Obj(Canvas(), "ABCDEF01"), Hexy, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Prefix_WithAHyphenInItsDPosition_Resolves()
        {
            AssertResolved(Obj(Canvas(), "abcdef01-2345"), Hexy, MatchedBy.GuidPrefix);
        }

        [Fact]
        public void Prefix_WithAMisplacedHyphen_MatchesNothing()
        {
            AssertFailed(Obj(Canvas(), "abcdef0-12345"), "No object named 'abcdef0-12345'.");
        }

        [Fact]
        public void Prefix_SevenHex_GetsTheEightDigitHint()
        {
            AssertFailed(Obj(Canvas(), "abcdef0"), "No object named 'abcdef0'. A GUID prefix needs at least 8 hex digits.");
        }

        [Fact]
        public void Prefix_LongerThan35_IsNotAPrefix()
        {
            Assert.False(ObjectResolver.IsGuidPrefixShaped(new string('a', 36)));
            Assert.True(ObjectResolver.IsGuidPrefixShaped(new string('a', 35)));
            Assert.False(ObjectResolver.IsGuidPrefixShaped("abcdef0-"));   // 7 hex digits
            Assert.False(ObjectResolver.IsGuidPrefixShaped("abcdefg1"));   // not hex
        }

        [Fact]
        public void Prefix_SharedByTwo_ListsBoth_ByAnIdThatWorks()
        {
            var p = Guid.Parse("9ac1d04b-0000-4000-8000-000000000001");
            var q = Guid.Parse("9ac1d04b-0000-4000-8000-000000000002");
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(p, "p"), TestCanvas.Node(q, "q") });

            AssertFailed(Obj(snap, "9ac1d04b"), $"GUID prefix '9ac1d04b' matches 2 objects: {p:D} Addition, {q:D} Addition; give more characters");
            Assert.Equal(p.ToString("D"), new DisplayNames(snap).DisplayId(p));
        }

        [Fact]
        public void Prefix_SharedOnlyWithInfrastructure_ResolvesTheUserObject()
        {
            var user = Guid.Parse("9ac1d04b-0000-4000-8000-000000000001");
            var infra = Guid.Parse("9ac1d04b-0000-4000-8000-000000000002");
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(user, "u"), TestCanvas.Node(infra, "MCP") }, infra: new[] { infra });

            AssertResolved(Obj(snap, "9ac1d04b"), user, MatchedBy.GuidPrefix);
            Assert.Equal("9ac1d04b", new DisplayNames(snap).ShortId(user));
        }

        // ---- hex nicknames ----

        [Fact]
        public void HexNickname_NamingADifferentObjectThanItsPrefixReading_IsAnError()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "00000002"), TestCanvas.Node(B, "b", "Point") });

            AssertFailed(Obj(snap, "00000002"),
                $"'00000002' names two objects: nickname → 00000001 Addition; GUID prefix → {B:D} Point. Use a longer GUID prefix");
        }

        [Fact]
        public void HexNickname_NamingTheSameObjectAsItsPrefixReading_Resolves()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "00000001"), TestCanvas.Node(B, "b") });

            AssertResolved(Obj(snap, "00000001"), A, MatchedBy.Nickname);
        }

        // ---- Objects scope and groups ----

        [Fact]
        public void ObjectsScope_GroupName_IsNotFound_WithTheGroupHint_AndNoFallback()
        {
            AssertFailed(Obj(Canvas(), "POSTS"),
                "No object named 'POSTS'. 'POSTS' is a group; group actions take its name, other actions its id (00000010).");
        }

        [Fact]
        public void ObjectsScope_GroupGuidAndGroupPrefix_BothResolve()
        {
            AssertResolved(Obj(Canvas(), Posts.ToString()), Posts, MatchedBy.Guid);
            AssertResolved(Obj(Canvas(), "00000010"), Posts, MatchedBy.GuidPrefix);
        }

        // ---- Groups scope ----

        [Fact]
        public void GroupsScope_UniqueName_Resolves()
        {
            AssertResolved(Grp(Canvas(), "POSTS"), Posts, MatchedBy.GroupName);
        }

        [Fact]
        public void GroupsScope_CaseDiffers_IsNotFound_AndSuggests()
        {
            AssertFailed(Grp(Canvas(), "posts"), "No group named 'posts'. Did you mean 'POSTS'?");
        }

        [Fact]
        public void GroupsScope_TwoGroupsWithOneName_ListsBoth()
        {
            var g1 = TestCanvas.Id(0x20);
            var g2 = TestCanvas.Id(0x21);
            var snap = TestCanvas.Snap(Array.Empty<CanvasNode>(), new[] { TestCanvas.Group(g1, "WALLS"), TestCanvas.Group(g2, "WALLS") });

            AssertFailed(Grp(snap, "WALLS"), "'WALLS' is the name of 2 groups: 00000020 Group, 00000021 Group; name one by its id");
        }

        [Fact]
        public void GroupsScope_ComponentGuidAndPrefix_AreNotGroups()
        {
            AssertFailed(Grp(Canvas(), A.ToString()), $"Object is not a group: {A}");
            AssertFailed(Grp(Canvas(), "00000001"), "Object is not a group: 00000001");
        }

        [Fact]
        public void GroupsScope_MissingOrInfrastructureGuid_IsGroupNotFound()
        {
            var absent = Guid.Parse("12345678-0000-4000-8000-000000000000").ToString();
            AssertFailed(Grp(Canvas(), absent), $"Group not found: {absent}");
            AssertFailed(Grp(Canvas(), Bridge.ToString()), $"Group not found: {Bridge}");
        }

        [Fact]
        public void GroupsScope_ObjectNickname_GetsTheHint()
        {
            AssertFailed(Grp(Canvas(), "COUNT"), "No group named 'COUNT'. 'COUNT' is an object's nickname, not a group.");
        }

        [Fact]
        public void GroupsScope_UnnamedGroup_IsReachableByIdOnly()
        {
            var snap = Canvas();
            AssertResolved(Grp(snap, Unnamed.ToString()), Unnamed, MatchedBy.Guid);
            AssertResolved(Grp(snap, "00000011"), Unnamed, MatchedBy.GuidPrefix);
            AssertFailed(Grp(snap, ""), "Component ID is required");
            Assert.Equal("00000011", new DisplayNames(snap).DisplayName(Unnamed));
        }

        [Fact]
        public void GroupsScope_GroupNameAndAComponentPrefix_NamingDifferentObjects_IsAnError()
        {
            var g = TestCanvas.Id(0x20);
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "a") }, new[] { TestCanvas.Group(g, "00000001") });

            AssertFailed(Grp(snap, "00000001"),
                "'00000001' names two objects: group name → 00000020 Group; GUID prefix → 00000001 Addition. Use a longer GUID prefix");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public void EmptyOrWhitespace_IsRequired_InBothScopes(string text)
        {
            AssertFailed(Obj(Canvas(), text), "Component ID is required");
            AssertFailed(Grp(Canvas(), text), "Component ID is required");
        }

        [Fact]
        public void NotFound_HintsAppearInOrder()
        {
            // "POSTZ" is close to the nickname POSTS (object) and is not a group: Did-you-mean only.
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "POSTS") }, new[] { TestCanvas.Group(Posts, "beef") });

            AssertFailed(Obj(snap, "POSTZ"), "No object named 'POSTZ'. Did you mean 'POSTS'?");
            // "beef" is a group and 4 hex digits: the group hint, then the prefix hint.
            AssertFailed(Obj(snap, "beef"),
                "No object named 'beef'. 'beef' is a group; group actions take its name, other actions its id (00000010). A GUID prefix needs at least 8 hex digits.");
        }

        // ---- display names ----

        [Fact]
        public void Display_UsesNicknameOnlyWhenItIsNotDefaultAndResolves()
        {
            var snap = Canvas();
            var names = new DisplayNames(snap);

            Assert.Equal("COUNT", names.DisplayName(A));
            Assert.Equal("00000002", names.DisplayName(B));        // shared default
            Assert.Equal("abcdef01", names.DisplayName(Hexy));     // unique, but a default nickname
            Assert.Equal("POSTS", names.DisplayName(Posts));
            Assert.Equal("00000010", names.DisplayId(Posts));
        }

        [Fact]
        public void Display_FallsBackToTheFullGuid_WhenANicknameShadowsTheShortId()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "00000002"), TestCanvas.Node(B, "b") });
            var names = new DisplayNames(snap);

            Assert.Equal("00000002", names.ShortId(B));
            Assert.Equal(B.ToString("D"), names.DisplayId(B));
            Assert.Equal("b", names.DisplayName(B));
            Assert.Equal("00000001", names.DisplayName(A)); // its nickname is ambiguous with B's prefix
        }

        [Fact]
        public void Display_GroupShortId_MustResolveInBothScopes()
        {
            // Group G2's short id equals group G1's name: in the Groups scope it reads two ways.
            var g1 = TestCanvas.Id(0x20);
            var g2 = TestCanvas.Id(0x21);
            var snap = TestCanvas.Snap(Array.Empty<CanvasNode>(), new[] { TestCanvas.Group(g1, "00000021"), TestCanvas.Group(g2, "") });
            var names = new DisplayNames(snap);

            Assert.Equal(g2.ToString("D"), names.DisplayId(g2));
            Assert.Equal("00000020", names.DisplayName(g1)); // its name is ambiguous with g2's prefix
            AssertRoundTrips(snap, names);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(42)]
        [InlineData(1234)]
        [InlineData(99991)]
        public void Display_EveryNameAndIdRoundTrips_OnAGeneratedCanvas(int seed)
        {
            var snap = Generate(new Random(seed));
            var names = new DisplayNames(snap);
            AssertRoundTrips(snap, names);

            // The fixture must reach the fallbacks, or the round trip proves little.
            var entries = snap.Entries.ToList();
            Assert.Contains(entries, e => names.ShortId(e.Id).Length > 8);
            Assert.Contains(entries, e => names.DisplayId(e.Id).Length == 36);
            Assert.Contains(entries, e => !e.IsGroup && names.DisplayName(e.Id) == e.NickName);
            Assert.Contains(entries, e => e.IsGroup && e.NickName.Length > 0 && names.DisplayName(e.Id) == e.NickName);
            Assert.Contains(entries, e => !e.IsGroup && e.NickName.Length > 0 && !e.IsDefaultNickName && names.DisplayName(e.Id) != e.NickName);
        }

        private static void AssertRoundTrips(CanvasSnapshot snap, DisplayNames names)
        {
            foreach (var e in snap.Entries)
            {
                var scope = e.IsGroup ? ResolveScope.Groups : ResolveScope.Objects;
                var name = names.DisplayName(e.Id);
                var id = names.DisplayId(e.Id);

                var byName = ObjectResolver.Resolve(snap, name, scope);
                Assert.True(byName.IsResolved && byName.Entry.Id == e.Id, $"DisplayName '{name}' of {e.Id} gave: {byName.Error ?? byName.Entry.Id.ToString()}");

                var byId = ObjectResolver.Resolve(snap, id, scope);
                Assert.True(byId.IsResolved && byId.Entry.Id == e.Id, $"DisplayId '{id}' of {e.Id} gave: {byId.Error ?? byId.Entry.Id.ToString()}");

                // A printed id works on object actions too, groups included.
                var byIdAsObject = ObjectResolver.Resolve(snap, id, ResolveScope.Objects);
                Assert.True(byIdAsObject.IsResolved && byIdAsObject.Entry.Id == e.Id, $"DisplayId '{id}' of {e.Id} in Objects scope gave: {byIdAsObject.Error}");
            }
        }

        /// <summary>
        /// A canvas built to collide: ids drawn from a few shared 8-character stems, nicknames from
        /// a small pool that includes hex text equal to those stems and to other objects' GUIDs,
        /// shared and default nicknames, groups named like nicknames or stems, and unnamed groups.
        /// </summary>
        private static CanvasSnapshot Generate(Random rng)
        {
            string Hex(int n) => string.Concat(Enumerable.Range(0, n).Select(_ => "0123456789abcdef"[rng.Next(16)]));
            var stems = Enumerable.Range(0, 6).Select(_ => Hex(8)).ToList();

            Guid NewId()
            {
                // Half the ids share a stem, a third of those share the next block too, so short ids grow past 8 and 13.
                var head = rng.Next(2) == 0
                    ? stems[rng.Next(stems.Count)] + "-" + (rng.Next(3) == 0 ? "0000" : Hex(4))
                    : Hex(8) + "-" + Hex(4);
                return Guid.Parse($"{head}-4{Hex(3)}-8{Hex(3)}-{Hex(12)}");
            }

            var ids = new List<Guid>();
            var seen = new HashSet<Guid>();
            while (ids.Count < 120)
            {
                var id = NewId();
                if (seen.Add(id)) ids.Add(id);
            }

            var pool = new List<string> { "Pt", "Crv", "COUNT", "RADIUS", "POSTS", "", "Circle" };
            pool.AddRange(stems);
            pool.Add(stems[0].Substring(0, 7));
            pool.Add(ids[5].ToString());
            pool.Add(ids[6].ToString("D").Substring(0, 10));
            // Real short ids as nicknames, so some objects' short ids are shadowed.
            var prefixes = new GuidPrefixIndex(ids.Take(110));
            pool.Add(prefixes.Shortest(ids[10]));
            pool.Add(prefixes.Shortest(ids[11]));

            var nodes = new List<CanvasNode>();
            var groups = new List<CanvasGroup>();
            for (int i = 0; i < ids.Count; i++)
            {
                var nick = rng.Next(4) == 0 ? "unique" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) : pool[rng.Next(pool.Count)];
                if (i % 7 == 0)
                {
                    var members = ids.Where((_, k) => k < 100 && rng.Next(10) == 0).ToList();
                    groups.Add(TestCanvas.Group(ids[i], rng.Next(3) == 0 ? "" : nick, members));
                }
                else
                {
                    nodes.Add(TestCanvas.Node(ids[i], nick, isDefault: rng.Next(3) == 0));
                }
            }
            var infra = ids.Skip(110).Take(3).ToList();
            return TestCanvas.Snap(nodes, groups, infra);
        }

        // ---- many ids ----

        [Fact]
        public void Many_DuplicatesAreNoted_AndEveryEntryIsResolved()
        {
            var counting = new CountingIndex(Canvas());
            var texts = new[] { "COUNT", A.ToString(), "nope", "00000001", "abcdef01" };
            var results = ObjectResolver.ResolveMany(counting, texts, ResolveScope.Objects);

            Assert.Equal(texts.Length, results.Count);
            Assert.Equal(Enumerable.Range(0, texts.Length), results.Select(r => r.Index));
            Assert.Equal(texts, results.Select(r => r.Text));

            Assert.Null(results[0].DuplicateOf);
            Assert.Equal(0, results[1].DuplicateOf);
            Assert.Equal("same object as entry 0", results[1].Note);
            Assert.False(results[2].Resolution.IsResolved);
            Assert.Null(results[2].DuplicateOf);
            Assert.Equal(0, results[3].DuplicateOf);
            Assert.True(results[4].Resolution.IsResolved);
            Assert.Null(results[4].Note);
            Assert.All(results.Where(r => r.Resolution.IsResolved), r => Assert.Same(counting.Inner.Find(r.Resolution.Entry.Id), r.Resolution.Entry));
            Assert.True(counting.Calls > 0);
        }

        [Fact]
        public void Many_EmptyList_GivesNoResults()
        {
            Assert.Empty(ObjectResolver.ResolveMany(Canvas(), Array.Empty<string>(), ResolveScope.Objects));
        }

        // ---- index decorators (the shape a batch overlay takes) ----

        /// <summary>Adds one entry to every lookup without filtering infrastructure, as a careless overlay would.</summary>
        private sealed class LeakyIndex : IObjectIndex
        {
            private readonly IObjectIndex _inner;
            private readonly ObjectEntry _extra;

            public LeakyIndex(IObjectIndex inner, ObjectEntry extra)
            {
                _inner = inner;
                _extra = extra;
            }

            public ObjectEntry Find(Guid id) => id == _extra.Id ? _extra : _inner.Find(id);
            public IReadOnlyList<ObjectEntry> WithNickname(string n) => n == _extra.NickName ? _inner.WithNickname(n).Append(_extra).ToList() : _inner.WithNickname(n);
            public IReadOnlyList<ObjectEntry> WithGroupName(string n) => _inner.WithGroupName(n);
            public IReadOnlyList<ObjectEntry> WithPrefix(string p) =>
                _extra.Id.ToString("D").StartsWith(p.ToLowerInvariant(), StringComparison.Ordinal) ? _inner.WithPrefix(p).Append(_extra).ToList() : _inner.WithPrefix(p);
            public bool IsInfrastructure(Guid id) => _inner.IsInfrastructure(id);
            public IEnumerable<ObjectEntry> Entries => _inner.Entries.Append(_extra);
        }

        private sealed class CountingIndex : IObjectIndex
        {
            public CountingIndex(IObjectIndex inner) { Inner = inner; }
            public IObjectIndex Inner { get; }
            public int Calls { get; private set; }

            public ObjectEntry Find(Guid id) { Calls++; return Inner.Find(id); }
            public IReadOnlyList<ObjectEntry> WithNickname(string n) { Calls++; return Inner.WithNickname(n); }
            public IReadOnlyList<ObjectEntry> WithGroupName(string n) { Calls++; return Inner.WithGroupName(n); }
            public IReadOnlyList<ObjectEntry> WithPrefix(string p) { Calls++; return Inner.WithPrefix(p); }
            public bool IsInfrastructure(Guid id) => Inner.IsInfrastructure(id);
            public IEnumerable<ObjectEntry> Entries => Inner.Entries;
        }
    }
}
