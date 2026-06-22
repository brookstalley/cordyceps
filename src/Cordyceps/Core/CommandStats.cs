using System.Threading;

namespace Cordyceps.Core
{
    /// <summary>
    /// Thread-safe activity counters for the MCP server. <see cref="Record"/> is called from
    /// concurrent HTTP worker threads, while <see cref="Count"/> and <see cref="Last"/> are read
    /// from the UI thread (component status) and off-thread (health endpoint). The increment uses
    /// <see cref="Interlocked"/> so concurrent commands can't lose an increment, and the
    /// last-command label is published/read through <see cref="Volatile"/> so a reader never sees a
    /// stale value. Host-free (no Rhino/Grasshopper, no DebugLog) so the lost-increment and
    /// snapshot-read contracts are unit-tested in CI rather than only verified live.
    /// </summary>
    public sealed class CommandStats
    {
        private int _count;
        private string _last = "(none)";

        /// <summary>Total commands recorded. Snapshot read — never torn.</summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>The most recently recorded command label, or "(none)" before any command.</summary>
        public string Last => Volatile.Read(ref _last);

        /// <summary>
        /// Atomically record one command: increment the count and publish <paramref name="label"/>
        /// as the most recent command. Safe to call concurrently from multiple threads.
        /// </summary>
        public void Record(string label)
        {
            Interlocked.Increment(ref _count);
            Volatile.Write(ref _last, label ?? "(none)");
        }
    }
}
