using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Cordyceps.Core.Canvas;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>Small builders for host-free canvases, shared by the canvas test classes.</summary>
    internal static class TestCanvas
    {
        /// <summary>A GUID whose first 8 characters are <paramref name="n"/> in hex, so short ids are predictable.</summary>
        public static Guid Id(int n) => Guid.Parse($"{n:x8}-0000-4000-8000-{n:x12}");

        public static PortSource From(Guid node, int output = 0) => new PortSource(node, output);

        public static CanvasPort Port(string name, params PortSource[] sources) =>
            new CanvasPort { Name = name, NickName = name, SourceCount = sources.Length, Sources = sources };

        public static CanvasNode Node(Guid id, string nick, string type = "Addition", bool isDefault = false,
            NodeKind kind = NodeKind.Component, CanvasPort[] inputs = null, CanvasPort[] outputs = null) =>
            new CanvasNode
            {
                Id = id,
                NickName = nick,
                TypeName = type,
                IsDefaultNickName = isDefault,
                Kind = kind,
                Inputs = inputs ?? Array.Empty<CanvasPort>(),
                Outputs = outputs ?? new[] { Port("R") }
            };

        public static CanvasGroup Group(Guid id, string name, IEnumerable<Guid> members = null, IEnumerable<Guid> children = null,
            float x = 0, float y = 0) =>
            new CanvasGroup
            {
                Id = id,
                NickName = name,
                MemberIds = (members ?? Enumerable.Empty<Guid>()).ToList(),
                ChildGroupIds = (children ?? Enumerable.Empty<Guid>()).ToList(),
                X = x,
                Y = y
            };

        public static CanvasSnapshot Snap(IEnumerable<CanvasNode> nodes, IEnumerable<CanvasGroup> groups = null,
            IEnumerable<Guid> infra = null, CanvasDetail detail = CanvasDetail.Full) =>
            new CanvasSnapshot("test.gh", new SolverFlags(), detail, nodes, groups ?? Enumerable.Empty<CanvasGroup>(), infra);
    }

    /// <summary>
    /// The snapshot's derived structure: wires recorded on the receiving input only, membership as
    /// a multimap (overlapping sibling groups), nesting under several parents, cycle-safe walks, and
    /// infrastructure absent everywhere.
    /// </summary>
    public class CanvasSnapshotTests
    {
        private static readonly Guid A = TestCanvas.Id(1), B = TestCanvas.Id(2), C = TestCanvas.Id(3), D = TestCanvas.Id(4);
        private static readonly Guid G1 = TestCanvas.Id(0x101), G2 = TestCanvas.Id(0x102), G3 = TestCanvas.Id(0x103);

        [Fact]
        public void Membership_IsAMultimap_WhenSiblingGroupsOverlap()
        {
            var snap = TestCanvas.Snap(
                new[] { TestCanvas.Node(A, "a"), TestCanvas.Node(B, "b"), TestCanvas.Node(C, "c") },
                new[] { TestCanvas.Group(G1, "LEFT", new[] { A, B }), TestCanvas.Group(G2, "RIGHT", new[] { B, C }) });

            Assert.Equal(new[] { G1 }, snap.GroupsOf(A));
            Assert.Equal(new[] { G1, G2 }, snap.GroupsOf(B));
            Assert.Equal(new[] { G2 }, snap.GroupsOf(C));
            Assert.Equal(new[] { G1, G2 }, snap.TopLevelGroupsOf(B));
            Assert.Equal(new[] { G1, G2 }, snap.TopLevelGroups.Select(g => g.Id));
            Assert.Empty(snap.GroupsOf(D));
        }

        [Fact]
        public void Nesting_RecordsEveryParent()
        {
            var snap = TestCanvas.Snap(
                new[] { TestCanvas.Node(A, "a"), TestCanvas.Node(B, "b") },
                new[]
                {
                    TestCanvas.Group(G1, "OUTER1", new[] { A }, new[] { G3 }),
                    TestCanvas.Group(G2, "OUTER2", null, new[] { G3 }),
                    TestCanvas.Group(G3, "INNER", new[] { B })
                });

            Assert.Equal(new[] { G1, G2 }, snap.ParentsOf(G3));
            Assert.Empty(snap.ParentsOf(G1));
            Assert.Equal(new[] { G1, G2 }, snap.TopLevelGroups.Select(g => g.Id));
            Assert.Equal(new[] { G1, G2 }, snap.TopLevelGroupsOf(B));
            Assert.Equal(new[] { G1 }, snap.TopLevelGroupsOf(A));
            Assert.Equal(new[] { G3 }, snap.GroupsOf(B));
        }

        [Fact]
        public void EffectiveMembers_IncludeNestedGroups_AndIgnoreUnknownIds()
        {
            var stranger = TestCanvas.Id(0x999);
            var snap = TestCanvas.Snap(
                new[] { TestCanvas.Node(A, "a"), TestCanvas.Node(B, "b"), TestCanvas.Node(C, "c") },
                new[]
                {
                    TestCanvas.Group(G1, "OUTER", new[] { A, stranger }, new[] { G2 }),
                    TestCanvas.Group(G2, "MIDDLE", new[] { B }, new[] { G3 }),
                    TestCanvas.Group(G3, "INNER", new[] { C, A })
                });

            Assert.Equal(new HashSet<Guid> { A, B, C }, new HashSet<Guid>(snap.EffectiveMembers(G1)));
            Assert.Equal(new HashSet<Guid> { B, C, A }, new HashSet<Guid>(snap.EffectiveMembers(G2)));
            Assert.Equal(new HashSet<Guid> { C, A }, new HashSet<Guid>(snap.EffectiveMembers(G3)));
            Assert.Empty(snap.EffectiveMembers(stranger));
        }

        [Fact]
        public void GroupCycle_Terminates_AndNoGroupVanishesFromTheTopLevel()
        {
            // G1 contains G2 and G2 contains G1: neither is parentless, so the first in document
            // order is promoted rather than both silently disappearing from the overview.
            var snap = TestCanvas.Snap(
                new[] { TestCanvas.Node(A, "a"), TestCanvas.Node(B, "b") },
                new[]
                {
                    TestCanvas.Group(G1, "ONE", new[] { A }, new[] { G2 }),
                    TestCanvas.Group(G2, "TWO", new[] { B }, new[] { G1 }),
                    TestCanvas.Group(G3, "SELF", null, new[] { G3 })
                });

            Assert.Equal(new HashSet<Guid> { A, B }, new HashSet<Guid>(snap.EffectiveMembers(G1)));
            Assert.Equal(new HashSet<Guid> { A, B }, new HashSet<Guid>(snap.EffectiveMembers(G2)));
            Assert.Equal(new[] { G1, G3 }, snap.TopLevelGroups.Select(g => g.Id));
            Assert.Empty(snap.ParentsOf(G3)); // a group listing itself is not its own parent
            Assert.Equal(new[] { G1 }, snap.TopLevelGroupsOf(B));
        }

        [Fact]
        public void UpstreamAndDownstream_AreDerivedFromSourcesRecordedOnTheInputOnly()
        {
            // B's input is fed from A's output 1; C's two inputs are both fed from B (one neighbour, listed once).
            var snap = TestCanvas.Snap(new[]
            {
                TestCanvas.Node(A, "a", outputs: new[] { TestCanvas.Port("X"), TestCanvas.Port("Y") }),
                TestCanvas.Node(B, "b", inputs: new[] { TestCanvas.Port("I", TestCanvas.From(A, 1)) }),
                TestCanvas.Node(C, "c", inputs: new[] { TestCanvas.Port("P", TestCanvas.From(B)), TestCanvas.Port("Q", TestCanvas.From(B)) }),
            });

            Assert.Equal(new[] { A }, snap.Upstream(B));
            Assert.Equal(new[] { B }, snap.Downstream(A));
            Assert.Equal(new[] { C }, snap.Downstream(B));
            Assert.Equal(new[] { B }, snap.Upstream(C));
            Assert.Empty(snap.Upstream(A));
            Assert.Empty(snap.Downstream(C));

            Assert.Equal(3, snap.Wires.Count);
            var first = snap.Wires[0];
            Assert.Equal((A, 1, B, 0), (first.SourceId, first.SourceOutput, first.TargetId, first.TargetInput));
            Assert.Equal("Y", snap.Node(A).OutputFor(first.SourceOutput).Name);
        }

        [Fact]
        public void ParameterObjectSource_UsesIndexMinusOne_AndMapsToItsOnlyOutput()
        {
            var slider = TestCanvas.Node(A, "N", "Number Slider", kind: NodeKind.Slider, outputs: new[] { TestCanvas.Port("N") });
            var snap = TestCanvas.Snap(new[]
            {
                slider,
                TestCanvas.Node(B, "b", inputs: new[] { TestCanvas.Port("I", TestCanvas.From(A, -1)) })
            });

            Assert.Equal(-1, snap.Wires.Single().SourceOutput);
            Assert.Equal("N", snap.Node(A).OutputFor(-1).Name);
            Assert.True(snap.Node(A).IsParameterObject);
            Assert.True(snap.Node(A).IsControl);
        }

        [Fact]
        public void Infrastructure_IsAbsentFromNodesGroupsWiresAndLookups()
        {
            var bridge = TestCanvas.Id(0xb0b);
            var infraGroup = TestCanvas.Id(0xb0c);
            var snap = TestCanvas.Snap(
                new[]
                {
                    TestCanvas.Node(bridge, "MCP", "Cordyceps"),
                    TestCanvas.Node(A, "a", inputs: new[] { TestCanvas.Port("I", TestCanvas.From(bridge)) })
                },
                new[] { TestCanvas.Group(infraGroup, "SERVER", new[] { bridge }), TestCanvas.Group(G1, "USER", new[] { A, bridge }) },
                infra: new[] { bridge, infraGroup });

            Assert.Equal(new[] { A }, snap.Nodes.Select(n => n.Id));
            Assert.Equal(new[] { G1 }, snap.Groups.Select(g => g.Id));
            Assert.Empty(snap.Wires);
            Assert.Empty(snap.Upstream(A));
            Assert.Equal(new[] { A }, snap.EffectiveMembers(G1));
            Assert.Null(snap.Find(bridge));
            Assert.Empty(snap.WithNickname("MCP"));
            Assert.Empty(snap.WithGroupName("SERVER"));
            Assert.Empty(snap.WithPrefix("00000b0b"));
            Assert.True(snap.IsInfrastructure(bridge));
        }

        [Fact]
        public void IndexLookups_AreExactAndOrdinal_AndScopedByKind()
        {
            var snap = TestCanvas.Snap(
                new[] { TestCanvas.Node(A, "Pt"), TestCanvas.Node(B, "Pt"), TestCanvas.Node(C, "POSTS"), TestCanvas.Node(D, "") },
                new[] { TestCanvas.Group(G1, "POSTS"), TestCanvas.Group(G2, "") });

            Assert.Equal(new[] { A, B }, snap.WithNickname("Pt").Select(e => e.Id));
            Assert.Empty(snap.WithNickname("pt"));
            Assert.Empty(snap.WithNickname(" Pt"));
            Assert.Equal(new[] { C }, snap.WithNickname("POSTS").Select(e => e.Id));
            Assert.Equal(new[] { G1 }, snap.WithGroupName("POSTS").Select(e => e.Id));
            Assert.Empty(snap.WithNickname(""));
            Assert.Empty(snap.WithGroupName(""));
            Assert.Equal(new[] { G1 }, snap.WithPrefix("00000101").Select(e => e.Id));
            Assert.True(snap.Find(G1).IsGroup);
            Assert.Equal("Group", snap.Find(G1).TypeName);
            Assert.False(snap.Find(A).IsGroup);
            Assert.Equal(6, snap.Entries.Count());
        }

        [Fact]
        public void DuplicateIds_AreKeptOnce()
        {
            var snap = TestCanvas.Snap(new[] { TestCanvas.Node(A, "first"), TestCanvas.Node(A, "second") },
                new[] { TestCanvas.Group(A, "clash") });

            Assert.Single(snap.Nodes);
            Assert.Equal("first", snap.Node(A).NickName);
            Assert.Empty(snap.Groups);
        }

        [Fact]
        public void ModifierWords_FollowAFixedOrder()
        {
            var port = new CanvasPort { Mapping = Cordyceps.Core.DataMappingChoice.Graft, Simplify = true, Reverse = true };
            Assert.Equal(new[] { "graft", "simplify", "reverse" }, port.ModifierWords);
            Assert.Equal(new[] { "flatten" }, new CanvasPort { Mapping = Cordyceps.Core.DataMappingChoice.Flatten }.ModifierWords);
            Assert.Empty(new CanvasPort().ModifierWords);
        }

        [Fact]
        public void SolverFlags_AreOnOnlyWhenBothSwitchesAre()
        {
            Assert.True(new SolverFlags().IsOn);
            Assert.False(new SolverFlags { DocumentEnabled = false }.IsOn);
            Assert.False(new SolverFlags { SolutionsEnabled = false }.IsOn);
        }

        [Theory]
        [InlineData("tr-TR")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void Numbers_AreInvariantWhateverTheCurrentCulture(string culture)
        {
            var saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Assert.Equal("0.5", CanvasText.Number(0.5m));
                Assert.Equal("10", CanvasText.Number(10.000m));
                Assert.Equal("-1234.25", CanvasText.Number(-1234.25m));
                Assert.Equal("0.125", CanvasText.Number(0.125));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Fact]
        public void Cap_CutsAtTheLimit_AndFlagsTruncation()
        {
            Assert.Equal("abc", CanvasText.Cap("abc", 3, out var t1));
            Assert.False(t1);
            Assert.Equal("ab", CanvasText.Cap("abc", 2, out var t2));
            Assert.True(t2);
            Assert.Null(CanvasText.Cap(null, 2, out var t3));
            Assert.False(t3);
            // A surrogate pair straddling the limit is dropped whole, never split.
            Assert.Equal("a", CanvasText.Cap("a\U0001F600b", 2, out var t4));
            Assert.True(t4);
            Assert.Equal(200, CanvasText.Cap(new string('x', 500), CanvasText.PanelTextCap, out _).Length);
        }

        // ---- Names detail ----

        private static CanvasSnapshot NamesSnap()
        {
            var g = TestCanvas.Group(TestCanvas.Id(0x101), "G", new[] { TestCanvas.Id(1) });
            return TestCanvas.Snap(
                new[]
                {
                    TestCanvas.Node(TestCanvas.Id(1), "a"),
                    TestCanvas.Node(TestCanvas.Id(2), "b", inputs: new[] { TestCanvas.Port("x", TestCanvas.From(TestCanvas.Id(1))) })
                },
                new[] { g }, detail: CanvasDetail.Names);
        }

        [Fact]
        public void NamesSnapshot_RefusesFullQuestions_NamingTheMemberAndTheDetail()
        {
            var s = NamesSnap();
            var a = TestCanvas.Id(1);
            var calls = new (string Member, Action Call)[]
            {
                ("Solver", () => _ = s.Solver),
                ("Wires", () => _ = s.Wires),
                ("Upstream", () => s.Upstream(a)),
                ("Downstream", () => s.Downstream(a))
            };
            foreach (var (member, call) in calls)
            {
                var ex = Assert.Throws<InvalidOperationException>(call);
                Assert.Contains(member, ex.Message);
                Assert.Contains("Names detail", ex.Message);
            }
        }

        [Fact]
        public void NamesSnapshot_StillAnswersWhatTheResolverAndDisplayNamesUse()
        {
            var s = NamesSnap();
            var a = TestCanvas.Id(1);
            var g1 = TestCanvas.Id(0x101);

            Assert.Equal(CanvasDetail.Names, s.Detail);
            Assert.NotNull(s.Node(a));
            Assert.NotNull(s.Group(g1));
            Assert.Equal(new[] { g1 }, s.GroupsOf(a));
            Assert.Empty(s.ParentsOf(g1));
            Assert.Single(s.TopLevelGroups);
            Assert.Equal(new[] { a }, s.EffectiveMembers(g1));
            Assert.Single(s.WithNickname("a"));
            Assert.Single(s.WithGroupName("G"));
            Assert.Single(s.WithPrefix("00000001"));
            Assert.NotNull(s.Find(a));
            Assert.Equal(3, s.Entries.Count());
            Assert.Equal("00000001", new DisplayNames(s).ShortId(a));
            Assert.Equal("a", new DisplayNames(s).DisplayName(a));
        }

        [Fact]
        public void FullSnapshot_AnswersSolverWiresAndNeighbours()
        {
            var a = TestCanvas.Id(1);
            var b = TestCanvas.Id(2);
            var s = TestCanvas.Snap(
                new[] { TestCanvas.Node(a, "a"), TestCanvas.Node(b, "b", inputs: new[] { TestCanvas.Port("x", TestCanvas.From(a)) }) });

            Assert.True(s.Solver.IsOn);
            Assert.Single(s.Wires);
            Assert.Equal(new[] { a }, s.Upstream(b));
            Assert.Equal(new[] { b }, s.Downstream(a));
        }

        [Fact]
        public void UnreadWires_DefaultsToNone_CarriesTheReadersCount_AndIsNeverNegative()
        {
            var nodes = new[] { TestCanvas.Node(A, "A", "Point") };
            Assert.Equal(0, TestCanvas.Snap(nodes).UnreadWires);
            Assert.Equal(2, new CanvasSnapshot("t", new SolverFlags(), CanvasDetail.Full, nodes, null, null, unreadWires: 2).UnreadWires);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CanvasSnapshot("t", new SolverFlags(), CanvasDetail.Full, nodes, null, null, unreadWires: -1));
        }
    }
}
