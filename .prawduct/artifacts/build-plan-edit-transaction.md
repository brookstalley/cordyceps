---
artifact: build-plan
version: 2
scope: edit-transaction
branch: feature/edit-transaction
base: develop
working-branches: []
last_validated: 2026-11-11
depends_on: [.prawduct/artifacts/build-plan-solve-policy.md]
governed_by: [.prawduct/artifacts/project-preferences.md, .prawduct/artifacts/boundary-patterns.md]
partition: 01 serial first (the keystone every other chunk builds on); then 02, 03, 04 and 05 delegated in parallel to four isolated worktrees, because each owns disjoint files (02 `GhCanvasTool.Values.cs` + `GhCanvasTool.Modifiers.cs`; 03 `GhCanvasTool.cs` + `GhDocumentTool.cs`; 04 `GhCanvasTool.Groups.cs`; 05 `GhCanvasTool.Zoomable.cs` + `GhScriptTool.cs`); 06 (documentation) is the coordinator's, who alone edits `McpServer.cs`, `CHANGELOG.md`, the Knowledge guides and every `ActionInfo` block
---

# Build Plan — every Cordyceps edit is one Grasshopper undo step, and a failed call rolls back

Branch: `feature/edit-transaction`, off `feature/solve-policy` (it uses `SolveCoordinator`); it
merges to `develop` after solve-policy. One PR.
Critic mode: inferred (six chunks: `chunk` reviews for 02–05, Chunk 01 overridden to `final`, and
Chunk 06 is `cumulative-final`).

## Goals

- **Response taken:** proceed, citing #91 and the owner's rulings: 2026-11-09 (undo comes back as
  the edit-transaction mechanism) and 2026-11-11 (`.prawduct/owner-rulings.md`, transaction wave).
  There is no Windows run and it does not block the release, but confidence is raised as far as
  possible without one. Agent-callable `gh_document undo`/`redo` stay off. #91 delivers human
  Ctrl+Z only.
- **Product:** a human working beside an agent presses Ctrl+Z in Grasshopper and gets the canvas
  back as it was before the agent's last call: one call, one undo step. Ctrl+Shift+Z (Ctrl+Y on
  Windows) restores it. A call that throws partway leaves the canvas as it was, not half-edited.
- **Architecture:** one edit layer. Every Cordyceps call that mutates a Grasshopper document runs
  inside one `EditTransaction`. The transaction records Grasshopper's own undo action before each
  mutation, then either commits (pushes one `GH_UndoRecord` and hands the expired objects to
  `SolveCoordinator.Schedule`) or rolls back. Batch (#96) and sweep (#97) build on it. Why every
  call, not only batch: once some edits are recorded and others are not, Ctrl+Z replays a record
  whose preconditions an unrecorded edit has broken. Grasshopper then raises a modal "undo failed"
  assert, leaves the record half undone, and drops it from both stacks (Hyperceps review,
  batch-execution §1.4). The branch migrates every action before it merges, so no release ships
  with the mixed state.
- **Evidence for confidence without Windows:**
  1. The macOS spike of 2026-11-09 (#91 comment): about 300 Hyperceps batch calls, 13 forced
     rollbacks, every response complete, no dialogs. Cmd+Z reverted a whole batch in one step and
     Cmd+Shift+Z restored it with the same ids.
  2. Hyperceps' README says it was tested on Windows. The same record, commit and rollback
     mechanism has therefore run on both platforms, though not as Cordyceps code.
  3. Recording undo is managed Grasshopper object work (`GH_UndoUtil`, `GH_UndoRecord`,
     `GH_UndoServer.PushUndoRecord`) with no platform UI. The 2026-07-02 cut was about performing
     undo from an HTTP call, which stays off.
  4. VRF-024 compares a full `GH_Archive` serialization before the call, after a forced rollback,
     and after Ctrl+Z and Ctrl+Shift+Z, for each op family, on macOS.
- **SDK facts** (decompiled, 2026-11-11; Chunk 01's verify-api re-checks them):
  - `GH_UndoUtil` has a dedicated event for most mutations:
    - `CreateWireEvent`, `CreateAddObjectEvent`, `CreateRemoveObjectEvent`
    - `CreateNickNameEvent`, `CreatePivotEvent`, `CreateLayoutEvent`
    - `CreatePreviewEvent`, `CreateEnabledEvent`
    - `CreatePersistentDataEvent<T>`, which is generic, so a `T` known only at run time needs
      reflection
    - `CreateGenericObjectEvent`, which archives the object whole
  - `GH_DataModificationAction(param)` records a param's mapping, reverse and simplify.
  - `GH_UndoRecord.Undo` runs its actions in reverse and stops at the first throw. Each
    `IGH_UndoAction.Undo(doc)` is public, so rollback can carry on past a failed action.
  - `PushUndoRecord` clears the redo stack, as every Grasshopper edit does.
- **Inferences** (made on the owner's behalf; the boundary review shows them to the owner):
  1. **Per-id actions keep their contract.** `delete`, `move`, `preview`, `enable`,
     `connect` (bulk), the group membership actions and `gh_document clear` still report per-id
     results. Ids that fail to resolve are refused before the first mutation, as today. The ids
     that succeed commit as one undo step. An unexpected exception during apply rolls back the
     whole call and reports it. Why: the per-id contract is an established norm (learnings, core,
     "validate-then-mutate"), and all-or-nothing belongs to batch (#96), which drops `atomic:false`.
  2. **The solver is paused for the whole transaction.** `doc.Enabled = false` while applying,
     restored in `finally`. Why: `GH_ValueList.SelectItem`, `GH_Panel.SetUserText` and the
     slider's change handler start their own solve (`ExpireSolution(true)`). A solve mid-call sees
     a half-applied edit, and today it runs synchronously inside our UI work. Cost: two
     `EnabledChanged` events per call. VRF-024 checks for preview flicker.
  3. **Prefer the dedicated event; use the generic archive only where none exists.**
     - Port-count changes (zoomable, script param sync) and script source use a generic event
       on the component, plus wire events on its inputs and on its outputs' recipients.
     - Slider range and value, toggle, panel text and value-list items use a generic event.
     - Group membership and colour use a generic event on the group.
     - Generic restores re-read the whole object. Third-party objects with lossy serialization
       may come back different. That is accepted and named in the Knowledge guide.
  4. **`delete` stops solving once per object.** It uses `RemoveObject(obj, false)` after
     recording, and one scheduled solve at commit. It is behaviour-visible (faster; same result).
  5. **Not recorded, by nature:**
     - `solver` is a switch, not content, as Grasshopper's own Lock Solver is.
     - `revert` installs a new document, whose undo stack starts empty. Its help text says so.
     - `bake` writes Rhino, and is already in a Rhino undo record.
     - `zoom`, `view` and the capture actions change the view only.
  6. **The commit returns a solve request; nothing waits.** An edit schedules its solve and returns
     at once, as edits do today through the bridge component's refresh. `recompute` is the call
     that waits (solve-policy).
- **Constraints:** all document access through `ExecuteOnUiThread`. The ordering rules live in a
  host-free core linked into the tests and checked through a fake undo sink. Record before mutate;
  track before add. Validate every input before the first mutation. Catch specific exceptions,
  except at the one apply boundary, which carries a waiver. Lifted Hyperceps code (MIT; the owner
  is a copyright holder) keeps its licence attribution in a file header.
- **Norm dispositions:** `project-preferences.md`: conforms. No new tool or action. Response shapes
  are kept and gain only an `undo` field naming the record (inference: `"undo": "<record name>"`
  when one was pushed, absent when nothing changed). `boundary-patterns.md`: the MCP action
  contract changes additively, plus the `delete` solve change (Inference 4), named in the
  changelog. The embedded docs contract is audited in Chunk 06. The host API is foreign, so
  Chunk 01 runs verify-api.
- **Release:** ships in the next major, with solve-policy (owner, 2026-11-11).
- **Not in scope:** agent-callable undo/redo (owner); batch (#96); sweep (#97); Rhino-side undo
  (#60); a fallback that restores a pre-apply archive when rollback cannot finish (reported
  instead; revisit if VRF-024 finds a family whose rollback fails).
- **Edit commits and the solve:** `SolveCoordinator.Schedule(doc, expireAll: false)` returns a
  refusal state when a solver switch is off. An edit reports that as an informational `solve`
  field and never fails because of it: the edit happened, and the human may have switched the
  solver off on purpose.
- **Decisions made mid-build:** *(one line each, naming what settled it)*
  - verify-api (Grasshopper.dll 8.0.23304.9001, the assembly the project builds against, via ilspycmd): every SDK fact above holds. `GH_UndoUtil` is the nested `GH_Document.GH_UndoUtil` (internal constructor, reached through `doc.UndoUtil`, a new instance per access) with `CreateWireEvent`, `CreateAddObjectEvent`, `CreateRemoveObjectEvent`, `CreateNickNameEvent`, `CreatePivotEvent`, `CreateLayoutEvent`, `CreatePreviewEvent`, `CreateEnabledEvent`, `CreatePersistentDataEvent<T>` (`T : class, IGH_Goo`, over `GH_PersistentParam<T>`) and `CreateGenericObjectEvent`, each returning a one-action record; `IGH_UndoAction.Undo(GH_Document)` is an interface member, so public; `GH_UndoServer.PushUndoRecord(GH_UndoRecord)` clears redo, inserts at the top and raises `UndoStateChanged`.
  - verify-api difference: the `IEnumerable` overload of `CreateRemoveObjectEvent` also re-wires through a deleted `GH_Relay` (`SafeDisconnect`, as the canvas's Delete does); the single-object overload Cordyceps uses does not. Deleting a relay keeps today's behaviour (its wires drop); settled by Inference 4, which names the only delete behaviour change; flagged for Chunk 03.
  - verify-api difference: `GH_GenericObjectAction` catches its own undo failure and raises `Tracing.Assert`, a modal dialog, instead of throwing, so a rollback cannot see or report that failure. Named for the Knowledge guide; settled by the SDK.
  - verify-api difference: `PushUndoRecord` does not set `IsModified`; whether an edit marks the document modified stays what each mutation does today (`AddObject`/`RemoveObject` call `Modified()`; for a wire edit it is whatever `AddSource`'s change events do, not traced). Settled by the norm dispositions, which add only `undo`.
  - verify-api: `GH_Document.AddObject` returns false when it refuses an object and otherwise inserts, calls `Modified()`, then `AddedToDocument` and `ObjectAdded` handlers, so the add-then-throw window is real; `doc.Enabled` raises `EnabledChanged`, which nothing in Grasshopper subscribes to.
  - Entry point: `GrasshopperContext.Edit(doc, name, Func<EditTransaction, string>)`, `internal`; it marshals through `ExecuteOnUiThread`, so it runs inline inside a tool's existing UI call. The body returns its JSON reply, which is parsed inside the transaction: a reply that is not a JSON object rolls the edit back. Settled by "every tool must use it in a few lines".
  - Adds and removes are compound calls (`tx.AddObject`, `tx.RemoveObject`) rather than recorder calls, so track-before-add and wires-before-remove are structural, not remembered; `AddObject` throws `EditRollbackException` when Grasshopper refuses the add. Settled by "hard to misuse".
  - `EditRollbackException(message)` lets a body roll back on a step that reports failure without throwing, answering with its own message. Settled by Chunk 05's `set_count`, which can stop partway.
  - Recording a kind whose action expires the solution (wires, generic, enabled, data modifiers, persistent data, add) also expires the changed object's top-level owner; `tx.Expire` covers anything else. Settled by the bulk-connect stale-target bug class: forgetting the expire is the common failure.
  - A generic record records the wires of the object's inputs and of every input it feeds first, then the object, the reverse of Hyperceps' order: undo then rebuilds the ports before restoring their wires, so a port a reshape removed exists again when its wire action looks it up. Settled by `GH_WireAction.Internal_Undo` throwing when the param is missing.
  - A commit with nothing recorded but an explicit `Expire` pushes nothing and still expires and schedules; one with neither does nothing. Settled by reading "an empty record schedules nothing" as "a call that changed nothing solves nothing".
  - Removing an object added in the same transaction drops its add (the net change is nothing); records on an object removed earlier in the transaction, or on its ports, are skipped. Settled by batch (#96), which will meet both.
  - An add whose `AddObject` threw and whose body carried on is removed at commit, never committed. Settled by "track before add".
  - A rollback restores the solver first, then expires the surviving objects it touched, then schedules one solve when it undid anything or expired anything (the same shape as commit, which schedules when it pushed or expired anything). Settled by undo actions leaving their objects expired, and by a body that expires without recording and then throws.
  - An `Edit` reached from inside an open edit of the same document joins it (one call, one undo step); of another document, it throws, which rolls the outer edit back. Settled by "one call, one undo step".
  - Every decision `EditTransaction.Run` made (join or refuse a nested edit, refused versus unexpected rollback, when `undo`, `solve` and `warning` are added) lives in host-free `src/Cordyceps/Core/Edits/EditScope.cs`, tested with a fake session; the host only opens a session and does its logging and redraw. Whether a record was pushed is decided once, by `CommitOutcome.Pushed`. Settled by "new decision logic goes in a host-free Core file with tests".
  - A failure at commit has a defined result. Up to and including the push (removing an unfinished add, pushing the record), it rolls the edit back and answers as an apply failure ("recording it for undo failed"): changes left in place without their record would be the unrecorded edit that breaks Ctrl+Z. After the push, the edit stands: restoring the solver, expiring and scheduling each still run when one throws, and the reply keeps the body's `success` and `undo` and gains a `warning` naming the failed steps. The record is pushed while the solver is still paused, so a push failure rolls back paused. Settled by the plan's Product goal (never half-edited) and by the reply contract (the edit happened, so it is not a failure).
  - Every step of a rollback (each undo, restoring the solver and the modified flag, each expire, the schedule) runs even when an earlier one throws, and each failure is in the report with its `step`; a throw in the host's follow-up (logging, redraw) is added to the report, never replaces it. `rolledBack` stays true when only a step after the undo failed, since the content is restored. Settled by "rollback reports what it could not undo".
  - A refused add (`AddObject` returned false) records nothing and forgets the object: the journal's `Add` takes a `Func<bool>`. Recording the add anyway would make the rollback undo an add that never happened, which finds its object by id and so would remove the pre-existing object whose id collided. Settled by reading `GH_AddObjectAction.Internal_Undo` (`FindObject(id, topLevelOnly: true)`).
  - Record name `Cordyceps: <tool> <action>` (`EditReplies.RecordName`), shown in Grasshopper's undo list and returned as `undo`. Settled by "named for the call".
  - Rollback reply: `success:false`, `error`, `rolledBack` (true only when every change was undone), and `rollbackFailures: [{step, kind?, id?, error}]` whenever any step failed. `solve` is added on commit only when a solve was asked for and refused. Settled by the plan's `undo`/`solve` rules.
  - Edit replies are parsed with `ParseHelpers.ParseJsonKeepingText`, so a date-shaped string in a body (a nickname such as `2026-01-01`) re-serializes exactly. Settled by the repo's existing text-preserving parse and its date-reinterpretation bug class.
  - Reply shapes are host-free in a second linked file, `src/Cordyceps/Core/Edits/EditReplies.cs`, with `src/Cordyceps.Tests/EditRepliesTests.cs`. `EditKind` is public because xUnit theory parameters must be. Settled by "new decision logic goes in a host-free Core file with tests".
  - `CreateLayoutEvent` is not wrapped: no action in the inventory changes layout alone. `tx.RemoveObject` refuses an object of another document, because the record lives on the transaction's document.
  - `gh_wire connect` of a wire that already exists, or of a param to itself, records nothing and still reports success as today: `AddSource` ignores both (verify-api), so an all-existing call pushes no `undo`. Settled by "absent when nothing changed".
  - `gh_wire clear` records only inputs that had sources, so a component with nothing wired gets no record and no `undo`. Settled by "absent when nothing changed".
- **Level:** Medium. The design is proven on macOS through Hyperceps and is said to be tested on
  Windows. The per-family undo choices, lossy third-party serialization, and the pause's visible
  side effects are host behaviour that tests cannot reach. Raised by VRF-024.

## Build Chunks

### Chunk 01: the transaction core, and wires as the first family

- **Foreign API:** Grasshopper undo (`GH_UndoUtil`, `GH_UndoRecord`, `GH_UndoServer`, `IGH_UndoAction`).
- **Critic mode:** final
- **Description:** the keystone. A host-free core keyed by object id and action kind enforces the
  rules through an `IUndoSink` abstraction:
  - one record per (kind, id) per transaction;
  - an object added in the transaction gets no records of its own changes;
  - "track before add" (an object added but not yet recorded is removed by hand on rollback);
  - the expire set;
  - commit skips an empty record;
  - rollback undoes in reverse, continues past a failed step, and returns the failures;
  - rollback schedules one solve when it undid or expired anything;
  - a failure at commit before the push rolls back; one after it is a warning on a successful reply.

  The host `EditTransaction` implements the sink over `GH_UndoUtil` and does the following:
  - pauses the solver and captures `IsModified`;
  - pushes the record on commit and schedules the solve through `SolveCoordinator`;
  - on rollback, undoes action by action, restores `IsModified` and the solver flag, and reports
    what could not be undone.

  It lifts Hyperceps' `ApplyContext` recording helpers, the `CreatePersistentDataEvent<T>`
  reflection helper, and the delete op's wire-capture ordering. One entry point for every tool:
  `_context.Edit(doc, name, tx => …)` runs the body on the UI thread inside a transaction.
  Migrated here: `gh_wire connect`, `disconnect`, `clear`.
- **Deliverables:**
  - new `src/Cordyceps/Core/Edits/EditJournal.cs` (host-free core, linked into the tests)
  - new `src/Cordyceps/Core/Edits/EditTransaction.cs` (host)
  - new `src/Cordyceps/Core/Edits/UndoEvents.cs` (host: the per-kind recorders, including the
    reflection helper)
  - `src/Cordyceps/Core/GrasshopperContext.cs` (the `Edit` entry point)
  - `src/Cordyceps/Tools/Unified/GhWireTool.cs`
  - tests: new `src/Cordyceps.Tests/EditJournalTests.cs`
- **Done when:**
  0. verify-api: list `GH_UndoUtil`'s `Create*Event` methods, `IGH_UndoAction.Undo`, and
     `GH_UndoServer.PushUndoRecord` in the Grasshopper assembly the project builds against.
     Confirm the SDK facts above, and record any difference in Decisions made mid-build.
  1. Committed, then `/prawduct:critic` (final) run and blocking findings resolved.
- **Tests:** for every recorder, the record is taken before the mutation; dedup per (kind, id); no
  record for an object added in the same transaction; track-before-add; rollback order, and
  continuing past a failing action with every failure reported; commit with an empty record
  pushes nothing and schedules nothing; the expire set reaches the solve request exactly once per
  object.
- **Acceptance criteria:** a `connect`, `disconnect` or `clear` call produces one undo record
  named for the call. A forced failure after the first wire leaves the wires as they were. The
  suite is green.

### Chunk 02: values and state (set, config, modifier, preview, enable)

- **Description:** Inferences 2 and 3. `set` and `config` record a generic event on the
  control: slider, toggle, panel and value list. `config` on a value list no longer leaves an
  empty list when an item fails mid-apply: it validates every item first, and the transaction
  covers any throw. `modifier` records `GH_DataModificationAction`. `preview` and `enable`
  record `CreatePreviewEvent` and `CreateEnabledEvent` per id.
- **Deliverables:** `src/Cordyceps/Tools/Unified/GhCanvasTool.Values.cs`,
  `src/Cordyceps/Tools/Unified/GhCanvasTool.Modifiers.cs`; host-free validation for value-list
  items in `src/Cordyceps/Core/`, with tests.
- **Tests:** value-list item validation (every item checked before the list is touched); any new
  decision logic is host-free and tested; host glue is code-read and covered by VRF-024.
- **Acceptance criteria:** each action produces one undo record; a value list with a bad item is
  refused unchanged.

### Chunk 03: objects (add, constant, delete, move, rename) and gh_document clear

- **Description:** Inference 4.
  - `add` and `constant` track, add, then record `CreateAddObjectEvent`. The slider's range and
    value are set after the add and need no record, because the object is new.
  - `delete` and `gh_document clear` record their wires first, then `CreateRemoveObjectEvent`
    before each `RemoveObject(obj, false)`. The wires are the deleted objects' inputs and their
    outputs' recipients. `clear` keeps its cluster-hook rule.
  - `move` records `CreatePivotEvent`.
  - `rename` records `CreateNickNameEvent`.
  - `add` no longer leaves an object on the canvas when a step after `AddObject` throws.
  - Settle first (Chunk 01 review, observation): `EditJournal.Add` can't record an add that
    Grasshopper refused. Decide what rollback does then, given an id that might collide with an
    existing object, and test it.
  - Carried from solve-policy's verify pass (observation): `GhDocumentTool.WaitsForSolve`, which
    decides which actions read `timeout`, moves into `src/Cordyceps/Core/SolvePolicy.cs` with a
    test, because this chunk edits `GhDocumentTool.cs` anyway.
- **Deliverables:** `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`,
  `src/Cordyceps/Tools/Unified/GhDocumentTool.cs` (`clear` only).
- **Tests:** any new decision logic is host-free and tested; host glue is covered by VRF-024.
- **Acceptance criteria:** each action produces one undo record. Undoing a delete restores the
  objects and every wire, upstream and downstream. `delete` of N objects schedules one solve.

### Chunk 04: groups

- **Description:** `group_create` and `group_add` (when creating) record the add.
  `group_delete` records the remove. Membership changes (`group_create`, `group_add`,
  `group_remove`) and `group_color` record a generic event on the group. `group_rename` records
  the nickname. `group_move` records `CreatePivotEvent` over the members.
- **Deliverables:** `src/Cordyceps/Tools/Unified/GhCanvasTool.Groups.cs`.
- **Acceptance criteria:** each group action produces one undo record, and undo restores
  membership, name, colour and member positions.

### Chunk 05: ports and scripts (zoomable, gh_script set and configure)

- **Description:** Inference 3. Before the first port or source change, record a generic event on
  the component, plus wire events on every input and on every recipient of every output. A port
  that a reshape removes takes its wires with it, and undo must restore both. The script rebuild
  runs after the record and is not itself recorded. Undo restores the source, and Grasshopper
  rebuilds on expiry. `set_count`, which can stop partway, now rolls back instead of leaving a
  partial reshape.
- **Deliverables:** `src/Cordyceps/Tools/Unified/GhCanvasTool.Zoomable.cs`,
  `src/Cordyceps/Tools/Unified/GhScriptTool.cs`.
- **Acceptance criteria:** each action produces one undo record. Undo restores the ports, their
  wires and the script source. A `set_count` that cannot finish leaves the component unchanged.

### Chunk 06: documentation and the live checklist

- **Description:** the whole plan's documentation audit, done once by the coordinator:
  - `ActionInfo` for every migrated action (the `undo` field; the per-id rule);
  - `revert`'s note (not undoable by Ctrl+Z);
  - `undo` and `redo`'s error text, which now says Ctrl+Z in Grasshopper undoes each Cordyceps
    call, while agent-callable undo stays off;
  - `GetServerInstructions()`;
  - `src/Cordyceps/Knowledge/CommonErrorsGuide.md` (rollback reports; lossy third-party objects);
  - `CHANGELOG.md` under `## [Unreleased]`;
  - `.prawduct/operator-verification.md` new VRF-024 (for each family on macOS: archive
    comparison before, after forced rollback, after Ctrl+Z and after Ctrl+Shift+Z; redo; the
    pause's preview flicker; a cluster editor; Ctrl+Z after an unrelated human edit);
  - `.prawduct/changes/edit-transaction.md`;
  - `CLAUDE.md`'s "Adding or modifying tools" template and rules, and `docs/architecture.md`:
    a document mutation goes through `_context.Edit`, not bare `ExecuteOnUiThread`. Without this
    a tool written from the template after the merge records no undo and reopens the
    interleaving hazard (Chunk 01 review, observation);
  - VRF-024's forced rollback uses a real partial failure, `zoomable set_count` stopping partway
    (Chunk 05 rolls it back), since nothing else can force a failure by hand.
- **Deliverables:** the files named above.
- **Type:** cumulative-final
- **Done when:** committed, then `/prawduct:critic cumulative` run and blocking findings resolved.
- **Acceptance criteria:** #91's "After an agent edit, Ctrl+Z in Grasshopper reverts it" is met
  for every action in the inventory. A grep for document mutations in the tool classes finds
  none outside a transaction (evidence in the record). The suite is green.
