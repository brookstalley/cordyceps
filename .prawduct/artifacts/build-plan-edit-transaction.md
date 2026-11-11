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
partition: 01 serial first (the keystone every other chunk builds on); then 02, 03, 04 and 05 delegated in parallel to four isolated worktrees, because each owns disjoint files (02 `GhCanvasTool.Values.cs` + `GhCanvasTool.Modifiers.cs`; 03 `src/Cordyceps/Tools/Unified/GhCanvasTool.cs` + `GhDocumentTool.cs`; 04 `src/Cordyceps/Tools/Unified/GhCanvasTool.Groups.cs`; 05 `GhCanvasTool.Zoomable.cs` + `src/Cordyceps/Tools/Unified/GhScriptTool.cs`); 06 (documentation) is the coordinator's, who alone edits `McpServer.cs`, `CHANGELOG.md`, the Knowledge guides and every `ActionInfo` block
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
- **Inferences** (made on the owner's behalf; all confirmed by the owner on 2026-11-11, `.prawduct/owner-rulings.md`):
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
  change as follows, each recorded in Decisions made mid-build and labelled in `CHANGELOG.md`:
  - added to edit replies: `undo` naming the record (inference: `"undo": "<record name>"` when one
    was pushed, absent when nothing changed), `rolledBack`, `rollbackFailures`, and `warning`;
  - removed: `configure`'s `sourceError`, and `codeSet: false`'s old meaning (a failed write; it
    now means only "refused before anything changed"), and `componentType` from `gh_script set`'s
    failure reply (CHANGELOG "Breaking: a failed `gh_script` source write or port sync fails the
    whole call");
  - per-action behaviour, each labelled Breaking in the CHANGELOG: `delete` and `group_delete`
    schedule one solve (Inference 4); toggle `set` parsing; value-list `config` `items`; slider
    `add`/`config` refusals; `zoomable set_count` rolls back; a failed `gh_script` write or sync
    fails the whole call; `configure` refuses `inputs`/`outputs` on a fixed-parameter component.
    Not breaking, and not labelled so: `gh_script set`/`configure` that send what the component
    already has write nothing and push no undo step, while given code is still rebuilt as before.

  `boundary-patterns.md`: it forbids silent renames and removals; none of these is silent. Each is
  named Breaking in the changelog, and the owner ruled that this ships in the next major
  (2026-11-11, Release below). The embedded docs contract is audited in Chunk 06. The host API is
  foreign, so Chunk 01 runs verify-api.
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
  - (04) `group_rename` records `RecordGeneric`, not `RecordNickName`: the action sets both `NickName` and `Name`, and the nickname event restores only `NickName` (ilspycmd was unavailable to confirm it also covers `Name`, so the whole-archive record is the safe choice). `group_remove` records only when a listed id is actually a member, so a no-op call pushes nothing.
  - `CreateLayoutEvent` is not wrapped: no action in the inventory changes layout alone. `tx.RemoveObject` refuses an object of another document, because the record lives on the transaction's document.
  - `gh_wire connect` of a wire that already exists, or of a param to itself, records nothing and still reports success as today: `AddSource` ignores both (verify-api), so an all-existing call pushes no `undo`. Settled by "absent when nothing changed".
  - `gh_wire clear` records only inputs that had sources, so a component with nothing wired gets no record and no `undo`. Settled by "absent when nothing changed".
  - (02) `GH_ValueList.SelectItem` pushes its own undo record (`RecordUndoEvent("Select: …")`) and calls `ExpireSolution(true)` (ilspycmd, Grasshopper 8.0.23304.9001), so `set` and `config` set `Selected` on the items directly after `RecordGeneric`. The other setters used (`GH_Panel.UserText`, `GH_NumberSlider.SetSliderValue`, `Slider.Minimum`/`Maximum`/`DecimalPlaces`, `GH_BooleanToggle.Value`, `Hidden`, `Locked`) push nothing. Settled by "one call, one undo step".
  - (02) Value-list `config` keeps the previous selection by name (exact): in a single-selection list the first item named like the first selected one, in a check list every item named like a selected one; else item 0. Host-free in `src/Cordyceps/Core/ValueListItems.cs`. Settled by the brief (today's reselect of item 0 lost the selection).
  - (02) Value-list `items` are refused whole, before the list is touched, for: not JSON, not an array, an empty array, a non-object entry, a key other than `name`/`value` (a misspelt key silently became the default), a duplicate key, a `null`/object/array `name` or `value`. A missing `name`/`value` keeps the defaults `Item`/`0`. Settled by "a value list with a bad item is refused unchanged" and the silent-reinterpretation rule (learnings, core).
  - (02) `set`, `config`, `modifier`, `preview` and `enable` record nothing for a target already in the requested state, so an all-unchanged call pushes no `undo`; `preview`/`enable` still count such ids in `changedCount`, as before. Settled by "absent when nothing changed" and the per-id contract.
  - (02) `set` on a toggle parses with `TryParseBool` (true/false, 1/0, yes/no) and refuses anything else; it used to read every other word, `yes` included, as false. `set` on a slider refuses NaN and infinity, and `config` refuses a min, max or value that does not fit a decimal (`SliderConfig.Plan`'s `OverflowException`, caught before the edit). Settled by the silent-reinterpretation rule and "validate before the first mutation".
  - (02) `preview` needs `IGH_ActiveObject` as well as `IGH_PreviewObject`, because `CreatePreviewEvent` takes an active object; Grasshopper's preview objects are active. `get` on a value list no longer reads `SelectedItems`, which selects item 0 itself (an unrecorded change made by a read). Settled by "every mutation recorded".
  - (03) `add` loses its broad `catch`: building the object (a third-party constructor) happens before the document is touched and, if it throws, reaches the tool-invocation boundary, which answers `{success:false}` as before; everything from `tx.AddObject` on is inside the edit, so a throw there rolls the add back. Settled by "never swallow an exception after recording".
  - (03) `delete` validates in pass 1 and removes every resolved id in pass 2 with no per-id `catch`: a removal that throws rolls the whole call back (Inference 1), so per-id results are fixed in pass 1 and hold whenever the edit commits. Former recipients are expired by the transaction's wire records (`RecordRemove` records each recipient input, which expires its owner, and drops the removed objects from the expire set), so the hand-collected recipient expiry is gone. A call with no resolvable id opens no edit. Settled by the plan's per-id contract and Chunk 01's expire rule.
  - (03) `move` refuses an object without attributes per id in pass 1 (it used to be a caught `NullReferenceException` mid-loop); `move` to the current position and `rename` to the current nickname record nothing and push no `undo`, as `gh_wire connect` of an existing wire does. Settled by "absent when nothing changed".
  - (03) `gh_document clear`'s `solve` keeps its meaning: the transaction adds `solve` only when a switch refused the solve, so host-free `SolvePolicy.WithScheduledEditSolve` fills in `scheduled` when the edit pushed a record and reported no `warning` (one may be a failed schedule, so it is not claimed). A clear with nothing to remove opens no edit and now has no `solve` (it used to schedule a solve that recomputed nothing). Settled by the brief ("keep the reply's `solve` field meaning") and "absent when nothing changed".
  - (03) `gh_document clear` refuses, before removing anything, when an object it would remove does not report itself as in the document (`OnPingDocument`), because `tx.RemoveObject` cannot record it; previously it was removed unrecorded. Settled by validate-then-mutate.
  - (03) `GhDocumentTool.WaitsForSolve` moved to `SolvePolicy.WaitsForSolve`, unchanged, with a theory test. Settled by the chunk description.
  - (05) Record names `Cordyceps: gh_canvas zoomable`, `Cordyceps: gh_script set`, `Cordyceps: gh_script configure`; each records `RecordGeneric(component)` once, before its first port or source change (zoomable `add`: before `CreateParameter`, which belongs to the component). A `set_count` already at its count runs outside the edit and a `configure` that changes nothing records nothing, so neither returns `undo`. Settled by "absent when nothing changed".
  - (05) Rhino 8 script rebuild (RhinoCodePluginGH, Rhino.Runtime.Code and RhinoCodePlatform.GH.Context decompiled): `IScriptObject.Expire` (`Code.ExpireCache`), `IScriptObject.ReBuild` (`PreBuild`, `TryBuildCode`) and `SetSource` (`SetScript`) neither expire nor solve and push no undo record of their own; `ExpireSolution(true)` plus a separate undo record live in `DestroyParameter` and `IScriptObject.ReCompute`, which Cordyceps never calls (it removes ports with `Params.Unregister*Parameter`, which does neither). Settled by reading the assemblies; nothing changed.
  - (05) `set_count` stopping partway (a `CanRemoveParameter`/`CanInsertParameter` refusal or a null `CreateParameter`) throws `EditRollbackException("set_count could not reach N <side> parameters: <today's reason> (it had reached M)")`, so the component is unchanged; a finished `set_count` keeps its reply, with `errors: null`. Its `catch (Exception)` is removed, so an unexpected throw rolls back with the layer's message. Settled by the chunk's acceptance criterion.
  - (05) zoomable's broad catch now covers validation only (a third-party `Can*Parameter` may throw), before the edit opens; nothing in the edit body is caught. Settled by "never swallow an exception after recording".
  - (05) A failed source write in `set` or either `configure` route rolls the whole call back, ports included, instead of committing the ports and answering `codeSet:false`/`sourceError`: `WriteScriptSource` throws `EditRollbackException("Failed to set script code: <writer error>")` (plus "Also: <parametersError>" when both halves failed). `codeSet` is now absent or true, `sourceError` is gone, and `RebuildForChange`'s `sourceWriteFailed` branch is removed as unreachable. Settled by the Product goal (never half-edited) and by `ScriptSourceWriter` reporting a failed host call, which may already have changed the component, as a failure rather than a throw.
  - (05) The param sync in `set` and `configure` (`SyncScriptParams`, `VariableParameterMaintenance`) no longer swallows a throw; it rolls the call back, source included. `SyncParamSide` skipping a port the component refused (it logged and continued, shifting later ports off their index) now rolls back too. `configure`'s `SyncParameters` probe catch is narrowed to `RuntimeBinderException` (member absent, thrown before anything runs) and `SetParameterTypeHint`'s to reflection failures, both still log-and-continue. Settled by "never swallow an exception after recording" and the `set_count` defect class.
  - (05) `configure`'s code route now returns `lostConnections`/`reconnectHint` like `set` (its signature sync cuts wires too and discarded them). Additive. Settled by the reply's own "same lostConnections contract as 'set'".
  - (integration) Reads never select: `GH_ValueList.SelectedItems` (decompiled) sets item 0's `Selected` itself on a non-check list with nothing selected, so the outline/status reader (`CanvasReader`) and `get` read the items' flags through host-free `ValueListItems.ReportedIndex` (first selected; item 0 for a dropdown, sequence or cycle with none; none for a check list or an empty list), which keeps the value each reported before. A grep of `src/Cordyceps` found no other read. Settled by "every mutation recorded" (a read is not an edit).
  - (integration) `SliderConfig.Plan` refuses, in `SliderConfigPlan.Error`, before either `add` or `config` touches the document: a non-number `value` (text unchanged, `Invalid number: <value>`), a `min`/`max`/`value` that is infinite, NaN or beyond decimal's range (the `(decimal)` cast used to throw `OverflowException`, which `add` did not catch), `decimals` above 8, and a minimum above the maximum. `config`'s `OverflowException` catch is gone as dead. Settled by validate-then-mutate and the silent-reinterpretation rule (learnings, core).
  - (integration) The range is checked on what the slider would hold: a bound the call does not give is the slider's current one (`config`) or the new slider's 0–1 (`add`), so `min=5` alone on a 0–1 slider is refused and teaches passing `max` too; `min == max` is accepted; a call that moves neither bound is not judged on an already-inverted range. Settled by `GH_SliderBase.FixValue` clamping to `max` last, which on an inverted range silently pins the value to `max`.
  - (integration) `decimals` 0–8 is Grasshopper's limit (`GH_SliderBase.DecimalPlaces` setter: `Math.Min(Math.Max(value, 0), 8)`, Grasshopper 8.0.23304.9001), so 9 or more is refused rather than silently becoming 8; a negative stays "not provided", the tool's sentinel. The theory case `decimals=10` accepted became `decimals=8` accepted, plus a refusal theory for 9, 28 and `int.MaxValue`: the requirement changed, not the test's strength. Settled by the silent-reinterpretation rule.
  - (integration) `constant` sets `GH_Panel.UserText` and expires the panel's layout itself: `SetUserText` (decompiled) is exactly `UserText = text`, then `Attributes.ExpireLayout()` and `ExpireSolution(true)` when attributes exist, and the solve is the commit's. Settled by "the solver is paused for the whole transaction" (Inference 2).
  - (integration) `gh_script configure` with `inputs`/`outputs` on a component that cannot vary its parameters is refused before its edit opens, with or without `code`: nothing is written, no `undo`; the reply keeps `parametersApplied:false`, `parametersError` (text unchanged) and the unchanged `inputs`/`outputs`, gains `error` (`No configuration applied: <parametersError>` plus how to set only the source), has `codeSet:false` when `code` was passed (the only case `codeSet` is now false, and it means nothing changed), and the rebuild-skipped fields. Settled by validate-then-mutate.
  - (integration) Removed as dead after a grep of `src/` found no caller: `ToolHelpers.GetOwnerDocument` and `GhScriptTool.CreateTypedParameter`. Settled by the brief.
  - (coordinator) Chunks 02-05 were built in parallel and integrated together (merges 874d4d1, 1d68068, d41c5a2, d8dcd4d), so their review is Chunk 06's cumulative review over the one combined diff, not four `chunk` reviews of interleaved merges; settled by the boundary review running everything (rigor is stage-keyed).
  - (coordinator) `group_rename`, `group_color` and `group_move` record nothing when the call changes nothing (same name, same colour, zero offset), matching every other action; settled by the docs audit, which found the inconsistency.
  - (review) `group_add` on an existing group, `gh_script set`, and `gh_script configure` decide "changes nothing" before the edit opens, in host-free `src/Cordyceps/Core/NoChange.cs` (tested), and answer with their usual success reply without `undo`: `group_add` when every id is already a member (Grasshopper's `GH_Group.AddObject` skips a held id, ilspycmd) and no new colour is given; `set` and `configure`'s code route when the stored source is the same text and the signature sync would leave the ports as they are; `configure`'s parameter route when every given side already has those names in order and the given input access, and the code (if any) is stored. A no-op `set`/`configure` writes nothing; code it was given is still rebuilt outside the edit (no undo step; the component is expired and a solve scheduled), because re-sending the same code is how a caller forces a recompile (coordinator, at integration: the delegate's no-rebuild version would have broken that, and a rebuild is not document content). A sides-only no-op rebuilds nothing (`rebuildSkipped` = `NoChange.ScriptAlreadyAppliedReason`). A def with a `type` always counts as a change, because the current type hint lives in a converter that cannot be compared with a type name without setting it; that call is recorded and the tip says so. Every other migrated action already decided before the edit or guarded its recorder (set, config, modifier, preview, enable, move, rename, wire connect/disconnect/clear, group_remove/rename/colour/move); add, constant, delete, group_create, group_delete and zoomable add/remove always change something. Settled by "absent when nothing changed".
  - (review) `gh_document clear`'s `solve` comes from the commit: `EditReplies.CommitSolve(CommitOutcome.Scheduled, refusal, reportScheduled)` reports a refusal for every edit and `scheduled` only for an edit run with `reportScheduledSolve: true`, which only `clear` passes. `SolvePolicy.WithScheduledEditSolve` and its tests are gone, replaced by `EditScopeTests`/`EditRepliesTests` cases on the new path. The documented contract is kept: only `clear` names a scheduled solve. One reply changes: a `clear` whose warning is about a step other than the schedule now carries `solve: scheduled`. Settled by "the transaction knows it" and the existing `clear` contract.
  - (review) `CLAUDE.md` and `docs/architecture.md` say canvas content (objects, wires, values) goes through `_context.Edit`, and reads, the `solver` switch and `revert` use `ExecuteOnUiThread` by design, naming why `solver` inside an edit would be silently reverted (`EndApply` restores `Document.Enabled`). Settled by the review: the old wording, followed, breaks `solver`.
  - (review) `gh_inspect(action='connection')`'s hint for a blocked UI thread held by Cordyceps says a dialog may be waiting for a human once the heartbeat is older than `SolverState.UiWorkDialogSuspectedAfter` (60 s). Measured by the heartbeat's age, not the call's, so a call queued behind a human's open dialog is covered too. 60 s because Cordyceps' own UI work (bakes, captures, saves, script compiles) finishes in seconds to tens of seconds, and it is half the document lock's 120 s wait. `modal_inferred` is unchanged (it cannot tell our work from a dialog inside it). Settled by "a signal is only as trustworthy as what the rest of the system does to it" (learnings, core).
  - (review) Removed as unconsumed (grep of `src/` found no caller): `EditTransaction.RecordPersistentData`, `EditTransaction.Expire`, `UndoEvents`' persistent-data case, its `CreatePersistentDataEvent<T>` reflection helper and `StoredType`, and `EditKind.PersistentData`. The journal's own `Expire` stays: `Record` and `Add` use it for every kind that expires. Batch (#96) re-adds and proves what it needs. Settled by "no unexercised reflection beside verified recorders".
  - (coordinator) The review's R-3 ("changes nothing → no undo" is decided per call site, not at commit) is accepted: the generic record cannot tell at commit whether an archived object changed without comparing whole archives, and every current site decides before its edit through host-free `src/Cordyceps/Core/NoChange.cs` or a guarded recorder (verified by the verify pass's grep). A new action must follow the same pattern, as the `CLAUDE.md` template shows.
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
  named for the call. Rollback leaving the canvas as it was is proven by the journal and scope
  tests (a host-side forced failure has no trigger in `gh_wire`; VRF-024 forces one through
  `zoomable set_count`). The
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
- **Mutation-grep evidence (review):** `grep -rnE` over `src/Cordyceps/Tools/` for `doc.AddObject`,
  `doc.RemoveObject`, `.ExpireSolution(`, `NewSolution`, `AddSource(`, `RemoveSource(`,
  `RemoveAllSources(`, `NickName =` and `Pivot =` has 18 hits, none a mutation outside a
  transaction:
  - inside a transaction body, after its recorder: `src/Cordyceps/Tools/Unified/GhWireTool.cs` `AddSource` (connect),
    `RemoveSource` (disconnect), `RemoveAllSources` (clear), after `RecordWires`;
    `src/Cordyceps/Tools/Unified/GhCanvasTool.Groups.cs` `group.NickName = newName` (group_rename, after `RecordGeneric`) and
    `member.Attributes.Pivot` (group_move, after `RecordPivot`); `src/Cordyceps/Tools/Unified/GhCanvasTool.cs`
    `obj.Attributes.Pivot = pivot` (move, after `RecordPivot`) and `component.NickName = nickname`
    (rename, after `RecordNickName`);
  - on a new object, before or inside the edit that adds it: `src/Cordyceps/Tools/Unified/GhCanvasTool.cs` `add`'s nickname
    and pivot and `constant`'s panel pivot and nickname (before `tx.AddObject`);
    `src/Cordyceps/Tools/Unified/GhCanvasTool.Groups.cs` `group_create`'s and `group_add`'s new group's nickname;
    `src/Cordyceps/Tools/Unified/GhScriptTool.cs` `newParam.NickName` in the port sync, a port just created inside a
    `set`/`configure` edit after `RecordGeneric` on its component;
  - out of scope: a comment in `src/Cordyceps/Tools/Unified/GhScriptTool.cs` naming `NewSolution`, and three comparisons
    (`==`), not assignments, in `src/Cordyceps/Tools/Unified/GhCanvasTool.Groups.cs` (group_rename's unchanged check),
    `src/Cordyceps/Tools/Unified/GhCanvasTool.cs` (find) and `src/Cordyceps/Tools/Unified/GhInspectTool.Tree.cs` (a port label).

  No code hit for `doc.AddObject`, `doc.RemoveObject` or `NewSolution` in a tool class: adds and
  removes go through `tx.AddObject`/`tx.RemoveObject`, and solves through the commit. One
  `.ExpireSolution(false)` hit stands outside a transaction by design: `RebuildUnchanged` in
  `src/Cordyceps/Tools/Unified/GhScriptTool.cs`, which expires a script whose source and ports already match after rebuilding its
  program, so the rebuilt program runs. Expiry is solve state, not document content, and nothing
  about the component changes, so there is nothing to record. The by-nature exceptions (Inference 5) do not match the pattern: `solver`'s
  `doc.Enabled` and `revert`'s `canvas.Document`.
