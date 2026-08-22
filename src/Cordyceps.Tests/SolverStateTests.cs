using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SolverState"/> — the cached liveness state that lets an MCP caller
    /// tell a busy solver from a dead bridge without ever marshaling to the Rhino UI thread.
    ///
    /// <para>The clock is injected in every test so staleness is exact rather than timing-dependent
    /// (the suite disables parallelization precisely because wall-clock tests flake).</para>
    /// </summary>
    public class SolverStateTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan Stale = TimeSpan.FromSeconds(5);

        /// <summary>A clock the test advances by hand.</summary>
        private sealed class FakeClock
        {
            public DateTime Now = T0;
            public DateTime Read() => Now;
            public void Advance(TimeSpan by) => Now += by;
        }

        private static SolverState NewState(FakeClock clock) =>
            new SolverState(clock.Read, Stale);

        // ---------------------------------------------------------------- solve transitions

        [Fact]
        public void NewState_IsIdle_WithNoHeartbeat()
        {
            var state = NewState(new FakeClock());

            Assert.False(state.AnySolving);
            Assert.Equal(0, state.SolvingCount);
            Assert.Null(state.ActiveSolve);
            Assert.Null(state.LastHeartbeatUtc);
            Assert.Null(state.ActiveDocument);
        }

        [Fact]
        public void BeginSolution_MarksThatDocumentSolving()
        {
            var clock = new FakeClock();
            var state = NewState(clock);
            var doc = Guid.NewGuid();

            state.BeginSolution(doc, "wall-study.gh");

            Assert.True(state.AnySolving);
            Assert.True(state.IsSolving(doc));
            Assert.False(state.IsSolving(Guid.NewGuid()));
            Assert.Equal("wall-study.gh", state.ActiveSolve.DocumentName);
            Assert.Equal(T0, state.ActiveSolve.StartedUtc);
        }

        [Fact]
        public void EndSolution_ClearsThatDocument()
        {
            var state = NewState(new FakeClock());
            var doc = Guid.NewGuid();

            state.BeginSolution(doc, "a.gh");
            state.EndSolution(doc);

            Assert.False(state.AnySolving);
            Assert.False(state.IsSolving(doc));
            Assert.Null(state.ActiveSolve);
        }

        [Fact]
        public void EndSolution_ForUnknownDocument_IsNoOp()
        {
            // A missed SolutionStart must not make the matching SolutionEnd throw on the UI thread.
            var state = NewState(new FakeClock());

            state.EndSolution(Guid.NewGuid());

            Assert.False(state.AnySolving);
        }

        [Fact]
        public void EndSolution_Twice_IsNoOp()
        {
            var state = NewState(new FakeClock());
            var doc = Guid.NewGuid();

            state.BeginSolution(doc, "a.gh");
            state.EndSolution(doc);
            state.EndSolution(doc);

            Assert.False(state.AnySolving);
        }

        [Fact]
        public void BeginSolution_Reentrant_KeepsOriginalStartTime()
        {
            // solving_since must report how long the caller has been waiting, so a re-raised or
            // nested start event must not restart the clock.
            var clock = new FakeClock();
            var state = NewState(clock);
            var doc = Guid.NewGuid();

            state.BeginSolution(doc, "a.gh");
            clock.Advance(TimeSpan.FromSeconds(30));
            state.BeginSolution(doc, "a.gh");

            Assert.Equal(T0, state.ActiveSolve.StartedUtc);
        }

        [Fact]
        public void BeginSolution_Reentrant_WithNullName_KeepsKnownName()
        {
            var state = NewState(new FakeClock());
            var doc = Guid.NewGuid();

            state.BeginSolution(doc, "named.gh");
            state.BeginSolution(doc, null);

            Assert.Equal("named.gh", state.ActiveSolve.DocumentName);
        }

        [Fact]
        public void ActiveSolve_WithTwoDocuments_ReportsTheEarliestStarted()
        {
            // The caller has been waiting longest on the oldest solve, so that is the one to name.
            var clock = new FakeClock();
            var state = NewState(clock);
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();

            state.BeginSolution(first, "first.gh");
            clock.Advance(TimeSpan.FromSeconds(10));
            state.BeginSolution(second, "second.gh");

            Assert.Equal(2, state.SolvingCount);
            Assert.Equal("first.gh", state.ActiveSolve.DocumentName);

            state.EndSolution(first);
            Assert.Equal("second.gh", state.ActiveSolve.DocumentName);
        }

        [Fact]
        public void ForgetDocument_ClearsSolveAndCachedIdentity()
        {
            // A document closed mid-solve never raises SolutionEnd; without this it would read as
            // solving forever.
            var state = NewState(new FakeClock());
            var doc = Guid.NewGuid();

            state.Heartbeat(doc, "closing.gh");
            state.BeginSolution(doc, "closing.gh");
            state.ForgetDocument(doc);

            Assert.False(state.AnySolving);
            Assert.Null(state.ActiveDocument);
        }

        [Fact]
        public void ForgetDocument_LeavesADifferentDocumentsIdentityAlone()
        {
            var state = NewState(new FakeClock());
            var kept = Guid.NewGuid();

            state.Heartbeat(kept, "kept.gh");
            state.ForgetDocument(Guid.NewGuid());

            Assert.Equal("kept.gh", state.ActiveDocument.DocumentName);
        }

        // ---------------------------------------------------------------- heartbeat staleness

        [Fact]
        public void Classify_WithNoStamp_IsUnknown()
        {
            Assert.Equal(UiLiveness.Unknown, SolverState.Classify(null, T0, Stale));
        }

        [Fact]
        public void Classify_JustStamped_IsResponsive()
        {
            Assert.Equal(UiLiveness.Responsive, SolverState.Classify(T0, T0, Stale));
        }

        [Fact]
        public void Classify_ExactlyAtTheWindow_IsStillResponsive()
        {
            // The boundary is exclusive: "not older than the window" counts as fresh.
            Assert.Equal(UiLiveness.Responsive, SolverState.Classify(T0, T0 + Stale, Stale));
        }

        [Fact]
        public void Classify_PastTheWindow_IsBlocked()
        {
            Assert.Equal(UiLiveness.Blocked,
                SolverState.Classify(T0, T0 + Stale + TimeSpan.FromMilliseconds(1), Stale));
        }

        [Fact]
        public void Classify_StampFromTheFuture_IsResponsive()
        {
            // A clock adjustment must not read as a wildly blocked UI.
            Assert.Equal(UiLiveness.Responsive, SolverState.Classify(T0 + TimeSpan.FromMinutes(1), T0, Stale));
        }

        [Fact]
        public void Heartbeat_StampsTheInjectedClock()
        {
            var clock = new FakeClock();
            var state = NewState(clock);

            clock.Advance(TimeSpan.FromSeconds(7));
            state.Heartbeat();

            Assert.Equal(T0 + TimeSpan.FromSeconds(7), state.LastHeartbeatUtc);
        }

        [Fact]
        public void Heartbeat_WithDocument_CachesIdentityForTheOffThreadReader()
        {
            var state = NewState(new FakeClock());
            var doc = Guid.NewGuid();

            state.Heartbeat(doc, "focused.gh");

            Assert.Equal(doc, state.ActiveDocument.DocumentId);
            Assert.Equal("focused.gh", state.ActiveDocument.DocumentName);
            Assert.Equal(T0, state.LastHeartbeatUtc);
        }

        // ---------------------------------------------------------------- modal inference table

        // The whole point of the heartbeat: heartbeat x solve determines whether an agent should
        // wait or fetch a human. Exhaustive over {Unknown, Responsive, Blocked} x {idle, solving}.

        [Fact]
        public void Derive_Unknown_Idle_IsNotModalAndNotHealthy()
        {
            var state = NewState(new FakeClock());

            var status = state.Derive(new StatusInputs());

            Assert.Equal(UiLiveness.Unknown, status.Ui);
            Assert.False(status.ModalInferred);
            Assert.False(status.Solving);
            Assert.False(status.IsHealthy);
            Assert.Null(status.HeartbeatAgeMs);
            Assert.Contains("unknown", status.Hint, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Derive_Unknown_Solving_IsNotModal()
        {
            // Never having heard from the UI thread is not evidence of a dialog.
            var state = NewState(new FakeClock());
            state.BeginSolution(Guid.NewGuid(), "a.gh");

            var status = state.Derive(new StatusInputs());

            Assert.Equal(UiLiveness.Unknown, status.Ui);
            Assert.False(status.ModalInferred);
            Assert.True(status.Solving);
        }

        [Fact]
        public void Derive_Responsive_Idle_IsHealthy()
        {
            var state = NewState(new FakeClock());
            state.Heartbeat();

            var status = state.Derive(new StatusInputs());

            Assert.Equal(UiLiveness.Responsive, status.Ui);
            Assert.False(status.ModalInferred);
            Assert.False(status.Solving);
            Assert.True(status.IsHealthy);
            Assert.Equal(0, status.HeartbeatAgeMs);
            Assert.Contains("Healthy", status.Hint);
        }

        [Fact]
        public void Derive_Responsive_Solving_IsBusyButNotBlocked()
        {
            var clock = new FakeClock();
            var state = NewState(clock);
            state.Heartbeat();
            state.BeginSolution(Guid.NewGuid(), "quick.gh");
            clock.Advance(TimeSpan.FromSeconds(1));

            var status = state.Derive(new StatusInputs());

            Assert.Equal(UiLiveness.Responsive, status.Ui);
            Assert.False(status.ModalInferred);
            Assert.True(status.Solving);
            Assert.False(status.IsHealthy);
            Assert.Contains("solving", status.Hint, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("quick.gh", status.SolvingDocumentName);
        }

        [Fact]
        public void Derive_Blocked_Solving_SaysBusyWait_NotModal()
        {
            var clock = new FakeClock();
            var state = NewState(clock);
            state.Heartbeat();
            state.BeginSolution(Guid.NewGuid(), "heavy.gh");
            clock.Advance(TimeSpan.FromMinutes(3));

            var status = state.Derive(new StatusInputs());

            Assert.Equal(UiLiveness.Blocked, status.Ui);
            Assert.False(status.ModalInferred);
            Assert.True(status.Solving);
            Assert.Equal(T0, status.SolvingSince);
            Assert.Contains("Wait and retry", status.Hint);
            Assert.Contains("heavy.gh", status.Hint);
        }

        [Fact]
        public void Derive_Blocked_Idle_InfersAModalNeedingAHuman()
        {
            // The key inference: nothing else holds the UI thread this long, and only a human can
            // clear a modal dialog.
            var clock = new FakeClock();
            var state = NewState(clock);
            state.Heartbeat();
            clock.Advance(TimeSpan.FromSeconds(30));

            var status = state.Derive(new StatusInputs());

            Assert.Equal(UiLiveness.Blocked, status.Ui);
            Assert.True(status.ModalInferred);
            Assert.False(status.Solving);
            Assert.False(status.IsHealthy);
            Assert.Equal(30000, status.HeartbeatAgeMs);
            Assert.Contains("modal dialog", status.Hint);
            Assert.Contains("human", status.Hint);
        }

        [Fact]
        public void Derive_ModalInference_ClearsWhenTheUiRecovers()
        {
            var clock = new FakeClock();
            var state = NewState(clock);
            state.Heartbeat();
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.True(state.Derive(new StatusInputs()).ModalInferred);

            state.Heartbeat();

            Assert.False(state.Derive(new StatusInputs()).ModalInferred);
        }

        // ---------------------------------------------------------------- derived payload

        [Fact]
        public void Derive_ReportsTheFocusedAndSolvingDocumentsSeparately()
        {
            // Several definitions share one UI thread: a solve in a file the agent is not touching
            // still blocks it, and naming the wrong file would send it looking in the wrong place.
            var state = NewState(new FakeClock());
            var focused = Guid.NewGuid();
            var solving = Guid.NewGuid();

            state.Heartbeat(focused, "focused.gh");
            state.BeginSolution(solving, "solving.gh");

            var status = state.Derive(new StatusInputs());

            Assert.Equal("focused.gh", status.DocumentName);
            Assert.Equal(focused, status.DocumentId);
            Assert.Equal("solving.gh", status.SolvingDocumentName);
            Assert.Equal(solving, status.SolvingDocumentId);
        }

        [Fact]
        public void Derive_BeforeAnyHeartbeat_FallsBackToTheSolvingDocumentName()
        {
            // The name must never be blank when something is known.
            var state = NewState(new FakeClock());
            var solving = Guid.NewGuid();

            state.BeginSolution(solving, "solving.gh");

            var status = state.Derive(new StatusInputs());

            Assert.Equal("solving.gh", status.DocumentName);
            Assert.Equal(solving, status.DocumentId);
        }

        [Fact]
        public void Derive_WhenIdle_HasNoSolvingDocument()
        {
            var state = NewState(new FakeClock());
            state.Heartbeat(Guid.NewGuid(), "focused.gh");

            var status = state.Derive(new StatusInputs());

            Assert.Null(status.SolvingDocumentName);
            Assert.Null(status.SolvingDocumentId);
        }

        [Fact]
        public void Derive_ASolveInAnUnfocusedDocument_StillReadsAsBusy_NotAsAModal()
        {
            // The regression that motivates watching every open document rather than only the
            // bridge's own: an unrecorded solve elsewhere would look like a modal dialog and send
            // an agent to fetch a human for nothing.
            var clock = new FakeClock();
            var state = NewState(clock);
            state.Heartbeat(Guid.NewGuid(), "focused.gh");
            state.BeginSolution(Guid.NewGuid(), "other.gh");
            clock.Advance(TimeSpan.FromMinutes(2));

            var status = state.Derive(new StatusInputs());

            Assert.Equal(UiLiveness.Blocked, status.Ui);
            Assert.True(status.Solving);
            Assert.False(status.ModalInferred);
            Assert.Contains("other.gh", status.Hint);
        }

        [Fact]
        public void Derive_WhenIdle_ReportsTheFocusedDocument()
        {
            var state = NewState(new FakeClock());
            var focused = Guid.NewGuid();

            state.Heartbeat(focused, "focused.gh");

            var status = state.Derive(new StatusInputs());

            Assert.Equal("focused.gh", status.DocumentName);
            Assert.Equal(focused, status.DocumentId);
        }

        [Fact]
        public void Derive_CarriesTheServerCountersThrough()
        {
            var state = NewState(new FakeClock());

            var status = state.Derive(new StatusInputs
            {
                ServerListening = true,
                Port = 26929,
                InFlightRequests = 3,
                UptimeSeconds = 1234,
                CommandCount = 42,
            });

            Assert.True(status.RhinoAlive);
            Assert.True(status.ServerListening);
            Assert.Equal(26929, status.Port);
            Assert.Equal(3, status.InFlightRequests);
            Assert.Equal(1234, status.UptimeSeconds);
            Assert.Equal(42, status.CommandCount);
        }

        [Fact]
        public void Derive_WithNullInputs_DoesNotThrow()
        {
            var state = NewState(new FakeClock());

            var status = state.Derive(null);

            Assert.False(status.ServerListening);
            Assert.NotNull(status.Hint);
        }

        // ---------------------------------------------------------------- server snapshot

        [Fact]
        public void ServerSnapshot_WithNoPublisher_ReportsNotListening()
        {
            // The truthful answer before any server has started, not a crash and not a fiction.
            var state = NewState(new FakeClock());

            var snapshot = state.ServerSnapshot();

            Assert.False(snapshot.ServerListening);
            Assert.Equal(0, snapshot.Port);
            Assert.False(state.Derive().ServerListening);
        }

        [Fact]
        public void PublishServerSnapshot_IsReadBackByTheNoArgDerive()
        {
            var state = NewState(new FakeClock());
            state.PublishServerSnapshot(() => new StatusInputs
            {
                ServerListening = true,
                Port = 26929,
                InFlightRequests = 2,
                CommandCount = 11,
            });

            var status = state.Derive();

            Assert.True(status.ServerListening);
            Assert.Equal(26929, status.Port);
            Assert.Equal(2, status.InFlightRequests);
            Assert.Equal(11, status.CommandCount);
        }

        [Fact]
        public void PublishServerSnapshot_IsReadEachTime_NotCached()
        {
            var state = NewState(new FakeClock());
            var commands = 0;
            state.PublishServerSnapshot(() => new StatusInputs { CommandCount = commands });

            Assert.Equal(0, state.Derive().CommandCount);
            commands = 5;
            Assert.Equal(5, state.Derive().CommandCount);
        }

        [Fact]
        public void ClearServerSnapshot_OnlyWithdrawsItsOwnProvider()
        {
            // A server shutting down must not unpublish the replacement that started meanwhile.
            var state = NewState(new FakeClock());
            Func<StatusInputs> oldServer = () => new StatusInputs { Port = 1 };
            Func<StatusInputs> newServer = () => new StatusInputs { Port = 2 };

            state.PublishServerSnapshot(oldServer);
            state.PublishServerSnapshot(newServer);
            state.ClearServerSnapshot(oldServer);

            Assert.Equal(2, state.ServerSnapshot().Port);

            state.ClearServerSnapshot(newServer);
            Assert.False(state.ServerSnapshot().ServerListening);
        }

        // ---------------------------------------------------------------- concurrency

        [Fact]
        public async Task BeginEnd_ConcurrentAcrossTwoDocuments_LeavesConsistentState()
        {
            // The UI thread writes solve state while HTTP worker threads read it; two documents
            // solving at once must not corrupt each other's record.
            var state = new SolverState();
            var docA = Guid.NewGuid();
            var docB = Guid.NewGuid();
            const int Iterations = 2000;
            var readErrors = 0;

            using (var start = new ManualResetEventSlim(false))
            {
                var writerA = Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 0; i < Iterations; i++)
                    {
                        state.BeginSolution(docA, "a.gh");
                        state.EndSolution(docA);
                    }
                });

                var writerB = Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 0; i < Iterations; i++)
                    {
                        state.BeginSolution(docB, "b.gh");
                        state.EndSolution(docB);
                    }
                });

                var reader = Task.Run(() =>
                {
                    start.Wait();
                    for (int i = 0; i < Iterations; i++)
                    {
                        // A reader must never see a torn record or throw while writers churn.
                        var solve = state.ActiveSolve;
                        if (solve != null && solve.DocumentId != docA && solve.DocumentId != docB)
                            Interlocked.Increment(ref readErrors);
                        state.Derive(new StatusInputs());
                    }
                });

                start.Set();
                await Task.WhenAll(writerA, writerB, reader).WaitAsync(TimeSpan.FromSeconds(30));
            }

            Assert.Equal(0, readErrors);
            Assert.False(state.AnySolving);
            Assert.Equal(0, state.SolvingCount);
        }

        [Fact]
        public void EndSolution_OnOneDocument_LeavesTheOtherSolving()
        {
            var state = new SolverState();
            var docA = Guid.NewGuid();
            var docB = Guid.NewGuid();

            state.BeginSolution(docA, "a.gh");
            state.BeginSolution(docB, "b.gh");
            state.EndSolution(docA);

            Assert.False(state.IsSolving(docA));
            Assert.True(state.IsSolving(docB));
            Assert.True(state.AnySolving);
        }

        [Fact]
        public void Heartbeat_ConcurrentStamps_AlwaysReadBackAsAValidStamp()
        {
            var state = new SolverState();
            var stamps = new List<DateTime>();

            Parallel.For(0, 500, _ =>
            {
                state.Heartbeat(Guid.NewGuid(), "doc.gh");
                var read = state.LastHeartbeatUtc;
                Assert.NotNull(read);
                lock (stamps) stamps.Add(read.Value);
            });

            // Every observed stamp is a real UTC instant, never a torn half-write.
            Assert.All(stamps, s => Assert.Equal(DateTimeKind.Utc, s.Kind));
            Assert.All(stamps, s => Assert.True(s > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(500, stamps.Count);
        }

        [Fact]
        public void Shared_IsASingleInstance()
        {
            // The solution events, the heartbeat tick and every reader must see one Rhino process.
            Assert.Same(SolverState.Shared, SolverState.Shared);
            Assert.Equal(SolverState.DefaultHeartbeatStaleAfter, SolverState.Shared.HeartbeatStaleAfter);
        }
    }
}
