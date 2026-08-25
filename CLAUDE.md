# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cordyceps is a Grasshopper plugin that exposes Grasshopper and Rhino functionality via the Model Context Protocol (MCP). It allows AI assistants to programmatically control Grasshopper (adding components, creating connections, configuring scripts) and Rhino (managing objects, layers, materials, rendering).

## Build Commands

```bash
# Build the plugin (Release configuration required)
dotnet build src/Cordyceps/Cordyceps.csproj -c Release

# The built .gha file is automatically copied to releases/ (gitignored - a build output)
```

The project targets .NET 8.0 and outputs a Grasshopper plugin (`.gha` file). Debug builds are blocked—always use `-c Release`.

## Architecture

### Core Components

**McpServer.cs** - HTTP/SSE server implementing the MCP protocol. Listens on a configurable port (default 26929), handles JSON-RPC requests, and manages SSE sessions for streaming responses. Discovers tools via reflection using `[McpServerToolType]` and `[McpServerTool]` attributes.

**CordycepsComponent.cs** - The Grasshopper component that users drop on the canvas. Manages the MCP server lifecycle - starts the server when placed, stops when removed. Outputs server status and last command received.

**Core/GrasshopperContext.cs** - Thread-safe wrapper for Grasshopper document access. All Grasshopper operations must run on the UI thread; this class provides `ExecuteOnUiThread()` methods that marshal calls correctly.

**Core/ComponentRegistry.cs** - Resolves component type names to actual Grasshopper components. Handles aliases (e.g., "python" -> "Python 3 Script") and supports creation by name or GUID.

**Core/UnifiedToolHelpers.cs** - Shared utilities for the unified tool architecture including action dispatch, help generation, and standardized response formatting.

**Core/ToolHelpers.cs** - Shared document/component utilities used across every tool: active-document and component resolution (by GUID or name), protected/infrastructure-id checks, cluster-safe recompute, component-info builders, JSON (de)serialization helpers, and `SuccessResponse`/`ErrorResponse` formatting.

**Core/DebugLog.cs** - Centralized logging with Info, Warn, Error, Debug levels. Logs are retrievable via `gh_inspect(action='log')`.

### User-Facing Documentation System

AI agents discover Cordyceps through a layered documentation system. All of these are user-facing and must be kept in sync with code changes:

**McpServer.cs `GetServerInstructions()`** - The first thing agents see on MCP initialize. Lists all tools with their actions, key points, and resource links. Must be updated when actions are added/removed/renamed.

**Knowledge/ (Embedded Guides)** - Markdown guides served as MCP resources (`gh://docs/*`, `gh://patterns/*`). Covers getting started, data trees, type system, best practices, component patterns, canvas layout, geometry orientation, rendering, common errors, and MCP testing. Registered in `Resources/ResourceRegistry.cs`.

**Tool Help Metadata (ActionInfo)** - Each tool class defines a `UnifiedToolInfo` with per-action metadata (description, required/optional params, example, tips). Accessed via `action='help'` on any tool. Must be updated when action signatures change.

**Resources/ResourceRegistry.cs** - Maps `gh://` URIs to Knowledge/ files and provides dynamic `gh://component/{name}` documentation. Update when adding new guides.

**Prompts/PromptRegistry.cs** - Workflow templates for multi-step operations (parametric geometry, debugging, script setup, optimization, planning). Update when workflows change.

### Tool Classes (in Tools/Unified/)

Each tool class is marked with `[McpServerToolType]` and contains a single method marked with `[McpServerTool]`. The method name is converted to snake_case for the MCP tool name (e.g., `GhCanvas` -> `gh_canvas`). Each tool uses an `action` parameter to dispatch to different operations.

**Grasshopper Tools (5):**
- **GhCanvasTool** (`gh_canvas`) - Components, values, groups: add, delete, move, find, search, list, bake, get/set values, group management, zoomable parameter management, per-parameter data modifiers (flatten/graft/simplify/reverse)
- **GhWireTool** (`gh_wire`) - Connect/disconnect components, bulk wiring, validate connections
- **GhDocumentTool** (`gh_document`) - Save, clear documents; snapshots; solver control; capture canvas/viewport
- **GhScriptTool** (`gh_script`) - Configure C#/Python script components
- **GhInspectTool** (`gh_inspect`) - Non-blocking connection/liveness probe, component status, trace data flow, retrieve debug output

**Rhino Tools (2):**
- **RhinoSceneTool** (`rhino_scene`) - Object management, selection, layers (full CRUD), visibility
- **RhinoRenderTool** (`rhino_render`) - Display modes, camera, render settings, materials, environments

### Adding or Modifying Tools

1. Create a method in an existing tool class (or create a new class with `[McpServerToolType]`)
2. Add `[McpServerTool]` attribute to the method
3. Add `[Description("...")]` attributes to the method and each parameter
4. All parameters should be primitive types (string, int, double, bool)
5. Return a JSON-serialized string with `success` field

Example:
```csharp
[McpServerTool, Description("Brief description of what this tool does")]
public string MyNewTool(
    [Description("Parameter description")] string param1,
    [Description("Optional param description")] int param2 = 0)
{
    return _context.ExecuteOnUiThread(() =>
    {
        // Implementation
        return JsonConvert.SerializeObject(new { success = true, ... });
    });
}
```

## Documentation Audit (MANDATORY)

**Every change to Cordyceps must include an audit of user-facing documentation.** AI agents only know what we tell them — if a feature isn't documented in the right places, it doesn't exist to users.

After any code change, check each of these and update as needed:

| What | File(s) | When to update |
|------|---------|----------------|
| **Tool help metadata** | `ActionInfo` in the tool class | Action added, removed, renamed, or params changed |
| **Server instructions** | `McpServer.cs` → `GetServerInstructions()` | Action added/removed, new tool, or key behavior change |
| **Knowledge base guides** | `src/Cordyceps/Knowledge/*.md` | New concepts, changed workflows, new error patterns |
| **Resource registry** | `Resources/ResourceRegistry.cs` | New guide added, URI scheme changed |
| **Prompt templates** | `Prompts/PromptRegistry.cs` | Workflow steps changed, tool names changed |
| **Common errors guide** | `Knowledge/CommonErrorsGuide.md` | New failure modes discovered or fixed |
| **CHANGELOG.md** | Root | Every user-visible change |

## Key Patterns

- All Grasshopper document access must go through `_context.ExecuteOnUiThread()`
- Tool methods return JSON strings (use Newtonsoft.Json for serialization)
- Component parameters can be referenced by name or index (0-based)
- Use `Core.DebugLog` for logging (Info, Warn, Error, Debug levels)

## Dependencies

- Grasshopper 8.0+ (Rhino 8)
- Newtonsoft.Json for JSON serialization
- .NET 8.0

## Branch model & Publishing

This repo uses **gitflow**: **`develop`** is the default/integration branch (features branch off it
and merge back via `/prawduct:pr`); **`main`** is the release surface and is **strict-protected**
(no direct pushes; a `develop → main` PR with the `build-test` check is required; no bypass).

Releases publish to **both** GitHub (`Release vX.Y.Z` commit + tag on `main`, and a published
**GitHub Release** with the `.gha` attached — that asset is the download the README links to) and
the **Yak** package manager. The `.gha` is a build output and is **not** tracked in git; `publish`
compiles it from the release commit and attaches it. Because `main` rejects direct pushes,
`scripts/release.sh` is a **two-step** flow around the release PR (branch protection guards
branches, not tags, so the publish step pushes only the `vX.Y.Z` tag). Don't run the yak/gh
commands by hand.

```bash
# Keep notes under a top `## [Unreleased]` in CHANGELOG.md; prep renames it. Then:

# 1) prep — on develop: bump version + CHANGELOG, build .gha, commit, push, open develop->main PR
git checkout develop && git pull
./scripts/release.sh prep            # auto-increment patch (e.g. 1.4.12 -> 1.4.13)
./scripts/release.sh prep 1.4.13     # or an explicit version

# 2) merge the develop->main release PR (merge commit, after build-test passes)

# 3) publish — on main: build/push yak, push the vX.Y.Z tag, create the GitHub Release
git checkout main && git pull
./scripts/release.sh publish 1.4.13

# --dry-run previews either step without changing anything.
```

Prerequisites (dotnet, Rhino 8 for the yak CLI, yak login, `gh` authenticated), the post-release
Prawduct bookkeeping (tag each shipped change-log entry `release=vX.Y.Z`, then
`prawduct-hook plan-backfill --apply`), and the full step-by-step flow are in
[`docs/release-process.md`](docs/release-process.md).

<!-- PRAWDUCT:ANCHOR — static governance pointer managed by the prawduct plugin. Keep it small and version-free: principles, methodology, and the active version live in the plugin and are injected at session start. -->

## Governance (Prawduct)

This repo is governed by **Prawduct**, installed as a Claude Code plugin — not as
committed framework files. The principles, methodology, Critic protocol, and PR
review live in the plugin and are read on demand (run `/prawduct:methodology`);
they are intentionally not copied into this repo.

**Before writing any code, STOP and read the build cycle: `/prawduct:building`.**
Skipping it is the #1 governance failure.

The hardest rules (everything else is in the plugin):

- **Tests are contracts** — fix the code, never weaken a test.
- **No "pre-existing" exception** — fix what you find, or flag why you can't.
- **Never silently drop a requirement** — say so explicitly.
- **Run `/prawduct:critic` after medium+ work** — never write Critic findings
  yourself; the independence is the value.

**Enforcement is structural:** the plugin's Stop hook runs at session end and
**blocks** if code changed against an active build plan with no Critic findings.
The session-start banner shows the active version and what changed — this anchor
stays version-free.
