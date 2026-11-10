// Several cases are ported from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang,
// MIT License (Cordyceps.Tests/CanvasOutlineTests.cs), onto the canvas snapshot.

using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Cordyceps.Core.Canvas;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// gh_canvas(action='outline'): the overview, group detail and around views over a host-free
    /// snapshot; qualified counts; overlapping groups; problems by the status rule; caps; the
    /// argument rules; and the identifier round-trip that makes every printed name usable as an id.
    /// </summary>
    public class CanvasOutlineTests
    {
        // ---- a small canvas builder ----------------------------------------------------------------

        private sealed class PortSpec
        {
            public string Name, Nick;
            public bool Optional;
            public DataMappingChoice Mapping;
            public bool Simplify, Reverse;
            public int Volatile;
            public int? SourceCountOverride;
            public readonly List<PortSource> Sources = new List<PortSource>();

            public CanvasPort Build() => new CanvasPort
            {
                Name = Name, NickName = Nick ?? Name, Optional = Optional, Mapping = Mapping,
                Simplify = Simplify, Reverse = Reverse, VolatileCount = Volatile,
                SourceCount = SourceCountOverride ?? Sources.Count, Sources = Sources.ToList()
            };
        }

        private sealed class NodeSpec
        {
            public Guid Id;
            public string Nick, Type;
            public bool IsDefault;
            public NodeKind Kind = NodeKind.Component;
            public float X, Y;
            public bool Locked;
            public bool? PreviewHidden;
            public NodePhase Phase = NodePhase.Computed;
            public MessageLevel Level;
            public List<string> Warnings = new List<string>(), Errors = new List<string>();
            public string Value, Min, Max, PanelText;
            public bool PanelTruncated;
            public readonly List<PortSpec> Inputs = new List<PortSpec>();
            public readonly List<PortSpec> Outputs = new List<PortSpec>();

            public NodeSpec In(string name, string nick = null, bool optional = false)
            {
                Inputs.Add(new PortSpec { Name = name, Nick = nick, Optional = optional });
                return this;
            }

            public NodeSpec Out(string name, string nick = null, int items = 0)
            {
                Outputs.Add(new PortSpec { Name = name, Nick = nick, Volatile = items });
                return this;
            }

            public NodeSpec Error(params string[] messages)
            {
                Level = MessageLevel.Error;
                Errors.AddRange(messages);
                return this;
            }

            public NodeSpec Warning(params string[] messages)
            {
                if (Level != MessageLevel.Error) Level = MessageLevel.Warning;
                Warnings.AddRange(messages);
                return this;
            }

            public CanvasNode Build() => new CanvasNode
            {
                Id = Id, NickName = Nick, TypeName = Type, IsDefaultNickName = IsDefault, Kind = Kind,
                X = X, Y = Y, Locked = Locked, PreviewHidden = PreviewHidden, Phase = Phase, MessageLevel = Level,
                Warnings = Warnings, Errors = Errors, ValueText = Value, SliderMin = Min, SliderMax = Max,
                PanelText = PanelText, PanelTextTruncated = PanelTruncated,
                Inputs = Inputs.Select(p => p.Build()).ToList(), Outputs = Outputs.Select(p => p.Build()).ToList()
            };
        }

        private sealed class Canvas
        {
            private readonly List<NodeSpec> _nodes = new List<NodeSpec>();
            private readonly List<CanvasGroup> _groups = new List<CanvasGroup>();
            private int _next;
            public SolverFlags Solver = new SolverFlags();

            private Guid NewId() => TestCanvas.Id(++_next);

            public NodeSpec Comp(string type, string nick = null, float x = 0, float y = 0)
            {
                var n = new NodeSpec { Id = NewId(), Type = type, Nick = nick ?? type, IsDefault = nick == null, X = x, Y = y, PreviewHidden = false };
                _nodes.Add(n);
                return n;
            }

            /// <summary>A parameter object: itself as its one input and output.</summary>
            public NodeSpec Param(string type, string nick = null, float x = 0, float y = 0, int items = 1, NodeKind kind = NodeKind.Parameter)
            {
                var n = new NodeSpec { Id = NewId(), Type = type, Nick = nick ?? type, IsDefault = nick == null, Kind = kind, X = x, Y = y };
                if (kind != NodeKind.Slider && kind != NodeKind.Toggle && kind != NodeKind.ValueList)
                    n.In(type, nick ?? type, optional: true);
                n.Out(type, nick ?? type, items);
                _nodes.Add(n);
                return n;
            }

            public NodeSpec Slider(string nick, string value, string min, string max, float x = 0, float y = 0)
            {
                var n = Param("Number Slider", nick, x, y, 1, NodeKind.Slider);
                n.Value = value; n.Min = min; n.Max = max;
                return n;
            }

            public NodeSpec Panel(string nick, string text, float x = 0, float y = 0)
            {
                var n = Param("Panel", nick, x, y, 1, NodeKind.Panel);
                n.PanelText = text;
                return n;
            }

            public void Wire(NodeSpec from, int output, NodeSpec to, int input)
            {
                to.Inputs[input].Sources.Add(new PortSource(from.Id, from.Kind == NodeKind.Component ? output : -1));
            }

            public CanvasGroup Group(string name, float x, float y, params object[] members)
            {
                var g = new CanvasGroup
                {
                    Id = TestCanvas.Id(0x1000 + _groups.Count + 1), NickName = name, X = x, Y = y,
                    MemberIds = members.OfType<NodeSpec>().Select(m => m.Id).ToList(),
                    ChildGroupIds = members.OfType<CanvasGroup>().Select(c => c.Id).ToList()
                };
                _groups.Add(g);
                return g;
            }

            public CanvasSnapshot Build() =>
                new CanvasSnapshot("test.gh", Solver, CanvasDetail.Full, _nodes.Select(n => n.Build()), _groups, Array.Empty<Guid>());
        }

        private static List<string> Lines(JToken t) => t?.Values<string>().ToList() ?? new List<string>();

        private static string Short(NodeSpec n) => n.Id.ToString("D").Substring(0, 8);
        private static string Short(CanvasGroup g) => g.Id.ToString("D").Substring(0, 8);

        // ---- the golden canvas ---------------------------------------------------------------------

        /// <summary>
        /// Two groups that overlap (RESULT sits in both), a nested group, an ungrouped disconnected
        /// component, a loose panel, a multi-output component, a warning and an error.
        /// </summary>
        private static CanvasSnapshot Golden(out Dictionary<string, NodeSpec> nodes, out Dictionary<string, CanvasGroup> groups, SolverFlags solver = null)
        {
            var c = new Canvas();
            if (solver != null) c.Solver = solver;
            var count = c.Slider("COUNT", "4", "0", "10", x: 0, y: 0);
            var nums = c.Panel("NUMS", "1\n2", x: 0, y: 50);
            var series = c.Comp("Series", x: 200).In("Start", "S").In("Step", "N", optional: true).In("Count", "C").Out("Series", "S", items: 4);
            var add = c.Comp("Addition", "SUM", x: 300).In("A").In("B").Out("Result", "R", items: 0)
                .Error("Data conversion failed from Text to Number", "second failure");
            var result = c.Param("Number", "RESULT", x: 400, items: 4);
            var split = c.Comp("Split List", x: 500).In("List", "L").In("Index", "i").Out("List A", "A", items: 2).Out("List B", "B", items: 2)
                .Warning("Index out of range");
            var circle = c.Comp("Circle", x: 600, y: 200).In("Plane", "P", optional: true).In("Radius", "R").Out("Circle", "C");
            circle.PreviewHidden = true;
            var old = c.Comp("Move", "OLD", x: 700, y: 300).In("Geometry", "G", optional: true).Out("Geometry", "G", items: 3);
            old.Locked = true;
            old.Phase = NodePhase.NotComputed;
            var note = c.Panel("NOTE", "read me", x: 900, y: 900);
            c.Wire(count, 0, series, 2);
            c.Wire(nums, 0, series, 0);
            c.Wire(series, 0, add, 0);
            c.Wire(series, 0, result, 0);
            c.Wire(result, 0, split, 0);
            split.Inputs[1].Mapping = DataMappingChoice.Graft;
            c.Wire(count, 0, split, 1);

            var inner = c.Group("INNER", 400, 0, result);
            var inputs = c.Group("INPUTS", 0, 0, count, nums);
            var maths = c.Group("MATHS", -500, 0, series, add, inner);
            var output = c.Group("OUTPUT", 450, 0, result, split);

            nodes = new Dictionary<string, NodeSpec>
            {
                ["count"] = count, ["nums"] = nums, ["series"] = series, ["add"] = add, ["result"] = result,
                ["split"] = split, ["circle"] = circle, ["old"] = old, ["note"] = note
            };
            groups = new Dictionary<string, CanvasGroup> { ["inner"] = inner, ["inputs"] = inputs, ["maths"] = maths, ["output"] = output };
            return c.Build();
        }

        [Fact]
        public void Golden_Overview()
        {
            var snap = Golden(out _, out _);
            var o = OutlineView.Overview(snap);
            Assert.Equal(GoldenOverview, o.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
        }

        [Fact]
        public void Golden_GroupDetail()
        {
            var snap = Golden(out _, out var groups);
            var o = OutlineView.Groups(snap, new[] { groups["maths"].Id });
            Assert.Equal(GoldenGroup, o.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
        }

        [Fact]
        public void Golden_Around()
        {
            var snap = Golden(out var nodes, out _);
            var o = OutlineView.Around(snap, nodes["result"].Id, 1);
            Assert.Equal(GoldenAround, o.ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
        }

        // ---- ported from Hyperceps: overview -------------------------------------------------------

        [Fact]
        public void Overview_GroupsInFlowOrder_WithWhatCrossesTheirBorders()
        {
            var c = new Canvas();
            var count = c.Slider("COUNT", "4", "0", "10", x: 0);
            var nums = c.Panel("NUMS", "1 | 2", x: 0, y: 50);
            var series = c.Comp("Series", x: 200).In("Start", "S").In("Count", "C").Out("Series", "S", items: 4);
            var result = c.Param("Number", "RESULT", x: 300, items: 4);
            var add = c.Comp("Addition", x: 250).In("A", optional: true).Out("Result", "R").Error("Data conversion failed from Text to Number");
            c.Wire(count, 0, series, 1);
            c.Wire(nums, 0, series, 0);
            c.Wire(series, 0, result, 0);
            // MATHS sits to the left of INPUTS, but INPUTS feeds it, so INPUTS comes first.
            c.Group("MATHS", -500, 0, series, result, add);
            c.Group("INPUTS", 0, 0, count, nums);

            var o = OutlineView.Overview(c.Build());

            Assert.True((bool)o["success"]);
            Assert.StartsWith("5 objects · 2 groups · 1 error · 0 warnings", (string)o["summary"]);
            Assert.Equal(new[] { "COUNT Number Slider = 4 (0..10) [INPUTS]" }, Lines(o["controls"]));
            var groups = Lines(o["groups"]);
            Assert.Equal(2, groups.Count);
            Assert.Equal("INPUTS · 2 objects · out: COUNT (1) → MATHS; NUMS (1) → MATHS", groups[0]);
            Assert.StartsWith("MATHS · 3 objects · 1 error · in ← NUMS, COUNT", groups[1]);
            Assert.Null(o["ungrouped"]);
            Assert.Contains($"{Short(add)} Addition [MATHS] error: Data conversion failed from Text to Number", Lines(o["problems"]));
            Assert.Contains("group='NAME'", (string)o["next"]);
        }

        [Fact]
        public void Overview_NestedGroupUnderItsParent_AndUngroupedObjects()
        {
            var c = new Canvas();
            var a = c.Panel("A", "a");
            var b = c.Panel("B", "b");
            c.Panel("LOOSE", "x");
            var child = c.Group("CHILD", 0, 0, a);
            c.Group("PARENT", 0, 0, b, child);

            var o = OutlineView.Overview(c.Build());

            Assert.Equal(new[] { "PARENT · 2 objects · contains: CHILD (1 object)" }, Lines(o["groups"]));
            Assert.Equal("1 object · inputs: LOOSE = \"x\"", (string)o["ungrouped"]);
        }

        [Fact]
        public void Overview_WithoutGroups_DescribesEachNetwork_WithEveryOutputOfAnEnd()
        {
            var c = new Canvas();
            var count = c.Slider("COUNT", "12", "1", "20", x: 0);
            var divide = c.Comp("Divide Curve", x: 100).In("Count", "N").Out("Points", "P", items: 12).Out("Tangents", "T", items: 12).Out("Parameters", "t", items: 12);
            c.Wire(count, 0, divide, 0);

            var radius = c.Panel("RADIUS", "2.5", x: 0, y: 300);
            var circle = c.Comp("Circle", "RING", x: 100, y: 300).In("Radius", "R").Out("Circle", "C", items: 1);
            var loft = c.Comp("Loft", x: 200, y: 300).In("Curves", "C").Out("Loft", "L", items: 1);
            c.Wire(radius, 0, circle, 0);
            c.Wire(circle, 0, loft, 0);
            c.Panel("NOTE", "read me", x: 500);

            var o = OutlineView.Overview(c.Build());

            Assert.Null(o["groups"]);
            Assert.Equal(new[]
            {
                $"network 1 · 3 objects · inputs: RADIUS = \"2.5\" · named: RING · ends: {Short(loft)} Loft (1)",
                $"network 2 · 2 objects · inputs: COUNT = 12 (1..20) · ends: {Short(divide)} Divide Curve (P: 12, T: 12, t: 12)"
            }, Lines(o["networks"]));
            Assert.Equal("1 object: NOTE", (string)o["unconnected"]);
            Assert.Contains("id='NAME'", (string)o["next"]);
            Assert.DoesNotContain("group=", (string)o["next"]);
        }

        // ---- ported from Hyperceps: group detail ---------------------------------------------------

        [Fact]
        public void Group_ListsUpstreamFirst_WithPortsModifiersAndOutsideSources()
        {
            var c = new Canvas();
            var chords = c.Param("Curve", "CHORDS", x: -500, items: 8);
            c.Group("OTHER", -500, 0, chords);

            // Positions deliberately against the flow: the consumer is leftmost.
            var item = c.Comp("List Item", x: 0).In("List", "L").In("Index", "i").Out("Item", "i", items: 4);
            var split = c.Comp("Text Split", x: 10).In("Text", "T").In("Separators", "C", optional: true).Out("Result", "R", items: 2);
            var panel = c.Panel("INDEX", "0 | 2", x: 20);
            c.Wire(panel, 0, split, 0);
            c.Wire(split, 0, item, 1);
            item.Inputs[1].Mapping = DataMappingChoice.Graft;
            c.Wire(chords, 0, item, 0);
            var after = c.Comp("Line", x: 900).In("Start", "A").Out("Line", "L");
            c.Wire(item, 0, after, 0);
            var block = c.Group("BLOCK", 0, 0, item, split, panel);

            var g = (JObject)OutlineView.Groups(c.Build(), new[] { block.Id })["groups"][0];

            Assert.Equal("BLOCK", (string)g["group"]);
            Assert.Equal("3 objects", (string)g["summary"]);
            Assert.Equal(new[] { "CHORDS [OTHER]" }, Lines(g["in"]));
            Assert.Equal(new[]
            {
                "INDEX Panel = \"0 | 2\"",
                $"{Short(split)} Text Split · T ← INDEX",
                $"{Short(item)} List Item · L ← CHORDS [OTHER] · i (graft) ← {Short(split)} Text Split"
            }, Lines(g["objects"]));
            Assert.Equal(new[] { $"{Short(item)} List Item (4) → {Short(after)} Line" }, Lines(g["out"]));
        }

        [Fact]
        public void Group_SourceWithASharedNickname_IsNamedByShortIdAndType()
        {
            var c = new Canvas();
            var one = c.Comp("Text Split", "Split", x: 0).Out("Result", "R").Out("Rest", "S");
            c.Comp("Text Split", "Split", x: 0, y: 100).Out("Result", "R");
            var item = c.Comp("List Item", x: 100).In("Index", "i");
            c.Wire(one, 0, item, 0);
            var block = c.Group("BLOCK", 0, 0, item);

            var g = (JObject)OutlineView.Groups(c.Build(), new[] { block.Id })["groups"][0];

            Assert.Equal(new[] { $"{Short(one)} Text Split (R)" }, Lines(g["in"]));
        }

        [Fact]
        public void Group_ChildGroupIsOneLine_AndADirectMemberAlsoInTheChildKeepsItsOwnLine()
        {
            var c = new Canvas();
            var a = c.Panel("A", "a", x: 0);
            var b = c.Panel("B", "b", x: 10);
            var own = c.Comp("Merge", x: 100).In("D1").In("D2", optional: true).Out("Result", "R");
            c.Wire(a, 0, own, 0);
            var inner = c.Group("INNER", 0, 0, a, b);
            var outer = c.Group("OUTER", 0, 0, inner, own, b);
            var model = c.Build();

            Assert.Equal(new[] { "OUTER · 3 objects · contains: INNER (2 objects)" }, Lines(OutlineView.Overview(model)["groups"]));

            var g = (JObject)OutlineView.Groups(model, new[] { outer.Id })["groups"][0];
            Assert.Equal(new[]
            {
                "INNER (group) · 2 objects · out: A (1) → OUTER",
                "B Panel = \"b\" [INNER]",
                $"{Short(own)} Merge · D1 ← A"
            }, Lines(g["objects"]));

            var innerDetail = (JObject)OutlineView.Groups(model, new[] { inner.Id })["groups"][0];
            Assert.Equal("2 objects · inside OUTER", (string)innerDetail["summary"]);
        }

        [Fact]
        public void Group_TooManyObjects_IsCappedAndMoreSaysHowManyAreLeft()
        {
            var c = new Canvas();
            var members = Enumerable.Range(0, 200).Select(i => (object)c.Panel(null, null, x: i)).ToArray();
            var big = c.Group("BIG", 0, 0, members);

            var o = OutlineView.Groups(c.Build(), new[] { big.Id });

            Assert.Equal(OutlineView.MaxObjectLines, ((JArray)o["groups"][0]["objects"]).Count);
            Assert.Equal(200 - OutlineView.MaxObjectLines, (int)o["more"]["objects"]);
        }

        [Fact]
        public void Group_SeveralGroupsShareOneLineBudget()
        {
            var c = new Canvas();
            var a = c.Group("A", 0, 0, Enumerable.Range(0, 100).Select(i => (object)c.Panel(null, null, x: i)).ToArray());
            var b = c.Group("B", 0, 0, Enumerable.Range(0, 100).Select(i => (object)c.Panel(null, null, x: i)).ToArray());

            var o = OutlineView.Groups(c.Build(), new[] { a.Id, b.Id });

            Assert.Equal(100, ((JArray)o["groups"][0]["objects"]).Count);
            Assert.Equal(50, ((JArray)o["groups"][1]["objects"]).Count);
            Assert.Equal(50, (int)o["more"]["objects"]);
        }

        // ---- ported from Hyperceps: around ---------------------------------------------------------

        [Fact]
        public void Around_FollowsWiresBothWays_ToTheGivenDepth()
        {
            var c = new Canvas();
            var chain = Enumerable.Range(0, 5).Select(i => c.Param("Number", $"N{i}", x: i * 100)).ToList();
            for (int i = 0; i < 4; i++) c.Wire(chain[i], 0, chain[i + 1], 0);
            var model = c.Build();

            var o = OutlineView.Around(model, chain[2].Id, 1);
            Assert.Equal("N2", (string)o["around"]);
            Assert.Equal(1, (int)o["depth"]);
            Assert.Equal(new[] { "N1 Number · ← N0", "N2 Number · ← N1", "N3 Number · ← N2" }, Lines(o["objects"]));
            Assert.Equal(new[] { "N0" }, Lines(o["fedFrom"]));
            Assert.Equal(new[] { "N4" }, Lines(o["feeds"]));

            var wide = OutlineView.Around(model, chain[2].Id, 2);
            Assert.Equal(5, ((JArray)wide["objects"]).Count);
            Assert.Null(wide["fedFrom"]);
            Assert.Null(wide["feeds"]);
            Assert.Null(wide["more"]);
        }

        [Fact]
        public void Around_TooManyObjects_IsCapped_AndFeedsIsCapped()
        {
            var c = new Canvas();
            var hub = c.Param("Number", "HUB", x: 0);
            for (int i = 0; i < 70; i++)
            {
                var leaf = c.Param("Number", $"L{i}", x: 10 + i);
                c.Wire(hub, 0, leaf, 0);
                c.Wire(leaf, 0, c.Param("Number", $"M{i}", x: 500 + i), 0);
            }
            var o = OutlineView.Around(c.Build(), hub.Id, 1);

            Assert.Equal(OutlineView.MaxAroundObjects, ((JArray)o["objects"]).Count);
            Assert.Equal(71 - OutlineView.MaxAroundObjects, (int)o["more"]["objects"]);
            Assert.Equal("HUB Number", Lines(o["objects"])[0]);
            // The 59 leaves shown feed 59 objects past the edge; the 11 leaves cut off are past it too.
            Assert.Equal("+62 more", Lines(o["feeds"]).Last());
            Assert.Equal(62, (int)o["more"]["feeds"]);
        }

        // ---- ported from Hyperceps: finding groups, now by the one resolver -------------------------

        [Fact]
        public void GroupArgument_UsesTheResolverRule_ExactNamesOnly()
        {
            var c = new Canvas();
            var chords = c.Group("TOP CHORDS", 0, 0);
            var bracing = c.Group("TOP BRACING", 0, 0);
            c.Group("GRID LINES (sorted W→E)", 0, 0);
            var snap = c.Build();

            OutlineTarget Resolve(string group)
            {
                Assert.True(OutlineRequest.TryParse(null, group, 0, out var req, out var e), e);
                Assert.True(req.TryResolve(snap, out var t, out var re), re);
                return t;
            }
            string Error(string group)
            {
                Assert.True(OutlineRequest.TryParse(null, group, 0, out var req, out _));
                Assert.False(req.TryResolve(snap, out _, out var e));
                return e;
            }

            Assert.Equal(new[] { bracing.Id }, Resolve("TOP BRACING").Groups);
            Assert.Equal(new[] { chords.Id }, Resolve(Short(chords)).Groups);
            Assert.Equal(new[] { chords.Id, bracing.Id }, Resolve("[\"TOP CHORDS\", \"TOP BRACING\", \"TOP CHORDS\"]").Groups);
            // Hyperceps matched case-insensitively and by a unique start of the name; the one rule does not.
            Assert.StartsWith("No group named 'top bracing'. Did you mean 'TOP BRACING'?", Error("top bracing"));
            Assert.StartsWith("No group named 'GRID LINES'.", Error("GRID LINES"));
            Assert.StartsWith("group[1]: No group named 'NOPE'.", Error("[\"TOP CHORDS\", \"NOPE\"]"));
        }

        [Fact]
        public void FlowOrder_KeepsEverything_EvenInACycle_AndWhatACycleFeedsComesAfterIt()
        {
            Guid a = TestCanvas.Id(1), b = TestCanvas.Id(2), cc = TestCanvas.Id(3);
            var pos = new Dictionary<Guid, (float, float)> { [a] = (1, 0), [b] = (2, 0), [cc] = (3, 0) };
            var order = OutlineView.FlowOrder(new[] { a, b, cc }, new[] { (b, a), (a, b) }, id => pos[id]);
            // The cycle is one unit, placed by its leftmost member.
            Assert.Equal(new[] { a, b, cc }, order);

            // d sits leftmost but is fed by the cycle, so it waits for it, then goes before c.
            var d = TestCanvas.Id(4);
            pos[d] = (-10, 0);
            var fed = OutlineView.FlowOrder(new[] { a, b, cc, d }, new[] { (b, a), (a, b), (b, d) }, id => pos[id]);
            Assert.Equal(new[] { a, b, d, cc }, fed);
        }

        // ---- overlapping groups (report 1.6 defect 2) ----------------------------------------------

        [Fact]
        public void OverlappingSiblingGroups_TagBoth_CountOnce_ShareAndKeepEdgesFromBoth()
        {
            var c = new Canvas();
            var src = c.Param("Number", "SRC", x: 1000, items: 2);
            var shared = c.Comp("Addition", "SHARED", x: 0).In("A").Out("Result", "R", items: 2);
            var leftOnly = c.Comp("Addition", "LEFTONLY", x: 0, y: 50).In("A").Out("Result", "R", items: 2);
            var rightOnly = c.Comp("Addition", "RIGHTONLY", x: 0, y: 100).In("A").Out("Result", "R", items: 2);
            c.Wire(src, 0, shared, 0);
            c.Wire(shared, 0, leftOnly, 0);
            c.Wire(shared, 0, rightOnly, 0);
            // FEED sits far right, so only its edges can put it first.
            c.Group("FEED", 1000, 0, src);
            var left = c.Group("LEFT", 0, 0, shared, leftOnly);
            c.Group("RIGHT", 0, 10, shared, rightOnly);
            // SINK is leftmost, but RIGHT's own member feeds it. The shared member makes LEFT and
            // RIGHT a group-level cycle; SINK must still come after that cycle, not before it.
            var down = c.Comp("Line", "DOWN", x: -900).In("A");
            c.Wire(rightOnly, 0, down, 0);
            c.Group("SINK", -900, 0, down);
            var snap = c.Build();

            var o = OutlineView.Overview(snap);

            Assert.StartsWith("5 objects · 4 groups", (string)o["summary"]);
            var groups = Lines(o["groups"]);
            Assert.Equal(new[]
            {
                "FEED · 1 object · out: SRC (2) → LEFT, RIGHT",
                "LEFT · 2 objects · shares 1 object with RIGHT · in ← SRC · out: SHARED (2) → RIGHT",
                "RIGHT · 2 objects · shares 1 object with LEFT · in ← SRC · out: SHARED (2) → LEFT; RIGHTONLY (2) → SINK",
                "SINK · 1 object · in ← RIGHTONLY"
            }, groups);

            var g = (JObject)OutlineView.Groups(snap, new[] { left.Id })["groups"][0];
            Assert.Equal(new[] { "SHARED Addition [RIGHT] · A ← SRC [FEED]", "LEFTONLY Addition · A ← SHARED" }, Lines(g["objects"]));
            Assert.Equal(new[] { "SHARED (2) → RIGHT" }, Lines(g["out"]));
        }

        [Fact]
        public void GroupLevelCycle_KeepsEveryGroup_ByPosition()
        {
            var c = new Canvas();
            var a = c.Comp("Addition", "A1", x: 0).In("A", optional: true).Out("R", items: 1);
            var b = c.Comp("Addition", "B1", x: 100).In("A", optional: true).Out("R", items: 1);
            c.Wire(a, 0, b, 0);
            c.Wire(b, 0, a, 0);
            c.Group("SECOND", 100, 0, b);
            c.Group("FIRST", 0, 0, a);
            var lines = Lines(OutlineView.Overview(c.Build())["groups"]);
            Assert.Equal(new[] { "FIRST", "SECOND" }, lines.Select(l => l.Split(' ')[0]));
        }

        // ---- qualified counts (report 1.6 defect 1) ------------------------------------------------

        [Theory]
        [InlineData(false, true, false, "off: this document's solver is disabled")]
        [InlineData(true, false, false, "off: Grasshopper's solver is disabled")]
        [InlineData(false, false, false, "off: this document's solver is disabled and Grasshopper's solver is disabled")]
        [InlineData(false, true, true, "off: this document's solver is disabled; the last solution was aborted")]
        [InlineData(true, true, true, "on; the last solution was aborted")]
        public void SolverNotPlainlyOn_IsStated_WithTheStaleNote(bool doc, bool global, bool aborted, string expected)
        {
            var snap = Golden(out var nodes, out var groups, new SolverFlags { DocumentEnabled = doc, SolutionsEnabled = global, LastSolutionAborted = aborted });
            var o = OutlineView.Overview(snap);
            Assert.Equal(expected, (string)o["solver"]);
            Assert.Equal(OutlineView.StaleNote, (string)o["note"]);
            var g = OutlineView.Groups(snap, new[] { groups["maths"].Id });
            Assert.Equal(expected, (string)g["solver"]);
            Assert.Equal(OutlineView.StaleNote, (string)g["note"]);
            Assert.Equal(expected, (string)OutlineView.Around(snap, nodes["result"].Id, 1)["solver"]);
        }

        [Fact]
        public void SolverOn_PrintsOn_WithNoNote_AndTheOtherViewsOmitIt()
        {
            var snap = Golden(out var nodes, out var groups);
            Assert.Equal("on", (string)OutlineView.Overview(snap)["solver"]);
            Assert.Null(OutlineView.Overview(snap)["note"]);
            Assert.Null(OutlineView.Groups(snap, new[] { groups["maths"].Id })["solver"]);
            Assert.Null(OutlineView.Around(snap, nodes["result"].Id, 1)["solver"]);
        }

        [Fact]
        public void SolverOff_EveryCountIsStale()
        {
            var snap = Golden(out _, out _, new SolverFlags { DocumentEnabled = false });
            var o = OutlineView.Overview(snap);
            var groups = Lines(o["groups"]);
            Assert.Equal("INPUTS · 2 objects · out: COUNT (1 stale) → MATHS, OUTPUT; NUMS (1 stale) → MATHS", groups[0]);
            Assert.Contains("out: RESULT (4 stale) → OUTPUT", groups[1]);
            Assert.Contains("ends: 00000007 Circle (0 stale), OLD (disabled)", (string)o["ungrouped"]);
        }

        [Fact]
        public void UnsolvedOwner_IsStale_FailedIsStale_LockedIsDisabled_AndTheSummaryCountsThem()
        {
            var c = new Canvas();
            var src = c.Slider("S", "1", "0", "2");
            var a = c.Comp("Series", "A", x: 1).In("C").Out("Series", "S", items: 5);
            var b = c.Comp("Series", "B", x: 2).In("C").Out("Series", "S", items: 0);
            b.Phase = NodePhase.NotComputed;
            var f = c.Comp("Series", "F", x: 3).In("C").Out("Series", "S", items: 0);
            f.Phase = NodePhase.Failed;
            var l = c.Comp("Series", "L", x: 4).In("C").Out("Series", "S", items: 7);
            l.Locked = true;
            l.Phase = NodePhase.NotComputed;
            foreach (var n in new[] { a, b, f, l }) c.Wire(src, 0, n, 0);

            var o = OutlineView.Overview(c.Build());

            Assert.Equal("5 objects · 0 groups · 0 errors · 0 warnings · 0 disconnected · 1 disabled · 2 not solved · 0 preview off", (string)o["summary"]);
            Assert.EndsWith("ends: A (5), B (0 stale), F (0 stale), L (disabled)", Lines(o["networks"]).Single());
            Assert.Equal(new[] { "L" }, Lines(o["disabled"]));
        }

        // ---- problems by the status rule (report 1.6 defects 3 and 6) ------------------------------

        [Fact]
        public void Problems_UseTheStatusRule_InStatusOrder_WithEveryMessageCounted()
        {
            var c = new Canvas();
            c.Comp("Circle", "WARN", x: 0).In("Radius", "R").Warning("w1", "w2", "w3");
            c.Comp("Circle", "DISC", x: -10).In("Plane", "P", optional: true).In("Radius", "R").In("Centre", "C");
            c.Comp("Circle", "ERR", x: 50).In("Radius", "R").Error(new string('x', 150));
            var param = c.Param("Number", "BADPARAM", x: 60).Error("bad data");
            var wiredEmpty = c.Comp("Circle", "WIREDEMPTY", x: 70).In("Radius", "R");
            c.Wire(param, 0, wiredEmpty, 0);
            c.Comp("Circle", "OPTIONAL", x: 80).In("Radius", "R", optional: true);
            var holdsData = c.Comp("Circle", "HOLDSDATA", x: 90).In("Radius", "R");
            holdsData.Inputs[0].Volatile = 1;

            var snap = c.Build();
            var o = OutlineView.Overview(snap);
            var problems = Lines(o["problems"]);

            Assert.Equal(new[]
            {
                $"ERR Circle error: {new string('x', 99)}…",
                "BADPARAM Number error: bad data",
                "WARN Circle warning: w1 (+2 more)",
                "DISC Circle disconnected: missing R, C"
            }, problems);
            // The same classification status uses, node for node.
            foreach (var n in snap.Nodes)
            {
                bool listed = problems.Any(p => p.StartsWith(n.NickName + " ", StringComparison.Ordinal));
                Assert.Equal(ObjectHealth.Classify(n).Status != HealthStatus.Ok, listed);
            }
            Assert.StartsWith("7 objects · 0 groups · 2 errors · 1 warning · 1 disconnected", (string)o["summary"]);
        }

        [Fact]
        public void ObjectLine_ShowsFlagsAndHealth()
        {
            var c = new Canvas();
            var n = c.Comp("Circle", "C1").In("Plane", "P", optional: true).In("Radius", "R");
            n.Locked = true;
            n.PreviewHidden = true;
            var g = c.Group("G", 0, 0, n);
            var line = Lines(OutlineView.Groups(c.Build(), new[] { g.Id })["groups"][0]["objects"]).Single();
            Assert.Equal("C1 Circle · disabled · preview off · disconnected: missing R", line);
        }

        // ---- caps ----------------------------------------------------------------------------------

        [Fact]
        public void EveryCappedOverviewField_EndsPlusNMore_AndMoreCountsIt()
        {
            var c = new Canvas();
            for (int i = 0; i < 21; i++) c.Slider($"S{i}", "1", "0", "2", x: i);
            for (int i = 0; i < 11; i++) c.Comp("Circle", $"E{i}", x: 100 + i).Error("boom");
            for (int i = 0; i < 21; i++) c.Comp("Circle", $"L{i}", x: 200 + i).Locked = true;
            for (int i = 0; i < 61; i++) c.Group($"G{i}", i, 0);

            var o = OutlineView.Overview(c.Build());

            void Check(string field, int max)
            {
                var lines = Lines(o[field]);
                Assert.Equal(max + 1, lines.Count);
                Assert.Equal("+1 more", lines.Last());
                Assert.Equal(1, (int)o["more"][field]);
            }
            Check("controls", OutlineView.MaxControls);
            Check("problems", OutlineView.MaxProblems);
            Check("disabled", OutlineView.MaxDisabled);
            Check("groups", OutlineView.MaxGroupLines);
            Assert.Contains("gh_inspect(action='status')", (string)o["next"]);
        }

        [Fact]
        public void UncappedOverview_HasNoMore_AndDoesNotSendAgentsToStatus()
        {
            var o = OutlineView.Overview(Golden(out _, out _));
            Assert.Null(o["more"]);
            Assert.DoesNotContain("status", (string)o["next"]);
        }

        [Fact]
        public void Networks_AreCapped()
        {
            var c = new Canvas();
            for (int i = 0; i < 11; i++) c.Wire(c.Param("Number", $"A{i}", x: i), 0, c.Param("Number", $"B{i}", x: i, y: 10), 0);
            var o = OutlineView.Overview(c.Build());
            Assert.Equal(OutlineView.MaxNetworks + 1, Lines(o["networks"]).Count);
            Assert.Equal("+1 more", Lines(o["networks"]).Last());
            Assert.Equal(1, (int)o["more"]["networks"]);
        }

        [Fact]
        public void InlineLists_EndPlusNMore()
        {
            var c = new Canvas();
            var sink = c.Comp("Merge", "SINK", x: 100).In("D");
            for (int i = 0; i < 10; i++) c.Wire(c.Param("Number", $"P{i}", x: i), 0, sink, 0);
            var g = c.Group("G", 0, 0, sink);
            var snap = c.Build();
            Assert.EndsWith("in ← P0, P1, P2, P3, P4, P5, P6, P7, +2 more", Lines(OutlineView.Overview(snap)["groups"]).Single());
            var line = Lines(OutlineView.Groups(snap, new[] { g.Id })["groups"][0]["objects"]).Single();
            Assert.Equal("SINK Merge · D ← P0, P1, P2, P3, P4, P5, P6, P7, +2 more", line);
        }

        [Fact]
        public void Values_AreClipped_NamesAreNot()
        {
            var c = new Canvas();
            var longNick = new string('N', 45);
            var p = c.Panel(longNick, "line one\nline two is quite a bit longer than thirty");
            var g = c.Group("G", 0, 0, p);
            var line = Lines(OutlineView.Groups(c.Build(), new[] { g.Id })["groups"][0]["objects"]).Single();
            Assert.Equal($"{longNick} Panel = \"line one / line two is quite…\"", line);
        }

        [Fact]
        public void UnwiredModifiers_InfrastructureOnlyWires_AndTruncatedPanels()
        {
            var c = new Canvas();
            var n = c.Comp("Circle", "C1").In("Radius", "R").In("Plane", "P");
            n.Inputs[0].Simplify = true;
            n.Inputs[0].Reverse = true;
            // Wired only to Cordyceps infrastructure: Grasshopper counts the source, the snapshot drops
            // it, so the input is neither listed as wired nor DISCONNECTED (status agrees).
            n.Inputs[1].SourceCountOverride = 1;
            var p = c.Panel("NOTE", "short");
            p.PanelTruncated = true;
            var g = c.Group("G", 0, 0, n, p);

            var lines = Lines(OutlineView.Groups(c.Build(), new[] { g.Id })["groups"][0]["objects"]);

            Assert.Equal(new[] { "C1 Circle · R (simplify, reverse) · disconnected: missing R", "NOTE Panel = \"short…\"" }, lines);
        }

        // ---- identifiers (report 1.6 defects 4 and 5) ----------------------------------------------

        [Fact]
        public void LongGroupName_IsPrintedWhole_Everywhere()
        {
            var name = new string('G', 30) + " " + new string('H', 29);
            Assert.Equal(60, name.Length);
            var c = new Canvas();
            var a = c.Param("Number", "A", x: 0);
            var b = c.Param("Number", "B", x: 100);
            c.Wire(a, 0, b, 0);
            c.Group(name, 0, 0, a);
            c.Group("OTHER", 100, 0, b);
            var snap = c.Build();
            var groups = Lines(OutlineView.Overview(snap)["groups"]);
            Assert.Equal($"{name} · 1 object · out: A (1) → OTHER", groups[0]);
            Assert.Equal($"A Number [{name}]", Lines(OutlineView.Around(snap, b.Id, 1)["objects"])[0]);
            Assert.Equal("B Number [OTHER] · ← A", Lines(OutlineView.Around(snap, b.Id, 1)["objects"])[1]);
            Assert.Equal(name, (string)OutlineView.Groups(snap, new[] { snap.Groups[0].Id })["groups"][0]["group"]);
        }

        [Fact]
        public void PortToken_IsNickname_ElseName_ElseIndex()
        {
            var ports = new[]
            {
                new CanvasPort { Name = "Alpha", NickName = "X" },
                new CanvasPort { Name = "Beta", NickName = "X" },
                new CanvasPort { Name = "In", NickName = "In" },
                new CanvasPort { Name = "In", NickName = "In" },
                new CanvasPort { Name = "Gamma", NickName = "G" }
            };
            Assert.Equal("Alpha", OutlineView.PortToken(ports, 0, true));
            Assert.Equal("Beta", OutlineView.PortToken(ports, 1, true));
            Assert.Equal("2", OutlineView.PortToken(ports, 2, true));
            Assert.Equal("3", OutlineView.PortToken(ports, 3, true));
            Assert.Equal("G", OutlineView.PortToken(ports, 4, true));
        }

        /// <summary>
        /// Every first token of every line, and every port token, read back through the resolver and
        /// the parameter-spec rule to the object or port it was printed for. The canvas is built to
        /// trip each fallback: shared nicknames, a hex nickname that is someone's prefix, default
        /// nicknames, an unnamed group, two groups with one name, ports that share nicknames and names.
        /// </summary>
        [Fact]
        public void EveryPrintedName_AndPortToken_RoundTrips()
        {
            var c = new Canvas();
            var s1 = c.Comp("Text Split", "Split", x: 0).In("Text", "T").Out("Result", "R", items: 1).Out("Rest", "R", items: 1);
            var s2 = c.Comp("Text Split", "Split", x: 0, y: 50).In("Text", "T").Out("Result", "R", items: 1);
            var hex = c.Comp("Addition", "00000001", x: 50).In("A", "X").In("B", "X").Out("Result", "R", items: 1);
            var dup = c.Comp("Merge", x: 100).In("In").In("In").In("Data", "D").Out("Result", "R", items: 1);
            var slider = c.Slider("Number Slider", "3", "0", "9", x: -50);
            slider.IsDefault = true;
            var panel = c.Panel("Panel", "txt", x: -50, y: 50);
            panel.IsDefault = true;
            var circle = c.Comp("Circle", x: 200).In("Radius", "R").In("Plane", "P").Out("Circle", "C").Out("Plane", "P");
            c.Wire(slider, 0, s1, 0);
            c.Wire(panel, 0, s2, 0);
            c.Wire(s1, 1, hex, 0);
            c.Wire(s2, 0, hex, 1);
            c.Wire(hex, 0, dup, 0);
            c.Wire(s1, 0, dup, 1);
            c.Wire(dup, 0, circle, 0);
            var sink = c.Comp("Line", "SINK", x: 300).In("A");
            c.Wire(circle, 1, sink, 0);
            var long60 = new string('L', 60);
            c.Group(long60, -50, 0, slider, panel);
            c.Group("DUP", 0, 0, s1, s2);
            c.Group("DUP", 50, 0, hex);
            c.Group("", 100, 0, dup, circle);
            var snap = c.Build();

            var heads = new List<string>();
            var groupHeads = new List<string>();
            var o = OutlineView.Overview(snap);
            heads.AddRange(Lines(o["controls"]));
            heads.AddRange(Lines(o["problems"]));
            groupHeads.AddRange(Lines(o["groups"]));
            foreach (var g in snap.Groups)
            {
                var detail = (JObject)OutlineView.Groups(snap, new[] { g.Id })["groups"][0];
                foreach (var line in Lines(detail["objects"]))
                    (line.Contains(" (group) · ") ? groupHeads : heads).Add(line);
                Assert.Equal(g.Id, ResolveGroup(snap, (string)detail["group"]));
                Assert.Equal(g.Id, ResolveGroup(snap, (string)detail["id"]));
            }
            foreach (var n in snap.Nodes)
                heads.AddRange(Lines(OutlineView.Around(snap, n.Id, 2)["objects"]));

            int portTokens = 0;
            foreach (var line in heads)
            {
                var node = ParseHead(snap, line);
                foreach (var f in line.Split(" · ").Skip(1))
                {
                    int arrow = f.IndexOf("← ", StringComparison.Ordinal);
                    if (arrow > 0 && !node.IsParameterObject)
                    {
                        var label = f.Substring(0, arrow).TrimEnd();
                        int paren = label.IndexOf(" (", StringComparison.Ordinal);
                        if (paren > 0) label = label.Substring(0, paren);
                        AssertPortRoundTrips(node.Inputs, label, true);
                        portTokens++;
                    }
                    if (f.StartsWith("disconnected: missing ", StringComparison.Ordinal))
                        foreach (var t in f.Substring("disconnected: missing ".Length).Split(", "))
                        {
                            AssertPortRoundTrips(node.Inputs, t, true);
                            portTokens++;
                        }
                }
            }

            int outputTokens = 0;
            foreach (var line in groupHeads)
            {
                var name = line.Split(" · ")[0];
                if (name.EndsWith(" (group)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - " (group)".Length);
                var id = ResolveGroup(snap, name);
                Assert.Equal(new DisplayNames(snap).DisplayName(id), name);
                // out: entries name an object and, when it has several outputs, one of its ports.
                foreach (var f in line.Split(" · ").Where(f => f.StartsWith("out: ", StringComparison.Ordinal)))
                    foreach (var entry in f.Substring(5).Split("; ").Where(e => !e.StartsWith("+", StringComparison.Ordinal)))
                    {
                        var head = entry.Substring(0, entry.IndexOf(" → ", StringComparison.Ordinal));
                        int open = head.LastIndexOf(" (", StringComparison.Ordinal);
                        var who = ParseHead(snap, head.Substring(0, open) + " ", allowBareName: true);
                        var inner = head.Substring(open + 2, head.Length - open - 3);
                        if (who.Outputs.Count > 1)
                        {
                            AssertPortRoundTrips(who.Outputs, inner.Substring(0, inner.IndexOf(": ", StringComparison.Ordinal)), false);
                            outputTokens++;
                        }
                    }
            }

            // Each fallback was exercised, not skipped.
            Assert.True(heads.Count > 20);
            Assert.True(portTokens > 5);
            Assert.True(outputTokens > 0);
            var all = string.Join("\n", heads.Concat(groupHeads));
            Assert.Contains($"{Short(s2)} Text Split", all);   // a shared nickname
            Assert.Contains($"{s1.Id:D} Text Split", all);     // its short id is another object's nickname, so the full GUID
            Assert.Contains($"{Short(dup)} Merge", all);       // a default nickname
            Assert.Contains(long60, all);
            Assert.Contains("A ← ", all);                       // a shared port nickname, so the name
            Assert.Contains(" · 0 ← ", all);                    // a shared port name and nickname, so the index
        }

        private static Guid ResolveGroup(CanvasSnapshot snap, string text)
        {
            var r = ObjectResolver.Resolve(snap, text, ResolveScope.Groups);
            Assert.True(r.IsResolved, $"'{text}': {r.Error}");
            return r.Entry.Id;
        }

        /// <summary>
        /// The node a line starts with: some prefix of the line, ending at a space, that resolves to
        /// a node, is that node's DisplayName, and is followed by its type (or, with
        /// <paramref name="allowBareName"/>, by nothing).
        /// </summary>
        private static CanvasNode ParseHead(CanvasSnapshot snap, string line, bool allowBareName = false)
        {
            var names = new DisplayNames(snap);
            for (int k = line.IndexOf(' '); k > 0; k = line.IndexOf(' ', k + 1))
            {
                var candidate = line.Substring(0, k);
                var r = ObjectResolver.Resolve(snap, candidate, ResolveScope.Objects);
                if (!r.IsResolved || r.Entry.IsGroup) continue;
                var node = snap.Node(r.Entry.Id);
                var rest = line.Substring(k + 1);
                if ((rest.StartsWith(node.TypeName, StringComparison.Ordinal) || (allowBareName && rest.Length == 0))
                    && names.DisplayName(node.Id) == candidate)
                    return node;
            }
            throw new Xunit.Sdk.XunitException($"no printed name at the start of: {line}");
        }

        private static void AssertPortRoundTrips(IReadOnlyList<CanvasPort> ports, string token, bool isInput)
        {
            var r = ParamSpecLookup.Resolve(token, ports.Select(p => new ParamIdentity(p.Name, p.NickName)).ToList(), isInput, "x");
            Assert.True(r.IsResolved, $"port token '{token}': {r.Error}");
            Assert.Equal(token, OutlineView.PortToken(ports, r.Index, isInput));
        }

        // ---- ordering cost -------------------------------------------------------------------------

        [Fact]
        public void FlowOrder_On2000Members_IsLinearithmic_NotQuadratic()
        {
            const int n = 2000;
            var ids = Enumerable.Range(1, n).Select(TestCanvas.Id).ToList();
            // A chain placed against the flow (each consumer left of its source), plus skip edges.
            var edges = new List<(Guid, Guid)>();
            for (int i = 0; i + 1 < n; i++) edges.Add((ids[i], ids[i + 1]));
            for (int i = 0; i + 7 < n; i += 3) edges.Add((ids[i], ids[i + 7]));
            var pos = ids.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => ((float)(n - t.i), 0f));
            var counter = new OutlineView.OrderCounter();

            var order = OutlineView.FlowOrder(ids, edges, id => pos[id], counter);

            Assert.Equal(ids, order);
            // Counted, not timed: (V + E) log2 V with a generous constant is under 1 M comparisons;
            // re-sorting the ready list on every pop costs n^2 log n, about 44 M.
            double bound = 20.0 * (n + edges.Count) * Math.Log(n, 2);
            Assert.True(counter.Comparisons < bound, $"{counter.Comparisons} comparisons");

            // All 2,000 ready at once (no edges) is the worst case for the queue.
            var flat = new OutlineView.OrderCounter();
            OutlineView.FlowOrder(ids, null, id => pos[id], flat);
            Assert.True(flat.Comparisons < 20.0 * n * Math.Log(n, 2), $"{flat.Comparisons} comparisons");
        }

        [Fact]
        public void GroupDetail_Of2000Members_OrdersUpstreamFirst_AndCaps()
        {
            var c = new Canvas();
            const int n = 2000;
            var nodes = new List<NodeSpec>();
            for (int i = 0; i < n; i++)
            {
                var node = c.Param("Number", $"N{i}", x: n - i);
                if (i > 0) c.Wire(nodes[i - 1], 0, node, 0);
                nodes.Add(node);
            }
            var g = c.Group("BIG", 0, 0, nodes.Cast<object>().ToArray());

            var o = OutlineView.Groups(c.Build(), new[] { g.Id });

            var lines = Lines(o["groups"][0]["objects"]);
            Assert.Equal(OutlineView.MaxObjectLines, lines.Count);
            Assert.Equal("N0 Number", lines[0]);
            Assert.Equal("N1 Number · ← N0", lines[1]);
            Assert.Equal(n - OutlineView.MaxObjectLines, (int)o["more"]["objects"]);
        }

        // ---- arguments -----------------------------------------------------------------------------

        [Theory]
        [InlineData("A", "G", 0, "Give id or group, not both")]
        [InlineData(null, null, 2, "depth only applies with id")]
        [InlineData(null, "G", 3, "depth only applies with id")]
        [InlineData("A", null, 11, "depth must be 1-10 (got 11)")]
        [InlineData("A", null, -1, "depth must be 1-10 (got -1)")]
        [InlineData(null, "[\"A\", ", 0, "group looks like a JSON array but does not parse")]
        [InlineData(null, "[1]", 0, "group[0] must be a string")]
        [InlineData(null, "[]", 0, "group is an empty JSON array")]
        public void Arguments_AreCheckedBeforeAnyRead(string id, string group, int depth, string error)
        {
            Assert.False(OutlineRequest.TryParse(id, group, depth, out var req, out var e));
            Assert.Null(req);
            Assert.StartsWith(error, e);
        }

        [Fact]
        public void Arguments_Defaults_AndModes()
        {
            Assert.True(OutlineRequest.TryParse(null, null, 0, out var overview, out _));
            Assert.Equal(OutlineMode.Overview, overview.Mode);
            Assert.True(OutlineRequest.TryParse("", "", 0, out var empty, out _));
            Assert.Equal(OutlineMode.Overview, empty.Mode);
            Assert.True(OutlineRequest.TryParse("A", null, 0, out var around, out _));
            Assert.Equal(OutlineRequest.DefaultDepth, around.Depth);
            Assert.True(OutlineRequest.TryParse("A", null, 10, out var deep, out _));
            Assert.Equal(10, deep.Depth);
            Assert.True(OutlineRequest.TryParse(null, "PLAIN NAME", 0, out var one, out _));
            Assert.Equal(new[] { "PLAIN NAME" }, one.GroupTexts);
        }

        [Fact]
        public void Id_OfAGroup_ShowsThatGroup_AndAnObjectNameShowsAround()
        {
            var snap = Golden(out _, out var groups);

            Assert.True(OutlineRequest.TryParse(Short(groups["maths"]), null, 0, out var byGroupId, out _));
            Assert.True(byGroupId.TryResolve(snap, out var t1, out _));
            Assert.Equal(OutlineMode.Groups, t1.Mode);
            Assert.Equal("MATHS", (string)t1.Render(snap)["groups"][0]["group"]);

            // depth means "wires around one object"; with a group's id it is refused, not dropped.
            Assert.True(OutlineRequest.TryParse(Short(groups["maths"]), null, 3, out var groupWithDepth, out _));
            Assert.False(groupWithDepth.TryResolve(snap, out _, out var depthError));
            Assert.Contains("this id is a group", depthError);

            Assert.True(OutlineRequest.TryParse("RESULT", null, 1, out var byName, out _));
            Assert.True(byName.TryResolve(snap, out var t2, out _));
            Assert.Equal(OutlineMode.Around, t2.Mode);
            Assert.Equal("RESULT", (string)t2.Render(snap)["around"]);

            // A group's name is not an object name: the resolver's hint, and no fallback.
            Assert.True(OutlineRequest.TryParse("MATHS", null, 0, out var groupName, out _));
            Assert.False(groupName.TryResolve(snap, out _, out var e));
            Assert.Contains("'MATHS' is a group", e);
        }

        [Fact]
        public void Numbers_AreInvariant_UnderACommaDecimalCulture()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var expected = GoldenOverview;
                Assert.Equal(expected, OutlineView.Overview(Golden(out _, out _)).ToString(Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        private const string GoldenOverview = @"{
  ""success"": true,
  ""document"": ""test.gh"",
  ""summary"": ""9 objects · 4 groups · 1 error · 1 warning · 1 disconnected · 1 disabled · 0 not solved · 1 preview off"",
  ""solver"": ""on"",
  ""controls"": [
    ""COUNT Number Slider = 4 (0..10) [INPUTS]""
  ],
  ""groups"": [
    ""INPUTS · 2 objects · out: COUNT (1) → MATHS, OUTPUT; NUMS (1) → MATHS"",
    ""MATHS · 3 objects · 1 error · contains: INNER (1 object) · shares 1 object with OUTPUT · in ← NUMS, COUNT · out: RESULT (4) → OUTPUT"",
    ""OUTPUT · 2 objects · 1 warning · shares 1 object with MATHS · in ← 00000003 Series, COUNT""
  ],
  ""ungrouped"": ""3 objects · inputs: NOTE = \""read me\"" · ends: 00000007 Circle (0), OLD (disabled)"",
  ""problems"": [
    ""SUM Addition [MATHS] error: Data conversion failed from Text to Number (+1 more)"",
    ""00000006 Split List [OUTPUT] warning: Index out of range"",
    ""00000007 Circle disconnected: missing R""
  ],
  ""disabled"": [
    ""OLD""
  ],
  ""next"": ""group='NAME' shows how one group is wired inside (a JSON list for several); id='NAME' shows what feeds one object and what it feeds (depth=N wires each way, default 2)""
}";
        private const string GoldenGroup = @"{
  ""success"": true,
  ""groups"": [
    {
      ""group"": ""MATHS"",
      ""id"": ""00001003"",
      ""summary"": ""3 objects · 1 error"",
      ""in"": [
        ""NUMS [INPUTS]"",
        ""COUNT [INPUTS]""
      ],
      ""objects"": [
        ""00000003 Series · S ← NUMS [INPUTS] · C ← COUNT [INPUTS]"",
        ""SUM Addition · A ← 00000003 Series · error: Data conversion failed from Text to Number (+1 more)"",
        ""INNER (group) · 1 object · in ← 00000003 Series · out: RESULT (4) → OUTPUT""
      ],
      ""out"": [
        ""RESULT (4) → OUTPUT""
      ]
    }
  ]
}";
        private const string GoldenAround = @"{
  ""success"": true,
  ""around"": ""RESULT"",
  ""depth"": 1,
  ""objects"": [
    ""00000003 Series [MATHS] · S ← NUMS [INPUTS] · C ← COUNT [INPUTS]"",
    ""RESULT Number [INNER, OUTPUT] · ← 00000003 Series"",
    ""00000006 Split List [OUTPUT] · L ← RESULT · i (graft) ← COUNT [INPUTS] · warning: Index out of range""
  ],
  ""fedFrom"": [
    ""NUMS [INPUTS]"",
    ""COUNT [INPUTS]""
  ],
  ""feeds"": [
    ""SUM [MATHS]""
  ]
}";
    }
}
