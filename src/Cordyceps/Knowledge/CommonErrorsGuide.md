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
name `Group`, even when only one object carries it; every other action accepts them.

| Error | Cause | Fix |
|-------|-------|-----|
| "'X' is the nickname of N objects: 1a2b3c4d Point, 5e6f7a8b Point; name one by its id" | Several objects carry that nickname (default nicknames like `Pt` or `Circle` included, except on the removal actions, which give the refusal below) | Pass one of the listed ids. `gh_canvas(action='outline')` prints each object by a name that resolves |
| "'Circle' is the default nickname of abcdef01 Circle, which every new Circle gets, so it never selects what this action removes. Pass id 'abcdef01', or name it first with gh_canvas(action='rename', id='abcdef01', nickname='NEW') and pass that name" | `delete`, `gh_wire disconnect` or `gh_wire clear` was given the nickname every new object of that type gets. It would remove whichever such object happens to be alone, so it is refused even when unique (on `gh_wire` the text starts `Source:`, `Target:` or nothing for `clear`) | Pass the id the error prints, or rename the object as shown and pass the new name. In `delete ids=[…]` only that entry fails; the others are deleted |
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
| `ui: "unknown"` | No heartbeat recorded yet (the component was just placed) | Retry in a second |

`gh_document(action='recompute')` refuses while a solution is running — or while a modal dialog holds the UI thread — rather than queueing or
blocking, and returns `success: false` with `solving: true` and `solving_since`. That is not a
failure to fix — it means "already working, come back". `gh_inspect(action='status')` answers the
same way instead of hanging when the UI thread is not responding.

The `GET /health` endpoint carries the same three-layer picture under `host`, for a caller outside
the MCP protocol.

## Solver Errors

| Error | Cause | Fix |
|-------|-------|-----|
| Not responding / loop | Cyclic dependency | `gh_document(action='solver', enabled=false)`, fix cycle |
| "Solver is disabled" | Not re-enabled | `gh_document(action='solver', enabled=true)` |

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
| `configure` returns `success:false` with `codeSet:false` and a `sourceError` | The parameter changes were applied but the source write failed | The component's params reflect the new configuration and its code is unchanged — a detectable partial apply. Fix the cause named in `sourceError`, then call `gh_script(action='set')` with just the code. |
| Code was set, no error anywhere, and the component keeps producing its old results | Storing source is not the same as recompiling. Rhino builds a program from the script and keeps running that one, so a component whose source was replaced goes on executing the previous build until something invalidates it | `set`/`configure` now ask for the rebuild themselves and report it: `rebuilt:true` means the new program is what the next solve runs. `rebuilt:false` comes two ways, and they are opposite facts: `rebuildSkipped` means the component has no rebuild hooks and recompiles from its own source member — expire it (change an input, or `gh_document(action='recompute')`) and re-check. `rebuildFailed` means a hook threw and the component is probably *still running the old program* — the reason names the exception, and `gh_inspect(action='log')` carries the probe trace. |
| `set` succeeded but nothing new appears on the outputs | The source compiles into a program that is running, but it failed to build, or it runs and produces nothing | A rebuild does not report compile errors — they surface on the component itself. Run `gh_inspect(action='status')` for the component's error/warning messages and `gh_inspect(action='reports')` for script output. |
| `verified` (on `set`) or `sourceDiverged` (on `get`) is missing entirely | The running program could not be read at all — most often because the component has never built | Not a silent match: `verificationSkipped` (on `set`) or `runningSourceUnavailable` (on `get`) carries the reason, and the probe trace is in `gh_inspect(action='log')`. Never read an absent `verified`/`sourceDiverged` as agreement. |
| `get` reports `sourceDiverged:true` | The stored source and the running program are not identical | Usually expected, not a fault: Rhino rewrites the `RunScript` signature of SDK-mode C# scripts while building them. `runningSource` in the response is what actually executes — compare against that, not against `source`. |
| Outputs stay null with `verified:true` and no error, warning or runtime message anywhere | The code *defines* a `RunScript` function but never calls it, and has no top-level statements. The write and rebuild were sound — it compiles, and the component even syncs its output ports from the signature — but nothing in the program ever executes | Write the body as top-level statements (`a = ...;`), which is what a script component runs. `verified:true` is truthful here and *cannot* catch this: the stored and running text are identical, so there is no divergence to see. Python 3 does surface it — Rhino rewrites the signature to `def RunScript(self, x, y)`, so `get` reports `sourceDiverged:true` — but a C# script gives no signal at all. |

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
