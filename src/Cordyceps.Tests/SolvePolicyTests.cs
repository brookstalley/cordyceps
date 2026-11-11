using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="SolvePolicy"/>: the table from solver switches and wait outcomes to a
    /// reported state, a remedy and a reply shape, plus the strict <c>timeout</c> input.
    /// </summary>
    public class SolvePolicyTests
    {
        private static IEnumerable<SolveState> AllStates => Enum.GetValues(typeof(SolveState)).Cast<SolveState>();

        // ---------------------------------------------------------------- timeout (strict input)

        [Theory]
        [InlineData(null, 30)]
        [InlineData("", 30)]
        [InlineData("1", 1)]
        [InlineData("30", 30)]
        [InlineData("300", 300)]
        [InlineData("+45", 45)]
        [InlineData("120.0", 120)]
        [InlineData("300.00", 300)]
        [InlineData("1.0", 1)]
        [InlineData("1.2e2", 120)]
        [InlineData("6E1", 60)]
        public void TryParseTimeout_AcceptsAbsentOrInRange(string raw, int expected)
        {
            Assert.True(SolvePolicy.TryParseTimeout(raw, out var seconds, out var error));
            Assert.Equal(expected, seconds);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("301")]
        [InlineData("-5")]
        [InlineData("99999999999999999999")]
        [InlineData("0.0")]
        [InlineData("301.0")]
        [InlineData("1e9")]
        public void TryParseTimeout_RefusesOutOfRange_AsOutOfRange(string raw)
        {
            Assert.False(SolvePolicy.TryParseTimeout(raw, out _, out var error));
            Assert.StartsWith("timeout must be 1-300 seconds", error);
            Assert.Contains(raw, error);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("30s")]
        [InlineData("2.5")]
        [InlineData(" 30")]
        [InlineData("-")]
        [InlineData("٣٠")] // Arabic-Indic digits: a number to a culture-aware parser, not to this contract
        [InlineData("120.5")]
        [InlineData("1.25e1")]
        [InlineData("120.")]
        [InlineData(".5")]
        [InlineData("1e")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("1,000")]
        public void TryParseTimeout_RefusesNonNumeric_AsNotAWholeNumber(string raw)
        {
            Assert.False(SolvePolicy.TryParseTimeout(raw, out _, out var error));
            Assert.StartsWith("timeout must be a whole number of seconds, 1-300", error);
        }

        // ---------------------------------------------------------------- switches

        [Fact]
        public void Refusal_BothOn_SchedulesASolve()
        {
            Assert.Null(SolvePolicy.Refusal(documentEnabled: true, solutionsEnabled: true));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Refusal_GlobalSwitchOff_IsSolutionsLocked_WhateverTheDocumentSays(bool documentEnabled)
        {
            // With the global switch off Grasshopper raises no solution events, so a wait would
            // never end: this state must stop the solve before a ticket is taken.
            Assert.Equal(SolveState.SolutionsLocked, SolvePolicy.Refusal(documentEnabled, solutionsEnabled: false));
        }

        [Fact]
        public void Refusal_OnlyDocumentSwitchOff_IsSolverDisabled()
        {
            Assert.Equal(SolveState.SolverDisabled, SolvePolicy.Refusal(documentEnabled: false, solutionsEnabled: true));
        }

        [Fact]
        public void Refusal_SwitchesOnButNotWatched_IsNotWatched()
        {
            // A solve whose end cannot be seen is not scheduled: a waiter could only ever report a
            // spent budget about it.
            Assert.Equal(SolveState.NotWatched, SolvePolicy.Refusal(true, true, watching: () => false));
            Assert.Null(SolvePolicy.Refusal(true, true, watching: () => true));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void Refusal_ASwitchOff_NeverAsksAboutWatching(bool documentEnabled, bool solutionsEnabled)
        {
            // Asking can start watching (adopting) a document, which a refused solve must not do.
            var asked = 0;
            var refusal = SolvePolicy.Refusal(documentEnabled, solutionsEnabled, watching: () => { asked++; return false; });

            Assert.NotNull(refusal);
            Assert.NotEqual(SolveState.NotWatched, refusal);
            Assert.Equal(0, asked);
        }

        // ---------------------------------------------------------------- wait outcomes

        [Theory]
        [InlineData(SolveWaitOutcome.Completed, false, SolveState.Completed)]
        [InlineData(SolveWaitOutcome.Completed, true, SolveState.Aborted)]
        [InlineData(SolveWaitOutcome.OverBudget, false, SolveState.OverBudget)]
        [InlineData(SolveWaitOutcome.OverBudget, true, SolveState.OverBudget)]
        [InlineData(SolveWaitOutcome.NotStarted, false, SolveState.NotStarted)]
        [InlineData(SolveWaitOutcome.Gone, false, SolveState.DocumentClosed)]
        public void FromWait_MapsEachOutcomeToOneState(SolveWaitOutcome outcome, bool aborted, SolveState expected)
        {
            Assert.Equal(expected, SolvePolicy.FromWait(outcome, aborted));
        }

        [Fact]
        public void Recomputed_IsTrueOnlyForCompleted()
        {
            foreach (var state in AllStates)
                Assert.Equal(state == SolveState.Completed, SolvePolicy.Recomputed(state));
        }

        [Fact]
        public void ReadsMessages_OnlyOnceASolveHasEnded()
        {
            // Reading during an over-budget solve would queue behind it on the UI thread.
            var reads = AllStates.Where(SolvePolicy.ReadsMessages).ToHashSet();
            Assert.Equal(new HashSet<SolveState> { SolveState.Completed, SolveState.Aborted }, reads);
        }

        // ---------------------------------------------------------------- names and remedies

        [Fact]
        public void Name_IsDistinctForEveryState_AndMatchesTheDocumentedWireNames()
        {
            var names = AllStates.Select(SolvePolicy.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.Equal("completed", SolvePolicy.Name(SolveState.Completed));
            Assert.Equal("aborted", SolvePolicy.Name(SolveState.Aborted));
            Assert.Equal("over_budget", SolvePolicy.Name(SolveState.OverBudget));
            Assert.Equal("solver_disabled", SolvePolicy.Name(SolveState.SolverDisabled));
            Assert.Equal("solutions_locked", SolvePolicy.Name(SolveState.SolutionsLocked));
            Assert.Equal("document_closed", SolvePolicy.Name(SolveState.DocumentClosed));
            Assert.Equal("scheduled", SolvePolicy.Name(SolveState.Scheduled));
            Assert.Equal("not_started", SolvePolicy.Name(SolveState.NotStarted));
            Assert.Equal("not_watched", SolvePolicy.Name(SolveState.NotWatched));
        }

        [Fact]
        public void Remedy_NoneWhenThereIsNothingToDo_OneForEveryOtherState()
        {
            foreach (var state in AllStates)
            {
                var remedy = SolvePolicy.Remedy(state);
                if (state == SolveState.Completed || state == SolveState.Scheduled)
                    Assert.Null(remedy);
                else
                    Assert.False(string.IsNullOrWhiteSpace(remedy), $"{state} has no remedy");
            }
        }

        [Fact]
        public void Remedy_OverBudget_SaysItCannotCancel_AndNamesTheProbe()
        {
            var remedy = SolvePolicy.Remedy(SolveState.OverBudget);
            Assert.Contains("still running", remedy);
            Assert.Contains("cannot cancel", remedy);
            Assert.Contains("gh_inspect(action='connection')", remedy);
            Assert.Contains("gh_inspect(action='status')", remedy);
        }

        [Fact]
        public void Remedy_NotStarted_SaysTheSolveNeverRan_AndHowToCheck()
        {
            var remedy = SolvePolicy.Remedy(SolveState.NotStarted);
            Assert.Contains("never ran", remedy);
            Assert.Contains("Lock Solver", remedy);
            Assert.Contains("gh_inspect(action='connection')", remedy);
            Assert.Contains("retry", remedy);
            Assert.DoesNotContain("still running", remedy);
        }

        [Fact]
        public void Remedy_NotWatched_PointsAtTheBridgeComponent()
        {
            var remedy = SolvePolicy.Remedy(SolveState.NotWatched);
            Assert.Contains("Cordyceps component", remedy);
            Assert.Contains("scheduled none", remedy);
        }

        [Fact]
        public void Remedy_NamesWhoCanFlipEachSwitch()
        {
            // The document switch is one Cordyceps can turn back on; the global one needs a human.
            Assert.Contains("gh_document(action='solver', enabled=true)", SolvePolicy.Remedy(SolveState.SolverDisabled));
            var locked = SolvePolicy.Remedy(SolveState.SolutionsLocked);
            Assert.Contains("human", locked);
            Assert.DoesNotContain("enabled=true", locked);
        }

        [Fact]
        public void Remedy_LockedWithTheDocumentAlsoOff_SaysBothNeedTurningOn()
        {
            var both = SolvePolicy.Remedy(SolveState.SolutionsLocked, documentEnabled: false);
            Assert.Contains("human", both);
            Assert.Contains("gh_document(action='solver', enabled=true)", both);
        }

        // ---------------------------------------------------------------- reply shapes

        [Fact]
        public void RecomputeReply_Completed_IsSuccessWithCountsAndNoRemedy()
        {
            var reply = JObject.Parse(SolvePolicy.RecomputeReply(SolveState.Completed, 1234, 2, 1));

            Assert.True(reply.Value<bool>("success"));
            Assert.True(reply.Value<bool>("recomputed"));
            Assert.Null(reply["error"]);
            var solve = (JObject)reply["solve"];
            Assert.Equal("completed", solve.Value<string>("state"));
            Assert.Equal(1234, solve.Value<long>("elapsedMs"));
            Assert.Equal(2, solve.Value<int>("errors"));
            Assert.Equal(1, solve.Value<int>("warnings"));
            Assert.Null(solve["remedy"]);
        }

        [Theory]
        [InlineData(SolveState.Aborted)]
        [InlineData(SolveState.OverBudget)]
        [InlineData(SolveState.SolverDisabled)]
        [InlineData(SolveState.SolutionsLocked)]
        [InlineData(SolveState.DocumentClosed)]
        [InlineData(SolveState.NotStarted)]
        [InlineData(SolveState.NotWatched)]
        public void RecomputeReply_AnythingButCompleted_IsAFailureCarryingTheStateAndRemedy(SolveState state)
        {
            var reply = JObject.Parse(SolvePolicy.RecomputeReply(state, 5000, null, null));

            Assert.False(reply.Value<bool>("success"));
            Assert.False(reply.Value<bool>("recomputed"));
            Assert.Contains(SolvePolicy.Name(state), reply.Value<string>("error"));
            var solve = (JObject)reply["solve"];
            Assert.Equal(SolvePolicy.Name(state), solve.Value<string>("state"));
            Assert.Equal(SolvePolicy.Remedy(state), solve.Value<string>("remedy"));
        }

        [Fact]
        public void RecomputeReply_LeavesUnknownCountsOut()
        {
            // An over-budget solve has not ended, so there is nothing true to count yet.
            var solve = (JObject)JObject.Parse(SolvePolicy.RecomputeReply(SolveState.OverBudget, 30000, null, null))["solve"];

            Assert.Null(solve["errors"]);
            Assert.Null(solve["warnings"]);
            Assert.Equal(30000, solve.Value<long>("elapsedMs"));
        }

        [Fact]
        public void FromWaitResult_ReportsTheWaitAndGrasshoppersOwnSolveTime()
        {
            var completed = SolvePolicy.FromWaitResult(new SolveWaitResult(SolveWaitOutcome.Completed,
                new SolveRecord(3, TimeSpan.FromMilliseconds(1500), aborted: false), TimeSpan.FromMilliseconds(1620)));
            Assert.Equal(SolveState.Completed, completed.State);
            Assert.Equal(1620, completed.ElapsedMs);
            Assert.Equal(1500, completed.SolveMs);

            var aborted = SolvePolicy.FromWaitResult(new SolveWaitResult(SolveWaitOutcome.Completed,
                new SolveRecord(3, TimeSpan.FromMilliseconds(400), aborted: true), TimeSpan.FromMilliseconds(450)));
            Assert.Equal(SolveState.Aborted, aborted.State);
            Assert.Equal(400, aborted.SolveMs);
        }

        [Theory]
        [InlineData(SolveWaitOutcome.OverBudget)]
        [InlineData(SolveWaitOutcome.NotStarted)]
        [InlineData(SolveWaitOutcome.Gone)]
        public void FromWaitResult_WithoutAFinishedSolve_HasNoSolveTime(SolveWaitOutcome outcome)
        {
            var result = SolvePolicy.FromWaitResult(new SolveWaitResult(outcome, null, TimeSpan.FromSeconds(30)));

            Assert.Null(result.SolveMs);
            Assert.Equal(30000, result.ElapsedMs);
            Assert.Null(JObject.Parse(SolvePolicy.RecomputeReply(result))["solve"]["solveMs"]);
        }

        [Fact]
        public void FromWaitResult_ASolveGrasshopperDidNotTime_HasNoSolveTime()
        {
            // A disabled document's end carries no span of its own, so none is reported.
            var result = SolvePolicy.FromWaitResult(new SolveWaitResult(SolveWaitOutcome.Completed,
                new SolveRecord(1, null, aborted: false), TimeSpan.FromMilliseconds(40)));

            Assert.Null(result.SolveMs);
        }

        [Fact]
        public void RecomputeReply_CarriesSolveMsBesideElapsedMs()
        {
            var solve = (JObject)JObject.Parse(SolvePolicy.RecomputeReply(
                new SolveOutcome(SolveState.Completed, elapsedMs: 1620, solveMs: 1500, errors: 0, warnings: 0)))["solve"];

            Assert.Equal(1620, solve.Value<long>("elapsedMs"));
            Assert.Equal(1500, solve.Value<long>("solveMs"));
        }

        [Fact]
        public void WaitedReply_CarriesTheActionsFields_AndFailsOnAnythingButCompleted()
        {
            // solver enabled=true and revert wait like recompute: their own fields stay, success
            // follows the solve.
            var ok = JObject.Parse(SolvePolicy.WaitedReply(
                new SolveOutcome(SolveState.Completed, 900, 850, 0, 1), new JObject { ["solverEnabled"] = true }));
            Assert.True(ok.Value<bool>("success"));
            Assert.True(ok.Value<bool>("solverEnabled"));
            Assert.Null(ok["error"]);
            Assert.Null(ok["recomputed"]);
            Assert.Equal("completed", ok["solve"].Value<string>("state"));
            Assert.Equal(1, ok["solve"].Value<int>("warnings"));

            var late = JObject.Parse(SolvePolicy.WaitedReply(
                new SolveOutcome(SolveState.OverBudget, 30000), new JObject { ["revertedTo"] = "before" }));
            Assert.False(late.Value<bool>("success"));
            Assert.Equal("before", late.Value<string>("revertedTo"));
            Assert.Contains("over_budget", late.Value<string>("error"));
            Assert.Equal(SolvePolicy.Remedy(SolveState.OverBudget), late["solve"].Value<string>("remedy"));
        }

        [Fact]
        public void WithCounts_KeepsTheRestOfTheOutcome()
        {
            var counted = new SolveOutcome(SolveState.Aborted, 10, 8, documentEnabled: false).WithCounts(2, 3);

            Assert.Equal(SolveState.Aborted, counted.State);
            Assert.Equal(10, counted.ElapsedMs);
            Assert.Equal(8, counted.SolveMs);
            Assert.Equal(2, counted.Errors);
            Assert.Equal(3, counted.Warnings);
            Assert.False(counted.DocumentEnabled);
        }

        [Fact]
        public void ScheduledField_IsScheduled_OrTheSwitchThatStoppedIt()
        {
            var scheduled = SolvePolicy.ScheduledField(null);
            Assert.Equal("scheduled", scheduled.Value<string>("state"));
            Assert.Null(scheduled["remedy"]);
            Assert.Null(scheduled["elapsedMs"]);

            var locked = SolvePolicy.ScheduledField(SolveState.SolutionsLocked, documentEnabled: false);
            Assert.Equal("solutions_locked", locked.Value<string>("state"));
            Assert.Equal(SolvePolicy.Remedy(SolveState.SolutionsLocked, documentEnabled: false), locked.Value<string>("remedy"));

            var disabled = SolvePolicy.ScheduledField(SolveState.SolverDisabled);
            Assert.Equal("solver_disabled", disabled.Value<string>("state"));

            var unwatched = SolvePolicy.ScheduledField(SolveState.NotWatched);
            Assert.Equal("not_watched", unwatched.Value<string>("state"));
            Assert.Equal(SolvePolicy.Remedy(SolveState.NotWatched), unwatched.Value<string>("remedy"));
        }
    }
}
