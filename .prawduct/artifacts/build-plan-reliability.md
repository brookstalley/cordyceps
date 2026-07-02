<!-- Scope-named plan per the gitflow plan lifecycle: the janitor plan
     (artifacts/build-plan.md) is complete but merge-pending, so it stays the
     `active_build_plan` until its PR merges. Repoint
     `active_build_plan: artifacts/build-plan-reliability.md` in
     project-state.yaml when the janitor PR merges (pr merge-flow step 8) and
     BEFORE starting Chunk 01. -->
---
artifact: build-plan
version: 2
scope: reliability
depends_on: []
last_validated: 2026-07-02
---

# Build Plan — reliability follow-through

Branch: `feat/reliability-follow-through` (stacked on `chore/janitor-2026-07-02`;
rebase onto `develop` once the janitor PR merges).
Scope: the six backlog items promoted in the 2026-07-02 triage — MCP server
lifecycle (MCP-5T7W, MCP-9F3Q, MCP-3D8V), snapshot-store bound (GHD-6M2J),
Rhino undo records (RSC-6K1W), and the stop-encouraging-renames guidance audit
(GHC-8V3T). The triage's four thematic chunks are split into six build chunks so
each is reviewable in one Critic pass.

## Requirements Confidence

**Level:** High

**Why:** All six items are `stage: ready` with recorded fix shapes; the one open
contract question (keep vs. deprecate rename) was decided by the user on
2026-07-02 (keep the capability, change the guidance).

**Open assumptions / unknowns:** Design-level inferences made in this plan, each
vetoable:
- [ASSUMPTION: `DrainWithin` should return `false` when the budget expires even
  if a handler also faulted — faulted-only still returns `true` | MED impact |
  user can override]
- [ASSUMPTION: Stop() teardown topology is "synchronous cancel + listener stop,
  drain moved to a background task" rather than "skip drain when on UI thread" —
  the backlog offered both; background drain keeps the drain's protective intent
  for the off-UI-thread caller case while removing the guaranteed UI stall | MED
  impact | user can override]
- [ASSUMPTION: snapshot cap = 20 snapshots, oldest-first eviction by creation
  time, same-name re-snapshot still overwrites in place | MED impact | user can
  correct the number]
- [ASSUMPTION: the `script` action (raw Rhino command strings) is excluded from
  undo-record wrapping — native commands create their own undo records; nesting
  ours around theirs risks confusing undo grouping | LOW impact | user can
  override]
- [ASSUMPTION: panel/scribble nicknames stay endorsed as annotation (they ARE the
  annotation convention); only guidance encouraging nicknames on functional
  components is removed | LOW impact | user can correct]

**What would raise confidence:** N/A (veto pass over the assumptions above is
sufficient).

## Status

- [ ] Chunk 01: DrainWithin timeout-vs-fault decision + regression test
- [ ] Chunk 02: ServerState enum as single source of truth
- [ ] Chunk 03: Stop() drain off the UI thread
- [ ] Chunk 04: Bound the snapshot store + snapshot_delete
- [ ] Chunk 05: Rhino undo records around mutating actions
- [ ] Chunk 06: Rename-guidance audit — annotate via groups
Context: Plan authored 2026-07-02 (triage session). No chunks started. Blocked on
the janitor PR (chore/janitor-2026-07-02 → develop) merging first; then rebase,
repoint active_build_plan, and start Chunk 01.

## Scaffolding

Existing repo — no scaffold. Standing commands:

- Build: `dotnet build src/Cordyceps/Cordyceps.csproj -c Release` (Debug is blocked)
- Tests: `python3 <plugin>/bin/prawduct-hook test-evidence record` (runs the
  declared `test_command`, stamps evidence — do NOT hand-edit `.test-evidence.json`)
- After any build/test: `git checkout -- releases/Cordyceps.gha` (post-build copy
  target restamps this tracked binary; it must only change at release time)
- Baseline at plan time: 224/224 tests green, 0 build warnings.

### Verification Strategy

Host-free logic (Core/ files linked into `src/Cordyceps.Tests/`) gets xUnit
regression tests in the same chunk. Host-coupled behavior (UI-thread teardown,
document revert, undo records) cannot be unit-tested — each such chunk appends an
entry to `.prawduct/operator-verification.md` describing the live-Rhino check,
to be burned down together with the pending VRF-001..007 queue (backlog TST-8B3D).
Timing-contract tests must exercise real concurrency (background task + a
`TaskCompletionSource` that never completes during the assertion window — see
learnings: the vacuous `DrainWithin_TakesSnapshot` test) and stay deterministic
under the serial test runner (parallelization is disabled on purpose).

## Project Structure

Existing structure, unchanged. New host-free logic goes in `src/Cordyceps/Core/`
as individually-linkable files (BCL only, no Grasshopper/Rhino/`DebugLog`
references — the test project links these files directly). Tool behavior stays in
`src/Cordyceps/Tools/Unified/`. Agent-facing docs live in the surfaces named by
the CLAUDE.md documentation-audit table; every chunk below ends with that audit.

## Build Chunks

### Chunk 01: DrainWithin timeout-vs-fault decision + regression test

- **Description:** (MCP-5T7W) `src/Cordyceps/Core/InFlightRequests.cs::DrainWithin` currently
  catches `AggregateException` from `Task.WaitAll` and returns `true`
  unconditionally, which masks a budget-timeout that coincides with a handler
  fault. Decision (assumption 1 above): a faulted handler counts as drained (it
  no longer touches shared state), but the return value must still report whether
  the snapshot actually drained — on `AggregateException`, return
  `pending.All(t => t.IsCompleted)` instead of `true`. Record the decision in a
  code comment at the catch site.
- **Depends on:** none
- **Artifacts consumed:** backlog item MCP-5T7W; `.prawduct/learnings.md`
  (timing-test discipline)
- **Deliverables:** `src/Cordyceps/Core/InFlightRequests.cs` (catch-site fix);
  `src/Cordyceps.Tests/InFlightRequestsTests.cs` (new cases)
- **Tests:** (a) one faulted task + one never-completing task (TCS held open
  through the assertion window), budget expires → `DrainWithin` returns `false`;
  (b) faulted-task-only → returns `true` (existing contract preserved). Drive the
  fault/timeout overlap with real concurrency via the `OnDrainSnapshot` test seam.
- **Acceptance criteria:** new tests pass; all existing `InFlightRequests` tests
  pass unchanged (contract elsewhere preserved); full suite green.
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic` run (inferred mode) and blocking findings resolved
  3. Committed with tagged change-log entry (`scope=reliability`)

### Chunk 02: ServerState enum as single source of truth

- **Description:** (MCP-9F3Q) Lifecycle state in `McpServer` is reconstructed
  from `IsRunning` + `StartError` + `_disposed`. Introduce a `ServerState` enum —
  `Stopped | Starting | Running | Failed | Stopping` (Stopping is consumed by
  Chunk 03) — as the single source of truth, held in a private `_state` field in
  `src/Cordyceps/McpServer.cs`; derive `IsRunning` (`state == Running`) and keep
  `StartError` as the detail string populated on `Failed`. Behavior-preserving
  refactor: identify transition sites by the pattern "any write to `IsRunning`,
  `StartError`, or `_disposed`", not by line number. Put the enum (and any pure
  transition-validation logic, if extracted) in a
  new `src/Cordyceps/Core/ServerState.cs` (host-free) so it links into the test
  project.
- **Depends on:** none (independent of Chunk 01)
- **Artifacts consumed:** backlog item MCP-9F3Q
- **Deliverables:** new `src/Cordyceps/Core/ServerState.cs`;
  `src/Cordyceps/McpServer.cs` (state field + derived properties);
  `src/Cordyceps/CordycepsComponent.cs` only if its status readouts need the new
  property names (message strings must not change)
- **Tests:** if transition logic is extracted as a pure helper, unit-test the
  legal-transition table host-free; otherwise this chunk is covered by the
  existing suite (behavior-preserving) plus compilation of the linked file.
- **Acceptance criteria:** full suite green; grep shows no remaining independent
  writes to `IsRunning`/`StartError` outside the state-transition path; component
  status output strings unchanged (doc-audit: nothing agent-facing surfaces the
  internal state names — verify `gh_inspect` and server instructions mention no
  lifecycle vocabulary that changed).
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic` run (inferred mode) and blocking findings resolved
  3. Committed with tagged change-log entry (`scope=reliability`)

### Chunk 03: Stop() drain off the UI thread

- **Description:** (MCP-3D8V) `McpServer.Stop()` is always invoked on the UI
  thread (component port-change, `RemovedFromDocument`, `DocumentContextChanged`)
  while an in-flight handler may be a worker blocked in `RhinoApp.InvokeAndWait`
  waiting for that same UI thread — so the synchronous
  `_inFlight.DrainWithin(2s)` can never succeed for exactly the handlers it
  protects: a guaranteed ~2s UI stall. Fix (assumption 2): `Stop()` synchronously
  transitions to `Stopping`, cancels the listener CTS, and stops the
  `HttpListener` (freeing the port for immediate restart), then moves the
  `DrainWithin` wait and final teardown onto a `Task.Run` continuation that logs
  the drain outcome (drained / timed out — Chunk 01's return value now
  distinguishes fault-plus-timeout) and transitions to `Stopped`. Correctness
  during the un-drained window remains protected by the existing
  captured-context guard. Fix the now-wrong comment claiming handlers finish
  against a still-valid context. Consider `Start()` re-entry while a background
  teardown is pending: with the port already freed synchronously, a new `Start()`
  must be safe — guard via the `ServerState` transitions from Chunk 02.
- **Depends on:** Chunk 01 (drain return semantics), Chunk 02 (Stopping state)
- **Artifacts consumed:** backlog items MCP-3D8V, MCP-9F3Q;
  `.prawduct/artifacts/api-notes-rhinocommon.md`
- **Deliverables:** `src/Cordyceps/McpServer.cs` (Stop topology);
  `src/Cordyceps/CordycepsComponent.cs` (only if the stop-path call contract
  changes)
- **Foreign API:** RhinoCommon (RhinoApp.InvokeRequired / InvokeAndWait)
- **Tests:** host-free coverage where possible (state transitions around
  stop/restart); the UI-stall behavior itself is host-coupled → operator
  verification entry: place component, issue a long-running tool call, delete the
  component mid-request, confirm no multi-second Rhino freeze and a clean
  server restart on re-place.
- **Acceptance criteria:** no blocking wait on `DrainWithin` remains on the
  synchronous `Stop()` path when called from the UI thread; drain outcome is
  logged via `Core.DebugLog` from the background task; full suite green;
  operator-verification entry appended.
- **Visual change:** yes
- **Done when:**
  0. verify-api — re-read `.prawduct/artifacts/api-notes-rhinocommon.md`;
     confirm InvokeRequired/InvokeAndWait semantics still match the design
     before coding
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic` run (inferred mode) and blocking findings resolved
  3. Committed with tagged change-log entry (`scope=reliability`)

### Chunk 04: Bound the snapshot store + snapshot_delete

- **Description:** (GHD-6M2J) `GhDocumentTool._snapshots` is a static unbounded
  `ConcurrentDictionary<string, byte[]>` of full document serializations, and the
  janitor branch now steers every mutation workflow into it ("undo/redo disabled —
  snapshot before changes"). Extract a
  new `src/Cordyceps/Core/SnapshotStore.cs` (host-free, lock-protected, modeled on
  `src/Cordyceps/Core/LogBuffer.cs`): capacity 20 (assumption 3), oldest-first
  eviction by creation time, same-name upsert refreshes in place without eviction,
  list returns name + size + created-at in insertion order, plus `Remove(name)`.
  Swap `GhDocumentTool` onto it (snapshot/revert/list under the existing threading
  model — writes on the UI thread, list off-thread) and add a `snapshot_delete`
  action (name → removed or not-found error; per learnings, a missing name is an
  error, never silent success). The snapshot response gains an `evicted` field
  naming anything the cap pushed out.
- **Depends on:** none
- **Artifacts consumed:** backlog item GHD-6M2J; `src/Cordyceps/Core/LogBuffer.cs`
  as the pattern
- **Deliverables:** new `src/Cordyceps/Core/SnapshotStore.cs`;
  new `src/Cordyceps.Tests/SnapshotStoreTests.cs`;
  `src/Cordyceps/Tools/Unified/GhDocumentTool.cs` (store swap + new action)
- **Tests:** SnapshotStore host-free: cap enforcement + oldest-first eviction
  order, upsert-does-not-evict, remove semantics, concurrent add/list safety.
- **Acceptance criteria:** suite green; doc-audit complete — `gh_document`
  ActionInfo documents the cap, eviction, `evicted` field, and `snapshot_delete`;
  `McpServer.cs` `GetServerInstructions()` action list gains `snapshot_delete`;
  the "use snapshot/revert" guidance surfaces (ActionInfo, server instructions,
  `README.md`) mention the bound; `CHANGELOG.md` entry under `[Unreleased]`.
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic` run (inferred mode) and blocking findings resolved
  3. Committed with tagged change-log entry (`scope=reliability`)

### Chunk 05: Rhino undo records around mutating actions

- **Description:** (RSC-6K1W) No code path calls
  `RhinoDoc.BeginUndoRecord`/`EndUndoRecord`, so a bulk MCP mutation leaves one
  undo step per object — Ctrl-Z after a fifty-object `set_layer` reverts one
  object. Wrap every mutating action body in `rhino_scene` (`set_layer`,
  `set_name`, `set_color`, `layer_create`, `layer_set`, `layer_delete`, `hide`,
  `show`, `delete`, `place_image`) and `rhino_render` (`light_add`, `light_set`,
  `light_delete`, `material_create`, `material_apply`, `material_delete`,
  `env_set`, `env_create`, and any other doc-mutating dispatch entries found in
  the Actions maps) in `doc.BeginUndoRecord("Cordyceps <action>")` with
  `doc.EndUndoRecord(serial)` in a `finally`. Identify the sites by pattern —
  every dispatch entry that mutates `RhinoDoc.ActiveDoc` — not by today's action
  list alone. Exclusion (assumption 4): the `script` action, since native Rhino
  commands manage their own undo records. Prefer a small shared wrapper helper
  (e.g. in the tool base or `src/Cordyceps/Core/ToolHelpers.cs`-adjacent host-coupled code)
  over 18 copy-pasted try/finally blocks; per learnings, trace by behavior so no
  mutating path bypasses the wrapper.
- **Depends on:** none
- **Artifacts consumed:** backlog item RSC-6K1W
- **Deliverables:** `src/Cordyceps/Tools/Unified/RhinoSceneTool.cs`,
  `src/Cordyceps/Tools/Unified/RhinoRenderTool.cs`, shared wrapper location TBD
  by builder within existing files
- **Foreign API:** RhinoCommon (BeginUndoRecord/EndUndoRecord)
- **Tests:** host-coupled — no unit tests possible. Operator verification entry:
  bulk `set_layer` on many objects, single Ctrl-Z restores all; `layer_delete`
  with object moves undoes as one step.
- **Acceptance criteria:** every doc-mutating action in both Rhino tools runs
  inside an undo record named `Cordyceps <action>`; `EndUndoRecord` is guaranteed
  on the error path (finally); build clean, suite green; doc-audit — undo-grouping
  note added to both tools' ActionInfo notes and the `RenderingGuide.md` /
  relevant Knowledge guide mentions Ctrl-Z now reverts a whole action;
  `CHANGELOG.md` entry; operator-verification entry appended.
- **Visual change:** yes
- **Done when:**
  0. verify-api — confirm the exact `RhinoDoc.BeginUndoRecord(string) : uint` /
     `EndUndoRecord(uint)` signatures and nesting semantics against RhinoCommon
     (decompile or docs), append findings to
     `.prawduct/artifacts/api-notes-rhinocommon.md`
  1. Acceptance criteria met and tests pass
  2. `/prawduct:critic` run (inferred mode) and blocking findings resolved
  3. Committed with tagged change-log entry (`scope=reliability`)

### Chunk 06: Rename-guidance audit — annotate via groups

- **Description:** (GHC-8V3T, user decision) Cordyceps must stop *encouraging*
  component renaming during canvas construction; the `rename` action and
  `nickname` parameter stay fully functional. (a) Audit every guidance surface
  and replace nickname-encouragement with the native convention — labeled groups
  (group_create with name/color), panels, scribbles. Known offenders from recon:
  `src/Cordyceps/Knowledge/BestPracticesGuide.md` ("Name components …
  nickname='Radius'" — the primary one), `src/Cordyceps/Knowledge/GettingStartedGuide.md`
  add-example with nickname, `src/Cordyceps/Knowledge/Prompts/CreateParametricGeometry.md`
  rename example, `src/Cordyceps/Knowledge/CommonErrorsGuide.md` find-by-nickname
  advice (reframe around find-by-type/group; find still works on default
  nicknames), plus a sweep of ActionInfo tips/examples in
  `src/Cordyceps/Tools/Unified/GhCanvasTool.cs` and the other tool classes, and
  `McpServer.cs` `GetServerInstructions()` (add a line: annotate with labeled
  groups, don't rename components). Panel nicknames stay (assumption 5).
  (b) Verify no code path auto-applies a nickname unprompted (check `add` and
  `gh_script` configure paths); remove any found. (c) Add an explicit
  discouragement note to the `rename` (and `add` nickname-param) ActionInfo:
  renaming makes components hard to find; groups are the preferred annotation.
  (d) No new capability — group actions already cover the convention.
- **Depends on:** none (ordered last as the doc-heavy tail; also the PR gate)
- **Artifacts consumed:** backlog item GHC-8V3T; recon surface list above
- **Deliverables:** Knowledge guides listed above; `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`
  ActionInfo text; `src/Cordyceps/McpServer.cs` instructions;
  `src/Cordyceps/Prompts/PromptRegistry.cs` if any workflow template encourages
  renaming; `CHANGELOG.md`
- **Tests:** none new unless (b) finds an auto-nickname code path — then fix +
  regression-test per the bugfix discipline.
- **Acceptance criteria:** grep for `nickname`/`rename` across
  `src/Cordyceps/Knowledge/`, `src/Cordyceps/Prompts/`, `McpServer.cs`, and
  ActionInfo blocks shows no remaining guidance that *encourages* renaming
  functional components; capability documentation (action exists, params) intact;
  suite green; full CLAUDE.md doc-audit table walked.
- **Type:** cumulative-final
- **Done when:**
  1. Acceptance criteria met and tests pass
  2. Committed with tagged change-log entry (`scope=reliability`)
  3. `/prawduct:critic cumulative` run against `merge-base develop...HEAD` (this
     chunk's review AND the PR gate) and blocking findings resolved
  4. Backlog hygiene pass: promoted items updated via `/prawduct:backlog`
     (merged work → `status=merged` change-log tags; flip to shipped at release)

## Early Feedback Milestone

**Milestone chunk:** Chunk 03
**What the user can do:** delete or re-port the Cordyceps component mid-request
without Rhino freezing — the first user-feelable change; Chunks 04–06 each add
further user-visible behavior (bounded snapshots, whole-action Ctrl-Z, cleaner
canvases without renamed components).

## Governance Checkpoints

**Commit & PR cadence:** Commit per chunk after its Critic passes; single PR
(`feat/reliability-follow-through → develop`) after Chunk 06's one
`/prawduct:critic cumulative` (its review and the PR gate) passes. PR via
`/prawduct:pr` only.

- After Chunk 03: trajectory review — the MCP lifecycle trio is done; confirm the
  ServerState + async-teardown topology is coherent before moving to the
  independent chunks (04–06).
- After Chunk 06: cumulative Critic (structural, `Type: cumulative-final`).
