# Cordyceps architecture

How the plugin is put together. The rules that bind changes to this code are in `CLAUDE.md`; this
file describes where things live.

## Core components

**McpServer.cs** - HTTP/SSE server implementing the MCP protocol. Listens on a configurable port
(default 26929), handles JSON-RPC requests, and manages SSE sessions for streaming responses.
Discovers tools via reflection using `[McpServerToolType]` and `[McpServerTool]` attributes.

**CordycepsComponent.cs** - The Grasshopper component that users drop on the canvas. Manages the
MCP server lifecycle - starts the server when placed, stops when removed. Outputs server status and
last command received.

**Core/GrasshopperContext.cs** - Thread-safe wrapper for Grasshopper document access. All
Grasshopper operations must run on the UI thread; this class provides `ExecuteOnUiThread()` methods
that marshal calls correctly, and `Edit(doc, name, tx => …)`, the one entry point for a call that
changes a document. Reads use bare `ExecuteOnUiThread`; mutations never do.

**Core/Edits/** - The edit layer: every Cordyceps call that changes a Grasshopper document runs as
one `EditTransaction`, so it is one Grasshopper undo step a human can Ctrl+Z, and a call that
throws partway is rolled back. The body records each change before making it (`tx.Record*`, the
per-kind Grasshopper undo actions in `UndoEvents.cs`) and adds or removes objects through
`tx.AddObject`/`tx.RemoveObject`. While it runs, the document's solver is paused. On commit the
transaction pushes one `GH_UndoRecord` named `Cordyceps: <tool> <action>` and hands the changed
objects to `SolveCoordinator` for one scheduled solve; on a throw it undoes the recorded actions
newest first and reports any it could not. The ordering rules (record before mutate, track before
add, one record per change and object, rollback past a failed step) live host-free in
`EditJournal.cs`; joining a nested edit, refused versus unexpected rollback, and when a reply gains
`undo`, `solve` or `warning` are decided host-free in `EditScope.cs`; the reply shapes are in
`EditReplies.cs`. All three are linked into the tests.

**Core/Naming/TypeNameRule.cs** + **Core/ComponentRegistry.cs** - The rule that turns a
component-type name into one type (GUID, exact aliases such as "python", pinned built-ins, exact
name or `Category/Name`; never a partial match) lives host-free in `Core/Naming/TypeNameRule.cs`,
with "did you mean" helpers in `Core/Naming/NameSuggestions.cs`. `ComponentRegistry` is only the
Grasshopper adapter: it builds the installed-type catalog the rule reads, creates the resolved
type, and runs `search`.

**Core/Canvas/** - The host-free canvas model. `CanvasReader.cs` is the only Grasshopper adapter:
it reads the document once into a `CanvasSnapshot` that outline and the id lookups read.
`ObjectResolver.cs` is the one resolver for object and group ids (GUID, 8+ hex prefix, unique
case-sensitive nickname; group names in the Groups scope). `Core/DataTrees/` holds the
`gh_inspect(action='tree')` summaries.

**Core/UnifiedToolHelpers.cs** - Shared utilities for the unified tool architecture including
action dispatch, help generation, and standardized response formatting.

**Core/ToolHelpers.cs** - Shared document/component utilities used across every tool:
active-document and component resolution, protected/infrastructure-id checks,
component-info builders, JSON (de)serialization helpers, and
`SuccessResponse`/`ErrorResponse` formatting.

**Core/SolveCoordinator.cs** - The one place Cordyceps starts a Grasshopper solve, including the
solve an edit schedules when it commits. It expires
(cluster-safe) and calls `ScheduleSolution` inside a short UI call, never `NewSolution`, so no call
holds the document lock while Grasshopper solves. A caller that needs the outcome waits off the UI
thread on a ticket (document id + solve generation) through host-free `Core/SolveWait.cs`;
host-free `Core/SolvePolicy.cs` maps the solver switches and the wait outcome to the reported
`solve.state` and its remedy. Per-document solve state (solving, last finished solve, aborted) is
kept by `Core/SolverState.cs`, fed from Grasshopper's solution events by `Core/SolutionWatcher.cs`, which also watches a scheduled
document the document server does not know (a reverted snapshot), bounded by host-free
`Core/WatchAdoption.cs`.

**Core/DebugLog.cs** - Centralized logging with Info, Warn, Error, Debug levels. Logs are
retrievable via `gh_inspect(action='log')`.

## Tool classes (`Tools/Unified/`)

Each tool class is marked with `[McpServerToolType]` and contains a single method marked with
`[McpServerTool]`. The method name is converted to snake_case for the MCP tool name (e.g.,
`GhCanvas` -> `gh_canvas`). Each tool uses an `action` parameter to dispatch to different
operations.

Grasshopper tools:
- **GhCanvasTool** (`gh_canvas`) - Components, values, groups: add, delete, move, find, search,
  list, outline (compact read of the whole definition), bake, get/set values, group management,
  zoomable parameter management, per-parameter data modifiers (flatten/graft/simplify/reverse)
- **GhWireTool** (`gh_wire`) - Connect/disconnect components, bulk wiring, validate connections
- **GhDocumentTool** (`gh_document`) - Save, clear documents; snapshots; solver control; capture
  canvas/viewport
- **GhScriptTool** (`gh_script`) - Configure C#/Python script components
- **GhInspectTool** (`gh_inspect`) - Non-blocking connection/liveness probe, component status,
  data-tree shape (`tree`), trace data flow, retrieve debug output

Rhino tools:
- **RhinoSceneTool** (`rhino_scene`) - Object management, selection, layers (full CRUD),
  visibility
- **RhinoRenderTool** (`rhino_render`) - Display modes, camera and named views, render settings,
  materials, environments, lights, ground plane, sun and skylight

## The user-facing documentation system

AI agents discover Cordyceps through layers, all user-facing (the audit that keeps them in sync is
in `CLAUDE.md`):

- **`McpServer.cs` `GetServerInstructions()`** - the first thing agents see on MCP initialize.
  Lists all tools with their actions, key points, and resource links.
- **`Knowledge/`** - Markdown guides served as MCP resources (`gh://docs/*`, `gh://patterns/*`):
  getting started, data trees, type system, best practices, component patterns, canvas layout,
  geometry orientation, rendering, common errors, MCP testing.
- **Tool help metadata (`ActionInfo`)** - each tool class defines a `UnifiedToolInfo` with
  per-action metadata (description, required/optional params, example, tips), served by
  `action='help'`.
- **`Resources/ResourceRegistry.cs`** - maps `gh://` URIs to Knowledge/ files and provides dynamic
  `gh://component/{name}` documentation.
- **`Prompts/PromptRegistry.cs`** - workflow templates for multi-step operations (parametric
  geometry, debugging, script setup, optimization, planning).
