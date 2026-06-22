using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Cordyceps.Core
{
    /// <summary>
    /// Tracks in-flight request-handler tasks so the server can <em>drain</em> them on
    /// shutdown instead of detaching them. Without tracking, <c>Stop()</c> would tear down
    /// shared state (the document context) while handlers are still running, risking a
    /// null-dereference; with it, shutdown waits — within a bounded budget — for outstanding
    /// handlers to finish first.
    ///
    /// Host-independent (no Grasshopper/Rhino references, no <c>DebugLog</c>) so the
    /// tracking/drain semantics can be unit-tested — see <c>Cordyceps.Tests</c>, which links
    /// this file directly. The HTTP plumbing that feeds it lives in <see cref="McpServer"/>.
    /// </summary>
    public sealed class InFlightRequests
    {
        // Task identity is by reference, which is exactly what we want: each handler task is a
        // distinct key. A completed task removes itself via the continuation registered in Track.
        private readonly ConcurrentDictionary<Task, byte> _tasks = new ConcurrentDictionary<Task, byte>();

        /// <summary>Number of handlers currently tracked (completed ones remove themselves).</summary>
        public int Count => _tasks.Count;

        /// <summary>
        /// Begin tracking a handler task. The task removes itself once it completes (success,
        /// fault, or cancellation), so the set does not grow without bound under normal traffic.
        /// </summary>
        public void Track(Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            _tasks.TryAdd(task, 0);
            // Remove on completion (success, fault, or cancellation). Pinned to the default
            // scheduler so the bookkeeping never runs on a UI/synchronization context.
            task.ContinueWith(t => _tasks.TryRemove(t, out _), TaskScheduler.Default);
        }

        /// <summary>
        /// Wait up to <paramref name="budget"/> for all currently-tracked handlers to finish.
        /// Returns <c>true</c> if they all completed (including faulted or cancelled — those are
        /// "done") within the budget, <c>false</c> if the budget expired with work still running.
        /// Tasks tracked after this call begins are not awaited (a fixed snapshot is taken), so a
        /// steady stream of new requests cannot make shutdown hang.
        /// </summary>
        public bool DrainWithin(TimeSpan budget)
        {
            var pending = _tasks.Keys.ToArray();
            if (pending.Length == 0)
                return true;

            try
            {
                return Task.WaitAll(pending, budget);
            }
            catch (AggregateException)
            {
                // One or more handlers faulted or were cancelled but all observed tasks completed
                // within the budget. A completed-with-error handler is still drained — it is no
                // longer touching shared state — so this counts as a successful drain.
                return true;
            }
        }
    }
}
