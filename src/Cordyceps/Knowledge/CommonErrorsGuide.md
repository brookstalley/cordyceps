# Common Errors

## Component Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Component not found: {guid}" | No object has that full GUID (or it is Cordyceps' own bridge, which reads the same) | `gh_canvas(action='outline')` or `gh_canvas(action='list')` (optionally with `typeFilter`/`group`), or `gh_canvas(action='find', nickname='...')` — find matches default nicknames like 'Circle'; no need to rename components |
| "No component type is named 'X'…" (`code: "unknown_type"`) | Names are matched exactly (case ignored), never partially — `Cull` no longer creates Cull Pattern. Nothing was created | Pick from `suggestions` and pass its `use` value as `type`, or `gh_canvas(action='search', query='...')` |
| "'X' matches N component types…" (`code: "ambiguous_name"`) | Several installed types share the name — often a component and a parameter (`Circle`, `Line`), or two plugins. Nothing was created | Pass the `use` value of the one you mean from `matches` (e.g. `Curve/Circle`, `Params/Circle`), or its GUID |
| "No installed category is named 'X'…" (`code: "unknown_category"`) | The `Category/...` part names a ribbon tab that isn't installed | Check the spelling against `gh_inspect(action='categories')`; the plugin may not be loaded |
| "No installed component type has the GUID …" (`code: "guid_not_installed"`) | The plugin providing it isn't loaded | Install/load the plugin, or find an installed equivalent with `search` |
| Response has `deprecated: true` and a `warning` | The only type with that name is deprecated, so it was used | Usually fine; to use the replacement, add `upgradeTo` / `upgradeToGuid` instead |

## Object Name Errors

Every parameter that takes a canvas object accepts its full GUID, an 8+ hex GUID prefix, or its
unique nickname (exact, case-sensitive, never trimmed). Group actions (`group_*`, `list group=`,
`outline group=`) take a group's exact name, GUID or prefix. Nothing is guessed: each error below
acted on nothing. The actions that remove something (`delete`, `group_delete`,
`gh_wire disconnect` and `gh_wire clear`) also refuse a default nickname and the default group
name `Group`, even when only one object carries it. So does a call that removes an existing port,
because that cuts the port's wires: `gh_canvas zoomable` with `operation='remove'` or a `set_count`
below the current count, and `gh_script set`/`configure` when the new `RunScript` signature or
`inputs`/`outputs` drop or rename a port. The same actions accept a unique default nickname when
they only add or keep ports; every other action accepts them.

| Error | Cause | Fix |
|-------|-------|-----|
| "'X' is the nickname of N objects: 1a2b3c4d Point, 5e6f7a8b Point; name one by its id" | Several objects carry that nickname (default nicknames like `Pt` or `Circle` included, except on the removal actions, which give the refusal below) | Pass one of the listed ids. `gh_canvas(action='outline')` prints each object by a name that resolves |
| "'Circle' is the default nickname of abcdef01 Circle, which every new Circle gets, so it never selects what this action removes. Pass id 'abcdef01', or name it first with gh_canvas(action='rename', id='abcdef01', nickname='NEW') and pass that name" | `delete`, `gh_wire disconnect` or `gh_wire clear` was given the nickname every new object of that type gets. It would remove whichever such object happens to be alone, so it is refused even when unique (on `gh_wire` the text starts `Source:`, `Target:` or nothing for `clear`) | Pass the id the error prints, or rename the object as shown and pass the new name. In `delete ids=[…]` only that entry fails; the others are deleted |
| "This call removes input 'D1', and every wire on it, so nothing was changed. 'Merge' is the default nickname of 1a2b3c4d Merge, which every new Merge gets, so it never selects what this action removes. Pass id '1a2b3c4d', …" | A call that removes an existing port named its component by a default nickname: `gh_canvas zoomable` `remove`, a `set_count` below the current count, or `gh_script set`/`configure` whose new signature or `inputs`/`outputs` drop or rename a port (a rename is a removal plus an addition). Removing a port cuts its wires, so it is refused like `delete`. Nothing was changed | Pass the id the error prints, or rename the component and pass the new name. The same call that only adds or keeps ports (`add`, a `set_count` upward, a configure that keeps every existing name) still accepts a unique default nickname |
| "'Pt' is the default nickname of 2 objects: 1a2b3c4d Point, 5e6f7a8b Point. A default nickname never selects what this action removes; pass one of these ids" | The same refusal when several objects carry the default nickname | Pass the id of the one you mean |
| "'Group' is the default name of group 1a2b3c4d, which every new group gets, so it never selects what this action removes. Pass id '1a2b3c4d', or name the group first with gh_canvas(action='group_rename', id='1a2b3c4d', name='NEW') and pass that name" | `group_delete` was given the default group name (with several such groups: `'Group' is the default name of N groups: …. A default group name never selects what this action removes; pass one of these ids`) | Pass the id the error prints, or rename the group as shown |
| "'X' is the name of N groups: …; name one by its id" | Several groups share the name | Pass one of the listed group ids |
| "GUID prefix 'X' matches N objects: …; give more characters" | The prefix is shared | Add characters, or use one of the listed ids |
| "'X' names two objects: nickname → …; GUID prefix → …. Use a longer GUID prefix" | The text is one object's nickname and the start of another object's GUID | Use a longer prefix or the full GUID of the one you mean |
| "No object named 'X'." (optionally "Did you mean 'Y'?") | No object carries that nickname, in that case. Matching is case-sensitive and the text is not trimmed (`' COUNT'` is not `COUNT`) | Use the suggested spelling, or find the object with `outline` or `find` |
| "… A GUID prefix needs at least 8 hex digits." | A short hex text such as `3f2a` was read as a nickname and matched nothing | Give 8 or more hex digits of the GUID |
| "… 'X' is a group; group actions take its name, and delete, move, rename, info, bounds and zoom take its id (…)." | A group's name was passed to an object action (`info`, `delete`, `gh_wire`, …) | Use a `group_*` action with that name, or pass the group id the message gives to one of the actions it lists |
| "'X' (GUID) is a group; this action works on components and parameters, not groups" | A group's id was passed to an action that reads or changes one component or parameter (`get`, `set`, `config`, `bake`, `gh_wire`, `gh_script`, `gh_inspect outputs`/`geometry`/`trace`/`tree`, …) | Pass a member of the group instead; `gh_canvas(action='outline', group='X')` lists them |
| An entry with "same object as entry 0: give one position per object" | Two entries of `moves` named the same object | The first entry moved it; send one position per object |
| "No group named 'X'. … 'X' is an object's nickname, not a group." | An object's nickname was passed where a group is expected | Pass a group's name or id; `group_list` and `outline` list them |
| "Object is not a group: X" | A group action, `list group=` or `outline group=` was given the id of something that is not a group | Pass the group's id or name |
| "Group not found: X" | No group has that full GUID | Check with `group_list` |
| "Invalid color: X" from `group_create`, `group_add` or `group_color` | The `color` is not hex `#RRGGBB`, `R,G,B` or a color name. The call was refused before anything changed; `group_create` and `group_add` used to ignore such a color and succeed without it | Pass a color in one of those forms, or omit `color` on `group_create`/`group_add` |
| "Nothing was changed: 1 of 2 ids did not resolve. ids[1] 'nope': No object named 'nope'." | A member array (`group_create`, `group_add`, `group_remove` `ids`) had an entry that did not resolve. These calls are all-or-nothing: no group is created or changed | Fix the named entries and resend |
| "Nothing was changed: ids[0] 'X' is the group itself; a group cannot contain itself" | `group_add` was asked to add a group to itself | Remove that entry |
| An entry with `note: "same object as entry 0"` | Two entries of `ids` in `delete`, `preview` or `enable` named the same object | Not an error: the object was acted on once. If entry 0 failed, the repeat fails with `same object as entry 0, which …` |
| `notes: ["ids[1]: same object as entry 0"]` at the top of the response | A member array (`group_create`, `group_add`, `group_remove`) named the same object twice | Not an error: the object was used once |

`delete`, `move`, `preview`, `enable` and bulk `gh_wire connect` resolve every entry before
changing anything, then act on the entries that resolved and report each failure per entry
(overall `success: false`). `gh_wire(action='list')` with an id that names nothing is an error, not
an empty list.

## Connection Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Source/Target: Parameter 'X' not found on the input/output side of 'C'…" (`gh_canvas modifier` returns it without the prefix) | The name isn't a port's exact name or nickname. Partial names are never matched (`Pattern` does not reach Cull Pattern's `Cull Pattern` input) | Use the port the error suggests ("Did you mean 'P' (1 P (Cull Pattern))?") or any name/nickname from "Available", or a 0-based index. `gh_canvas(action='info', id='...')` lists every port |
| "Source/Target: Parameter 'X' is ambiguous on the … side of 'C'…" | Two ports share that name or nickname (or match it when case is ignored) | Pass the 0-based index the error lists, or the exact case |
| "Source/Target: Parameter index N is out of range" | The port does not exist — commonly a variable-parameter component whose input count has not been raised yet | The error names the valid range. Add the port first (`gh_canvas(action='zoomable', id='...', side='input', operation='add')`), then wire. An index is never rescued as a name match, so re-sending the same number will not connect. (A free-floating param object has no ports of its own — it is the target and the spec is ignored) |
| "Type mismatch" | Incompatible types | `gh_wire(action='validate', ...)` first; add conversion component |

## Data Tree Errors

| Symptom | Cause | Fix |
|---------|-------|-----|
| N×M expected, got max(N,M) | Both inputs flat | Graft one input: `gh_canvas(action='modifier', id='...', param='B', mapping='graft')` |
| Unexpected combinations | Mismatched structures | Align with Graft/Flatten; read current state with `gh_canvas(action='info')` → `modifiers` |
| Wrong item count | Access mode mismatch | Check Item vs List mode |
| Branch counts don't match what you expect | Inputs arrive with different structures | `gh_inspect(action='tree', id='...', side='input')` shows every input's paths and notes `inputs don't line up: …` |
| A count reads `0 stale` (outline) or a tree has `solved: false` | The solver is off, or the object has not computed in the current solution, so the number may be from an earlier solution | Read the `solver` field and the tree's `note`. Turn the solver on (`gh_document(action='solver', enabled=true)`) or recompute before trusting the count. `disabled` means the object is locked |

See `gh://docs/data-trees` for details.

## Is It Busy, Or Is It Dead?

Every tool response carries a compact `status` block:

```json
"status": {"document": "wall-study.gh", "ui": "responsive", "solving": false}
```

When something is off it also carries `solving_since`, `modal_inferred`, `solving_document`, and a
`hint`. `document` is the `.gh` file the call acted on — tools follow whichever canvas tab the human
focused, so check it if results look like they belong to another file. `solving_document` appears
only when a *different* open definition is the one holding the solver: all open definitions share
the single Rhino UI thread, so a heavy solve in a file you are not touching still blocks you.

If a call is slow, times out, or returns nothing at all, call `gh_inspect(action='connection')`.
It answers from cached state and never touches the Grasshopper document, so it replies even when
every other call is stuck.

| What you see | What it means | What to do |
|---|---|---|
| `ui: "responsive"` | The host is fine | Treat any error as a real tool error, not a host problem |
| `ui: "blocked"`, `solving: true` | Grasshopper is mid-solve and holding the UI thread | **Wait.** Heavy solves take minutes. Re-probe with `connection`; do not retry in a tight loop |
| `modal_inferred: true` | The UI thread is stuck with nothing solving — a modal dialog is open in Rhino | **Only a human can clear it.** Stop retrying and tell the user to check Rhino for a dialog |
| `ui: "blocked"`, `solving: false`, no `modal_inferred`, hint "A Cordyceps operation…" | A Cordyceps call holds the UI thread (a bake, capture or save), which also keeps `modal_inferred` from seeing a dialog | **Wait.** Once the thread has been silent for more than a minute the hint says a dialog may be open; then ask a human to check Rhino |
| `ui: "unknown"` | No heartbeat recorded yet (the component was just placed) | Retry in a second |

`gh_document(action='recompute')` refuses while a solution is running — or while a modal dialog holds the UI thread — rather than queueing or
blocking, and returns `success: false` with `solving: true` and `solving_since`. That is not a
failure to fix — it means "already working, come back". `gh_inspect(action='status')` answers the
same way instead of hanging when the UI thread is not responding.

When it does run, `recompute` schedules the solve and waits for it (up to `timeout` seconds, 1-300,
default 30) without holding the document, so other calls and the `connection` probe keep answering
while Grasshopper solves. `solver` with `enabled=true` and `revert` wait the same way. The reply
carries `solve.state`; see [How a solve ended](#how-a-solve-ended).

The `GET /health` endpoint carries the same three-layer picture under `host`, for a caller outside
the MCP protocol.

## Solver Errors

| Error | Cause | Fix |
|-------|-------|-----|
| Not responding / loop | Cyclic dependency | `gh_document(action='solver', enabled=false)`, fix cycle |
| Nothing recomputes; `recompute` replies `solve.state: "solver_disabled"`, or `gh_document(action='info')` shows `solverEnabled: false` | The document's solver was turned off and not turned back on | `gh_document(action='solver', enabled=true)` |
| `solve.state: "solutions_locked"`, or `info` shows `solutionsLocked: true` | Grasshopper's global solver lock is on | Only a human can unlock it; see the table below |

### How a solve ended

`gh_document(action='recompute')`, `gh_document(action='solver', enabled=true)` and
`gh_document(action='revert')` wait for their solve and report
`solve: {state, elapsedMs, solveMs, errors, warnings, remedy}`. `elapsedMs` is how long Cordyceps
waited; `solveMs` is Grasshopper's own solve time, present when a solve ended. `success` (and
`recomputed` for recompute) is true only for `completed`; every other state comes with
`solve.remedy` saying what to do. For `solver` and `revert` the switch or the revert has happened
either way; only the solve's outcome differs.

| `solve.state` | What happened | What to do |
|---|---|---|
| `completed` | The solve finished. `errors`/`warnings` count objects at that level, as `gh_inspect(action='status')` does | Read `status` for details when either is non-zero |
| `aborted` | The solve was stopped before it finished: someone pressed **Escape** in Grasshopper, or an abort was requested. Outputs may be partial or stale | A human usually stops a solve on purpose. Ask before recomputing again; `outline` shows `the last solution was aborted` and `tree` notes which owners it did not reach |
| `over_budget` | The wait ran out while the solve was **still running**. Cordyceps cannot cancel a solve | Poll `gh_inspect(action='connection')` until `solving` is false, then `gh_inspect(action='status')`. `recompute` is refused while it runs. Pass a larger `timeout` (up to 300) next time |
| `solver_disabled` | This document's own solver is off, so nothing was computed | `gh_document(action='solver', enabled=true)` — Cordyceps can flip this switch, and so can the human |
| `solutions_locked` | Grasshopper's **global** solver switch is off — the "Lock Solver" toggle, or Rhino is mid-command — so no document solves | Cordyceps never changes the global lock: ask a human to unlock it. If Rhino was only busy with a command, retry once it is idle |
| `not_started` | The wait ran out with nothing solving and no solve finished: the scheduled solve never ran. The solver was locked meanwhile, the Grasshopper editor window was not available to start it, or the document raises no solution events | Check `gh_inspect(action='connection')` and `gh_inspect(action='status')`, then retry `recompute` |
| `not_watched` | Cordyceps could not watch the document's solution events — the Cordyceps component's solution watcher is not running — so it could not have told when a solve ended, and scheduled none | Make sure the Cordyceps component is on the canvas and enabled, then retry |
| `document_closed` | The document closed before the solve finished | The outcome is unknown; reopen it and recompute if you still need it |

`clear` only schedules its solve and returns before it runs, with `solve.state: "scheduled"` (or
`solver_disabled` / `solutions_locked` / `not_watched` with a `remedy`). Call `recompute` before
reading results after a `clear`.

## Cluster Editor Errors

| Error | Cause | Fix |
|-------|-------|-----|
| All cluster inputs null after recompute | Native F5/recompute destroys input hooks | Use `gh_document(action='recompute')` instead — it is cluster-safe |
| Cluster inputs lost after re-enabling solver | `NewSolution(true)` expires input hooks | Use `gh_document(action='solver', enabled=true)` — it is cluster-safe |

**CRITICAL:** When working inside a cluster editor, NEVER use Grasshopper's native F5/recompute button — it will destroy all cluster input data (all inputs become null). This is a Grasshopper architectural limitation. Always use `gh_document(action='recompute')` which is cluster-safe. If inputs are lost, close and reopen the cluster editor to recover.

## Script Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Compilation failed" | Syntax error | `gh_inspect(action='reports')` for details |
| "Type not found" | Missing import | C#: `using Rhino.Geometry;` / Python: `import Rhino.Geometry as rg` |
| Connections lost after `gh_script set` | Param renamed or removed | Check `lostConnections` in response, re-wire with `gh_wire(action='connect')` |
| "Can not determine input code language" | Directive-less body set on a bare unified **Script** component, which has no language until one is given | `gh_script(set)` preserves an existing directive automatically; for a bare Script component with no language yet it now returns a `languageWarning` in the response. Start your `code` with `#! python 3` (or `// #! csharp`) as line 1 and call `set` again — this also recovers a component already in this state. (The dedicated `C# Script` / `Python 3 Script` components carry a concrete language and don't need a directive.) |
| "Cannot set source on 'X': the component's code input parameter is visible" | The component exposes its code as a wired input parameter, so its source is driven by that wire and cannot be assigned directly | Disconnect and remove the component's code input parameter, then retry `gh_script(action='set')` — or feed the code through that parameter with `gh_wire` instead. Nothing was written; the component is unchanged. |
| "Cannot set source on 'X': no writable source member was found" | The component has neither a `SetSource(string)` method nor a writable `Code` property — it is probably not a script component, or its script API is one Cordyceps cannot write to | Confirm the target with `gh_script(action='info')`; the response's `componentType` names what was actually resolved. Reading source may still work even when writing does not. |
| `set` or `configure` returns `success:false`, `rolledBack:true` and "Failed to set script code: …" | The source write failed, or a port sync the component refused. The whole call is rolled back, ports included, so a `configure` never leaves new ports around old code | Nothing changed. Fix the cause the error names and send the call again. |
| `configure` returns `success:false`, "No configuration applied: …", `parametersApplied:false` and a `parametersError` (plus `codeSet:false` when you passed `code`) | The component cannot vary its parameters, so the call was refused whole before anything changed: the code was not written either, and there is no `undo` | The `inputs`/`outputs` echoed in the response are the component's **unchanged** surface, so do not wire to a port you assumed this call created. To set only the source, call again without `inputs`/`outputs`; to reshape ports, use a component that supports variable parameters (the dedicated `C# Script` / `Python 3 Script` components do). |
| Code was set, no error anywhere, and the component keeps producing its old results | Storing source is not the same as recompiling. Rhino builds a program from the script and keeps running that one, so a component whose source was replaced goes on executing the previous build until something invalidates it | `set`/`configure` ask for the rebuild themselves and report it: `rebuilt:true` means the new program is what the next solve runs. `rebuilt:false` always arrives with the reason beside it, and the reason is what tells you which case you have. `rebuildFailed` means a hook threw and the component is probably *still running the old program*: the string names the exception, and `gh_inspect(action='log')` carries the probe trace. `rebuildSkipped` is not a fault — read its string: *no rebuild hooks* means the component recompiles from its own source member, so expire it (change an input, or `gh_document(action='recompute')`) and re-check; *changed neither the component's parameters nor its source* means the call was a no-op and there was no stale program to replace, so recomputing chases nothing. Re-sending code the component already stores writes nothing and records no undo step, but still rebuilds (`rebuilt:true`), so it is the way to force a recompile. |
| A script component's ports were reshaped and it still runs against its old ports | The build reads the component's parameters to shape the program's inputs and outputs, so a program compiled against the ports you just replaced is stale until it is rebuilt | `gh_script(action='configure')` rebuilds whenever it changed anything, parameters included, and `gh_canvas(action='zoomable')` rebuilds a C# or Python script component after `add`, `remove` or `set_count`; both report it the same way `set` does. With no code written, `verified`/`sourceDiverged` compares the running program against the source already stored on the component, so `verified:false` on a C# SDK-mode script is the usual signature rewrite, not a fault. A reply with no `rebuilt` field at all comes from a component that is not a script (Merge, Entwine), which has no program to rebuild. |
| `configure` (or a `zoomable set_count` already at its count) returns `rebuilt:false` with `rebuildSkipped` saying nothing changed | The call reached the component but changed nothing — no `code` and no `inputs`/`outputs`, or `inputs`/`outputs` on a component that cannot vary its parameters, which refuses the whole call | Nothing is wrong with the component; there was no stale program to replace. Check `success`: a `success:false` here comes with `error` and `parametersError` naming the change the component could not accept. Confirm the target is a script component with `gh_script(action='info')`. |
| `set` succeeded but nothing new appears on the outputs | The source compiles into a program that is running, but it failed to build, or it runs and produces nothing | A rebuild does not report compile errors — they surface on the component itself. Run `gh_inspect(action='status')` for the component's error/warning messages and `gh_inspect(action='reports')` for script output. |
| `verified` (on `set`/`configure`/`zoomable`) or `sourceDiverged` (on `get`) is missing entirely | The running program could not be read at all — most often because the component has never built — or there was nothing to compare it against | Not a silent match: `verificationSkipped` (on `set`/`configure`/`zoomable`) or `runningSourceUnavailable` (on `get`) carries the reason, and the probe trace is in `gh_inspect(action='log')`. Never read an absent `verified`/`sourceDiverged` as agreement. |
| `get` reports `sourceDiverged:true` | The stored source and the running program are not identical | Usually expected, not a fault: Rhino rewrites the `RunScript` signature of SDK-mode C# scripts while building them. `runningSource` in the response is what actually executes — compare against that, not against `source`. |
| Outputs stay null with `verified:true` and no error, warning or runtime message anywhere | The code *defines* a `RunScript` function but never calls it, and has no top-level statements. The write and rebuild were sound — it compiles, and the component even syncs its output ports from the signature — but nothing in the program ever executes | Write the body as top-level statements (`a = ...;`), which is what a script component runs. `verified:true` is truthful here and *cannot* catch this: the stored and running text are identical, so there is no divergence to see. Python 3 does surface it — Rhino rewrites the signature to `def RunScript(self, x, y)`, so `get` reports `sourceDiverged:true` — but a C# script gives no signal at all. |

## Edits, Undo and Rollback

Every Cordyceps call that changes the Grasshopper canvas is one Grasshopper undo step. A human
beside you presses Ctrl+Z (Cmd+Z on macOS) in Grasshopper and gets the canvas back as it was before
your last call, and Redo (Ctrl+Y on Windows, Cmd+Shift+Z on macOS) puts it back. The reply's `undo`
field names the step as it appears in Grasshopper's undo list (`"Cordyceps: gh_canvas delete"`); it
is absent when the call changed nothing. `gh_document` `undo`/`redo` stay unavailable to agents:
take `gh_document(action='snapshot')` before a risky change and `revert` to restore. Not recorded:
`solver` (a switch), `revert` (it installs a new document whose undo history starts empty), `bake`
(Rhino's own undo covers it), and `zoom`, `view` and the capture actions (view only).

Per-id actions (`delete`, `move`, `preview`, `enable`, bulk `connect`) keep their per-id results:
an id refused at validation is reported and never touched, and the ids that succeed are one undo
step. For `delete` that includes an object that does not report itself as in this document, whose
removal cannot be recorded for undo. The group member arrays (`group_create`, `group_add`,
`group_remove` `ids`) are all-or-nothing instead: one entry that does not resolve fails the call
and nothing is changed. `gh_document clear` takes no ids, and refuses the whole call when any
object on the canvas does not report itself as in this document. An unexpected failure while
applying rolls back the whole call and answers with the rollback reply below instead.

While a call applies its changes, Cordyceps turns the document's solver off and back on, so the
document raises two `EnabledChanged` events per call; a plugin that listens to them sees each call.

| What you see | What it means | What to do |
|---|---|---|
| `success:false`, `rolledBack:true`, error ending "Nothing was changed: every change this call had made was rolled back." | The call failed partway, and everything it had changed was undone | Nothing to clean up. Fix the cause the error names and retry |
| `rolledBack:false` with `rollbackFailures: [{step, kind, id, error}]`, error "The rollback did not complete: N of M change(s) could not be undone" | Some changes could not be undone; each one is listed with the object `id` and the kind of change | Check the listed objects on the canvas (`gh_canvas(action='info', id=...)`). Ctrl+Z does not cover this call. Revert to a snapshot if you took one |
| `rolledBack:true` and `rollbackFailures` whose `step` is `restore`, `expire`, `schedule` or `afterRollback` | The content was restored, but a step after the undo failed (restoring the solver switch, marking objects for recompute, scheduling the solve, redrawing) | Results may be stale: check `gh_document(action='info')` for the solver switch, then `gh_document(action='recompute')` |
| "… failed: recording it for undo failed (…)" | The change could not be pushed as an undo record, so it was rolled back rather than left on the canvas without one | Retry. If it repeats, read `gh_inspect(action='log')` |
| `success:true` with a `warning` "The edit was made and Ctrl+Z undoes it, but … failed" | The edit happened and is undoable, but a step after it (restoring the solver switch, expiring, scheduling the solve) failed | Check `gh_document(action='info')`, then `gh_document(action='recompute')` |
| An edit's reply carries `solve: {state: "solver_disabled", remedy}` (or `solutions_locked` / `not_watched`) | The edit was made; the solve it asked for was refused by a solver switch. Expected while you have the solver off for bulk work | Not an error. Turn the solver back on (`gh_document(action='solver', enabled=true)`) when the bulk work is done |
| Your call hangs, and `connection` shows `ui: "blocked"`, `solving: false` and no `modal_inferred`, with a hint that a dialog may be open (after a minute) | A modal Grasshopper "undo failed" dialog opened during a rollback: Grasshopper restored an object from its saved state, the restore threw, and it showed a dialog instead of reporting it to Cordyceps. The rollback runs inside your call, which holds the UI thread, so `modal_inferred` cannot turn true; for its first minute the hint reads as Cordyceps' own work | Ask a human to check Rhino for a dialog and dismiss it; your call then returns. Afterwards check the object on the canvas |
| A modal "undo failed" dialog after a human's Ctrl+Z; `connection` shows `modal_inferred:true` | The same restore failure, raised outside any Cordyceps call | Only a human can dismiss it. Afterwards check the object on the canvas |
| A plugin's object comes back different after Ctrl+Z or a rollback | Slider, toggle, panel and value-list values, group membership and colour, and port or script changes are restored from the object's saved state. A third-party object that does not save all of its state comes back with only what it saves | Check that object and set it up again |
| A Relay's wires are gone after `delete` | Deleting a Relay drops its wires; Grasshopper's own Delete reconnects the relay's source to its targets, Cordyceps' does not | Reconnect with `gh_wire(action='connect')`, or Ctrl+Z the delete |
| "Invalid toggle value: 'x'. Use true/false, 1/0, or yes/no." | `set` on a Boolean Toggle reads only those words (case ignored). It used to read every other word, `yes` included, as false | Pass one of them |
| "Configuring a value list needs 'items' …" | `config` on a value list was called without `items` | Pass `items='[{"name":"A","value":"1"}]'` |
| "'items' must be a JSON array of {name, value} objects …", "'items' is empty …", "Item N has an unknown key 'X' …", "Item N has a 'name' that is Null …" | One item is malformed, so the whole list was refused before it was touched. Only the keys `name` and `value` are allowed (a missing one defaults to `Item` / `0`), and each must be text, a number or a boolean | Fix the item the error names. The list is unchanged |
| "min must be a finite number within ±7.92E+028 (a slider holds a decimal); got …" (or `max`, `value`) | A slider `add` or `config` value that is infinite, or too large for a slider to hold | Pass a smaller number. Nothing was changed |
| "decimals must be 0 to 8; got N. …" | Grasshopper keeps at most 8 decimal places on a slider; a larger request would silently become 8 | Pass 0-8. Nothing was changed |
| "min (X) is greater than max (Y) …", "min (X) is greater than the slider's max (Y); pass max as well …", "max (Y) is less than the slider's min (X) …" | The range would be inverted. A lone `min` or `max` is checked against the slider's other bound: its current one on `config`, the default 0-1 on `add` | Pass both `min` and `max` to move the range (`min` equal to `max` is allowed). Nothing was changed |
| "set_count could not reach N input parameters: Cannot add parameter at index K (it had reached M). Nothing was changed: …" | The component refused a port partway through `zoomable set_count`, so the call rolled back | The component has a limit; pick a count it accepts, or reshape one port at a time with `add`/`remove` to find it |
| "Nothing was cleared: N object(s) on the canvas do not report themselves as in this document …" | `gh_document clear` found objects that have lost their link to the document, whose removal cannot be recorded for undo, so it removed nothing | Rare. Save and reopen the definition, or delete the listed objects by hand in Grasshopper, then clear again |

## Type Marshaling

Numeric parameters (e.g., `x`, `y`, `lens`, `wait`, `timeout`, `azimuth`) accept both JSON numbers (`300`) and string-encoded numbers (`"300"`). The server coerces automatically. If you see "Cannot convert String to int/double", the value is not a valid number.

## Other Errors

| Error | Cause | Fix |
|-------|-------|-----|
| "Unknown argument: 'x' (did you mean 'y'?). Valid parameters: …" (one wrong name) or "Unknown arguments: 'x' (did you mean 'y'?), 'z'. Valid parameters: …" (several) | An argument name is not a parameter of that tool. Names are exact and case-sensitive (`sourceid` is not `sourceId`). Nothing was run | Every unknown name is listed in one response with the closest valid parameter where one is near; fix the names and resend. When `arguments` is not a JSON object the error is "Invalid arguments: 'arguments' must be a JSON object…" instead |
| "Component not found" / "No object named …" for the Cordyceps component | Cordyceps' own bridge component and the objects wired to it are hidden from every tool | Work with other components |
| "No active document" | GH/Rhino not ready | Ensure document open, `gh_document(action='info')` |
| "No bakeable outputs" | Non-geometry component | Only geometry types can bake |
| "Render timed out" | Slow render | Increase timeout or reduce complexity |
| "Document is busy: another operation held the ... document lock for more than N seconds" | A prior request is running a long operation, or the Rhino UI thread is wedged (e.g. an infinite-loop script component) | Wait and retry; if it persists, a script component is likely stuck — fix or remove it, and restart Rhino if needed. Operations are serialized on the single Rhino UI thread, so only one runs at a time. |
| "MCP server is shutting down; the request was not processed" | The Cordyceps component was removed (or its port changed) while this request was in flight | Transient — the server stopped. Re-place the component (or restore its port) and reconnect, then retry. |
