---
artifact: api-contract
version: 1
depends_on:
  - artifact: data-model
  - artifact: security-model
last_validated: 2026-11-11
---

# API Contract

The contract every Cordyceps user depends on: the MCP tools, their actions and parameters, and the
reply shape. Producers, consumers and the investigation a change owes are in
`boundary-patterns.md` → MCP Tool / Action Contract.

<!-- OPTIONAL norm home. One owner norm already governs this surface and lives in
     project-preferences.md (few tools, many actions; owner, 2026-11-09). Other candidates are at
     the end and bind only once ratified. -->

## Overview & Surface Type

- **Surface:** a network service, local only: MCP over JSON-RPC 2.0, protocol version
  `2025-06-18`, Streamable HTTP in stateless mode at `POST http://127.0.0.1:<port>/mcp` (default
  port 26929). JSON responses only; no SSE stream. Also `GET /health` (status JSON) for humans and
  scripts.
- **Consumers:** external. AI agents in MCP clients (Claude Desktop, Claude Code, Cursor, VS Code)
  and developers calling the tools from their own scripts with no AI in the loop.
- **Where the contract lives:** there is no static schema file. The canonical contract is what the
  server returns at runtime: `tools/list` (built by reflection from the `[McpServerTool]` methods
  and their `[Description]`s), `initialize`'s `instructions` (`McpServer.GetServerInstructions()`),
  and each tool's `action='help'` (`ActionInfo` in the tool class). The tool names are pinned by a
  test in `Cordyceps.Tests`.

## Operations

| MCP method | Purpose |
|---|---|
| `initialize` | Server info (`name`, `version` with pre-release tag), capabilities, the server instructions |
| `tools/list`, `tools/call` | The seven tools below |
| `resources/list`, `resources/read` | `gh://docs/*` guides, `gh://patterns/*`, and generated `gh://component/{name}` docs |
| `prompts/list`, `prompts/get` | Workflow templates (`create_parametric_geometry`, `debug_data_mismatch`, `setup_script_component`, `optimize_canvas`, `plan_definition`, …) |
| `ping`, `notifications/initialized` | Protocol housekeeping |

**Tools.** Each tool is one method taking an `action` and dispatching on it; `action='help'` on any
tool returns its per-action metadata. The current action lists are in the server instructions and
each tool's `[Description]`, and are not repeated here so they cannot drift:

| Tool | Domain | Mutates? |
|---|---|---|
| `gh_canvas` | Components, values, groups, layout, ports, bake | Yes (one undo step per call); reads: `find`, `search`, `list`, `outline`, `info`, `bounds`, `validate`, `get` |
| `gh_wire` | Wires | `connect`, `disconnect`, `clear`; reads: `list`, `validate` |
| `gh_script` | C# and Python 3 script components | `set`, `configure` (rebuild and report); reads: `get`, `info` |
| `gh_document` | Save, clear, solver, recompute, snapshots, captures | `clear`, `revert` (replaces the document), `save` (writes a file), `solver`; `undo`/`redo` are refused for agents |
| `gh_inspect` | Status, data flow, trees, log, liveness | Read-only. `connection` never touches the UI thread |
| `rhino_scene` | Rhino objects, layers, images, command scripts | Yes (Rhino's own undo, not Cordyceps') |
| `rhino_render` | Display, camera, views, lights, materials, environments | Yes |

Idempotence: reads are safe to repeat. Edits are not idempotent in general (`add` adds another),
with documented exceptions (`place_image replace=true`; a `gh_script set` of unchanged code records
nothing).

## Inputs & Outputs

**Inputs.** Every parameter is a primitive (string, integer, number, boolean). Collections are
passed as a **JSON array inside a string** (`ids='["a1b2c3d4","Slider"]'`). Argument names are
exact and case-sensitive; an argument the tool does not declare is refused before the tool runs,
naming the closest real one (`Core/ToolArgumentBinder.cs`). Names of canvas objects, groups,
ports and component types follow `data-model.md` → Entities: exact match or a refusal with
candidates, never a guess.

**Outputs.** A `tools/call` result is one text content item holding a JSON object:

```json
{ "success": true, "...action fields...": "...",
  "undo": "Cordyceps: gh_canvas add",
  "status": { "document": "bridge.gh", "ui": "responsive", "solving": false } }
```

- `success` (boolean) is always present. MCP `isError` is set exactly when `success` is `false`.
- `undo` names the Grasshopper undo step a canvas-changing call recorded; absent when nothing
  changed.
- `solve` appears on calls that wait for, or were refused, a solve:
  `{state, elapsedMs, solveMs, errors, warnings, remedy}`.
- `status` is added to **every** reply, failures included, by one choke point
  (`McpServer.WithStatus`): `document`, `ui`, `solving`, plus `solving_since`, `modal_inferred`,
  `solving_document`, `hint` when something is off.
- Per-id actions return per-id results, and `success:false` overall when any id failed.
- There is no pagination. Long listings are capped where they are (`outline` problems at 10,
  `tree` at 50 ids per call); `status` is not capped.

## Error Model

Recorded in `project-state.yaml` → `design_decisions.api_error_model_approach`. As built:

| Layer | When | Shape |
|---|---|---|
| HTTP | Bad origin (403), wrong `Accept` (406), no `Content-Length` (411), body > 10 MB (413), unparseable request (400), wrong method on `/mcp` (405) | Status code, no body |
| JSON-RPC error | Unknown method, or `tools/call` naming a tool that does not exist | `{"error": {"code": -32603, "message": "..."}}` |
| Tool failure | Anything that goes wrong for a known tool: bad arguments, refusals, host errors, exceptions | A normal result with `isError: true` and `{"success": false, "error": "<sentence>", ...}` |

Tool failures are never thrown across the boundary (`project-preferences.md` → Architecture
Patterns). Exceptions become `{success:false, error: <message>}` with the message only; the stack
trace goes to the debug log, never the reply.

Structured failure fields that clients can rely on: `rolledBack` (boolean) and `rollbackFailures`
(`[{step, kind, id, error}]`) on a failed edit; `notFound` (array) on bulk Rhino id actions;
per-id result entries on bulk canvas actions; `solve.state` and `solve.remedy`.

**Gap: no stable error codes.** `error` is an English sentence written for the agent to read, and it
is good at that: it names the bad input, the candidates and the fix. A script cannot branch on it
without matching text, which breaks whenever the wording improves. Options: add a short
machine-readable `code` beside `error` for the refusals scripts most often handle (ambiguous name,
not found, busy, invalid argument), additive and non-breaking; or record "the error is prose and
scripts match on structured fields only" as the decision. This needs an owner call.

## Versioning

**Deferred (owner, 2026-11-11):** *"Deferral is OK. Our API is tightly coupled from client to server
to MCP, and version mismatches are unlikely."* Mirrored in `project-state.yaml` →
`design_decisions.api_versioning_approach`.

- **Mechanism today:** none on the wire. The surface is versioned only through the package (semver
  `X.Y.Z` on the `.gha`, reported as `serverInfo.version` on `initialize`). The schema, the
  instructions and the server always ship together in one `.gha`, and clients re-read them on
  every connect, so a client never calls a schema older than the server it is talking to.
- **Where skew still bites:** saved agent prompts, scripts written against an older release, and a
  model's memory of a tool surface it saw in training or in an earlier session.
- **Revisit trigger:** the first rename or removal of a tool, action or parameter that existing
  prompts or saved scripts would call; or the first consumer that pins a recorded schema instead of
  reading it live.

## Deprecation & Compatibility

What the project actually does, from the CHANGELOG:

- New capability is **a new action on an existing tool**, never a new tool, unless the owner rules
  otherwise (`project-preferences.md`, owner norm 2026-11-09). Adding an action or an optional
  parameter is non-breaking.
- **Breaking changes ship in ordinary releases**, including patch and minor versions, each flagged
  `**Breaking:**` in `CHANGELOG.md` with what changed and what to call instead (the Unreleased
  section has 25 at the time of writing). There is no deprecation window and no in-band
  deprecation warning.
- Most breaking changes so far made the server **stricter** (refuse what it used to guess at),
  which breaks callers that relied on the guess, not callers using the documented form.
- `Core/DeprecationRegistry.cs` tracks deprecated **Grasshopper components** (`IGH_UpgradeObject`),
  so `search` and `add` can steer agents to the replacement. It is not a deprecation mechanism for
  Cordyceps' own tools or actions.

Retention: none — breaking changes ship in any release, each flagged `**Breaking:**` in CHANGELOG.md.

## Surface Inventory & Stability Tiers

The tool level is the declared surface; actions and parameters within each tool are listed live by
`action='help'`. The `stable` tier means a tool is not renamed or removed without an owner decision
and a `**Breaking:**` entry; with `Retention: none` above, it does not promise a deprecation window.

- `gh_canvas` — stable
- `gh_wire` — stable
- `gh_script` — stable
- `gh_document` — stable
- `gh_inspect` — stable
- `rhino_scene` — stable
- `rhino_render` — stable

Not a public contract: the text of `error` messages, the exact wording of `hint` and `remedy`, the
`/health` JSON (a debugging aid), and the debug log format.

## Conventions

- Tool names are snake_case, derived from the method name (`GhCanvas` → `gh_canvas`,
  `Core/McpNaming.cs`). Action names are lower snake_case. Parameter names are camelCase.
- Ids are Grasshopper GUIDs (or 8+ hex prefixes, or unique nicknames), opaque to the caller.
- Canvas coordinates are Grasshopper canvas pixels; geometry uses Rhino model units.
- Timeouts are seconds (`timeout`); reported durations are milliseconds (`elapsedMs`, `solveMs`).
- An omitted optional parameter takes its declared default; a missing required one is refused
  (`Missing required parameter: <name>`). On output, optional fields are omitted when they do not
  apply.

## Security

Authentication and trust are in `security-model.md`. At this boundary specifically: arguments are
validated before any change (`.claude/rules/learnings/core.md`, validate-then-mutate); undeclared
arguments are refused; request bodies are capped at 10 MB; error replies carry the message only,
never a stack trace. Two actions execute code by design (`gh_script set`/`configure`,
`rhino_scene script`); the security model explains why that is acceptable and where it is not.

## Conditional Patterns

- **Long-running work:** a solve is started and waited for with a caller-chosen `timeout`
  (1–300 s). Past it, the reply says `over_budget` and the solve keeps running; the caller polls
  `gh_inspect(action='connection')` until `solving` is false. Nothing is held open longer.
- **Concurrency:** calls may arrive concurrently; document work is serialized on the Rhino UI
  thread, and a caller waiting more than 120 s for it gets a busy error. There are no conditional
  writes (no ETag); a human editing the same canvas is the expected concurrent writer.
- **Correlation:** the JSON-RPC `id` is echoed exactly (including `"id": null`). No separate request
  id.

## Norm candidates (not ratified)

1. Every tool reply is a JSON object with a boolean `success`, and every reply carries `status`.
2. Tool failures are results, never protocol errors; the reply never carries a stack trace.
3. A change to a tool, action, parameter or reply field updates the server instructions,
   `action='help'`, the knowledge guides and the CHANGELOG in the same change (already a
   preference row: the Documentation Audit).
