using System;
using System.Diagnostics;
using System.Threading;

namespace Cordyceps.Core
{
    /// <summary>How a bounded wait for a scheduled solve ended.</summary>
    public enum SolveWaitOutcome
    {
        /// <summary>A solve finished after the ticket was taken; its record is attached.</summary>
        Completed,

        /// <summary>The budget ran out first. The solve may still be running or not yet started.</summary>
        OverBudget,

        /// <summary>The document stopped being tracked (it closed) before a solve finished.</summary>
        Gone,
    }

    /// <summary>The result of <see cref="SolveWait.Wait"/>.</summary>
    public sealed class SolveWaitResult
    {
        public SolveWaitResult(SolveWaitOutcome outcome, SolveRecord record, TimeSpan elapsed)
        {
            Outcome = outcome;
            Record = record;
            Elapsed = elapsed;
        }

        public SolveWaitOutcome Outcome { get; }

        /// <summary>The finished solve's record when <see cref="Outcome"/> is Completed; otherwise null.</summary>
        public SolveRecord Record { get; }

        /// <summary>How long the wait itself took, by the injected clock.</summary>
        public TimeSpan Elapsed { get; }
    }

    /// <summary>
    /// Waits, within a budget, for the solve a <see cref="SolveTicket"/> claims. It never touches
    /// Grasshopper and holds no lock: it polls a probe (in the host, <see cref="SolverState.Observe"/>)
    /// that reads state the UI thread writes. That is the point: a caller waits here on its own
    /// thread while the UI thread solves, instead of holding the document lock and the UI-work
    /// bracket for the whole solve.
    ///
    /// <para>Done means the document's generation passed the ticket AND the document is not
    /// solving. The second half matters: a solve that ends can schedule another that starts at
    /// once, and an answer read between them would describe a document that is still changing.</para>
    ///
    /// <para>Host-free and linked into <c>Cordyceps.Tests</c>; the clock and the sleep are injected
    /// so the budget boundary can be tested exactly, and the real-time form is tested with a
    /// background waiter.</para>
    /// </summary>
    public static class SolveWait
    {
        /// <summary>
        /// How often the probe is read. Short against any budget a caller can ask for (a second at
        /// least) and cheap: each reading is two dictionary lookups.
        /// </summary>
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(25);

        /// <summary>
        /// Wait for <paramref name="ticket"/> to be answered, for at most <paramref name="budget"/>.
        /// Blocks the calling thread, which must not be the Rhino UI thread (the solve it waits for
        /// runs there).
        /// </summary>
        /// <param name="ticket">The claim taken when the solve was scheduled.</param>
        /// <param name="observe">Reads one document's solve state; must not block.</param>
        /// <param name="budget">The longest to wait. Zero or less checks once.</param>
        /// <param name="pollInterval">Gap between readings; defaults to <see cref="DefaultPollInterval"/>.</param>
        /// <param name="clock">The clock the budget is measured on; defaults to a monotonic one.</param>
        /// <param name="sleep">How to pause between readings; defaults to <see cref="Thread.Sleep(TimeSpan)"/>.</param>
        public static SolveWaitResult Wait(
            SolveTicket ticket,
            Func<Guid, SolveObservation> observe,
            TimeSpan budget,
            TimeSpan? pollInterval = null,
            Func<DateTime> clock = null,
            Action<TimeSpan> sleep = null)
        {
            if (observe == null) throw new ArgumentNullException(nameof(observe));
            clock ??= MonotonicClock();
            sleep ??= Thread.Sleep;
            var poll = pollInterval ?? DefaultPollInterval;
            if (poll <= TimeSpan.Zero) poll = DefaultPollInterval;

            var started = clock();
            while (true)
            {
                var reading = observe(ticket.DocumentId) ?? SolveObservation.Untracked;
                var elapsed = Since(started, clock());

                if (!reading.Tracked)
                    return new SolveWaitResult(SolveWaitOutcome.Gone, null, elapsed);

                if (IsAnswered(ticket, reading))
                    return new SolveWaitResult(SolveWaitOutcome.Completed, reading.Record, elapsed);

                if (elapsed >= budget)
                    return new SolveWaitResult(SolveWaitOutcome.OverBudget, null, elapsed);

                var left = budget - elapsed;
                sleep(left < poll ? left : poll);
            }
        }

        /// <summary>
        /// True when <paramref name="reading"/> shows a solve finished after <paramref name="ticket"/>
        /// was taken and nothing is solving now.
        /// </summary>
        public static bool IsAnswered(SolveTicket ticket, SolveObservation reading)
            => reading != null
               && reading.Tracked
               && reading.Record != null
               && reading.Record.Generation > ticket.Generation
               && !reading.Solving;

        // An injected clock that steps backwards reads as no time passed, never as negative time.
        private static TimeSpan Since(DateTime started, DateTime now)
            => now > started ? now - started : TimeSpan.Zero;

        // The default clock counts with a stopwatch: a wall clock adjusted backwards mid-wait would
        // otherwise hold the elapsed time at zero and stretch the wait past its budget.
        private static Func<DateTime> MonotonicClock()
        {
            var origin = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            return () => origin + stopwatch.Elapsed;
        }
    }
}
