using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Cordyceps.Core.Canvas;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// <see cref="ObjectHealth"/>, the one rule <c>status</c> and <c>outline</c> share: Error, then
    /// Warning, then DISCONNECTED (a required input with no source and no data), else OK. Remarks are
    /// ignored and parameter objects never read as DISCONNECTED. The message text is the one
    /// <c>status</c> prints, byte for byte.
    /// </summary>
    public class ObjectHealthTests
    {
        private static HealthInput Required(string name, int sources = 0, int data = 0) => new HealthInput(name, false, sources, data);
        private static HealthInput Optional(string name) => new HealthInput(name, true, 0, 0);

        [Fact]
        public void ErrorLevel_IsError_WithEveryErrorJoined()
        {
            var r = ObjectHealth.Classify(MessageLevel.Error, new[] { "bad A", "bad B" }, new[] { "warn" }, new[] { Required("X") }, true);
            Assert.Equal(HealthStatus.Error, r.Status);
            Assert.Equal("ERROR", r.StatusText);
            Assert.Equal(new[] { "bad A", "bad B" }, r.Messages);
            Assert.Equal("bad A; bad B", r.Message);
            Assert.Empty(r.MissingInputs);
        }

        [Fact]
        public void WarningLevel_IsWarning_AndOutranksDisconnected()
        {
            var r = ObjectHealth.Classify(MessageLevel.Warning, null, new[] { "1. Input parameter A failed to collect data" },
                new[] { Required("A") }, true);
            Assert.Equal(HealthStatus.Warning, r.Status);
            Assert.Equal("WARNING", r.StatusText);
            Assert.Equal("1. Input parameter A failed to collect data", r.Message);
            Assert.Empty(r.MissingInputs);
        }

        [Fact]
        public void RequiredInputWithNoSourceAndNoData_IsDisconnected_ListingItsName()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("A"), Required("B", sources: 1), Required("C") }, true);
            Assert.Equal(HealthStatus.Disconnected, r.Status);
            Assert.Equal("DISCONNECTED", r.StatusText);
            Assert.Equal(new[] { "A", "C" }, r.MissingInputs);
            Assert.Equal("Missing: A, C", r.Message);
        }

        [Fact]
        public void OptionalInputs_AreIgnored()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Optional("A"), Required("B", sources: 1) }, true);
            Assert.Equal(HealthStatus.Ok, r.Status);
            Assert.Equal("OK", r.StatusText);
            Assert.Equal(string.Empty, r.Message);
        }

        [Fact]
        public void InputWiredWithNoData_IsNotDisconnected_BecauseItHasASource()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("A", sources: 1, data: 0) }, true);
            Assert.Equal(HealthStatus.Ok, r.Status);
        }

        [Fact]
        public void InputWithStoredDataButNoSource_IsNotDisconnected()
        {
            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("A", sources: 0, data: 1) }, true);
            Assert.Equal(HealthStatus.Ok, r.Status);
        }

        [Fact]
        public void Remarks_AreIgnored()
        {
            Assert.Equal(HealthStatus.Ok, ObjectHealth.Classify(MessageLevel.Remark, null, null, new[] { Optional("A") }, true).Status);
            Assert.Equal(HealthStatus.Disconnected, ObjectHealth.Classify(MessageLevel.Remark, null, null, new[] { Required("A") }, true).Status);
        }

        [Fact]
        public void ParameterObject_GetsOnlyErrorAndWarning_NeverDisconnected()
        {
            Assert.Equal(HealthStatus.Ok, ObjectHealth.Classify(MessageLevel.None, null, null, new[] { Required("Number") }, false).Status);
            Assert.Equal(HealthStatus.Warning, ObjectHealth.Classify(MessageLevel.Warning, null, new[] { "w" }, null, false).Status);
            Assert.Equal(HealthStatus.Error, ObjectHealth.Classify(MessageLevel.Error, new[] { "e" }, null, null, false).Status);
        }

        [Fact]
        public void SnapshotNode_UsesTheSameRule()
        {
            var component = new CanvasNode
            {
                Kind = NodeKind.Component,
                Inputs = new[]
                {
                    new CanvasPort { Name = "Radius", NickName = "R" },
                    new CanvasPort { Name = "Plane", NickName = "P", Optional = true }
                }
            };
            var r = ObjectHealth.Classify(component);
            Assert.Equal(HealthStatus.Disconnected, r.Status);
            Assert.Equal("Missing: Radius", r.Message); // by Name, as status prints it

            var param = new CanvasNode { Kind = NodeKind.Panel, Inputs = new[] { new CanvasPort { Name = "Panel" } } };
            Assert.Equal(HealthStatus.Ok, ObjectHealth.Classify(param).Status);

            var failing = new CanvasNode { Kind = NodeKind.Slider, MessageLevel = MessageLevel.Error, Errors = new[] { "x", "y" } };
            Assert.Equal("x; y", ObjectHealth.Classify(failing).Message);
        }

        [Fact]
        public void ObjectWithNoSolveState_IsAlwaysOk_EvenWithRequiredInputsUnset()
        {
            var scribble = new CanvasNode
            {
                Kind = NodeKind.Component,
                HasSolveState = false,
                Inputs = new[] { new CanvasPort { Name = "A" } },
                MessageLevel = MessageLevel.Error,
                Errors = new[] { "x" }
            };

            Assert.Equal(HealthStatus.Ok, ObjectHealth.Classify(scribble).Status);
        }

        [Fact]
        public void NamesDetailNode_CannotBeClassified()
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => ObjectHealth.Classify(new CanvasNode { Detail = CanvasDetail.Names }));
            Assert.Contains("Classify", ex.Message);
            Assert.Contains("Names detail", ex.Message);
        }

        [Fact]
        public void NullMessageLists_AreTreatedAsEmpty()
        {
            var r = ObjectHealth.Classify(MessageLevel.Error, null, null, null, true);
            Assert.Equal(HealthStatus.Error, r.Status);
            Assert.Equal(string.Empty, r.Message);
            Assert.Throws<ArgumentNullException>(() => ObjectHealth.Classify((CanvasNode)null));
        }

        // gh_inspect(action='disconnected') applies this predicate per input, whatever the level.
        [Theory]
        [InlineData(false, 0, 0, true)]   // required, unwired, empty: missing
        [InlineData(true, 0, 0, false)]   // optional
        [InlineData(false, 1, 0, false)]  // wired (any source counts, infrastructure included)
        [InlineData(false, 0, 3, false)]  // holds data (internalised or persistent)
        public void IsMissing_IsTheRuleClassifyUses(bool optional, int sources, int items, bool missing)
        {
            var input = new HealthInput("A", optional, sources, items);
            Assert.Equal(missing, ObjectHealth.IsMissing(input));

            var r = ObjectHealth.Classify(MessageLevel.None, null, null, new[] { input }, isComponent: true);
            Assert.Equal(missing ? HealthStatus.Disconnected : HealthStatus.Ok, r.Status);
        }

        // ---- the population status lists and outline counts (issue #106) ----

        [Theory]
        [InlineData(false, true, true)]    // a component or a parameter object
        [InlineData(false, false, false)]  // a scribble or sketch: no solve state
        [InlineData(true, false, false)]   // a group
        [InlineData(true, true, false)]    // a group is never listed, whatever else it is
        public void IsSubject_IsEveryNonGroupObjectWithSolveState(bool isGroup, bool hasSolveState, bool subject)
        {
            Assert.Equal(subject, ObjectHealth.IsSubject(isGroup, hasSolveState));
        }

        [Theory]
        [InlineData(NodeKind.Component)]
        [InlineData(NodeKind.Parameter)]
        [InlineData(NodeKind.Slider)]
        [InlineData(NodeKind.Toggle)]
        [InlineData(NodeKind.ValueList)]
        [InlineData(NodeKind.Panel)]
        [InlineData(NodeKind.Other)]
        public void IsSubject_OfANode_DependsOnSolveStateOnly_NotOnKind(NodeKind kind)
        {
            Assert.True(ObjectHealth.IsSubject(new CanvasNode { Kind = kind }));
            Assert.False(ObjectHealth.IsSubject(new CanvasNode { Kind = kind, HasSolveState = false }));
            Assert.Throws<ArgumentNullException>(() => ObjectHealth.IsSubject((CanvasNode)null));
        }

        [Fact]
        public void StatusPopulation_MatchesOutline_WarningOnASliderCountsInBoth()
        {
            var id = 0;
            CanvasNode Node(NodeKind kind, string nick, MessageLevel level = MessageLevel.None, bool solves = true, params CanvasPort[] inputs) =>
                new CanvasNode
                {
                    Id = TestCanvas.Id(++id), Kind = kind, NickName = nick, TypeName = kind.ToString(), X = id,
                    HasSolveState = solves, Phase = NodePhase.Computed, MessageLevel = level,
                    Warnings = level == MessageLevel.Warning ? new[] { "w" } : Array.Empty<string>(),
                    Errors = level == MessageLevel.Error ? new[] { "e" } : Array.Empty<string>(),
                    Inputs = inputs, Outputs = new[] { new CanvasPort { Name = "R" } }
                };
            var nodes = new[]
            {
                Node(NodeKind.Slider, "WALL", MessageLevel.Warning),
                Node(NodeKind.Panel, "NOTE", MessageLevel.Error),
                Node(NodeKind.Parameter, "PTS", MessageLevel.Warning, true, new CanvasPort { Name = "Point" }),
                Node(NodeKind.Component, "CIRCLE", MessageLevel.None, true, new CanvasPort { Name = "Radius" }),
                Node(NodeKind.Component, "OK"),
                Node(NodeKind.Other, "SCRIBBLE", MessageLevel.Error, solves: false)
            };
            var snap = new CanvasSnapshot("t.gh", new SolverFlags(), CanvasDetail.Full, nodes, Array.Empty<CanvasGroup>(), Array.Empty<Guid>());

            // What status lists: the subjects, each classified by the one rule.
            var listed = snap.Nodes.Where(ObjectHealth.IsSubject).ToDictionary(n => n.NickName, n => ObjectHealth.Classify(n).StatusText);
            Assert.Equal(new[] { "WALL", "NOTE", "PTS", "CIRCLE", "OK" }, listed.Keys);
            Assert.Equal("WARNING", listed["WALL"]);
            Assert.Equal("ERROR", listed["NOTE"]);
            Assert.Equal("WARNING", listed["PTS"]);      // a parameter object with an unwired input is never DISCONNECTED
            Assert.Equal("DISCONNECTED", listed["CIRCLE"]);
            Assert.Equal("OK", listed["OK"]);

            // Outline's counts and problems agree with that list, object for object.
            var o = OutlineView.Overview(snap);
            int errors = listed.Values.Count(s => s == "ERROR"), warnings = listed.Values.Count(s => s == "WARNING");
            int disconnected = listed.Values.Count(s => s == "DISCONNECTED");
            Assert.StartsWith($"6 objects · 0 groups · {errors} error · {warnings} warnings · {disconnected} disconnected", (string)o["summary"]);
            var problems = o["problems"].Values<string>().ToList();
            Assert.Equal(listed.Count(kv => kv.Value != "OK"), problems.Count);
            Assert.Contains("WALL · Slider · warning: w", problems);
            Assert.DoesNotContain(problems, p => p.StartsWith("SCRIBBLE", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("Wall Height", "Number Slider", "Wall Height")]
        [InlineData("", "Number Slider", "Number Slider")]      // nobody named it: the type stands in
        [InlineData(null, "Panel", "Panel")]
        [InlineData("Panel", "Panel", "Panel")]
        [InlineData("", null, "")]
        public void LabelFor_IsTheNickname_OrTheTypeNameWhenThereIsNone(string nickName, string typeName, string expected)
        {
            Assert.Equal(expected, ObjectHealth.LabelFor(nickName, typeName));
        }
    }
}
