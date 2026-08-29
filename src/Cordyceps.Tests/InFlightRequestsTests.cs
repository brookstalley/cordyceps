using System;
using System.Threading.Tasks;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="InFlightRequests"/> — the host-free tracking/drain semantics that
    /// let the server wait for outstanding request handlers (within a bounded budget) before it
    /// tears down shared state on shutdown. The HTTP plumbing that feeds it is host-coupled and
    /// verified live in Rhino (see the build plan's Chunk 02 verification note).
    /// </summary>
    public class InFlightRequestsTests
    {
        // Generous budget for "should drain immediately" cases so a loaded CI box never trips it;
        // the "should NOT drain" cases use their own short, explicit budget.
        private static readonly TimeSpan AmpleBudget = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ShortBudget = TimeSpan.FromMilliseconds(50);

        [Fact]
        public void DrainWithin_NothingTracked_ReturnsTrueImmediately()
        {
            var inflight = new InFlightRequests();
            Assert.True(inflight.DrainWithin(TimeSpan.Zero));
        }

        [Fact]
        public void DrainWithin_AllCompleted_ReturnsTrue()
        {
            var inflight = new InFlightRequests();
            inflight.Track(Task.CompletedTask);
            inflight.Track(Task.FromResult(7));

            Assert.True(inflight.DrainWithin(AmpleBudget));
        }

        [Fact]
        public void DrainWithin_PendingHandler_ReturnsFalseAfterBudget()
        {
            var inflight = new InFlightRequests();
            var gate = new TaskCompletionSource<bool>();
            inflight.Track(gate.Task); // never completes during this test

            Assert.False(inflight.DrainWithin(ShortBudget));

            gate.SetResult(true); // let the tracked task finish so the test leaves nothing running
        }

        [Fact]
        public void DrainWithin_PendingThenCompletes_ReturnsTrue()
        {
            var inflight = new InFlightRequests();
            var gate = new TaskCompletionSource<bool>();
            inflight.Track(gate.Task);

            // Complete the handler shortly after the drain starts waiting.
            _ = Task.Run(async () => { await Task.Delay(20); gate.SetResult(true); });

            Assert.True(inflight.DrainWithin(AmpleBudget));
        }

        [Fact]
        public void DrainWithin_FaultedHandler_CountsAsDrained()
        {
            var inflight = new InFlightRequests();
            var faulted = Task.FromException(new InvalidOperationException("boom"));
            inflight.Track(faulted);

            // A handler that completed by faulting is no longer touching shared state, so it
            // counts as drained rather than blocking shutdown or surfacing the fault here.
            Assert.True(inflight.DrainWithin(AmpleBudget));
        }

        [Fact]
        public async Task DrainWithin_TakesSnapshot_IgnoresTasksTrackedAfterItStarts()
        {
            // Locks in the "a request that arrives after shutdown begins can't extend the wait"
            // contract: DrainWithin must observe only the handlers tracked at call time, not the
            // live set. If it re-read the dictionary it would wait for `late` (which never
            // completes here) and the drain would not return within the assertion's guard.
            var inflight = new InFlightRequests();
            var snapshotted = new TaskCompletionSource<bool>();
            var late = new TaskCompletionSource<bool>();
            inflight.Track(snapshotted.Task);

            // Deterministic seam: DrainWithin signals right after it captures its snapshot, so
            // `late` is guaranteed to be tracked after the snapshot — no sleep-and-hope timing.
            var snapshotTaken = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            inflight.OnDrainSnapshot = () => snapshotTaken.TrySetResult(true);

            // Start the drain on its own thread; it snapshots the set, then blocks waiting.
            var drain = Task.Run(() => inflight.DrainWithin(AmpleBudget));

            // Once the snapshot is confirmed taken, track a second handler that never completes.
            Assert.True(await Task.WhenAny(snapshotTaken.Task, Task.Delay(AmpleBudget)) == snapshotTaken.Task,
                "drain never reached its snapshot point");
            inflight.Track(late.Task);

            // Complete only the snapshotted handler. The drain must now finish promptly, ignoring
            // the still-pending `late` task.
            snapshotted.SetResult(true);

            Assert.True(await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(3))) == drain,
                "drain did not return — it waited on a task tracked after it started");
            Assert.True(await drain);

            late.SetResult(true); // cleanup: leave nothing running
        }

        [Fact]
        public void DrainWithin_FaultCoincidingWithTimeout_ReturnsFalse()
        {
            // MCP-5T7W regression: a handler fault must not mask a budget timeout. With one
            // handler faulted and another still running when the budget expires, the drain did
            // NOT complete — DrainWithin must say so, whatever exception plumbing Task.WaitAll
            // uses for the fault.
            var inflight = new InFlightRequests();
            var stillRunning = new TaskCompletionSource<bool>();
            inflight.Track(Task.FromException(new InvalidOperationException("boom")));
            inflight.Track(stillRunning.Task); // never completes during the assertion window

            Assert.False(inflight.DrainWithin(ShortBudget));

            stillRunning.SetResult(true); // cleanup: leave nothing running
        }

        [Fact]
        public async Task DrainWithin_FaultedPlusLateCompleting_WithinBudget_ReturnsTrue()
        {
            // Companion to the timeout case: when the faulted handler's peers DO finish inside
            // the budget, the drain succeeded — the fault alone must not flip the result either
            // way. Drives the mixed fault+success completion path with real concurrency.
            var inflight = new InFlightRequests();
            var gate = new TaskCompletionSource<bool>();
            inflight.Track(Task.FromException(new InvalidOperationException("boom")));
            inflight.Track(gate.Task);

            var drain = Task.Run(() => inflight.DrainWithin(AmpleBudget));
            _ = Task.Run(async () => { await Task.Delay(20); gate.SetResult(true); });

            Assert.True(await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(3))) == drain,
                "drain did not return within its budget");
            Assert.True(await drain);
        }

        [Fact]
        public void Track_NullTask_Throws()
        {
            var inflight = new InFlightRequests();
            Assert.Throws<ArgumentNullException>(() => inflight.Track(null));
        }

        [Fact]
        public void Count_ReflectsTrackedHandlers_AndDropsOnCompletion()
        {
            var inflight = new InFlightRequests();
            var gate = new TaskCompletionSource<bool>();
            inflight.Track(gate.Task);

            Assert.Equal(1, inflight.Count);

            gate.SetResult(true);

            // The completion continuation removes the task asynchronously; poll briefly so the
            // test asserts the no-leak contract without depending on continuation timing.
            Assert.True(SpinUntil(() => inflight.Count == 0, TimeSpan.FromSeconds(2)));
        }

        private static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                System.Threading.Thread.Sleep(5);
            }
            return condition();
        }
    }
}
