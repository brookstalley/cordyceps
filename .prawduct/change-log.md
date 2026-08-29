# Change Log — Cordyceps

<!-- Append new entries at the top. Each entry is a ## section.
     This file is separate from project-state.yaml to reduce merge conflicts
     when multiple branches add entries simultaneously.

     # Tagged entries

     Add a tag-line directly under each ## header to mark which build-plan
     chunks the entry shipped and which release it belongs to.

     Nothing is derived from these tags any more — there are no generated
     views, and `prawduct-hook regen-views` is inert (it warns and writes
     nothing). The tags are read directly: `check-releasability` and
     `plan-backfill` use `scope=` and the presence/absence of `release=`.

     Format:

         ## YYYY-MM-DD: title

         <!-- prawduct: type=bugfix | chunks=01,02 | scope=my-scope -->

         **Why:** ...

     Copy that line as-is for a new entry: no `release=` (the release adds it)
     and no `status=` (retired). Historical entries below carry both.

     Recognized keys:
       chunks   - comma-separated chunk IDs (zero-padded, must match
                  build-plan.md ## Status headers exactly: `Chunk 00:`)
       release  - version string, added when the develop->main release ships
                  the entry. Its ABSENCE is the release-pending state, so never
                  write a placeholder: any value at all drops the entry's whole
                  scope out of `check-releasability`'s pending set.
       status   - LEGACY, inert. Older entries carry shipped|merged|built|
                  deferred; nothing reads it. Don't add it to new entries.
       scope    - rollup identifier (e.g., release-artifact-provenance)

     The `## Status` checkboxes in a build plan are NOT derived from this file.
     They are written by hand: tick a box when its chunk's review passes, and
     nothing will overwrite it. -->

## 2026-08-29: migrate the backlog from markdown to GitHub Issues

<!-- prawduct: type=tooling | scope=backlog-service-migration -->

**Why:** `.prawduct/backlog.md` was a single merge-prone file that only this repo's tooling could
read. Moving it onto GitHub Issues puts the backlog where the reporters already are — the repo
already carried 11 reporter issues that the markdown backlog could not see or link to — and makes
it queryable by the backlog service instead of by reading a 57 KB file into context every session.

**What:** All 41 items migrated to `brookstalley/cordyceps` issues **#35-#75**, verified by
`prawduct-hook backlog verify-migration` at **exit 0** (`missing` / `unaliasable` / `collisions` /
`status_mismatch` / `duplicate_alias` all empty; 41 source items, 41 aliased). Every item kept its
hand-minted `PFX-XXXX` id as an `id:PFX` alias, so existing citations in `docs/` still resolve.
Bodies migrated verbatim; all 41 titles were rewritten to the `area: summary` issue standard and
assigned a `kind:`. `backlog_service_repo: brookstalley/cordyceps` is now set in
`project-state.yaml`, which is the switch that makes the service the live backlog and retires the
markdown-premise advisory probes.

Two owner decisions changed the corpus rather than just moving it. Two `non_atomic` items were
**split before import** — `TST-5N9X` into `TST-4K9P`/`TST-7B2M`/`TST-3F8W`, and `CQ-9W2F` into
`CQ-6H4N`/`CQ-8M3R`/`CQ-1P7T` — with the originals retained as `dropped` (not deleted) so the split
is auditable. Four altitude/dedup clusters the scrub surfaced were deliberately **deferred** to
native triage rather than merged during migration. Issue #28's unresolved net48/Rhino 7 contribution
offer, which had no backlog counterpart at all, was filed as **#76**.

**Not done:** `.prawduct/backlog.md` was **deleted**, by owner decision, rather than kept as
frozen history behind a banner. The cost is recorded and accepted: `verify-migration` requires
`--from <backlog.md>` and so can never be re-run, which is why it was run to exit 0 *before* the
deletion. Recovery is via git history; `CLAUDE.md` carries the signpost that replaces the banner.
Full rationale, the target/scope decisions, and the plugin build that performed the migration
(3.4.1-dev.2) are in `.prawduct/artifacts/migration-scrub-decisions.md`.

**Follow-up:** `backlog counts` reports `untriaged: 0` on this repo while `backlog list --untriaged`
correctly finds the 11 pre-existing reporter issues, folding them into `shipped` instead. Does not
affect the migration (the gate is the authority, not the count arithmetic) but looks like an
upstream prawduct defect worth reporting.

## 2026-08-29: document the silently-never-invoked RunScript trap (issue #33)

<!-- prawduct: type=docs | scope=runscript-never-invoked-docs | release=v1.5.0 -->

**Why:** The reporter on #33 filed a regression against v1.5.0-rc.1 that turned out to be their
own test code: a C# script whose body only *defines* `RunScript`, with no top-level statements,
compiles and syncs its output ports from the signature but is never invoked — null outputs,
`runtimeMessageLevel: Blank`, no diagnostic anywhere. It cost a full false-positive report and a
production rollback to 1.4.12 while it was investigated.

**What:** A row in `Knowledge/CommonErrorsGuide.md` and a note in
`Knowledge/Prompts/SetupScriptComponent.md`, both naming the fix (write top-level statements) and
the reason the machinery stays quiet: on C# the stored and running text are identical, so
`verified:true` is correct and has nothing to flag. Python 3 does surface it via the signature
rewrite, which is why the blind spot lands on the language that needs the warning most.

**Not done:** the source-shape heuristic on `set`/`configure` that would catch this at write time.
Backlogged rather than bolted on — the rule ("defines RunScript, no top-level statements") needs a
requirements pass on what counts as a top-level statement per language and how SDK-mode C# differs,
and a wrong rule warns on correct scripts.

**Also:** merged three duplicate `### Fixed`/`### Changed` headings under `## [Unreleased]` in
CHANGELOG.md, which `release.sh prep` would otherwise have shipped into the 1.5.0 section verbatim.
Entry content is unchanged — 34 bullets before and after.

## 2026-08-27: writing a script's source now recompiles it (issue #33)

<!-- prawduct: type=bugfix | chunks=01,02,03,04 | scope=script-recompile-on-set | release=v1.5.0 -->

**Why:** [GitHub #33] A reporter on a real 149-object definition found `gh_script(action='set')`
returning `success/codeSet:true`, `get` round-tripping the new source, and the component executing
its previous program regardless — through a slider change, a human slider drag, `recompute`, and
solver off/on. No error, no runtime message. Reading the shipped Rhino 8.32 assemblies
(`RhinoCodePluginGH.gha`, `RhinoCodePlatform.GH*.dll`) found two defects behind it. First, storing
source does not recompile: Rhino builds a `Code` from the script and keeps running it, and every
source-changing path *inside* Rhino pairs the store with a rebuild or an explicit
`code.Text.Set(...)` — the editor and file-load paths, the language switch (`SetScript` then
`ReBuild`), the "Discard Caches" menu item (`ExpireCache` then `ReCompute`). Cordyceps was the only
writer that stored text and asked for nothing. Second, `get` could not see the divergence it was
being used to rule out: `TryGetSource` returns the *stored* text, while Rhino's own `GetText()`
prefers the built code's — so the clean round-trip the reporter checked was never evidence.

**What:** (a) New `Core/ScriptProgram` reaches the component's live program through the public
`IScriptObject` interface it implements explicitly — `Expire()` then `ReBuild()` to rebuild, and
`TryGetCode` → `ICode.Text` to read what will actually run. Host-free reflection over `object`, like
its sibling `ScriptSourceWriter`, so the whole thing is unit-testable against fakes. (b) `set` and
both `configure` write paths rebuild after the params are final, then verify the write by reading
the running program back. (c) One vocabulary at both surfaces, and every branch says something:
`rebuildSkipped` (no hooks — normal) is a different key from `rebuildFailed` (a hook threw — probably
still running the old program), and an unreadable program yields `verificationSkipped` /
`runningSourceUnavailable` rather than an omitted field, because an absent `verified` read as
agreement is the very failure being fixed. (d) MCP `initialize` reports the
informational version, so two pre-releases of the same version are distinguishable — without it a
tester cannot confirm they are running the build that contains this fix.

**Not the regression it was reported as.** The report blames #30 for removing a per-call
`ExpireSolution` from `set`; `git show v1.4.12` shows the path was behaviourally identical, the only
rc.1 change being `dynamic` → reflection onto the same `SetSource(String)`. The implicit rebuild
`set` once inherited was `SetParametersFromScript()` → `Context.OnScriptChanged()`, removed in
**v1.4.6** for the cluster-corruption fix — long before the reporter's 1.4.12 baseline. Their exact
stale program has not been reproduced here, and this bundle does not claim to have root-caused it;
it fixes two defects provable from the Rhino source that make the reported symptom either
impossible or visible.

**What no test here can reach.** Every unit test drives fakes written to the shape the Rhino
assemblies were read to have — which proves the reflection resolves, and cannot prove that
`Expire()`+`ReBuild()` makes the *next solve* run the new program, nor that cluster inputs survive
(issue #12 only manifests inside a cluster). Both are enqueued as **VRF-014**, and rc.2 exists so the
reporter's own battery can answer them.

**Descoped, explicitly.** Reporting compile diagnostics from the write call was in the first cut of
the plan and is not reachable on public API: `ReBuild()` → `PreBuild(kind)` →
`Context.TryBuildCode(runContext, out _)` discards the `Diagnosis`, and `IScriptObject.HasErrors` is
just "does the component have error runtime messages", which a rebuild never adds. The alternatives
— reading the `protected ScriptContext Context` field, or building the `Code` against a hand-made
`RunContext` — mean reaching past the public surface or building under settings the host would not
use. Build errors keep surfacing where they do today: on the component at the next solve, via
`gh_inspect(action='status')`. Said in the help text, the errors guide and the changelog rather than
left for a user to discover.

## 2026-08-25: the release publishes the binary it built (release-artifact provenance)

<!-- prawduct: type=bugfix | chunks=01,02,03,04 | scope=release-artifact-provenance | release=v1.5.0 -->

**Why:** [GitHub #29] A reporter with no .NET toolchain asked for a `develop` build and there was
no supported way to give them one. CI built and tested but uploaded nothing, and the two
obvious-looking sources were both wrong: the tracked `releases/Cordyceps.gha` on `develop` is the
last *released* build (swapping it in would have tested code containing none of the fixes and
produced a false negative on #27/#29/#30), and any local `dotnet build -c Release` silently
overwrote that same file through the csproj `CopyToReleases` target. Underneath all three sat one
defect: `do_publish` called `prepare_dist` without ever calling `build_gha`, so the published
binary was whatever happened to be sitting in the working tree. That was safe only by accident -
the file was tracked, so `require_clean_tree` would catch a stray local build.

**What:** (a) `publish` now runs `ensure_dotnet` + `build_gha` before `prepare_dist`, so it ships a
binary compiled from the checked-out release commit and works on a fresh clone; `prepare_dist`
hard-fails with a pointed message if the build reported success but produced no `.gha`.
(b) `releases/` is gitignored and `releases/Cordyceps.gha` untracked - `prep` no longer stages it,
and a local Release build can no longer dirty the tree or stage an unreleased binary into the
manual-install download path. (c) The README manual-install link moved from `raw/main/releases/...`
to `/releases/latest/download/Cordyceps.gha`, and `check_readme` greps for the new path.
(d) `dotnet-ci.yml` uploads `Cordyceps.gha` per run (`if-no-files-found: error`, 90-day retention)
so any commit is obtainable without a toolchain.

**Reconcile, don't skip:** `create_github_release` previously logged a warning and returned when a
Release for the tag already existed. With the README now resolving to that Release's asset, a
skip leaves a Release with no `.gha` - a 404 for every user, not a cosmetic gap. It now reconciles:
`gh release upload --clobber` plus `gh release edit --latest`, each failing loudly.

**Also, from PR review — the post-release ritual was already dead.** `docs/release-process.md`
made `prawduct-hook regen-views` *the* governance bookkeeping step and `CLAUDE.md` repeated it, but
that command is retired in prawduct 3.4.0: it prints a warning and writes nothing. Four more places
described the world it created — this file's own header (declaring build-plan `## Status`
checkboxes a derived view that must not be hand-edited, which this bundle contradicts by correctly
hand-ticking all four), a `learnings.md` rule forbidding exactly what the methodology now
prescribes, `project-state.yaml`'s `views_enabled`/`scope_rollups` keys, and
`build-plan-reliability.md`, whose six checkboxes sit unticked for work merged in PR #26 precisely
because the mechanism that owned them stopped running. All six sites now state what is actually
true; the release doc's bookkeeping step is `release=vX.Y.Z` tags plus `plan-backfill --apply`.
`views_enabled` and `scope_rollups` were deleted outright via `prawduct-hook lifecycle-repair
--apply` after three independent checks agreed nothing reads them — an inline comment explaining
why a retired key was kept is not durable when the health check that flags it cannot read comments.

A second review round then caught the one place a shipped behavior change and its documentation
still disagreed: the `publish` step-list in `docs/release-process.md` — a line this very bundle had
rewritten — still promised the GitHub Release step was "skipped if it already exists". Re-running
`publish` is not a no-op any more; it re-uploads the asset and re-marks the Release latest.
The reliability plan's checkboxes are deliberately left unticked — its own Context lists
undischarged VRF-009/VRF-010 operator verification, so "built and merged, verification
outstanding" is the honest state and no checkbox says that. Flagged for an owner ruling.

**Housekeeping in this bundle:** `94572ed` also archives the completed issues-2026-08 build plan
into `artifacts/archive/` (preserved, not overwritten) and installs this cycle's plan at the
conventional path — about two-thirds of the diff's line count.

**Accepted costs:** existing `raw/main/releases/Cordyceps.gha` links 404 (a loud 404 beats silently
serving a build that is not the release you think it is), and the 56 historical `.gha` blobs
(~27.4 MiB) stay in history - untracking stops the future churn and repo size is not a reported
problem. Neither `publish` path has run for real yet; the next release exercises both for the
first time.

## 2026-08-21: bridge liveness, solution safety, status envelope (issues #30, #29)

<!-- prawduct: type=feature | chunks=01,02,03 | scope=issues-2026-08 | status=built | release=v1.5.0 -->

**Why:** [GitHub #30, #29] Two companion reports from one agent-driven session. Every MCP call
refreshed the bridge component by expiring it immediately; landing mid-solve, that raised
Grasshopper's modal breakpoint dialog, which stops the canvas and blocks every later call until a
human clicks Close — fatal unattended. Separately, a caller could not tell a busy solver from a
dead bridge: one measured outage was ~32 minutes of total silence from a read-only probe. The
ambiguity caused the retries that triggered the dialog, so the two are one problem.

**What:** (a) `RefreshComponent` defers via `ScheduleSolution` instead of `ExpireSolution(true)`,
so no MCP call can expire the bridge inside a running solution; bursts coalesce into one recompute.
(b) Host-free `Core/SolverState` tracks solve state per document (`SolutionStart`/`SolutionEnd` are
per-document, and several definitions share one UI thread) plus a UI-thread heartbeat, fed by
`Core/SolutionWatcher` watching `GH_DocumentServer` globally — per-instance subscription would miss
a solve in a definition holding no bridge and report a false modal. (c) `gh_inspect(action=
'connection')` answers from cached state without ever marshaling; the pre-existing `status` action,
which does need the UI thread and was itself the source of the 32-minute silence, now returns a
prompt busy result instead of hanging. (d) Compact host status injected into every tool response at
one choke point in `HandleToolCallAsync`. (e) `recompute` refuses during an active solve with a
structured busy result. (f) `GET /health` enriched and no longer reads the document off-thread.

**Modal inference is guarded on three conditions, not two:** stale heartbeat, no solve running,
*and* no Cordyceps UI work in flight. Tool bodies execute on the UI thread, so a long bake or
capture starves the heartbeat exactly as a dialog does; with only the first two conditions a
healthy host mid-bake reported a dialog that was not there and the guidance told the agent to stop
and fetch a human. `GrasshopperContext` records UI-thread occupancy at its marshaling choke point;
the connection probe never marshals, so it never counts itself.

**Known limitation:** `modal_inferred` does not fire for issue #30's own dialog, which appears
*inside* a solve and so reads as "busy solving". The deferred refresh prevents that dialog at the
source; the inference catches every other modal. Recorded in VRF-012 so a verifier does not test
for the wrong thing.

## 2026-08-21: per-parameter data modifiers on gh_canvas (issue #27)

<!-- prawduct: type=feature | chunks=04 | scope=issues-2026-08 | status=built | release=v1.5.0 -->

**Why:** [GitHub #27] Flatten/Graft/Simplify/Reverse — the right-click options on component ports —
were unreachable through the API, and `action='info'` did not report them, so an agent could not
even detect an existing Graft; it had to be inferred from downstream branch counts, and
round-tripping a document silently lost the information. The reporter surveyed the comparable
Grasshopper MCP projects and found the same gap in all of them.

**What:** host-free `Core/DataModifiers` parses and plans (`none|flatten|graft`, tri-state
simplify/reverse, partial update, all bad arguments reported in one message);
`gh_canvas(action='modifier')` reads with `id`/`side`/`param` alone and writes otherwise, over
component ports and free-floating params alike; `modifiers` added to both branches of
`BuildParameterList` and to the free-floating branch of `BuildFullComponentInfo`. Param resolution
accepts a name or a 0-based index and null-guards the spec. `ExpireSolution(false)` only when
something actually changed. `RemoveEffects()` deliberately unused — an explicit
`mapping='none', simplify=false, reverse=false` expresses the same clear without clearing more
than these three. Reparameterize stays out of scope (the issue calls it phase 2).

## 2026-08-21: script source write cascade (issue #28 finding)

<!-- prawduct: type=fix | chunks=05 | scope=issues-2026-08 | status=built | release=v1.5.0 -->

**Why:** [GitHub #28] An external source audit found the read path probing a five-member cascade
while the write path called `SetSource` bare through `dynamic`. A script component without
`SetSource` therefore read fine and failed opaquely on write. Applies on Rhino 8 too — third-party
script components need not expose it either.

**What:** host-free `Core/ScriptSourceWriter` mirrors the read cascade on the write side —
`SetSource(string)`, then a writable `Code` property gated by a `HiddenCodeInput` pre-check so a
visible code-input parameter yields an actionable message rather than an opaque host exception, then
a specific failure naming the type and every member probed. Never a silent no-op. Overload- and
shadowing-tolerant (both `AmbiguousMatchException` sources). All three call sites route through one
helper; `PreserveLanguageDirective`, param sync and expire behavior unchanged. `IsScriptComponent`
now probes `CanWrite`, or a `Code`-only component would be rejected before the fallback could run.

**Not done:** the net48/Rhino 7 multi-target the same issue proposed was declined — a Mono-era
runtime that cannot be tested here is a real maintenance surface, and the reporter's fork ends
naturally at Rhino 9.

## 2026-08-21: System.Text.Json to Newtonsoft conversion dropped (issue #28 finding)

<!-- prawduct: type=decision | chunks=06,06a | scope=issues-2026-08 | status=deferred | release=v1.5.0 -->

**Why:** [user decision 2026-08-21] The conversion's sole justification was a net48 assembly-load
conflict: on `net48` System.Text.Json arrives as a package needing `System.Memory` >= 4.0.1.2 and
`Unsafe` 6.0.0.0, while Rhino 7 already holds older versions in-process for Roslyn, .NET Framework
binds by exact version, and a `.gha` cannot inject binding redirects. net48 was declined, and on
.NET 8 System.Text.Json IS the BCL — no package, no transitive versions, nothing to conflict. The
residual "one JSON library" argument points the other way on this runtime, since STJ is the
platform-native and faster option. `project-preferences.md` already scopes the split deliberately.

**What:** not built. Recon identified five behavior traps in what the issue framed as mechanical —
`WhenWritingNull` not governing dictionary values, `DateParseHandling` rewriting date-shaped string
ids, `GetRawText` vs `ToString` formatting, `prompts/get` throwing vs coercing on non-string
arguments, and lossless numeric id echo. Three were untested, so the reporter's "all 56 tests pass"
could not have caught a regression in them. Carried forward instead: `JsonRpcWireFormatTests` pins
the current wire format (null `result` emitted explicitly as JSON-RPC 2.0 requires, verbatim string
ids, exact numeric literals, compactness, no naming policy, unicode escaping with round-trip). Doing
so corrected a comment in `JsonRpcEnvelope` that claimed `WhenWritingNull` drops a null `result`; it
does not, and the emitted null is required.

## 2026-07-02: stop encouraging component renames — annotate via groups (reliability chunk 06)

<!-- prawduct: type=feature | chunks=06 | scope=reliability | status=merged | release=v1.5.0 -->

**Why:** [GHC-8V3T, user decision 2026-07-02] Renamed components are hard to find on the canvas
and renaming is not the Grasshopper convention (labeled groups, panels, scribbles are). Cordyceps
guidance actively encouraged nicknaming while building. The rename capability stays (explicit
user/agent use); only the propensity changes.

**What:** (a) Guidance surfaces rewritten: server instructions gain an annotate-with-groups key
point; BestPracticesGuide #2 flipped from "Name components" to "Annotate with labeled groups,
not renames"; GettingStartedGuide workflow example de-nicknamed; CreateParametricGeometry prompt
replaces its rename step with group_create. (b) Verified no code path auto-applies nicknames
unprompted (add applies only an explicit nickname; script-param and group naming are functional;
panel default annotation unchanged). (c) Discouragement notes on rename/add ActionInfo; find tip
clarifies default nicknames match so renaming isn't needed for findability. (d) No new
capability — group_create/rename/color already serve annotation. Kept: panel nicknames
(annotation convention), script-param rename docs (different concern), McpTestingGuide
capability mention. CHANGELOG under [Unreleased].

**[2026-07-02] Cumulative-Critic close-out:** CommonErrorsGuide find-by-nickname row —
initially skipped without a recorded descope (Critic warning) — reframed to lead with
list/typeFilter/group and note that find matches default nicknames.

## 2026-07-02: Rhino undo records around mutating actions (reliability chunk 05)

<!-- prawduct: type=feature | chunks=05 | scope=reliability | status=merged | release=v1.5.0 -->

**Why:** [RSC-6K1W] No code path called `RhinoDoc.BeginUndoRecord`/`EndUndoRecord`, so each
per-object mutation was its own undo step — Ctrl-Z after a bulk MCP `set_layer` reverted one
object of fifty. User-felt.

**What:** verify-api probe (MetadataLoadContext on RhinoCommon 8.0.23304.9001) confirmed
`uint BeginUndoRecord(string)` / `bool EndUndoRecord(uint)` and that Begin returns 0 when a
record is already active — nesting-safe by skipping End on 0 (recorded in
api-notes-rhinocommon.md). New `ToolHelpers.WithUndoRecord(doc, action, body)` brackets the
UI-thread lambda of all 28 doc-mutating actions: rhino_scene (set_layer, set_name, set_color,
layer_create/set/delete, hide, show, delete, place_image), rhino_render (lights, materials,
environments, settings/ground/sun/skylight, view_save/view_delete), and — found by behavior
trace, outside the item's literal file list — `gh_canvas(action='bake')`, which bulk-adds
objects in a loop. Exclusions by decision: `script` (native commands own their records),
select/deselect (selection isn't undoable), camera/zoom/display/view_load (viewport state),
reads. Doc-audit: tool Notes (both Rhino tools), bake Tips, RenderingGuide "Undo Behavior"
section, CHANGELOG. Host behavior queued as VRF-010.

## 2026-07-02: bounded snapshot store + snapshot_delete (reliability chunk 04)

<!-- prawduct: type=feature | chunks=04 | scope=reliability | status=merged | release=v1.5.0 -->

**Why:** [GHD-6M2J] `GhDocumentTool._snapshots` was an unbounded process-lifetime dictionary of
full document serializations — and with undo/redo formally cut, every documented mutation
workflow ("snapshot before changes, revert to restore") funnels into it, so memory grew for the
life of the Rhino session.

**What:** New host-free `Core/SnapshotStore.cs` (LogBuffer pattern): cap 20, oldest-first
eviction, same-name re-save replaces in place (refreshing its age) without eviction, oldest-first
listing with `createdAtUtc`, `Remove` — linked into the test project with 7 tests including a
concurrent hammer. `gh_document` swapped onto it; new `snapshot_delete` action (missing name is
an error); `snapshot` response gains `maxSnapshots` + `evicted`. Doc-audit: ActionInfo
(snapshot/snapshots/snapshot_delete), server instructions, README, CHANGELOG (also added the
missed user-facing CHANGELOG entry for chunk 03's teardown change); stale undo/redo `TODO`
comments retired (PR #25 reviewer note). 406/406 green.

## 2026-07-02: Stop() drain moved off the UI thread (reliability chunk 03)

<!-- prawduct: type=bugfix | chunks=03 | scope=reliability | status=merged | release=v1.5.0 -->

**Why:** [MCP-3D8V] `McpServer.Stop()` always runs on the UI thread (component port-change,
`RemovedFromDocument`, `DocumentContextChanged`), while an in-flight handler is a worker blocked
in `RhinoApp.InvokeAndWait` waiting for that same UI thread — so the synchronous
`DrainWithin(2s)` could never succeed for exactly the handlers it protects: a guaranteed ~2s
Rhino UI stall whenever teardown overlapped a request, and the drain's "handlers finish against
a still-valid context" comment was wrong for that case.

**What:** `Stop()` now splits: synchronously transition to `Stopping`, cancel the listener CTS,
and stop/close the `HttpListener` (port freed immediately for a replacement server), then run
the listener-task wait + `DrainWithin` + final teardown (`_context` release, `Stopped`
transition) on a `Task.Run` background task that logs the drain outcome. With the UI thread
returned, a handler blocked in `InvokeAndWait` can actually complete, so the drain now does what
its comment always claimed. Correctness during the un-drained window remains protected by the
captured-context guard in `HandleToolCallAsync`. Host-observable behavior (no UI freeze,
restart-while-draining) queued as VRF-009; 399/399 green.

## 2026-07-02: ServerState enum as lifecycle single source of truth (reliability chunk 02)

<!-- prawduct: type=refactor | chunks=02 | scope=reliability | status=merged | release=v1.5.0 -->

**Why:** [MCP-9F3Q] `McpServer` lifecycle was reconstructed from three interdependent signals
(`IsRunning` + `StartError` + `_context`); upcoming teardown-topology work (chunk 03) adds a
real Stopping window, which that combinatorial encoding can't represent safely.

**What:** New host-free `Core/ServerState.cs` — `Stopped/Starting/Running/Stopping/Failed` enum
plus a `ServerStateTransitions` predicate table (CanStart from Stopped|Failed only, CanStop from
Running only) linked into the test project with a full transition-table test (10 cases).
`McpServer` now holds one volatile `_state` field; `IsRunning` is derived, `StartError` is set
only on the Failed transition, and Start/Stop guards go through the shared predicates.
Behavior-preserving: component status output strings unchanged; 399/399 green.

## 2026-07-02: DrainWithin fault-vs-timeout contract pinned (reliability chunk 01)

<!-- prawduct: type=bugfix | chunks=01 | scope=reliability | status=merged | release=v1.5.0 -->

**Why:** [MCP-5T7W] `InFlightRequests.DrainWithin` returned `true` on any `AggregateException`,
which could mask a drain-budget timeout coinciding with a handler fault — the combination was
undecided and untested.

**What:** Decision recorded at the catch site: a faulted handler counts as drained, but drain
success requires every snapshotted handler to have completed — on `AggregateException` the
method now returns `pending.All(t => t.IsCompleted)` instead of `true`. Empirically
`Task.WaitAll` only throws that exception when all observed tasks completed (a timeout returns
`false` instead), so behavior is unchanged today; the check makes the contract independent of
that undocumented BCL nuance. Two regression tests added (fault+timeout → false;
fault+late-completion within budget → true), 389/389 green.

## 2026-07-02: janitor full reliability audit — hygiene, doc contract, HIGH bugs, MEDIUM sweeps, testability

<!-- prawduct: type=maintenance | chunks=01,02,03,04,05,06 | scope=janitor-2026-07-02 | status=merged | release=v1.5.0 -->

**Why:** The user requested a thorough audit ("quality, bugs, consistency, gaps — ultra reliable
and stable"). A six-agent survey found the dominant defect class was mutate-then-report-success
(silent-success paths in the tool layer), plus agent-facing doc drift and governance debt.

**What (branch `chore/janitor-2026-07-02`, six chunks, each Critic-gated):**
- Chunk 01 hygiene: merged-branch cleanup (11 refs), `.work-model-index.json` untrack+ignore,
  shipped-feature bug report archived + advisory resolved, stale gitflow build plan replaced,
  unused images pruned, stale sln removed.
- Chunk 02 doc contract: 14 verified drift fixes across guides/ActionInfo/server
  instructions/prompts/README/csproj message.
- Chunk 03 HIGH bugs (6): document-close server teardown; configure JSON param-wipe;
  preview/enable per-id results; layer_delete current-layer ordering; material_create PBR
  application; place_image replace ordering. New host-free `Core/ScriptParamDefs` (+12 tests).
  Host-bound halves → VRF-007.
- Chunk 04 MEDIUM GH+boundary (29 items): silent-success→structured errors, group protection,
  cluster-safe clear, bulk expire, lossless JSON-RPC id echo, binding-error contract, coercion,
  registry races, case-insensitive actions, strict bool.
- Chunk 05 MEDIUM Rhino (17 items): InvariantCulture coordinates, select safety + type='all',
  light validation, render-wait precheck, sun mode restore, error-field + notFound conventions,
  FindByLayer guards, nested-layer resolution/creation, place_image absolute path.
- Chunk 06 testability: `ParseHelpers`/`ResponseHelpers`/`McpNaming`/`PromptTemplate`/`LogBuffer`
  extracted host-free with tests (+108); PromptRegistry unfilled-placeholder bug fixed; tool-name
  contract pinned; xUnit1031 fixed async; drain-snapshot flake eliminated via deterministic seam.

**Verification:** 224 → 370 tests, all green; plugin + test builds 0 warnings; Critic per chunk
(03: clean; 04: 1 warning resolved in 06, 1 note fixed in-chunk; 05: 2 notes — one fixed
in-chunk, one closed in 06; 06: clean). Cumulative Critic at close-out (0 blocking, 2 warnings,
11 notes — warnings resolved in the findings-fix commit; see below). Live-Rhino halves recorded
in VRF-007 (Chunk 03 HIGH fixes) and VRF-008 (Chunk 04/05 sweep behaviors); queue burn-down
(VRF-001..008) remains operator work.

**Deliberate deviations recorded (cumulative-Critic notes):**
- Chunk 06 plan said "swappable console sink" for DebugLog; shipped as a host-free `LogBuffer`
  whose `Add()` returns the emission decision, with `RhinoApp.WriteLine` staying in the wrapper —
  simpler seam, same testability outcome (no test needs to observe the sink itself).
- `JsonTypeConverter` number→bool is deliberate C-truthiness (`GetDouble() != 0`, tested): JSON
  numbers for bools get numeric semantics (0/nonzero), while STRING booleans use the strict
  true/false/1/0/yes/no grammar that errors on garbage. Rationale: a number is unambiguous about
  truthiness; a garbage string is not.
- `select type='all'` opt-in went slightly beyond the plan text ("require ≥1 filter") — accepted,
  documented in ActionInfo + CHANGELOG.

## 2026-06-24: gitflow + two-step release (prep/publish)

<!-- prawduct: type=tooling | scope=gitflow-release-refactor | status=merged -->

**Why:** Adopted gitflow — `develop` is now the default/integration branch and `main` is the
release surface, to be strict-protected (require PR + `build-test`, no bypass). The old
`scripts/release.sh` pushed the `Release vX.Y.Z` commit **directly to `main`**, which strict
protection rejects — breaking releases. Split it into `prep` (on develop: bump version + CHANGELOG,
build `.gha`, commit, push, open a `develop→main` PR) and `publish` (on main, after the PR merges:
build the yak package, **push only the `vX.Y.Z` tag** — branch protection guards branches not tags —
create the GitHub Release, push to yak). Also: `base_branch: develop` in project-state.yaml so the
prawduct gates/`resolve-base` diff against develop; CI runs on `develop` pushes; untracked the
gitignored-yet-tracked `dist/manifest.yml` (restamped every release). A bash release script isn't
unit-testable — syntax + branch-guard + dispatch were verified statically; the next real release is
operator-verified (VRF-006). Also disabled xUnit test parallelization
(`src/Cordyceps.Tests/AssemblyInfo.cs`) — a pre-existing flaky timing test
(`InFlightRequestsTests.Count_…`) starved its removal continuation under parallel execution on the
2-core CI runner, and `build-test` must be reliable since it becomes the required main-protection
check. Docs: `docs/release-process.md` rewritten, `CLAUDE.md` Publishing updated. Strict main
protection itself is applied as a separate step after this lands.

## 2026-06-24: slider add-params + configure wire-preservation (v1.4.12)

<!-- prawduct: type=bugfix | scope=gh-canvas-slider-add | status=merged -->

**Why:** `gh_canvas(action='add', type='slider', ...)` silently dropped min/max/value/decimals —
the `add` dispatcher forwarded only type/x/y/nickname to `ActionAdd`, so a new slider always
landed at the default 0–1 range / 0.5 value regardless of args, forcing a second `config` call.
The slider-config decision is now a pure host-free helper (`Core/SliderConfig.cs`, parsing the
value with InvariantCulture — a latent locale fix) shared by both `add` (applied after
`AddObject` when the new object is a `GH_NumberSlider`: range first, then value so it isn't
clamped to the old range) and `config` (refactored for parity). 19 unit tests incl. the
dropped-params regression; non-slider adds ignore the params. Host glue queued for operator
verification (VRF-004). (GHC-7X4B)

<!-- prawduct: type=bugfix | scope=gh-script-configure-wires -->

**Why:** `gh_script(action='configure')` unregistered every input/output param and re-registered
them from scratch (`ConfigureViaVariableParams`), silently destroying ALL wires — even on
name-unchanged params — and reported nothing lost. It now reshapes params by name via the same
LCS sync `set` already used, extracted to a pure helper (`Core/ParamSyncPlan.cs`): name-matched
params keep their connections; wires on renamed/removed params return in a `lostConnections` array
(with a `reconnectHint`) usable directly with `gh_wire(action='connect')`. `configure` is now also
a partial update — omit a side to leave it untouched, pass `[]` to clear it (previously,
configuring only `inputs` wiped all `outputs`). 10 unit tests; host glue queued for operator
verification (VRF-005). (GHS-3W9N)

## 2026-06-24: gh_script(set) flags a silently-broken Script component (issue #15)

<!-- prawduct: type=bugfix | scope=gh-script-language | status=merged -->

**Why:** Setting a directive-less body on a bare unified **Script** component (Rhino 8's
`ScriptComponent`, which has no language until one is chosen) leaves it unable to compile —
it fails at solve time with "Can not determine input code language" and emits no geometry.
`gh_script(set)` returned `codeSet:true` with no signal, so the natural retry silently
re-broke it (the painful core of issue #15). The error is produced during SolveInstance, so
it can't be observed synchronously inside `set` without forcing a cluster-unsafe recompute
(confirmed live: with the solver disabled the error doesn't surface). Instead, `set` now
detects the at-risk condition statically — a unified `ScriptComponent` whose final source
(after directive preservation) still has no language directive — and returns a
`languageWarning` with the remedy. Both `set` and `configure` (which share the same
`SetSource` path) emit it. New pure helper `ScriptDirective.LanguageWarning(...)` with 11
unit tests; docs audited (CommonErrorsGuide, gh_script set/configure help, CHANGELOG). Live
investigation also showed the component is **recoverable** by setting source with a
directive, correcting issue #15's "permanently broken" claim. Reported by @anthonyesau (#15).

<!-- prawduct: type=bugfix | scope=gh-document-save | status=merged -->

**Why:** `gh_document(action='save')` could not overwrite an existing `.gh` (binary) file —
every repeated save returned a bare `"Failed to write file"`, breaking incremental
checkpoints and "save before mutating" safety nets. Root cause was a format-dependent
overwrite flag: the `.gh` branch passed `overwrite=false` to GH_IO's
`GH_Archive.WriteToFile`, while `.ghx` correctly passed `overwrite=true`. The save policy
is now a pure, host-free helper (`Core/GhArchiveSave.cs`) that returns
`overwrite=true, rememberPath=true` for both formats (File→Save semantics), with 15 unit
tests including a regression guard for the format-dependent overwrite. Reproduced and to be
re-verified live against the running Cordyceps MCP server. Reported by @anthonyesau (#14).

<!-- prawduct: type=bugfix | chunks=01,02,03 | scope=solidity-hardening | status=merged -->

**Why:** Stage 1 of the firsthand solidity-hardening analysis (2026-06-21) closes the
highest-severity operational hazards in the HTTP/SSE server and UI-thread marshaling.
**Chunk 01** — `GrasshopperContext.ExecuteOnUiThread` could wedge every later request forever
behind one hung UI operation (infinite-loop script, modal) with no recovery but a Rhino restart,
and would deadlock on a re-entrant UI-thread caller. New host-free `Core/DocumentLock` bounds the
mutex acquire (7 unit tests); the wait on the UI invocation is bounded; a re-entrancy guard
(`RhinoApp.InvokeRequired`) runs inline when already on the UI thread; on timeout the caller now
gets a structured `{success:false,error:"… timed out"}` (existing `FormatExceptionResult` shapes
it — no `McpServer` change). `verify-api` confirmed `InvokeAndWait` exposes no native
timeout/cancellation (notes in `api-notes-rhinocommon.md`), so the timeout bounds waiters, not the
holder. **Chunk 02** — three lifecycle defects: a failed listener bind was swallowed and returned a
silently-dead server, so the component now records an actionable `StartError` surfaced as a canvas
error + Status output (no more bare "NOT RUNNING"); request handlers were fire-and-forgotten, so
they are now tracked via host-free `Core/InFlightRequests` (8 unit tests) and drained on `Stop()`
within the shutdown budget; and the teardown race that let an in-flight handler NRE on a nulled
`_context` is closed by capturing `_context` once and returning a structured "shutting down"
result. **Chunk 03** — two remaining data races: `CommandCount`/`LastCommand` (unsynchronized
auto-properties mutated from concurrent HTTP worker threads) now route through host-free
`Core/CommandStats` (`Interlocked.Increment` + `Volatile`; 5 tests incl. a genuinely-concurrent,
mutation-verified lost-increment test), and `GhDocumentTool._snapshots` (written on the UI thread,
listed off-thread) is now a `ConcurrentDictionary`. 169 tests pass; Release build 0/0. Host-coupled
behavior is verified live in Rhino — operator queue `VRF-001/002/003` (agent has no headless
Rhino). Doc-audit: root CHANGELOG + `CommonErrorsGuide.md` ("timed out" and "shutting down" rows).

## 2026-06-21: Place raster images as PictureFrame objects — rhino_scene(place_image) (RSC-2H9K)

<!-- prawduct: type=feature | chunks=01 | scope=place-image | status=merged -->

**Why:** External feature request from the Puzzles print-and-cut generator (Chunk 06, deferred on
this): preview a cut layout *over* printed artwork by placing the image as a real Rhino object. No
prior path existed — `rhino_render material_texture` is a PBR texture, not a placed object. New
`rhino_scene(action='place_image')` places a Rhino PictureFrame at a caller-specified
origin/size/optional Z-rotation on an auto-created layer and returns the new object id;
`replace=true`+`name` makes repeated parametric calls idempotent. Foreign API
`AddPictureFrame(Plane, path, asMesh, width, height, selfIllumination, embedBitmap)` re-verified by
reflection on Rhino 8 RhinoCommon (no `ObjectAttributes` overload → layer/name set post-add). New
host-free `Core/PlaceImageValidation.cs` (path-exists + positive-dimension checks) with 12 unit
tests; the find-or-create-layer block shared with `set_layer` was extracted to one helper. Doc
audit: server instructions, `rhino_scene` ActionInfo (`place_image`), root CHANGELOG. Release build
0/0; 149 tests pass. Per project-preferences, the document-touching handler is verified live in
Rhino, not by host-free unit tests.

## 2026-06-21: Flag failed component introspection in gh_inspect docs (CQ-7T4P)

<!-- prawduct: type=bugfix | chunks=01 | scope=cq-7t4p | status=merged -->

**Why:** `gh_inspect(action='docs')` returned `success:true` with empty `inputs`/`outputs`
when a component proxy couldn't be instantiated, so callers couldn't distinguish "component
has no parameters" from "introspection failed" (CQ-5J9N had added the log but no caller
signal). `ToolHelpers.WithProxyComponent` now returns `bool` (did the callback run); on
failure `ActionDocs` adds `paramsUnavailable:true` + a `note`, success-path shape unchanged.
The cumulative Critic surfaced a third params-surfacing path — `gh://component/{name}`
(`ResourceRegistry.GenerateComponentDocumentation`), reached via a direct `CreateComponent`
that bypassed the helper — which silently omitted its markdown Inputs/Outputs sections; now
emits a `## Parameters` note instead. Doc-audit: root CHANGELOG `[Unreleased]` Fixed entry +
`gh_inspect` `docs` ActionInfo Tips. Critic `final` + `verify-resolutions` clean; 137 tests
pass. Committed `ccd8e1d` on `fix/proxy-params-unavailable`; pushed direct to main (`d1e1787`).

## 2026-06-20: Backlog batch — docs sync, test coverage, code-quality cleanup

<!-- prawduct: type=maintenance | chunks=01,02,03,04 | scope=backlog-batch-2026-06-20 | status=merged -->

**Why:** four ready backlog items addressed as one stacked PR on `fix/mcp-error-contract`.
(DOC-8M3T) `GetServerInstructions()` lagged the code by 11 live actions agents see on MCP
initialize — synced in code-dispatch order (gh_canvas `zoomable`; rhino_scene `set_color`,`bbox`;
rhino_render 4 view + 4 light actions). (TST-6W7H) the host-free `RequestValidator` +
`UnifiedToolHelpers` contract classes had zero coverage — linked into the test project with ~69
new unit cases (suite 68→137). (CQ-2X8B) duplicated proxy-instantiation unified behind
`ToolHelpers.WithProxyComponent`; dead `GrasshopperContext.ExecuteOnUiThreadAsync` removed.
(CQ-5J9N) every silent `catch` swallow in `src/Cordyceps/` now logs with context or is narrowed
to the expected exception type, and the MCP tool-boundary catch logs the full exception (type +
stack) operator-side. Internal quality + agent-facing docs; no shipped-plugin behavior change, so
no root CHANGELOG entry. Merged to main via PR #18 (squash `f9e0663`).

## 2026-06-20: Honor the MCP error contract at the server boundary (MCP-4R2K)

<!-- prawduct: type=bugfix | scope=mcp-error-contract | status=merged -->

**Why:** `McpServer.HandleToolCallAsync` hardcoded the transport `isError` flag to `false`, so
tool results carrying `{"success": false}` were reported to MCP clients as successes; and
tool-body exceptions escaped as raw JSON-RPC `-32603` protocol errors (only `GhScriptTool`
caught them), so the 7 tools behaved inconsistently. Both are now routed through a new
host-free `Core/McpResultFormatter` (`IsErrorResult` derives `isError` from the parsed
`success` field; `FormatExceptionResult` converts any tool-body throw — unwrapping
`TargetInvocationException` — into a `{success:false,error}` result with `isError:true`),
applied uniformly at the boundary. 15 new unit tests (68 total). Broad boundary catch carries a
`prawduct:allow` waiver. Committed `905825c`/`0a525d0` on `fix/mcp-error-contract`; merged to main
via PR #18 (squash `f9e0663`). Root CHANGELOG `[Unreleased]` Fixed entry + `McpTestingGuide.md`
contract line already added.

## 2026-06-20: Drop attribution trailer from release commits

<!-- prawduct: type=chore | scope=release-attribution | status=merged -->

**Why:** `scripts/release.sh` `git_commit_and_tag` hard-coded a `🤖 Generated with …` +
`Co-Authored-By: Claude …` trailer on every `Release vX.Y.Z` commit, contradicting the
project's `Commit attribution: none` preference. Removed both lines so release commits carry a
plain `Release vX.Y.Z` message. Release tooling only; no shipped-plugin change.

## 2026-06-20: Janitor maintenance pass

<!-- prawduct: type=maintenance | chunks=01,02,03 | scope=janitor-2026-06-20 | status=merged -->

**Why:** periodic `/prawduct:janitor` survey + user-approved cleanup. Fixed release-metadata
drift (tracked `manifest.yml` was stale at 1.4.0 while shipping 1.4.9) and closed the gap that
let it drift — `scripts/release.sh` now bumps the manifest version, not just the csproj. Added
the first build/test CI (`.github/workflows/dotnet-ci.yml`: `dotnet build`/`dotnet test` on
push/PR) so the 53 xUnit tests run automatically. Removed obsolescence: a 402-line unreferenced
`src/` planning doc that contradicted the HTTP+SSE implementation, the shipped GHS-7K2P bug
report, stray `output/`/`memory/` dirs, and merged/stale branches. Documented
`Core/ToolHelpers.cs` in CLAUDE.md. No compiled C# changed; build 0/0, 53/53 tests pass. Not
user-facing (dev tooling + release plumbing), so no root CHANGELOG entry.

## 2026-06-20: Wire .NET/xUnit test evidence into the Prawduct gate (TST-9Q4M)

<!-- prawduct: type=tooling | chunks=01 | scope=gate-soundness | status=merged -->

**Why:** `prawduct-hook test-evidence record` defaulted to pytest and could not run this
C#/xUnit repo, so no `.test-evidence.json` was ever produced and the freshness/Critic/PR
gates were unsound (every code chunk warned "no test evidence"). Added the
`JunitXml.TestLogger` package to `Cordyceps.Tests` and declared `test_command` in
project-state.yaml so the hook runs the real xUnit suite and records exact counts.
Verified end-to-end: `test-evidence record` → 53 passed / 0 failed @ HEAD; `test-status`
→ current. No user-facing change (dev tooling), so no root CHANGELOG entry.

## 2026-06-20: Fix gh_script dropping the script language directive (GHS-7K2P)

<!-- prawduct: type=bugfix | chunks=01 | scope=gh-script-language | status=merged -->

**Why:** `gh_script(set/configure)` replaced the whole script body via `SetSource`,
stripping the Rhino 8 language directive (`#! python 3`, `// #! csharp`) when the new
body omitted it — causing "Can not determine input code language" at solve time and no
geometry, which bit anyone following the plain-body examples in the docs. The
component's existing directive is now preserved automatically (a directive in the new
code is respected as-is). New pure helper `Core/ScriptDirective.cs` with 28 unit tests;
docs audited (CommonErrorsGuide, gh_script help, templates, root CHANGELOG).
