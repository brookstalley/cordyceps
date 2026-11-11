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
- **Decisions made mid-build:** *(one line each, naming what settled it)*
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
  - rollback undoes in reverse, continues past a failed action, and returns the failures.

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
  - `.prawduct/changes/edit-transaction.md`.
- **Deliverables:** the files named above.
- **Type:** cumulative-final
- **Done when:** committed, then `/prawduct:critic cumulative` run and blocking findings resolved.
- **Acceptance criteria:** #91's "After an agent edit, Ctrl+Z in Grasshopper reverts it" is met
  for every action in the inventory. A grep for document mutations in the tool classes finds
  none outside a transaction (evidence in the record). The suite is green.
