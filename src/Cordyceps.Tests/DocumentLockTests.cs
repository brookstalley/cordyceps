using System;
using System.Threading;
using System.Threading.Tasks;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="DocumentLock"/> — the host-free timeout/release semantics that
    /// keep a single slow or wedged document operation from blocking every other request
    /// forever. The UI-thread marshaling that wraps this lock is host-coupled and verified
    /// live in Rhino (see the build plan's Chunk 01 verification note).
    /// </summary>
    public class DocumentLockTests
    {
        // Wide guard so a loaded CI box never produces a false failure; the behavior under
        // test (the acquire timeout) uses its own short, explicit value per test.
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

        [Fact]
        public void Run_ReturnsBodyResult_WhenLockFree()
        {
            var lk = new DocumentLock(TimeSpan.FromSeconds(1));
            Assert.Equal(42, lk.Run(() => 42));
        }

        [Fact]
        public void Run_Action_ExecutesBody_WhenLockFree()
        {
            var lk = new DocumentLock(TimeSpan.FromSeconds(1));
            var ran = false;
            lk.Run(() => { ran = true; });
            Assert.True(ran);
        }

        [Fact]
        public void Run_PropagatesBodyException()
        {
            var lk = new DocumentLock(TimeSpan.FromSeconds(1));
            Assert.Throws<InvalidOperationException>(
                () => lk.Run<int>(() => throw new InvalidOperationException("boom")));
        }

        [Fact]
        public void Run_ReleasesLock_AfterBodyThrows()
        {
            var lk = new DocumentLock(TimeSpan.FromSeconds(1));
            Assert.Throws<InvalidOperationException>(
                () => lk.Run<int>(() => throw new InvalidOperationException()));

            // If the lock were not released in the finally, this second call would time out.
            Assert.Equal(7, lk.Run(() => 7));
        }

        [Fact]
        public async Task Run_ThrowsDocumentBusy_WhenLockHeldBeyondTimeout()
        {
            var lk = new DocumentLock(TimeSpan.FromMilliseconds(100));
            using var holding = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);

            var holder = Task.Run(() => lk.Run(() => { holding.Set(); release.Wait(Guard); }));
            try
            {
                Assert.True(holding.Wait(Guard), "holder failed to acquire the lock");
                Assert.Throws<DocumentBusyException>(() => lk.Run(() => 0));
            }
            finally
            {
                release.Set();
                await holder.WaitAsync(Guard);
            }
        }

        [Fact]
        public async Task Run_AcquiresAfterHolderReleases()
        {
            var lk = new DocumentLock(Guard); // generous timeout: acquire should succeed once released
            using var holding = new ManualResetEventSlim(false);
            using var release = new ManualResetEventSlim(false);

            var holder = Task.Run(() => lk.Run(() => { holding.Set(); release.Wait(Guard); }));
            Assert.True(holding.Wait(Guard), "holder failed to acquire the lock");

            release.Set(); // holder releases; the waiting acquire below then succeeds
            Assert.Equal(7, lk.Run(() => 7));
            await holder.WaitAsync(Guard);
        }

        [Fact]
        public void DocumentBusyException_MessageNamesTimeoutAndRecovery()
        {
            var ex = new DocumentBusyException(TimeSpan.FromSeconds(120));
            Assert.Contains("120", ex.Message);
            Assert.Contains("restart Rhino", ex.Message);
        }
    }
}
