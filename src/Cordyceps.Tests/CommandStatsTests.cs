using System.Threading;
using System.Threading.Tasks;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="CommandStats"/> — the host-free, thread-safe activity counters behind
    /// the MCP server's <c>CommandCount</c>/<c>LastCommand</c> status. The HTTP plumbing that calls
    /// <see cref="CommandStats.Record"/> is host-coupled and verified live in Rhino (see the build
    /// plan's Chunk 03 verification note); the lost-increment and snapshot-read contracts that the
    /// concurrency fix turns on are pinned here in CI.
    /// </summary>
    public class CommandStatsTests
    {
        [Fact]
        public void FreshInstance_HasZeroCountAndNoneLabel()
        {
            var stats = new CommandStats();

            Assert.Equal(0, stats.Count);
            Assert.Equal("(none)", stats.Last);
        }

        [Fact]
        public void Record_IncrementsCountAndPublishesLabel()
        {
            var stats = new CommandStats();

            stats.Record("gh_canvas @ 12:00:00");

            Assert.Equal(1, stats.Count);
            Assert.Equal("gh_canvas @ 12:00:00", stats.Last);
        }

        [Fact]
        public void Record_KeepsMostRecentLabel()
        {
            var stats = new CommandStats();

            stats.Record("first");
            stats.Record("second");

            Assert.Equal(2, stats.Count);
            Assert.Equal("second", stats.Last);
        }

        [Fact]
        public void Record_NullLabel_StoresNoneSentinel()
        {
            var stats = new CommandStats();

            stats.Record(null);

            Assert.Equal(1, stats.Count);
            Assert.Equal("(none)", stats.Last);
        }

        [Fact]
        public async Task Record_UnderConcurrency_LosesNoIncrements()
        {
            // The whole point of the Chunk 03 fix: concurrent HTTP worker threads must not lose
            // increments. With a plain `_count++` (read-modify-write) this assertion fails reliably
            // under contention; with Interlocked.Increment it holds. A start barrier releases every
            // worker at once to maximize the contention window — without it the test could pass
            // vacuously by serializing the workers.
            const int threads = 8;
            const int perThread = 50_000;
            var stats = new CommandStats();
            var ready = new CountdownEvent(threads);
            var go = new ManualResetEventSlim(false);

            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    ready.Signal();
                    go.Wait();
                    for (int i = 0; i < perThread; i++)
                        stats.Record("hammer");
                });
            }

            ready.Wait();   // all workers spun up and parked on the gate
            go.Set();       // release them simultaneously
            await Task.WhenAll(tasks);

            Assert.Equal(threads * perThread, stats.Count);
            Assert.Equal("hammer", stats.Last);
        }
    }
}
