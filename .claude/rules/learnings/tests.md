---
paths:
  - "src/Cordyceps.Tests/**"
  - "src/Cordyceps/Core/**"
---
# Learnings — tests

## Code linked into `Cordyceps.Tests` must stay host-free — can't call `DebugLog`

The test project links specific `Core/*.cs` files individually (`RequestValidator`,
`UnifiedToolHelpers`, `ScriptDirective`, `McpResultFormatter`, `JsonTypeConverter`) to avoid
pulling in the Grasshopper/Rhino runtime. `Core/DebugLog.cs` uses `RhinoApp.WriteLine`, so it is
**host-coupled and not linkable**. Therefore: when de-silencing a `catch` (or adding any logging)
in one of those linked files, you can't add a `DebugLog` call — it won't compile in the test
project. **Narrow the catch to the expected exception type instead** (e.g. `GetParam` →
`FormatException`/`InvalidCastException`/`OverflowException`/`JsonException`). Narrowing is usually
the better fix anyway: unexpected errors surface instead of being swallowed.

## Test parallelization is disabled on purpose — don't re-enable it naively

`src/Cordyceps.Tests/AssemblyInfo.cs` sets `[assembly: CollectionBehavior(DisableTestParallelization
= true)]`. Several tests assert timing/concurrency contracts (`InFlightRequests` drain + count,
`DocumentLock`, `CommandStats`) by waiting on thread-pool continuations within a bounded budget. Under
xUnit's default parallel collections, those tests contend for the **2-core CI runner's** thread pool —
a sibling test's `Thread.Sleep`/blocking `Wait` can starve a `ContinueWith(..., TaskScheduler.Default)`
continuation past its budget, so `build-test` flakes (passed locally + on #21, failed twice on #22 with
no relevant change). Since `build-test` is the **required check for strict `main` protection**, a flaky
gate is unacceptable. The suite is sub-second, so serial execution costs ~nothing. If you re-enable
parallelization (or add a new timing test), make those tests deterministic first — don't trade the
gate's reliability for parallel speed.

## A test naming a race/snapshot/ordering contract must actually exercise concurrency

When a host-free concurrency primitive is extracted and unit-tested (e.g. `DocumentLock`,
`InFlightRequests`), a test whose name claims a *timing* contract — "ignores tasks tracked after
it starts", "snapshot taken up front", "drains within budget" — must drive it with real
concurrency: a background task running the method under test plus a `TaskCompletionSource` that
deliberately never completes during the assertion window, so the test fails if the contract
breaks. Standing in an *already-completed* task makes the test pass vacuously — it would still
pass if the implementation re-read live state — which is false confidence, worse than no test.
The Chunk-02 `DrainWithin_TakesSnapshot...` test shipped vacuous and the Critic (chunk mode,
Goal 1 test-quality) caught it; the fix was a background-drain + mid-wait-tracked-TCS rewrite.
Write the concurrent form from the start for any timing-contract test.
