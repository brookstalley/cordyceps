using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="SolveWait"/>: the bounded, lock-free wait for a scheduled solve.
    ///
    /// <para>The budget boundary is tested on a fake clock whose sleep advances it, so it is exact.
    /// The timing contracts (waits while a solve runs, gives up on one that never ends, wakes when
    /// one ends mid-wait) run the waiter on a background task against a real
    /// <see cref="SolverState"/>, with a solve that does not end during the assertion window.</para>
    /// </summary>
    public class SolveWaitTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Utc);

        /// <summary>A clock that moves only when the waiter sleeps.</summary>
        private sealed class SleepClock
        {
            public DateTime Now = T0;
            public readonly List<TimeSpan> Sleeps = new List<TimeSpan>();
            public DateTime Read() => Now;
            public void Sleep(TimeSpan by)
            {
                Sleeps.Add(by);
                Now += by;
            }
        }

        private static SolveObservation Reading(long generation, bool solving, bool aborted = false)
            => new SolveObservation(true, new SolveRecord(generation, TimeSpan.FromSeconds(1), aborted), solving);

        // ---------------------------------------------------------------- the answered rule

        [Fact]
        public void IsAnswered_OnlyWhenTheGenerationPassedTheTicket_AndNothingIsSolving()
        {
            var ticket = new SolveTicket(Guid.NewGuid(), 3);

            Assert.False(SolveWait.IsAnswered(ticket, Reading(3, solving: false)));
            Assert.False(SolveWait.IsAnswered(ticket, Reading(2, solving: false)));
            Assert.False(SolveWait.IsAnswered(ticket, Reading(4, solving: true)));
            Assert.False(SolveWait.IsAnswered(ticket, SolveObservation.Untracked));
            Assert.False(SolveWait.IsAnswered(ticket, null));
            Assert.True(SolveWait.IsAnswered(ticket, Reading(4, solving: false)));
            Assert.True(SolveWait.IsAnswered(ticket, Reading(9, solving: false)));
        }

        // ---------------------------------------------------------------- fake clock: exact boundaries

        [Fact]
        public void Wait_AlreadyAnswered_ReturnsCompletedWithoutSleeping()
        {
            var clock = new SleepClock();
            var ticket = new SolveTicket(Guid.NewGuid(), 0);

            var result = SolveWait.Wait(ticket, _ => Reading(1, false, aborted: true), TimeSpan.FromSeconds(5),
                TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);

            Assert.Equal(SolveWaitOutcome.Completed, result.Outcome);
            Assert.True(result.Record.Aborted);
            Assert.Equal(TimeSpan.Zero, result.Elapsed);
            Assert.Empty(clock.Sleeps);
        }

        [Fact]
        public void Wait_StillSolvingAtTheDeadline_IsOverBudget_AtExactlyTheBudget()
        {
            var clock = new SleepClock();
            var ticket = new SolveTicket(Guid.NewGuid(), 2);

            var result = SolveWait.Wait(ticket, _ => Reading(2, true), TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);

            Assert.Equal(SolveWaitOutcome.OverBudget, result.Outcome);
            Assert.Null(result.Record);
            Assert.Equal(TimeSpan.FromMilliseconds(250), result.Elapsed);
            // The last sleep is trimmed to the budget left, so the wait never overshoots it.
            Assert.Equal(new[] { 100, 100, 50 }, clock.Sleeps.ConvertAll(s => (int)s.TotalMilliseconds));
        }

        [Fact]
        public void Wait_IdleAtTheDeadline_WithNoFinishedSolve_IsNotStarted()
        {
            // Nothing solving and the generation never passed the ticket: the scheduled solve never
            // ran. Reporting "still running" would send the caller to wait for nothing.
            var clock = new SleepClock();
            var ticket = new SolveTicket(Guid.NewGuid(), 2);

            var result = SolveWait.Wait(ticket, _ => Reading(2, false), TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);

            Assert.Equal(SolveWaitOutcome.NotStarted, result.Outcome);
            Assert.Null(result.Record);
            Assert.Equal(TimeSpan.FromMilliseconds(250), result.Elapsed);
        }

        [Fact]
        public void Wait_TheDeadlineReadingDecides_NotAnEarlierOne()
        {
            // Solving early in the wait, idle at the deadline with nothing finished: not started
            // is judged on the last reading, and the reverse case is over budget.
            var clock = new SleepClock();
            var ticket = new SolveTicket(Guid.NewGuid(), 0);
            var readings = 0;

            var idleAtEnd = SolveWait.Wait(ticket, _ => Reading(0, ++readings < 3), TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);
            Assert.Equal(SolveWaitOutcome.NotStarted, idleAtEnd.Outcome);

            readings = 0;
            var solvingAtEnd = SolveWait.Wait(ticket, _ => Reading(0, ++readings >= 3), TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);
            Assert.Equal(SolveWaitOutcome.OverBudget, solvingAtEnd.Outcome);
        }

        [Fact]
        public void Wait_CompletesOnTheReadingWhereTheGenerationPasses_NotBefore()
        {
            var clock = new SleepClock();
            var ticket = new SolveTicket(Guid.NewGuid(), 0);
            var readings = 0;

            var result = SolveWait.Wait(ticket, _ => ++readings < 4 ? Reading(0, false) : Reading(1, false),
                TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);

            Assert.Equal(SolveWaitOutcome.Completed, result.Outcome);
            Assert.Equal(4, readings);
            Assert.Equal(TimeSpan.FromMilliseconds(300), result.Elapsed);
        }

        [Fact]
        public void Wait_ZeroBudget_ChecksOnce()
        {
            var clock = new SleepClock();
            var ticket = new SolveTicket(Guid.NewGuid(), 0);

            var result = SolveWait.Wait(ticket, _ => Reading(0, true), TimeSpan.Zero,
                TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);

            Assert.Equal(SolveWaitOutcome.OverBudget, result.Outcome);
            Assert.Empty(clock.Sleeps);
        }

        [Fact]
        public void Wait_UntrackedDocument_IsGone_AtOnce()
        {
            var clock = new SleepClock();
            var state = new SolverState(clock.Read);

            var result = SolveWait.Wait(new SolveTicket(Guid.NewGuid(), 0), state.Observe, TimeSpan.FromSeconds(10),
                TimeSpan.FromMilliseconds(100), clock.Read, clock.Sleep);

            Assert.Equal(SolveWaitOutcome.Gone, result.Outcome);
            Assert.Empty(clock.Sleeps);
        }

        [Fact]
        public void Wait_ClockSteppingBackwards_NeverReportsNegativeElapsed()
        {
            var now = T0;
            var ticket = new SolveTicket(Guid.NewGuid(), 0);
            var sleeps = 0;

            var result = SolveWait.Wait(ticket, _ => Reading(0, false), TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(100), () => now,
                by =>
                {
                    // First sleep: the clock jumps back a minute. Later sleeps advance it.
                    now = ++sleeps == 1 ? now.AddMinutes(-1) : now + by;
                });

            Assert.Equal(SolveWaitOutcome.NotStarted, result.Outcome);
            Assert.True(result.Elapsed >= TimeSpan.Zero);
        }

        // ---------------------------------------------------------------- real concurrency

        [Fact]
        public async Task Wait_SolveThatNeverEnds_ReportsOverBudget_AndDoesNotReturnEarly()
        {
            // A background "solve" begins and then blocks on a signal that never completes during
            // the test. The waiter, on its own task, must give up at its budget and not before.
            var state = new SolverState();
            var doc = Guid.NewGuid();
            var ticket = state.TakeTicket(doc);
            var neverEnds = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var solving = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var solve = Task.Run(async () =>
            {
                state.BeginSolution(doc, "slow.gh");
                solving.SetResult(true);
                await neverEnds.Task;
                state.EndSolution(doc);
            });
            await solving.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var budget = TimeSpan.FromMilliseconds(300);
            var stopwatch = Stopwatch.StartNew();
            var result = await Task.Run(() => SolveWait.Wait(ticket, state.Observe, budget))
                .WaitAsync(TimeSpan.FromSeconds(10));
            stopwatch.Stop();

            Assert.Equal(SolveWaitOutcome.OverBudget, result.Outcome);
            Assert.True(stopwatch.Elapsed >= budget - TimeSpan.FromMilliseconds(20),
                $"returned after {stopwatch.Elapsed.TotalMilliseconds} ms, before its {budget.TotalMilliseconds} ms budget");
            Assert.True(state.IsSolving(doc));
            Assert.False(solve.IsCompleted);

            neverEnds.SetResult(true);
            await solve.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task Wait_SolveEndingMidWait_CompletesWithItsRecord_NotBefore()
        {
            var state = new SolverState();
            var doc = Guid.NewGuid();
            var ticket = state.TakeTicket(doc);
            state.BeginSolution(doc, "a.gh");

            var waiter = Task.Run(() => SolveWait.Wait(ticket, state.Observe, TimeSpan.FromSeconds(30)));

            // The solve is still running: the waiter must still be waiting after several polls.
            var early = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromMilliseconds(200)));
            Assert.NotSame(waiter, early);

            state.EndSolution(doc, TimeSpan.FromMilliseconds(200), aborted: true);

            var result = await waiter.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(SolveWaitOutcome.Completed, result.Outcome);
            Assert.True(result.Record.Aborted);
            Assert.Equal(1, result.Record.Generation);
        }

        [Fact]
        public async Task Wait_GenerationAdvancedWhileStillSolving_IsNotDone_UntilThatSolveEnds()
        {
            // A solve ends and at once another begins (a chained schedule). The new generation
            // alone must not end the wait while the document is still marked solving.
            var state = new SolverState();
            var doc = Guid.NewGuid();
            var ticket = state.TakeTicket(doc);
            state.BeginSolution(doc, "a.gh");
            state.EndSolution(doc);
            state.BeginSolution(doc, "a.gh");

            var waiter = Task.Run(() => SolveWait.Wait(ticket, state.Observe, TimeSpan.FromSeconds(30)));

            var early = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromMilliseconds(200)));
            Assert.NotSame(waiter, early);

            state.EndSolution(doc);

            var result = await waiter.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(SolveWaitOutcome.Completed, result.Outcome);
            Assert.Equal(2, result.Record.Generation);
        }

        [Fact]
        public async Task Wait_ScheduledSolveThatNeverStarts_ReportsNotStarted_AtItsBudget()
        {
            // The ticket is taken, but no solve ever begins (a schedule that was dropped).
            var state = new SolverState();
            var doc = Guid.NewGuid();
            var ticket = state.TakeTicket(doc);

            var budget = TimeSpan.FromMilliseconds(300);
            var stopwatch = Stopwatch.StartNew();
            var result = await Task.Run(() => SolveWait.Wait(ticket, state.Observe, budget))
                .WaitAsync(TimeSpan.FromSeconds(10));
            stopwatch.Stop();

            Assert.Equal(SolveWaitOutcome.NotStarted, result.Outcome);
            Assert.True(stopwatch.Elapsed >= budget - TimeSpan.FromMilliseconds(20),
                $"returned after {stopwatch.Elapsed.TotalMilliseconds} ms, before its {budget.TotalMilliseconds} ms budget");
        }

        [Fact]
        public async Task Wait_AcrossANestedSolve_CompletesOnlyWhenTheOutermostSolveEnds()
        {
            // begin, begin (a solution started inside the first), then the inner end while the
            // waiter runs: the waiter must keep waiting until the outer end.
            var state = new SolverState();
            var doc = Guid.NewGuid();
            var ticket = state.TakeTicket(doc);
            state.BeginSolution(doc, "outer.gh");
            state.BeginSolution(doc, "outer.gh");

            var waiter = Task.Run(() => SolveWait.Wait(ticket, state.Observe, TimeSpan.FromSeconds(30)));

            state.EndSolution(doc);
            var early = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromMilliseconds(250)));
            Assert.NotSame(waiter, early);
            Assert.True(state.IsSolving(doc));

            state.EndSolution(doc, TimeSpan.FromSeconds(2));

            var result = await waiter.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(SolveWaitOutcome.Completed, result.Outcome);
            Assert.Equal(1, result.Record.Generation);
            Assert.Equal(TimeSpan.FromSeconds(2), result.Record.Span);
        }

        [Fact]
        public async Task Wait_DocumentForgottenMidWait_ReportsGone()
        {
            var state = new SolverState();
            var doc = Guid.NewGuid();
            var ticket = state.TakeTicket(doc);
            state.BeginSolution(doc, "closing.gh");

            var waiter = Task.Run(() => SolveWait.Wait(ticket, state.Observe, TimeSpan.FromSeconds(30)));

            var early = await Task.WhenAny(waiter, Task.Delay(TimeSpan.FromMilliseconds(150)));
            Assert.NotSame(waiter, early);

            state.ForgetDocument(doc);

            var result = await waiter.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(SolveWaitOutcome.Gone, result.Outcome);
            Assert.Null(result.Record);
        }
    }
}
