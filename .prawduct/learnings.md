# Learnings

Accumulated wisdom from building this product.

## Code linked into `Cordyceps.Tests` must stay host-free — can't call `DebugLog`

The test project links specific `Core/*.cs` files individually (`RequestValidator`,
`UnifiedToolHelpers`, `ScriptDirective`, `McpResultFormatter`, `JsonTypeConverter`) to avoid
pulling in the Grasshopper/Rhino runtime. `Core/DebugLog.cs` uses `RhinoApp.WriteLine`, so it is
**host-coupled and not linkable**. Therefore: when de-silencing a `catch` (or adding any logging)
in one of those linked files, you can't add a `DebugLog` call — it won't compile in the test
project. **Narrow the catch to the expected exception type instead** (e.g. `GetParam` →
`FormatException`/`InvalidCastException`/`OverflowException`/`JsonException`). Narrowing is usually
the better fix anyway: unexpected errors surface instead of being swallowed.

## `views_enabled`: don't hand-flip build-plan `## Status` checkboxes

With `views_enabled: true` (project-state.yaml), the build-plan `## Status` boxes are a **derived
view** — they flip to `[x]` only via `prawduct-hook regen-views` from change-log `status=shipped`
tags at merge time. On an unmerged branch they stay `[ ]`. Hand-flipping them to `[x]` as a
local-progress gesture is view↔tag drift (the Critic flags it; regen-views would revert it).
**Track in-flight progress in the `## Status` Context prose**, not by editing the boxes. (Recurred:
the MCP-4R2K plan did it, then the backlog-batch plan copied it — Critic-caught both times.)

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

## Refresh stale `.test-evidence.json` via the hook, not by hand

The cumulative-Critic staleness check and the `test-status` gate READ
`.prawduct/.test-evidence.json`; the WRITER is `prawduct-hook test-evidence record` (a real run +
ISO timestamp + F4a coverage overlay). **Don't hand-edit the JSON** — the hook's own docstring notes
"every product repo improvised a hand-written JSON," which is exactly what drifts. Gotcha: this repo
declares `test_command:` in `project-state.yaml`, so `record --from-junit <report>` is **rejected**
(it would be two runners — the declared command vs. an ingested report). Just run
`python3 <plugin>/bin/prawduct-hook test-evidence record` and it runs the declared command itself
(substituting `{junit_xml}`), stamps the timestamp, and merges coverage. Note the `changes_*` fields
will list every C# file under `changes_unjudged` — the floor verifier is Python-only symbol-grep, so
that's structurally expected here, not a coverage failure.

## Building dirties the tracked `releases/Cordyceps.gha` binary

`Cordyceps.csproj` has a post-build `CopyToReleases` target that copies the built `.gha`
into `releases/` (a *tracked* binary). So **any** `dotnet build`/`dotnet test` during
non-release work — including baseline verification and `prawduct-hook test-evidence record` —
restamps `releases/Cordyceps.gha` (it embeds a build timestamp via `SourceRevisionId`), leaving
it modified in `git status`. **Run `git checkout -- releases/Cordyceps.gha` after building** so a
rebuilt binary never lands in a non-release diff. The binary should only change via
`scripts/release.sh` at actual release time. (Mirror of the GHS-7K2P housekeeping snag with the
regenerable `.work-model-index.json`: discard regenerable build artifacts before committing.)

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

## Tracing consumers of a shared helper misses paths that bypass it

When fixing a defect that lives in a shared helper (e.g. `ToolHelpers.WithProxyComponent`'s
empty-vs-failed param ambiguity), a consumer trace that greps for the *helper's callers* will miss
any path that produces the **same result by reimplementing the behavior without the helper**. In
CQ-7T4P, two surfaces went through `WithProxyComponent`, but a third — `ResourceRegistry`
`GenerateComponentDocumentation` (`gh://component/{name}`) — instantiated a proxy directly via
`ComponentRegistry.CreateComponent` and had the identical defect. **Trace by the defect/behavior
(grep for the symptom: `CreateInstance`, `CreateComponent`, "Params.Input", silent omission of
params), not only by the helper's call sites.** The Critic caught this one; the cheaper move is to
widen the grep up front.

## Version lives in three places; keep them in lockstep

A release version must match across `src/Cordyceps/Cordyceps.csproj` `<Version>`, the tracked
root `manifest.yml`, and `CHANGELOG.md`. The root `manifest.yml` is the yak source-of-truth that
`scripts/release.sh` → `prepare_dist` copies into the (gitignored) `dist/`. It silently drifted to
1.4.0 while shipping 1.4.9 because `release.sh` only bumped the csproj — fixed by adding
`update_manifest_version` to the script (2026-06-20 janitor).

## Build-plan chunk headings must be `### Chunk NN: title`, refs repo-root-relative

`prawduct-hook verify-chunk-refs` derives each chunk id from the `## Status` checkbox line by
taking the text after `Chunk ` up to the first `:` — so the Status line **and** the chunk heading
must both be `Chunk NN: <title>` (colon right after the number). A `## Chunk 01 — title` (em-dash,
no colon) makes the hook treat the whole title as the id and report `chunk '01 — …' not found`,
silently disabling the per-chunk ref gate. Then, within the matched chunk section, the hook checks
every backticked token that looks like a file path and resolves it **relative to repo root** — so
write `src/Cordyceps/Core/Foo.cs`, not `Core/Foo.cs`, and avoid backticked slash-containing
non-paths like `` `"true"/"false"` `` (split them: `` `true`/`false` ``). This recurred: CQ-7T4P
hit the same exit-1 as a NOTE, RSC-2H9K as a WARNING. Use the canonical format from the start.

## Validate-then-mutate, and never conflate "unparseable" with "empty"

The 2026-07-02 janitor audit found one defect CLASS behind most of its 10 HIGH findings,
recurring across independently-written actions: mutate the document first, then discover the
operation can't complete (layer_delete moved objects before the current-layer check;
place_image deleted the old frame before the new add; configure wiped params from JSON that
failed to parse and was silently read as `[]`). The rules that kill the class: (1) resolve and
validate EVERY input — and pick destinations/replacements — before the first document mutation;
(2) a parse failure is an error, never an empty collection (`TryParse(out result, out error)`
shape, per `Core/ScriptParamDefs`); (3) any per-id loop returns per-id results with overall
`success=false` when something failed (the `ActionDelete` pattern) — silent skips reported as
`success:true` were the single most common bug. New pure decision logic goes in a host-free
`Core/` file linked into the test project WITH tests in the same chunk, even when it feels like
host glue — the audit found ~10 helpers untestable only because they sat in host-coupled files.
