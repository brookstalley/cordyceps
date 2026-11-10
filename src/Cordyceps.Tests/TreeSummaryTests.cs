using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core.DataTrees;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for the host-free parts of gh_inspect(action='tree'): the summary, its notes, the
    /// input-alignment check, the status wording, and argument validation.
    /// </summary>
    public class TreeSummaryTests
    {
        private static TreeBranch B(int count, params int[] path) => new TreeBranch(path, count);

        private static List<string> Notes(JObject o) => o["notes"]?.Values<string>().ToList() ?? new List<string>();

        // ---- TreeSummary ----

        [Fact]
        public void Describe_FlatList_IsOneBranch()
        {
            var o = TreeSummary.Describe(new[] { B(6, 0) });
            Assert.Equal(1, (int)o["branches"]);
            Assert.Equal(6, (int)o["items"]);
            Assert.Equal(1, (int)o["depth"]);
            Assert.Equal(6, (int)o["perBranch"]);
            Assert.Equal(new[] { "{0}: 6" }, o["paths"].Values<string>());
            Assert.Null(o["notes"]);
        }

        [Fact]
        public void Describe_Grafted_GroupsIntoOneRunAndSaysSo()
        {
            var branches = Enumerable.Range(0, 10).Select(i => B(1, 0, i)).ToList();
            var o = TreeSummary.Describe(branches);
            Assert.Equal(10, (int)o["branches"]);
            Assert.Equal(10, (int)o["items"]);
            Assert.Equal(2, (int)o["depth"]);
            Assert.Equal(new[] { "{0;0}..{0;9}: 1 each" }, o["paths"].Values<string>());
            Assert.Contains("one item per branch (grafted shape)", Notes(o));
        }

        [Fact]
        public void Runs_BreakOnCountChangeGapOrNewParent()
        {
            var runs = TreeSummary.Runs(new[]
            {
                B(2, 0, 0), B(2, 0, 1), B(2, 0, 2),
                B(1, 0, 3),
                B(1, 0, 5),
                B(1, 1, 0), B(1, 1, 1)
            });
            Assert.Equal(new[] { "{0;0}..{0;2}: 2 each", "{0;3}: 1", "{0;5}: 1", "{1;0}..{1;1}: 1 each" }, runs);
        }

        [Fact]
        public void Describe_UnevenBranches_ReportsMinAndMax()
        {
            var o = TreeSummary.Describe(new[] { B(3, 0), B(0, 1), B(5, 2) });
            Assert.Equal(0, (int)o["perBranch"]["min"]);
            Assert.Equal(5, (int)o["perBranch"]["max"]);
            Assert.Equal(1, (int)o["emptyBranches"]);
            Assert.Contains("1 empty branch", Notes(o));
        }

        [Fact]
        public void Describe_MixedDepths_IsFlaggedWithoutSimplifyAdvice()
        {
            var o = TreeSummary.Describe(new[] { B(1, 0), B(1, 0, 1) });
            Assert.Equal(1, (int)o["depth"]["min"]);
            Assert.Equal(2, (int)o["depth"]["max"]);
            var note = Assert.Single(Notes(o), n => n.StartsWith("paths have different depths"));
            Assert.DoesNotContain("Simplify", note);
        }

        [Fact]
        public void Describe_SharedLeadingIndices_NoteIsWordedPerGuideAndMakesNoSimplifyClaim()
        {
            var branches = Enumerable.Range(0, 4).Select(i => B(2, 0, 0, i)).ToList();
            var note = Assert.Single(Notes(TreeSummary.Describe(branches)), n => n.StartsWith("the first 2 path indices are the same"));
            Assert.Contains("Shift Paths", note);
            Assert.DoesNotContain("Simplify", note);
        }

        [Fact]
        public void Describe_SingleBranchOfDepthThree_HasNoSharedIndexNote()
        {
            var o = TreeSummary.Describe(new[] { B(3, 0, 0, 0) });
            Assert.DoesNotContain(Notes(o), n => n.Contains("path index"));
            Assert.Null(o["notes"]);
        }

        [Fact]
        public void Describe_Nulls_AreCounted()
        {
            var o = TreeSummary.Describe(new[] { new TreeBranch(new[] { 0 }, 3, nulls: 1) });
            Assert.Equal(1, (int)o["nulls"]);
            Assert.Null(o["invalid"]);
            Assert.Contains("1 null item", Notes(o));
        }

        [Fact]
        public void Describe_Invalid_AreCountedSeparatelyFromNulls()
        {
            var o = TreeSummary.Describe(new[] { new TreeBranch(new[] { 0 }, 5, nulls: 1, invalid: 2), new TreeBranch(new[] { 1 }, 1, invalid: 1) });
            Assert.Equal(3, (int)o["invalid"]);
            Assert.Equal(1, (int)o["nulls"]);
            Assert.Equal(6, (int)o["items"]);
            Assert.Contains(Notes(o), n => n.StartsWith("3 invalid items"));
        }

        [Fact]
        public void Describe_Empty_IsJustZeros()
        {
            var o = TreeSummary.Describe(new List<TreeBranch>());
            Assert.Equal(0, (int)o["branches"]);
            Assert.Equal(0, (int)o["items"]);
            Assert.Null(o["paths"]);
        }

        [Fact]
        public void Describe_ManyRuns_AreCappedAndCounted()
        {
            var branches = Enumerable.Range(0, 60).Select(i => B(i % 2 + 1, i)).ToList();
            var o = TreeSummary.Describe(branches, maxRuns: 20);
            Assert.Equal(20, ((JArray)o["paths"]).Count);
            Assert.Equal(40, (int)o["morePathGroups"]);
        }

        [Fact]
        public void Describe_CapOfExactlyOneHundred_IsHonoured()
        {
            var branches = Enumerable.Range(0, 150).Select(i => B(i % 2 + 1, i)).ToList();
            var o = TreeSummary.Describe(branches, maxRuns: 100);
            Assert.Equal(100, ((JArray)o["paths"]).Count);
            Assert.Equal(50, (int)o["morePathGroups"]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(501)]
        public void Describe_OutOfRangeCap_IsRefusedNotClamped(int cap)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TreeSummary.Describe(new[] { B(1, 0) }, cap));
        }

        [Fact]
        public void PathText_UsesInvariantDigits()
        {
            Assert.Equal("{1234;5}", B(1, 1234, 5).PathText);
        }

        // ---- TreeAlignment ----

        private static LabeledTree L(string label, params TreeBranch[] branches) => new LabeledTree(label, branches);

        private static TreeBranch[] Flat(int n) => Enumerable.Range(0, n).Select(i => B(1, i)).ToArray();

        [Fact]
        public void Alignment_DifferingBranchCounts_NamesBoth()
        {
            var note = TreeAlignment.Check(new[] { L("A", Flat(3)), L("B", Flat(12)) });
            Assert.Equal("inputs don't line up: A has 3 branches, B has 12 branches", note);
        }

        [Fact]
        public void Alignment_EqualCountsDifferentPaths_NamesBothAndTheFirstDifference()
        {
            var a = new[] { B(1, 0), B(1, 1) };
            var b = new[] { B(1, 0), B(1, 5) };
            var note = TreeAlignment.Check(new[] { L("A", a), L("B", b) });
            Assert.Equal("inputs have the same number of branches (2) but different paths: at branch 1, A has {1} and B has {5}", note);
        }

        [Fact]
        public void Alignment_OneInputWithASingleBranch_GivesNoNote()
        {
            Assert.Null(TreeAlignment.Check(new[] { L("A", Flat(1)), L("B", Flat(12)) }));
        }

        [Fact]
        public void Alignment_SinglePortOrIdenticalPaths_GivesNoNote()
        {
            Assert.Null(TreeAlignment.Check(new[] { L("A", Flat(4)) }));
            Assert.Null(TreeAlignment.Check(new[] { L("A", Flat(4)), L("B", Flat(4)) }));
        }

        [Fact]
        public void Alignment_IgnoresSingleBranchInputsWhenOthersDiffer()
        {
            var note = TreeAlignment.Check(new[] { L("A", Flat(3)), L("S", Flat(1)), L("B", Flat(12)) });
            Assert.Equal("inputs don't line up: A has 3 branches, B has 12 branches", note);
        }

        // ---- TreeStatusText ----

        [Fact]
        public void SolverOff_OnReturnsNull_AndEachRouteHasItsOwnWords()
        {
            Assert.Null(TreeStatusText.SolverOff(true, true, false));
            Assert.Null(TreeStatusText.SolverOff(true, true, true));
            Assert.Equal("off: this document's solver is disabled", TreeStatusText.SolverOff(false, true, false).Value.Text);
            Assert.Equal("off: Grasshopper's solver is disabled", TreeStatusText.SolverOff(true, false, false).Value.Text);
            Assert.Equal("off: Grasshopper's solver is disabled; the last solution was aborted", TreeStatusText.SolverOff(true, false, true).Value.Text);
            Assert.Equal("off: this document's solver is disabled and Grasshopper's solver is disabled",
                TreeStatusText.SolverOff(false, false, false).Value.Text);
        }

        [Fact]
        public void OwnerNote_ExplainsEachState_AndNeverAsksAboutTheSolver()
        {
            Assert.Null(TreeStatusText.OwnerNote(OwnerState.Computed, null));
            Assert.Equal("not solved: this document's solver is disabled, so the tree may be empty or out of date",
                TreeStatusText.OwnerNote(OwnerState.NotComputed, "this document's solver is disabled"));
            foreach (OwnerState s in Enum.GetValues(typeof(OwnerState)))
                Assert.DoesNotContain("Is the solver on", TreeStatusText.OwnerNote(s, null) ?? "");
            Assert.Contains("locked", TreeStatusText.OwnerNote(OwnerState.Locked, null));
            Assert.Contains("failed", TreeStatusText.OwnerNote(OwnerState.Failed, null));
        }

        [Fact]
        public void FormatMessage_ClipsAtOneSixtyAndCountsTheRest()
        {
            var text = TreeStatusText.FormatMessage("Warning", new[] { new string('x', 300), "b", "c" });
            Assert.Equal("Warning: " + new string('x', 159) + "… (+2 more)", text);
            Assert.Equal("Error: short", TreeStatusText.FormatMessage("Error", new[] { "short" }));
            Assert.Null(TreeStatusText.FormatMessage("Error", new string[0]));
            var exact = new string('y', 160);
            Assert.Equal("Error: " + exact, TreeStatusText.FormatMessage("Error", new[] { exact }));
        }

        // ---- LimitRule ----

        [Theory]
        [InlineData(0, true, 100)]
        [InlineData(1, true, 1)]
        [InlineData(100, true, 100)]
        [InlineData(5000, true, 5000)]
        [InlineData(-1, false, 100)]
        public void LogLimit_ZeroIsDefault_NegativeIsError(int limit, bool ok, int expected)
        {
            Assert.Equal(ok, LimitRule.TryResolveLog(limit, out var count, out var error));
            Assert.Equal(ok, error == null);
            Assert.Equal(expected, count);
        }

        [Theory]
        [InlineData(0, true, 20)]
        [InlineData(1, true, 1)]
        [InlineData(100, true, 100)]
        [InlineData(500, true, 500)]
        [InlineData(501, false, 20)]
        [InlineData(-3, false, 20)]
        public void TreeLimit_ZeroIsDefault_ExplicitIsExact_OutOfRangeIsError(int limit, bool ok, int expected)
        {
            Assert.Equal(ok, LimitRule.TryResolveTree(limit, out var runs, out var error));
            Assert.Equal(ok, error == null);
            Assert.Equal(expected, runs);
        }

        // ---- TreeRequestParser ----

        private static TreeRequest Parse(string id = null, string ids = null, string param = null, string side = null, int limit = 0)
        {
            Assert.True(TreeRequestParser.TryParse(id, ids, param, side, limit, out var request, out var error), error);
            return request;
        }

        private static string Fail(string id = null, string ids = null, string param = null, string side = null, int limit = 0)
        {
            Assert.False(TreeRequestParser.TryParse(id, ids, param, side, limit, out var request, out var error));
            Assert.Null(request);
            return error;
        }

        [Fact]
        public void Parse_SingleId_DefaultsToOutputAndTwentyRuns()
        {
            var r = Parse(id: "abc12345");
            var t = Assert.Single(r.Targets);
            Assert.Equal("abc12345", t.Id);
            Assert.Equal("id", t.Label);
            Assert.Null(t.Param);
            Assert.False(r.InputSide);
            Assert.Equal(20, r.MaxRuns);
        }

        [Fact]
        public void Parse_NeverTrimsAnIdOrParam()
        {
            // The name rule reads text exactly as given (" COUNT" is not COUNT, and the resolver
            // suggests the right spelling); tree must not be the one tool that trims.
            var single = Parse(id: " abc12345 ", param: " R ");
            Assert.Equal(" abc12345 ", single.Targets[0].Id);
            Assert.Equal(" R ", single.Targets[0].Param);

            var many = Parse(ids: "[\" a1\", {\"id\": \"b2 \", \"param\": \" X\"}]");
            Assert.Equal(new[] { " a1", "b2 " }, many.Targets.Select(t => t.Id));
            Assert.Equal(" X", many.Targets[1].Param);
        }

        [Fact]
        public void Parse_IdWithParamSideAndLimit()
        {
            var r = Parse(id: "abc", param: "Result", side: "INPUT", limit: 100);
            Assert.True(r.InputSide);
            Assert.Equal(100, r.MaxRuns);
            Assert.Equal("Result", r.Targets[0].Param);
        }

        [Fact]
        public void Parse_Ids_StringsAndObjectsWithLabels()
        {
            var r = Parse(ids: "[\"a1\", {\"id\": \"b2\", \"param\": \"X\"}, {\"id\":\"c3\",\"param\":0}]");
            Assert.Equal(new[] { "ids[0]", "ids[1]", "ids[2]" }, r.Targets.Select(t => t.Label));
            Assert.Equal(new[] { "a1", "b2", "c3" }, r.Targets.Select(t => t.Id));
            Assert.Equal(new string[] { null, "X", "0" }, r.Targets.Select(t => t.Param));
        }

        [Fact]
        public void Parse_Ids_DateShapedTextStaysAnId()
        {
            // Json.NET reads date-shaped strings as dates unless told not to; a nickname can look like one.
            var r = Parse(ids: "[\"2026-01-01T00:00:00\", {\"id\": \"2026-01-01T00:00:00Z\"}]");
            Assert.Equal(new[] { "2026-01-01T00:00:00", "2026-01-01T00:00:00Z" }, r.Targets.Select(t => t.Id));
        }

        [Fact]
        public void Parse_Ids_PortIndexBeyondInt64_IsALabelledError()
        {
            var error = Fail(ids: "[{\"id\": \"a1\", \"param\": 99999999999999999999999}]");
            Assert.StartsWith("ids[0]: 'param' index", error);
            Assert.Contains("out of range", error);
        }

        [Fact]
        public void Parse_Ids_TextAfterTheArray_IsAnError()
        {
            Assert.Contains("not valid JSON", Fail(ids: "[\"a1\"] [\"b2\"]"));
        }

        [Fact]
        public void Parse_ExactlyOneOfIdOrIds()
        {
            Assert.Contains("not both", Fail(id: "a", ids: "[\"b\"]"));
            Assert.Contains("needs 'id'", Fail());
        }

        [Fact]
        public void Parse_ParamWithIdsIsAnError()
        {
            Assert.Contains("'param' goes with 'id'", Fail(ids: "[\"a\"]", param: "X"));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{\"id\": \"a\"}")]
        [InlineData("[]")]
        [InlineData("[\"a\"] extra")]
        [InlineData("[\"\"]")]
        [InlineData("[3]")]
        [InlineData("[null]")]
        [InlineData("[{\"param\": \"X\"}]")]
        [InlineData("[{\"id\": \"a\", \"side\": \"input\"}]")]
        [InlineData("[{\"id\": \"a\", \"param\": \"\"}]")]
        [InlineData("[{\"id\": \"a\", \"param\": true}]")]
        [InlineData("[{\"id\": \"a\", \"id\": \"b\"}]")]
        [InlineData("[{\"ID\": \"a\"}]")]
        public void Parse_MalformedIds_AreErrorsNeverAnEmptyList(string ids)
        {
            Assert.False(string.IsNullOrEmpty(Fail(ids: ids)));
        }

        [Fact]
        public void Parse_IdsErrorNamesTheEntry()
        {
            Assert.StartsWith("ids[1]:", Fail(ids: "[\"ok\", 5]"));
            Assert.StartsWith("ids[0]: unknown key 'x'", Fail(ids: "[{\"id\": \"a\", \"x\": 1}]"));
        }

        [Fact]
        public void Parse_FiftyEntriesAccepted_FiftyOneRefused()
        {
            string Many(int n) => "[" + string.Join(",", Enumerable.Range(0, n).Select(i => "\"a" + i + "\"")) + "]";
            Assert.Equal(50, Parse(ids: Many(50)).Targets.Count);
            Assert.Contains("1-50", Fail(ids: Many(51)));
        }

        [Theory]
        [InlineData("both")]
        [InlineData("")]
        [InlineData("outputs")]
        public void Parse_BadSide_IsAnError(string side)
        {
            Assert.Contains("side must be", Fail(id: "a", side: side));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(501)]
        public void Parse_BadLimit_IsAnError(int limit)
        {
            Assert.Contains("limit must be 1-500", Fail(id: "a", limit: limit));
        }

        [Fact]
        public void Parse_BlankIdOrParam_IsAnError()
        {
            Assert.Contains("'id' is empty", Fail(id: "  "));
            Assert.Contains("'param' is empty", Fail(id: "a", param: " "));
        }
    }
}
