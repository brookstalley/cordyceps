---
schema: 1
type: feature
scope: edit-transaction
date: 2026-11-11
title: Every Cordyceps edit is one Grasshopper undo step, and a failed call rolls back
closes: [brookstalley/cordyceps#91]
---
**Why:** a human working beside an agent had no way to take back the agent's last change: Cordyceps
recorded nothing in Grasshopper's undo history, and agent-callable `gh_document undo`/`redo` were
cut on 2026-07-02 because performing undo from an HTTP call broke the response. A call that threw
partway left the canvas half edited (a value list emptied, a `set_count` with some of its ports, an
object added and then abandoned). Recording only some edits would be worse than none: Ctrl+Z
replays a record whose preconditions an unrecorded edit has broken, and Grasshopper then raises a
modal "undo failed" assert, leaves the record half undone and drops it (Hyperceps review). The owner
ruled on 2026-11-09 that undo returns as the edit-transaction mechanism and on 2026-11-11 that
agent-callable undo/redo stay off; batch (#96) and sweep (#97) build on this layer (#91).

**What changed:** one edit layer, `Core/Edits/`. Every tool call that changes a Grasshopper
document's canvas content (objects, wires, values; not the `solver` switch or `revert`) runs its body through `GrasshopperContext.Edit(doc, "Cordyceps: <tool> <action>", tx => …)`
on the UI thread. The body records each change before making it through Grasshopper's own undo
actions (`tx.RecordWires`, `RecordGeneric`, `RecordNickName`, `RecordPivot`, `RecordPreview`,
`RecordEnabled`, `RecordDataModifiers`; `tx.AddObject`/`tx.RemoveObject`
record adds and removals, wires first). The document's solver is paused for the body. On return the
transaction pushes one `GH_UndoRecord` and hands the changed objects to `SolveCoordinator.Schedule`;
on a throw it undoes every recorded action newest first, continuing past a failed one, restores the
solver and `IsModified`, and reports what it could not undo. The ordering rules live host-free in
`EditJournal`, the routing (nested edits, refused versus unexpected rollback, commit failures
before and after the push) in `EditScope`, and the reply shapes in `EditReplies`, all linked into
the tests; `EditTransaction` and `UndoEvents` are the Grasshopper side, adapted from Hyperceps
(MIT, attribution in the file header). Migrated: `gh_wire` `connect`/`disconnect`/`clear`;
`gh_canvas` `add`, `constant`, `delete`, `move`, `rename`, `set`, `config`, `preview`, `enable`,
`modifier`, `zoomable` and the seven group actions; `gh_script` `set`/`configure`; `gh_document
clear`. Not recorded, by nature: `solver`, `revert`, `bake` (Rhino undo), `zoom`, `view`, captures.
Replies gain `undo` (the record name, absent when nothing changed), `solve` only when a solver
switch refused the solve, and `warning` when a step after the push failed; a rollback answers
`success:false`, `error`, `rolledBack` and, when any step failed, `rollbackFailures`. Along the way:
`delete` removes without solving and schedules one solve; value-list `set`/`config` stopped going
through `SelectItem` (its own undo record and solve) and reads of a value list stopped selecting
item 0; the toggle `set` parse and value-list `items` became strict; slider plans refuse what a
slider cannot hold; `set_count`, the script source write and the port sync roll back instead of
partly succeeding; `configure` refuses `inputs`/`outputs` on a fixed-parameter component up front.

**Decisions:** recorded one line each in the plan's "Decisions made mid-build". The ones a reader of
the replies meets: per-id actions keep per-id results (refused ids are never touched, the rest are
one step, an unexpected throw rolls back the whole call); an edit reached inside an open edit of the
same document joins it; a commit failure before the push rolls back, one after it is a `warning` on
a success; a refused add records nothing; generic records (values, groups, ports, scripts) take the
object's wires first so undo rebuilds ports before reconnecting them; a failed generic restore is a
Grasshopper modal dialog the rollback cannot see; deleting a Relay does not re-wire through it.

**Verification:** host-free rules unit-tested (`EditJournalTests`, `EditScopeTests`,
`EditRepliesTests` with a fake undo sink; `ValueListItemsTests`, `SliderConfigTests`,
`SolvePolicyTests`); Release build clean. The SDK facts were re-read in the 8.0.23304.9001 assembly.
The live behaviour of each family (archive comparison before, after a forced rollback, after Ctrl+Z
and Redo; the solver pause; a cluster editor; a human edit in between; a third-party object) is
enqueued as VRF-024. There is no Windows run.

## Consumer note
Every `gh_canvas`, `gh_wire`, `gh_script` and `gh_document clear` call that changes the canvas is now one Grasshopper undo step a human can Ctrl+Z, and its reply gains `undo: "Cordyceps: <tool> <action>"` (absent when nothing changed). A call that fails partway answers `success: false`, `error`, `rolledBack` (and `rollbackFailures` when a step failed) with the canvas unchanged; a success may carry `warning` (the edit was made, a later step failed) or `solve` (a solver switch refused the solve; informational). `gh_document undo`/`redo` stay unavailable to agents. breaking: `delete` and `group_delete` schedule one solve and return before it runs (`delete` solved once per object). breaking: `set` on a toggle accepts only true/false, 1/0, yes/no; anything else is an error (it read every other word as false). breaking: value-list `config` requires `items`, a non-empty array of `{name, value}` objects with no other keys, refused whole on one bad item; the selection is kept by name instead of resetting to item 0. breaking: slider `add`/`config` refuse `decimals` outside 0-8, a non-finite or out-of-range `min`/`max`/`value`, and min > max. breaking: `zoomable set_count` that stops partway rolls back instead of answering with a partial reshape. breaking: a failed `gh_script` source write or port sync rolls the whole call back; `configure`'s `codeSet: false`/`sourceError` for a failed write are gone, and `set`'s failure reply drops `componentType`. breaking: `configure` with `inputs`/`outputs` on a component that cannot vary its parameters refuses the whole call, code included (`codeSet: false` when code was passed). `gh_script` `set`/`configure` that send what the component already has (same source, ports already matching, no `type`) write nothing and return no `undo`, but code that was given is still rebuilt (`rebuilt: true`), so re-sending the same code still forces a recompile; a sides-only `configure` that matches rebuilds nothing (`rebuilt: false` with `rebuildSkipped`). `group_add` of ids already in the group with no new colour returns no `undo`. `configure` with code now reports `lostConnections`. `gh_inspect(action='connection')`'s hint says a dialog may be open once a Cordyceps call has held a silent UI thread for over a minute.
