---
artifact: data-model
version: 1
depends_on:
  - artifact: project-state
last_validated: 2026-11-11
---

# Data Model

Cordyceps has **no database and no persisted format of its own.** The data it works on — Grasshopper
definitions and the Rhino model — belongs to the host and the human, and is saved by them. What
Cordyceps owns is a small amount of process-lifetime state that dies with Rhino, plus the
identifiers it uses to name host objects on the wire. Those identifiers are the part of this model
that consumers depend on, so they get the most care below.

<!-- OPTIONAL norm home. No norms are ratified yet; see "Norm candidates" at the end. -->

## Entities

### Host-owned (read and changed, never stored by Cordyceps)

| Entity | Owner | How Cordyceps names it on the wire |
|---|---|---|
| Grasshopper document (`GH_Document`) | Grasshopper / the human | Implicitly: the **focused canvas tab**. Every reply's `status.document` names the `.gh` file the call acted on |
| Canvas object (component, parameter, slider, panel, scribble…) | Grasshopper | GUID, 8+ hex GUID prefix, or unique case-sensitive nickname. One resolver: `Core/Canvas/ObjectResolver.cs` |
| Group | Grasshopper | Exact name, GUID or prefix, in the Groups scope only (a group name is never an object id) |
| Port (component input/output) | the component | 0-based index, or exact name or nickname (case ignored only when that leaves one match). `Core/ParamSpecLookup.cs` |
| Component type | installed plugins | Exact name (case ignored), `Category/Name`, `Category/Subcategory/Name`, `Name (Category)`, GUID, or a pinned alias. Never a partial match. `Core/Naming/TypeNameRule.cs` |
| Undo record | Grasshopper's undo stack | Named `Cordyceps: <tool> <action>`, returned as the reply's `undo` field |
| Rhino object, layer, material, light, named view, environment | RhinoDoc | GUID for objects; names (full path `Parent::Child` for layers) for the rest |

### Cordyceps-owned, process lifetime

| Entity | Fields | Bound | Lives in |
|---|---|---|---|
| **Snapshot** | `name` (string, unique key), `data` (binary `GH_Archive` of a whole document), `createdAtUtc`, `sizeBytes` | 20, oldest evicted first; evictions reported in the reply and the log | `Core/SnapshotStore.cs`, one static store per Rhino process |
| **Log entry** | `timestamp`, `level` (INFO/WARN/ERROR/DEBUG), `message` | 500, ring buffer | `Core/LogBuffer.cs` via `Core/DebugLog.cs` |
| **Document solve state** | `documentId`, `documentName`, solving since, depth, last finished solve, aborted | one per watched document | `Core/SolverState.cs` |
| **Server state** | `ServerState` (Stopped / Starting / Running / Stopping / Failed), port, start error, command count, last command, in-flight count | one per server (per port) | `McpServer.cs`, `Core/ServerState.cs` |
| **Edit transaction journal** | ordered recorded undo actions, added/removed objects | one per call; discarded at commit or rollback | `Core/Edits/` |

### Cordyceps-written files

| File | Where | Lifetime |
|---|---|---|
| Canvas and viewport captures (`.png`, `.jpg`, `.bmp`) | The caller's `path`, or `<temp>/Cordyceps/<prefix>_<timestamp>.<ext>` | **Never deleted by Cordyceps**; temp captures accumulate until the OS clears the temp directory |
| Saved definitions (`gh_document save`) | The caller's `path` | The user's file from then on |

## Relationships

- A Grasshopper document has many canvas objects; a group has many member objects, and an object
  may be in several groups. A component has ordered input and output ports.
- A snapshot is a copy of **one** document's content, but **the store is not keyed by document**:
  snapshot names are global across every open definition in the Rhino process (see Constraints).
- An edit transaction produces at most one undo record, on the document it changed.
- A server is owned by exactly one `Cordyceps` component; one server per port.

## State Machines

### Server lifecycle (`Core/ServerStateTransitions.cs`)

- `Stopped → Starting` on start; `Starting → Running` once the listener is bound; `Starting →
  Failed` on a bind or discovery error (with `StartError`).
- `Running → Stopping` on stop (port is freed before `Stop()` returns); `Stopping → Stopped` when
  in-flight handlers drain or after 2 s.
- `Failed` and `Stopped` may start again.

### Edit transaction (`Core/Edits/EditScope.cs`, `EditJournal.cs`)

- open (solver paused) → body runs, recording each change before making it → **commit** (one undo
  record pushed, solver restored, one solve scheduled) or **rollback** (recorded actions undone
  newest first; failures reported in `rollbackFailures`).
- A nested `Edit` joins the outer transaction rather than opening a second one.

### Solve (as reported in `solve.state`, `Core/SolvePolicy.cs`)

`completed | aborted | over_budget | not_started | solver_disabled | solutions_locked | not_watched |
document_closed`, plus `scheduled` for a solve an edit or `clear` started without waiting.

## Constraints

- **Ids resolve exactly or not at all.** A name that fits two things acts on nothing and the error
  lists the candidates. Removal actions never resolve a default nickname or the default group
  name `Group`.
- **Validate, then mutate.** Every input is resolved and validated before the first change; a parse
  failure is an error, never an empty collection (`.claude/rules/learnings/core.md`).
- **Snapshot names are unique per Rhino process.** Saving an existing name replaces it.
- **Known gap — snapshots are not tied to their document.** `revert` installs the named snapshot
  into whichever canvas tab is focused. A snapshot taken from definition A and reverted while the
  human has definition B focused replaces B's canvas content with A's. Nothing refuses or warns.
  Not yet tracked; raised with the owner on 2026-11-11.
- **Snapshots do not survive Rhino.** They are memory-only; a crash or restart loses all 20.

## Access Paths

Only two entities grow:

| Entity | Volume | Read | Found by |
|---|---|---|---|
| Snapshot | ≤ 20 per process; each is a whole document (can be MBs) | `revert`, `snapshots` | Dictionary by name; listing returns metadata only (`name`, `sizeBytes`, `createdAtUtc`), never `data` |
| Log entry | ≤ 500 | `gh_inspect(action='log')` | Ring buffer scan, newest last |

Heavy field: `Snapshot.data`. The store has a count cap, not a byte cap, so 20 snapshots of a large
definition can hold a lot of memory. A byte cap was considered and deferred (project-state,
2026-07-02: "revisit if document sizes make 20 snapshots too heavy").

Canvas reads (`outline`, `list`, `find`) read the whole document once per call into a
`CanvasSnapshot` (`Core/Canvas/CanvasReader.cs`) and resolve against that.

## Norm candidates (not ratified)

1. Cordyceps keeps no persisted format of its own; anything that must outlive Rhino is saved by
   the human or by the host's own file format.
2. Object, group, port and type names resolve exactly or fail with candidates — never a guess.
