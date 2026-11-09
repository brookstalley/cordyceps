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

## Running tests

Run tests with `testament run`, not `pytest` directly. It sends this worktree (uncommitted
and untracked files included; ignored files such as `.env` never leave the machine) to
testament's runners and streams the results back. If no coordinator answers, it runs the same
tests here under this machine's test-slot cap and says so.

If `testament` isn't installed: `uv tool install --python 3.14
git+ssh://git@github.com/brookstalley/testament@main` (or `git+https://…`), then
`testament guide client`. Until it is, run `pytest` as before. To set testament up here,
ask the person for their pool's console address and follow `<console>/public/docs/start`
(a pool too old to serve that page has `<console>/public/docs/client` instead).

    testament run --json -- tests/unit/test_foo.py -k bar   # any pytest selection
    testament run --json                                     # the project's default suite

Read the JSON on stdout:

- `exit_code`: 0 all passed; 1 tests failed (a real red: fix it); 5 nothing collected
  (an existing selection; a path that does not exist is 2, `path_not_found`);
  2 anything else, which is **not** a test result. Read `error.code`; when `error.retryable` is
  true, run again.
- `report.items[]`: one per test, with `outcome` (`passed`, `failed`, `error`, `skipped`),
  `message` (pytest's failure text for a failed or errored test, or why a skipped test
  skipped) and `runner`.
- `report.missing`: tests that were requested but never reported. A non-empty list is exit 2
  (`population_mismatch`), never a pass. When every missing test is also in `report.given_up`
  (the pool gave it up after the batches holding it were lost or crashed three times: runners
  dropped, or pytest died), the code is `batches_lost` instead, and it is retryable.
- `report.counts`: how many tests ended each way, over the whole run.
- `fallback`: present when the tests ran on this machine instead of the runners. Flags
  that need one local pytest process (`-x`, `--maxfail`, `-n`, `--lf`) always run here;
  `fallback.reason` names the flag. Flags that need a terminal (`--pdb`, `-s`, `--co`) are
  refused under `--json`.
- `setup`: present when testament isn't fully set up for this repo or machine. The tests
  still ran; tell the person, and point them to the `guide` command it names.
- `report.advice[]`: what the run advises, none of it a test result. Kind `cache` (also
  `report.cache.advice`): what keeps the most tests from ever being reused, each cause with
  its `share` of the run and a one-line `fix`. Kind `slow_test`: one test that delayed the
  run, with its `delay_s`; split or parametrize it, or make it cheaper. Kind `slow_batch`:
  the batch that ended the run late, with its `cause` (`group`, `slow_test`, `pool` or none);
  for a group, give its tests finer group keys. Kind `upgrade`: the pool runs a newer
  testament than this machine (`coordinator_version`, `client_version`); run its `command`,
  which replaces only the testament tool, and the next run uses it. Every entry has an `id`
  (its kind and eight hex digits, stable while what it names is unchanged), a `severity` and
  a one-line `headline`; entries are ordered `upgrade`, then the other important ones by
  delay, then `info` (cache, slow tests, slow batch). `important` is every `upgrade`, a
  `slow_test` that delayed the run over 30 s, a `slow_batch` that did, from a group, and the `cache`
  advice when at least a quarter of a full run's tests are uncacheable and not declined; a batch slow from a slow test is `info` (its test's entry carries it), and one slow from the
  pool is no advice at all, only a figure (the pool's operator hears of it). Important entries are the last lines of the run's output (stderr
  under `--json`), at most three, as `testament ADVICE [<id>]: <headline>`, then `testament
  ADVICE: <n> more in report.advice`; they print on every run while they hold, because a
  wrapper's log tail is often all that gets read. When you see one, run the upgrade, or fix
  the test, group or cache cause it names when that is your task, or tell the person. The rest (`info`,
  smaller cache causes included) appears about once a day per project, and again at once when it
  names something new; `report.advice_state` is `throttled` on the runs in between. When it's
  non-empty, tell the person, and act on it only when that is your task, except `upgrade`,
  which you run. `--advice always` (or `TESTAMENT_ADVICE=always`) shows every entry on every
  run while you work through fixes, and `never` hides all of it. The same text goes to stderr
  for whoever watches.
- `report.durations`: every run, the shape of its test time (mean, stdev, median, p90,
  p99, max, the five longest and their share) for its tests, files and groups, and how
  parallel it ran. `report.slow_tests` and `report.slow_batches` are the figures behind the
  slow-test and slow-batch advice, on every run and never throttled: how much later the run
  ended because of each (`delay_s`), at most five tests and one batch. `report.long_poles`
  is the older long-pole figure, kept for older clients.

Progress (request accepted, tests collected, each failure) arrives on stderr as the run
goes; stdout is the envelope alone. A run that ends `upgrade_required` with a message
naming an install command comes from a testament too old to have `upgrade` advice,
which the pool no longer serves: run that command, then run again (one that names
none means the coordinator is the older side: tell the person). Keep one envelope per run, in a file in a temp
directory that you age out, so a failure can be re-read without running again. When you
need only the verdict and the tests that didn't pass, run `testament run --json=summary`:
the same envelope, but `report.items` lists only the tests that failed, errored, skipped or
never reported, each with its `outcome` and `message` (a skip is not a failure: read
`outcome`; `report.detail` is `"summary"`); `report.counts`, `report.missing` and
`error.ran` still describe the whole run.

A test that fails and then passes when you run it again may be flaky, not fixed. Before
deciding, `testament history '<test id>' --json` shows its past runs here and on the
pool, newest first, each with its outcome, runner, commit and failure text, and how often
it flipped on one tree. `testament report --json` ranks the project's slowest and flakiest
tests and those failing only on some runners.

`testament status` shows the runners and what's queued.

**In this repo** the tests are xUnit, not pytest: `.testament.toml` runs them as one `files`
suite (`dotnet`) whose single test "file" is `src/Cordyceps.Tests/Cordyceps.Tests.csproj`, in
the .NET 8 SDK image at `.devcontainer/Dockerfile`. A bare `testament run --json` is the whole
suite; pytest-style selections above do not apply. Where the block says "run `pytest` as
before", the local equivalent here is
`dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release`.

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

## Backlog — GitHub Issues, not a file

**There is no `.prawduct/backlog.md`.** The backlog cut over to **GitHub Issues on
`brookstalley/cordyceps`** on 2026-08-29 and the source markdown was deleted; if you
are looking for that file, this section is the answer. Use `/prawduct:backlog`, which
routes to the service automatically — the plugin owns the item shape, the labels, and
the commands, and this repo does not restate them.

**This makes the backlog an external dependency.** Reading or filing needs network
access plus an authenticated `gh` (`gh auth status`); there is no offline copy, and a
fresh clone carries no backlog content until the cache is built. A read that fails with
an auth or network error is telling you about your environment, not about the backlog.

**`PFX-XXXX` ids still work and still resolve.** Every item kept its hand-minted id as
an `id:PFX` alias, so citations like `RSC-2H9K` in `docs/place-image-action.md` and
`REL-6H4X` in `docs/release-process.md` still resolve. Pass `--repo brookstalley/cordyceps`
when resolving a bare id — the hook cannot infer the target repo from the id alone.
PFX ids are the durable handle for the **migrated corpus only** — `backlog file` mints
no PFX, so items filed after the cutover (such as #76) are cited by issue number.

The pre-cutover markdown is recoverable from git history. The deletion commit does not
contain the file, so read it from that commit's parent:

```bash
git show "$(git rev-list -1 HEAD -- .prawduct/backlog.md)^:.prawduct/backlog.md"
```

The migration's target, scope, accepted costs, the plugin build that ran it, and the
full `PFX -> issue` mapping are recorded in
[`.prawduct/artifacts/migration-scrub-decisions.md`](.prawduct/artifacts/migration-scrub-decisions.md).
