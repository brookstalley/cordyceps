---
artifact: build-plan
version: 2
scope: solve-policy
branch: feature/solve-policy
base: develop
working-branches: []
last_validated: 2026-11-11
depends_on: []
governed_by: [.prawduct/artifacts/project-preferences.md, .prawduct/artifacts/boundary-patterns.md]
partition: serial, delegated — one Opus delegate in an isolated worktree builds 01 then 02, because 02's wait reads the per-document solve record 01 adds to `SolverState`; the coordinator owns integration, the full suite, the review and the records
---

# Build Plan — one solve policy: every Cordyceps solve is scheduled, bounded, and honest about aborts

Branch: `feature/solve-policy` (off `develop` at 89c3f2c). One PR.
Critic mode: one `cumulative` review at the end (a two-chunk plan; Chunk 02 is `cumulative-final`).

## Goals

- **Response taken:** proceed, citing #95 and the owner's ruling of 2026-11-11 (release after the
  transaction wave, `.prawduct/owner-rulings.md`). This is the first plan of that wave. The edit
  transaction (#91) is the next plan, and its commit hands its solve to the coordinator built
  here.
- **Product:** an agent that asks Cordyceps to solve gets an answer that is true. A solve that
  finished reports `completed`. A solve the human stopped with Escape reports `aborted`, not a
  result. A solve still running at the deadline reports `over_budget`. A disabled solver reports
  which switch is off and who can turn it back on. No Cordyceps call holds the document lock or the
  UI-work bracket while Grasshopper solves.
- **Architecture:** one `SolveCoordinator` owns every solve Cordyceps starts. Batch (#96), sweep
  (#97) and the edit transaction (#91) use it rather than calling `NewSolution` themselves.
- **Facts this plan rests on** (read in the decompiled Grasshopper SDK, 2026-11-11; Chunk 02's
  verify-api step re-checks them against the shipped assembly):
  1. `GH_Document.NewSolution` sets `SolutionState = Aborted` inside `SolveAllObjects`, then
     overwrites it with `PostProcess` before raising `SolutionEnd`. So `SolutionState == Aborted`
     is never true after a solve returns. `AbortRequested` (set from `IsEscapeKeyDown()` and
     during the solve) survives until the next solution starts. **Consequence:** the shipped
     `LastSolutionAborted` flag (`src/Cordyceps/Core/Canvas/CanvasReader.cs`, and the `tree`
     solver-off text in `src/Cordyceps/Tools/Unified/GhInspectTool.Tree.cs`) can never be true.
     That is a defect, and Chunk 01 fixes it.
  2. When the global `GH_Document.EnableSolutions` is off (the canvas's "Lock solver"),
     `NewSolution` returns before raising `SolutionStart`/`SolutionEnd`. A wait for completion
     would never end, so the coordinator reports it up front. When only `doc.Enabled` is off, the
     events fire and nothing is solved.
  3. `ScheduleSolution(delay)` arms a timer whose tick marshals through
     `Instances.DocumentEditor.BeginInvoke` and runs `NewSolution(false)` on the UI thread, unless
     a solve is already in process. A scheduled solve is erased by any solution that starts first,
     and that solution solves whatever we expired.
  4. Public `GH_Document.ExpireSolution()` expires every object. It expires cluster hooks too, so
     the cluster path keeps `ClusterSafeRecompute`'s source-object expiry.
- **Inferences** (made on the owner's behalf; all confirmed by the owner on 2026-11-11, `.prawduct/owner-rulings.md`):
  1. **Schedule, never solve synchronously.** The coordinator expires (cluster-safe) and calls
     `ScheduleSolution` inside a short `ExecuteOnUiThread`, then returns. Any waiting happens off
     the UI thread, outside the document lock. Why: a synchronous `NewSolution` inside
     `InvokeAndWait` holds the lock for the whole solve, blocking every other request for up to
     its 120 s timeout. It also makes a solver-exception modal read as "Cordyceps is working"
     rather than "fetch a human", because `UiWorkInProgress` suppresses modal inference (Hyperceps
     review, batch-execution §3).
  2. **`recompute`, `solver` (enable) and `revert` wait; `clear` schedules.** *(Amended after
     the cumulative review: as first written only `recompute` waited, and `solver` and `revert`
     scheduled and returned.)* Each waiting action waits up to `timeout` seconds (default 30,
     range 1–300, out of range refused per strict inputs) through one shared path and reports
     `solve: { state, elapsedMs, solveMs, errors, warnings, remedy }` with state
     `completed | aborted | over_budget | not_started | solver_disabled | solutions_locked |
     not_watched | document_closed`. `over_budget` means the solve is still running. The reply
     says Cordyceps cannot cancel it and points to `gh_inspect(action='connection')`, then
     `status`. `success` (and `recompute`'s `recomputed`) is true only when `completed`.
     `solver` (enable) and `revert` used to solve before replying, so callers that read results
     afterwards keep working. `clear` schedules through the coordinator and reports
     `solve: { state: "scheduled" }` without waiting. Single-step edits keep today's path (expire,
     then the bridge component's scheduled refresh); the edit-transaction plan moves them onto the
     coordinator.
  3. **Abort truth lives in `SolverState`.** `SolutionWatcher` records, at `SolutionEnd`, a
     per-document last-solve record: a generation counter, span, and `AbortRequested`.
     `outline` and `tree` read the aborted flag from that record. The wait detects completion as
     "the generation advanced past the ticket and the document is not solving".
  4. **No cancel action.** `GH_Document.RequestAbortSolution()` exists, but an agent cancelling a
     human's solve is a new capability nobody asked for. Not built here.
- **Constraints:** all document access through `ExecuteOnUiThread`. New decision logic goes in
  host-free `Core/` files linked into the tests, with tests in the same chunk. A test named for a
  timing contract drives real concurrency (learnings, tests). Validate before mutate.
- **Norm dispositions:** `project-preferences.md`: conforms (few tools, many actions: no new tool;
  strict inputs: `timeout` validated and refused when out of range). `boundary-patterns.md`: the
  MCP action contract changes in a breaking way for `recompute`, `solver`, `clear` and `revert`
  (`recompute` answers `success: false` unless its solve completed, including for a disabled
  solver and a solve longer than the wait, which used to succeed; `solver` and `revert` gain
  `solve` and can be `success: false`; `clear` returns before its solve), allowed because the
  owner ruled the release a major and labelled `Breaking:` in `CHANGELOG.md`; the embedded docs
  contract is audited in Chunk 02 and again after the review; the host API is foreign and gets a
  verify-api step.
- **Release:** breaking (see the norm dispositions); ships in the next major with the transaction wave (owner, 2026-11-11).
- **Not in scope:** the edit transaction and undo (#91); batch (#96); sweep (#97); cancelling a
  solve; the bridge component's own refresh scheduling (`src/Cordyceps/CordycepsComponent.cs`).
- **Decisions made mid-build:** *(one line each, naming what settled it)*
  - Every `EndSolution` advances the generation, even with no recorded start: Grasshopper raised the end, so a solve finished, and a waiter must not miss it (01).
  - `SolverState.TakeTicket` starts tracking the document (generation 0), so a missing record means "forgotten" (`Gone`), never "not solved yet"; a ticket taken while that document is solving asks for one generation more, because the running solve began before the expiry (01).
  - `tree` keeps its documented contract that `solver` appears only when the solver is off (`DataTreesGuide.md`, a `TreeSummaryTests` assertion); with the solver on, an aborted last solve shows in the `note` of each owner that did not compute ("not computed: the last solution was aborted before it reached this object"). Settled by not weakening a test-pinned contract while still making tree say so (01).
  - A disabled document's `SolutionEnd` carries the previous solve's span (the SDK skips the profiler when `Enabled` is off), so the watcher records no span for it (verify-api reading of `NewSolution`, 01).
  - `SolveWait` measures its budget on a stopwatch-backed clock by default; a wall clock stepped backwards would stretch the wait past its budget (01).
  - verify-api (02), against `~/.nuget/packages/grasshopper/8.0.23304.9001/lib/net48/Grasshopper.dll` decompiled with `ilspycmd`: Facts 1-3 confirmed as written. Additions: `EnableSolutions` reads `m_enableSolutions & RhinoApp.CanSave`, so "locked" also holds transiently while Rhino cannot save (mid-command), and the remedy says so; outside a running solution `ScheduleSolution(0)` never starts the timer (`StartSchedule` needs a delay > 0), so the coordinator uses 10 ms; the timer tick is dropped silently when `Instances.DocumentEditor` is null, disposed or has no handle.
  - verify-api correction to Fact 4 (02): `NewSolution(true)`'s expire-all is `ExpireSolution()` plus `ClearReferenceTable()`; public `ExpireSolution()` alone leaves the Rhino reference table, so the coordinator calls both on a main canvas. `ExpireSolution()` is a no-op while the document is in `Process`.
  - verify-api (02): a cluster opened with "Edit Cluster" is a duplicate document added to the document server (`GH_Cluster.EditClusterAsSeparateDocument`), so the watcher sees its solves; snapshot `revert` assigns `GH_Canvas.Document` directly, which never adds to the server, so `SolutionWatcher.EnsureWatched` watches the scheduled document explicitly.
  - A document closed mid-wait is reported as its own state, `document_closed` (not in the plan's list), rather than mislabelled `over_budget`; settled by the wait's `Gone` outcome having no honest home among the planned states (02).
  - Every `recompute` state but `completed` is `success: false` with `solve.remedy`; `error` is a one-line pointer to it. Settled by the plan's rule that `recomputed` is true only for `completed` and the codebase convention that a call that did not do what it says is `success: false` (02).
  - Over budget points at `gh_inspect(action='connection')` first, then `status`: the plan named `status`, but `status` refuses while the UI thread is blocked by the very solve being waited on, and `connection` is the probe that answers then (02).
  - `elapsedMs` is how long Cordyceps waited (schedule delay included), not Grasshopper's solve span; `errors`/`warnings` count objects at that runtime-message level over the population `status` reads, so the numbers match its summary (02). *(Superseded by the review-answer lines below.)*
  - Per-action expiry keeps today's scope: `recompute` and `revert` expire everything; `solver enabled=true` expires everything on a main canvas and only already-expired objects in a cluster editor (as before); `clear` expires nothing more (02).
  - Counting messages runs only for `completed` and `aborted`: during an over-budget solve that UI call would queue behind the solve, the wait the budget exists to bound (02).
  - "Solving" is a per-document depth: a start inside an open solve deepens it (start time kept), an end only shallows it until the end that reaches zero, which alone clears solving and advances the generation with that outermost solve's span and aborted flag. The "end without a seen start advances the generation" rule stays: with depth, an end at zero can only be a solve whose start went unseen (the watcher attaches from inside the bridge component's own solve), since Grasshopper raises one start and one end per solution (02, fix round).
  - At the deadline a wait reports `over_budget` only when the document is solving; idle with the generation not past the ticket is a new state `not_started` (the schedule never ran: lock switched on meanwhile, editor unavailable, or no solution events), `success: false` with a remedy to check `connection`/`status` and retry (02, fix round).
  - A solve whose end cannot be watched is not scheduled: `SolvePolicy.Refusal` takes a `watching` callback, asked only after both switches allow a solve (so a refused solve adopts no document), and failure is the state `not_watched` with a remedy pointing at the Cordyceps component. Every action reports it, since scheduling an unobservable solve is what the state exists to prevent (02, fix round).
  - Adopted documents (watched by `EnsureWatched` but unknown to the document server) are bounded by host-free `WatchAdoption`: each `EnsureWatched` releases every adopted document that is neither the one requested nor the active canvas document, so at most two are held and repeated reverts hold one; a document later added to the server stops being adopted. Releasing unwatches; it forgets the solve state only when no other watched document carries the same id, because state is keyed by id and a reverted document may share its id with the one it replaced (02, fix round).
  - `gh_document info` reports `solutionsLocked` (`!GH_Document.EnableSolutions`) beside `solverEnabled` (02, fix round).
  - `SolveCoordinator.Wait` keeps throwing on the UI thread rather than returning a state: tool bodies run on thread-pool threads (`McpServer.cs:310` `Task.Run(HandleRequestAsync)`, `:770` `tool.Method.Invoke`), and no code calls `GhDocument` directly, so the throw is a programming-error guard, not a reachable outcome (02, fix round).
  - `solver` (enable) and `revert` wait for their solve exactly as `recompute` does (same `timeout`, `solve` object and states, one shared wait path in `GhDocumentTool.AwaitSolve`); `clear` stays schedule-only. Settled by the cumulative review's R-4/R-7: the taught workflows (and any caller) read results straight after `solver enabled=true`, which solved synchronously before this plan, and rewording them could not help while the only waiting call re-solves and is refused during the scheduled solve (02, review round).
  - The breaking changes are labelled `Breaking:` in `CHANGELOG.md` (recompute's `success: false` for every non-completed state; `solver`/`revert` gaining `solve` and failing on a non-completed solve; `clear` returning before its solve), and the norm-disposition line no longer calls the contract change additive. Settled by the review's R-1/R-8 (02, review round).
  - `solve.solveMs` reports Grasshopper's own solve time from the record's span when a solve ended (completed or aborted) and Grasshopper timed it; `elapsedMs` stays the wait. `SolveRecord.EndedUtc` had no reader and is removed. Settled by the review's R-2 (02, review round).
  - `errors`/`warnings` are counted through `ObjectHealth.IsSubject` and `CanvasReader.Health`, the rule `status` uses, so the documented "matches status" holds by construction. Settled by the review's R-6 (02, review round).
  - `timeout` accepts anything written as a JSON number with a whole value (`120.0`, `1.2e2`), matching the int parameters' acceptance of whole-valued doubles; fractions, `NaN`/`Infinity` and other shapes are refused as not a whole number (review observation, 02, review round).
  - `timeout` is a string parameter parsed by shape (`SolvePolicy.TryParseTimeout`), so `0`, `301`, `-5`, an Int32 overflow and non-numbers are all refused with a message that says which; it is read only for `recompute`, like the tool's other action-specific parameters (02). *(Superseded by the review-answer lines below.)*
- **Level:** Medium. The SDK facts come from decompiled source, not a live run. A scheduled solve
  depends on `Instances.DocumentEditor` existing, which holds whenever the canvas is open. The
  cluster-editor path is code-read only. Raised by VRF-023, which runs in live Rhino.

## Today's behaviour this plan changes (verified in source on develop at 89c3f2c)

- `GhDocumentTool.ActionRecompute`: refuses while solving or modal (kept), then calls
  `ToolHelpers.ClusterSafeRecompute`, which runs `NewSolution` synchronously inside
  `ExecuteOnUiThread`. Reply: `{ success, recomputed: true }`, with no outcome.
- `ActionSolver(true)`, `ActionClear` and `ActionRevert` each call `NewSolution` directly and
  synchronously.
- `GrasshopperContext.RecomputeSolution` and `ScheduleRecompute` have no callers.
- `CanvasReader` sets `LastSolutionAborted = doc.SolutionState == GH_ProcessStep.Aborted`, which
  Fact 1 says is never true after a solve.

## Build Chunks

### Chunk 01: a true per-document solve record (generation, span, aborted) and a bounded wait

- **Description:** Inference 3. `SolverState` keeps, per document, the last completed solve:
  generation (increments on each `EndSolution`), end time, span, and aborted. `SolutionWatcher`'s
  `SolutionEnd` handler passes `document.AbortRequested` and the event's span. A host-free
  `SolveWait` waits for a ticket (document id plus the generation at scheduling) to complete within
  a budget, polling an injected probe on an injected clock. It returns `Completed(record)`,
  `OverBudget(elapsed)` or `Gone` (the document was forgotten). `CanvasReader` and the `tree`
  solver text read the aborted flag from the record, not from `SolutionState`.
- **Deliverables:**
  - `src/Cordyceps/Core/SolverState.cs` (the record; `EndSolution` takes span and aborted;
    `ForgetDocument` drops the record).
  - `src/Cordyceps/Core/SolutionWatcher.cs` (passes `AbortRequested` and the span).
  - new `src/Cordyceps/Core/SolveWait.cs`, linked into `src/Cordyceps.Tests/Cordyceps.Tests.csproj`.
  - `src/Cordyceps/Core/Canvas/CanvasReader.cs`, `src/Cordyceps/Tools/Unified/GhInspectTool.Tree.cs`.
  - tests: new `src/Cordyceps.Tests/SolveWaitTests.cs`; additions to the existing `SolverState`
    tests.
- **Tests:** the generation advances once per end; nested begin/end keeps today's start-time rule;
  the aborted flag round-trips; `ForgetDocument` clears the record and a waiter reports `Gone`.
  `SolveWait` completes when the generation passes the ticket, not before. It reports over budget
  with a solve that never ends (a background solve simulated with a never-completing signal, per
  the tests learning), and treats a generation that advanced while the document is still marked
  solving as not done.
- **Acceptance criteria:** an aborted solve is recorded as aborted, and outline and tree say so;
  the wait is bounded and tested under real concurrency; the suite is green.

### Chunk 02: SolveCoordinator; recompute waits and reports; solver, clear and revert schedule

- **Foreign API:** Grasshopper `GH_Document` solve and schedule surface.
- **Description:** Inferences 1, 2. A host-free `SolvePolicy` maps the solver flags
  (`doc.Enabled`, `GH_Document.EnableSolutions`) and the wait outcome to a reported state, a
  remedy and the reply's `solve` object. The host `SolveCoordinator.Schedule(doc)` runs on the UI
  thread: refuse-or-report the locked or disabled solver; otherwise expire (the main canvas
  through `doc.ExpireSolution()`, a cluster through the existing source-object expiry), take the
  ticket, and call `ScheduleSolution`. `recompute` validates `timeout`, keeps its busy refusal,
  schedules in one short UI call, waits off-thread through `SolveWait` (no lock held), then makes
  one short UI call to count error and warning runtime messages. Fold
  `ToolHelpers.ClusterSafeRecompute` into the coordinator. Delete the dead
  `GrasshopperContext.RecomputeSolution` and `ScheduleRecompute`.
- **Deliverables:**
  - new `src/Cordyceps/Core/SolvePolicy.cs` (host-free, linked).
  - new `src/Cordyceps/Core/SolveCoordinator.cs` (host).
  - `src/Cordyceps/Tools/Unified/GhDocumentTool.cs` (`recompute` gains `timeout`; `solver`,
    `clear` and `revert` go through the coordinator), `src/Cordyceps/Core/ToolHelpers.cs`,
    `src/Cordyceps/Core/GrasshopperContext.cs`.
  - tests: new `src/Cordyceps.Tests/SolvePolicyTests.cs`; strict-input tests for `timeout`.
- **Done when:**
  0. verify-api: re-read the facts in Goals against the Grasshopper assembly the project builds
     with (decompile or reflect `GH_Document.NewSolution`, `ScheduleSolution`,
     `ScheduleTimerTick`, `ExpireSolution()`, `AbortRequested`). Record any difference in
     Decisions made mid-build before writing the coordinator.
  1. Committed, then `/prawduct:critic cumulative` run and blocking findings resolved.
- **Tests:** the policy table: each flag combination and wait outcome gives one state, one remedy
  and one reply shape. `solutions_locked` never waits. `recomputed` is true only for
  `completed`. `timeout` 0, 301 and non-numeric are refused before any document access.
- **Documentation (whole plan, done in this chunk):** `ActionInfo` for `recompute` (the `timeout`
  param, states, over-budget guidance), `solver`, `clear` and `revert` (the `solve` field);
  `GetServerInstructions()` where it describes recompute and solver; `src/Cordyceps/Knowledge/CommonErrorsGuide.md`
  (aborted, over budget, the two solver switches and who can flip each); `CHANGELOG.md` under
  `## [Unreleased]` (Added: recompute outcome and timeout; Fixed: the last-solution-aborted flag
  was never true; Changed: solver, clear and revert schedule their solve);
  `.prawduct/operator-verification.md` new VRF-023 (live: Escape mid-solve reports aborted, a
  slow definition reports over budget and the status probe stays live, Lock solver reports
  locked, recompute inside a cluster editor); `.prawduct/changes/solve-policy.md` narrative and
  consumer note.
- **Type:** cumulative-final
- **Acceptance criteria:** #95's three boxes hold: `recompute` solves through the helper and stays
  cluster-safe; an over-budget solve is reported as over budget; an Escape-aborted solve is
  reported as aborted. No production call to `NewSolution` remains outside the coordinator except
  the bridge component's own refresh (grep evidence in the report). The suite is green.
