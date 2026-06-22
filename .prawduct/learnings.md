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
