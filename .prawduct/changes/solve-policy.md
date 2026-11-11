---
schema: 1
type: feature
scope: solve-policy
date: 2026-11-11
title: One solve policy: every Cordyceps solve is scheduled, bounded and honest about aborts
closes: [brookstalley/cordyceps#95]
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
on the outermost `SolutionEnd`, span, aborted), fed by `SolutionWatcher` from
`GH_Document.AbortRequested` and the event's span. `outline` and `tree` read the aborted flag from
it. New host-free `Core/SolveWait` waits for a ticket (document id + generation at scheduling)
within a budget, on the caller's thread and without a lock: done is "the generation passed the
ticket and the document is not solving". New host-free `Core/SolvePolicy` maps the two solver
switches, the watcher and the wait outcome to `solve.state` (`completed`, `aborted`,
`over_budget`, `not_started`, `solver_disabled`, `solutions_locked`, `not_watched`,
`document_closed`, or `scheduled`), its remedy, the reply
shape and the strict `timeout` input. New `Core/SolveCoordinator` is the only place Cordyceps
starts a solve: inside a short UI call it refuses or reports an off switch, expires (main canvas:
`ExpireSolution()` plus `ClearReferenceTable()`, the expire-all `NewSolution(true)` performs;
cluster editor: the source-object expiry that used to be `ToolHelpers.ClusterSafeRecompute`),
takes the ticket and calls `ScheduleSolution(10)`. `recompute`, `solver` (enable) and `revert`
gain `timeout` (1-300, default 30): each schedules, waits off-thread through one shared path, then
counts error and warning objects by `status`'s own health rule in a second short UI call, and
replies with `solve: {state, elapsedMs, solveMs, errors, warnings, remedy}`. `clear` schedules and
replies with `solve: {state: "scheduled"}`.
`ToolHelpers.ClusterSafeRecompute` and the dead `GrasshopperContext.RecomputeSolution` /
`ScheduleRecompute` are gone; no production `NewSolution` call remains outside the bridge
component's own refresh.

**Decisions:** recorded one line each in the plan's "Decisions made mid-build". The ones a reader
of the replies meets: `tree` keeps "solver appears only when off" and names an abort in the
`note` of each owner the solve did not reach; a closed document is its own state
(`document_closed`) rather than a fake over-budget; every state but `completed` is
`success: false`; over-budget points at `gh_inspect(action='connection')` (it answers while the
solve holds the UI thread) and then `status`; `elapsedMs` is the time Cordyceps waited, schedule
delay included, and `solveMs` is Grasshopper's own solve time; `errors`/`warnings` are counted by
the health rule `status` uses. The cumulative review settled that `solver` (enable) and `revert`
wait like `recompute`: they solved synchronously before this change, and the taught workflows
read results straight after them. A
document put on the canvas without passing through the document server (snapshot revert) is
watched explicitly, since its solves would otherwise never be recorded; such adopted documents
are released once they are neither being scheduled nor on the canvas, so at most two are held.
"Solving" is a depth: a solution started inside another does not end the solve, or answer a
wait, until the outer one ends. At the deadline a wait says `over_budget` only when the document
is solving; idle with no finished solve is `not_started`. A solve whose end could not be watched
is not scheduled (`not_watched`). `gh_document info` reports the global lock as `solutionsLocked`.

**Verification:** host-free decisions unit-tested (`SolvePolicyTests`, `SolveWaitTests` with
background waiters against a solve that does not end in the window, the record tests in
`SolverStateTests` including nested solves, the adoption bound in `WatchAdoptionTests`, the abort
note in `TreeSummaryTests`); Release build clean. The SDK facts were
re-read in the 8.0.23304.9001 assembly the project builds against. The live behaviour (Escape,
over budget with the probe live, Lock Solver, cluster editor, revert) is enqueued as VRF-023.

## Consumer note
breaking: `gh_document(action='recompute')` answers `success: true` only when its solve completed; `aborted`, `over_budget`, `not_started`, `solver_disabled`, `solutions_locked`, `not_watched` and `document_closed` are `success: false` with `solve.remedy` (previously every run reported `success: true, recomputed: true`, including a disabled solver and a solve of any length). It waits at most `timeout` seconds (new, 1-300, default 30; a whole-valued number is accepted, anything else refused) and returns `solve: {state, elapsedMs, solveMs, errors, warnings, remedy}`. breaking: `solver` with `enabled=true` and `revert` take the same `timeout`, reply with the same `solve` object, and are `success: false` when the solve did not complete (a solve longer than the timeout is `over_budget` where it used to block until done); the switch or revert has happened either way. breaking: `clear` returns before its solve runs (`solve: {state: "scheduled"}`); call `recompute` before reading results after it. `gh_document info` gains `solutionsLocked`. `outline`'s `the last solution was aborted` can now actually appear, and `tree` notes an aborted solve on owners it did not reach.
