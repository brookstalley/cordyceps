---
schema: 1
type: feature
scope: solve-policy
date: 2026-11-11
title: One solve policy: every Cordyceps solve is scheduled, bounded and honest about aborts
---
**Why:** `gh_document` `recompute`, `solver` (enable), `clear` and `revert` each ran
`GH_Document.NewSolution` synchronously inside `ExecuteOnUiThread`. That held the document lock
for the whole solve, so every other request queued for up to the lock's 120 s timeout, and it kept
the UI-work bracket open, which tells the liveness layer "Cordyceps is busy" when a
solver-exception dialog is in fact waiting for a human. `recompute` answered `recomputed: true`
whatever happened: a solve the human stopped with Escape, or one still running, read as a result.
Separately, the decompiled SDK shows `NewSolution` overwrites `SolutionState = Aborted` with
`PostProcess` before raising `SolutionEnd`, so the shipped "last solution was aborted" flag in
`outline` and `tree` could never be true (#95). The owner ruled on 2026-11-11 that this ships with
the transaction wave; it is that wave's first plan, and batch (#96), sweep (#97) and the edit
transaction (#91) will start their solves through the coordinator built here.

**What changed:** `SolverState` keeps a per-document last-solve record (a generation that advances
on every `SolutionEnd`, end time, span, aborted), fed by `SolutionWatcher` from
`GH_Document.AbortRequested` and the event's span. `outline` and `tree` read the aborted flag from
it. New host-free `Core/SolveWait` waits for a ticket (document id + generation at scheduling)
within a budget, on the caller's thread and without a lock: done is "the generation passed the
ticket and the document is not solving". New host-free `Core/SolvePolicy` maps the two solver
switches and the wait outcome to `solve.state` (`completed`, `aborted`, `over_budget`,
`solver_disabled`, `solutions_locked`, `document_closed`, or `scheduled`), its remedy, the reply
shape and the strict `timeout` input. New `Core/SolveCoordinator` is the only place Cordyceps
starts a solve: inside a short UI call it refuses or reports an off switch, expires (main canvas:
`ExpireSolution()` plus `ClearReferenceTable()`, the expire-all `NewSolution(true)` performs;
cluster editor: the source-object expiry that used to be `ToolHelpers.ClusterSafeRecompute`),
takes the ticket and calls `ScheduleSolution(10)`. `recompute` gains `timeout` (1-300, default 30),
schedules, waits off-thread, then counts error and warning objects in a second short UI call.
`solver` (enable), `clear` and `revert` schedule and reply with `solve: {state: "scheduled"}`.
`ToolHelpers.ClusterSafeRecompute` and the dead `GrasshopperContext.RecomputeSolution` /
`ScheduleRecompute` are gone; no production `NewSolution` call remains outside the bridge
component's own refresh.

**Decisions:** recorded one line each in the plan's "Decisions made mid-build". The ones a reader
of the replies meets: `tree` keeps "solver appears only when off" and names an abort in the
`note` of each owner the solve did not reach; a closed document is its own state
(`document_closed`) rather than a fake over-budget; every state but `completed` is
`success: false`; over-budget points at `gh_inspect(action='connection')` (it answers while the
solve holds the UI thread) and then `status`; `elapsedMs` is the time Cordyceps waited, schedule
delay included; `errors`/`warnings` count objects at that level, matching `status`'s summary. A
document put on the canvas without passing through the document server (snapshot revert) is
watched explicitly, since its solves would otherwise never be recorded.

**Verification:** host-free decisions unit-tested (`SolvePolicyTests`, `SolveWaitTests` with
background waiters against a solve that does not end in the window, the record tests in
`SolverStateTests`, the abort note in `TreeSummaryTests`); Release build clean. The SDK facts were
re-read in the 8.0.23304.9001 assembly the project builds against. The live behaviour (Escape,
over budget with the probe live, Lock Solver, cluster editor, revert) is enqueued as VRF-023.

## Consumer note
`gh_document(action='recompute')` now waits for the solve (new optional `timeout`, 1-300 s, default 30; out of range or non-numeric is refused) and returns `solve: {state, elapsedMs, errors, warnings, remedy}`; `success` and `recomputed` are true only when `solve.state` is `completed`, and `aborted`, `over_budget`, `solver_disabled`, `solutions_locked` and `document_closed` are `success: false` with a remedy (previously every run reported `recomputed: true`). `solver` with `enabled=true`, `clear` and `revert` no longer solve before replying: they return at once with a new `solve` field (`scheduled`, or `solver_disabled`/`solutions_locked` with a remedy), so read results after the solve rather than straight from their reply. `outline`'s `the last solution was aborted` can now actually appear, and `tree` notes an aborted solve on owners it did not reach.
