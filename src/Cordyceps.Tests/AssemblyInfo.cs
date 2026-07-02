using Xunit;

// Disable xUnit test-collection parallelization.
//
// Several tests assert timing/concurrency contracts (InFlightRequests drain + count,
// DocumentLock, CommandStats) by waiting on thread-pool continuations within a bounded
// budget. With xUnit's default parallel collections, those tests contend for the 2-core
// CI runner's thread pool — sibling tests doing Thread.Sleep / blocking waits can starve a
// removal continuation past its budget, so `build-test` flakes (it passed on #21, failed
// twice on #22 with no relevant code change). A flaky required check is unacceptable for the
// strict main-protection gate, so we trade a little wall-clock for determinism: the suite is
// small and fast (sub-second), and serial execution gives the timing tests an uncontended
// pool. No assertion is weakened — the contracts still run, just without the contention that
// made a valid assertion flaky.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
