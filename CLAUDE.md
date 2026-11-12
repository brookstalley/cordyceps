# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cordyceps is a Grasshopper plugin that exposes Grasshopper and Rhino functionality via the Model Context Protocol (MCP). It allows AI assistants to programmatically control Grasshopper (adding components, creating connections, configuring scripts) and Rhino (managing objects, layers, materials, rendering).

Where things live — the server, the canvas model, the resolvers, the seven tool classes and the
documentation layers agents read — is in [`docs/architecture.md`](docs/architecture.md).

## Build Commands

```bash
# Build the plugin (Release configuration required)
dotnet build src/Cordyceps/Cordyceps.csproj -c Release

# The built .gha file is automatically copied to releases/ (gitignored - a build output)
```

The project targets .NET 8.0 (Grasshopper 8.0+ / Rhino 8, Newtonsoft.Json) and outputs a Grasshopper plugin (`.gha` file). Debug builds are blocked—always use `-c Release`.

## Running tests

Run tests with `testament run --json` (the whole xUnit suite, on the testament pool; it falls back
to running here when no coordinator answers). Before ending work, record evidence with
`prawduct-hook test-evidence record`, which is what the governance gates read. Without testament:
`dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release`. How to read testament's
output, and what differs from its pytest-shaped instructions: read `docs/testing.md` when you need them.

## Rules for code changes

- Grasshopper is touched only on the UI thread. A change to canvas content (objects, wires,
  values) goes through `_context.Edit(doc, EditReplies.RecordName(tool, action), tx => …)`: one
  undo step, rolled back on a throw. Validate every input before it opens, record each change
  (`tx.Record*`) before making it, add or remove objects with `tx.AddObject`/`tx.RemoveObject`
  (rules: `Core/Edits/EditTransaction.cs`). Reads use `_context.ExecuteOnUiThread()`, and so, by
  design, do the solver switch (`solver`; `Edit` restores it at commit) and `revert`.
- Component-type names resolve in `Core/Naming/TypeNameRule.cs` (never a partial match). Change
  resolution or aliases there, not in the `ComponentRegistry` adapter.
- Object and group ids resolve in `Core/Canvas/ObjectResolver.cs`. Change name resolution there,
  not at a call site.
- New decision logic goes in a host-free `Core/` file linked into `Cordyceps.Tests`, with tests.
- Tool methods return JSON strings with a `success` field (Newtonsoft.Json); log with
  `Core.DebugLog`. Component parameters can be referenced by name or 0-based index.

### Adding or modifying tools

1. Create a method in an existing tool class (or create a new class with `[McpServerToolType]`)
2. Add `[McpServerTool]` to the method and `[Description("...")]` to the method and each parameter
3. All parameters should be primitive types (string, int, double, bool)
4. Return a JSON-serialized string with `success` field; a canvas change runs inside `_context.Edit`

```csharp
[McpServerTool, Description("Brief description of what this tool does")]
public string MyNewTool(
    [Description("Object: GUID, 8+ hex prefix, or unique nickname")] string id,
    [Description("Optional param description")] string nickname = null)
{
    return _context.ExecuteOnUiThread(() =>
    {
        // Resolve and validate first: a refusal returns before anything changes.
        if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var obj, out var error))
            return ToolHelpers.ErrorResponse(error);
        return _context.Edit(doc, EditReplies.RecordName("my_tool", "rename"), tx =>
        {
            tx.RecordNickName(obj);   // record, then mutate
            obj.NickName = nickname;
            return JsonConvert.SerializeObject(new { success = true, ... });
        });
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

## Branch model & Publishing

**gitflow**: `develop` is the default/integration branch (features merge back via `/prawduct:pr`);
`main` is the release surface and is strict-protected (no direct pushes; a `develop → main` PR with
the `build-test` check; no bypass). Releases go to GitHub (tag + Release with the `.gha`) and Yak
through `scripts/release.sh` in two steps around the release PR. Don't run the yak/gh commands by hand.

```bash
# Notes go under a top `## [Unreleased]` in CHANGELOG.md; prep renames it.
git checkout develop && git pull
./scripts/release.sh prep [X.Y.Z]    # bump, build, commit, push, open develop->main PR
# merge the release PR (merge commit, after build-test passes), then:
git checkout main && git pull
./scripts/release.sh publish X.Y.Z    # yak push, vX.Y.Z tag, GitHub Release
# --dry-run previews either step.
```

Prerequisites, the post-release Prawduct bookkeeping (`prawduct-hook cut-change-log --release vX.Y.Z`,
then `prawduct-hook plan-backfill --apply`) and the full flow are in [`docs/release-process.md`](docs/release-process.md).

@AGENTS.md

## Backlog — GitHub Issues, not a file

**There is no `.prawduct/backlog.md`.** The backlog lives in **GitHub Issues on
`brookstalley/cordyceps`** (cut over 2026-08-29). Use `/prawduct:backlog`, which routes there;
the plugin owns the item shape, labels and commands. Reading or filing needs network access and
an authenticated `gh` (`gh auth status`); there is no offline copy, and a fresh clone has no
backlog content until the cache is built. An auth or network error is about your environment, not
the backlog.

Migrated items kept their `PFX-XXXX` ids as `id:PFX` aliases, so citations like `RSC-2H9K` still
resolve (pass `--repo brookstalley/cordyceps` with a bare id). Items filed after the cutover are
cited by issue number. The pre-cutover file is recoverable with
`git show "$(git rev-list -1 HEAD -- .prawduct/backlog.md)^:.prawduct/backlog.md"`; the mapping
and the migration's decisions are in
[`.prawduct/artifacts/migration-scrub-decisions.md`](.prawduct/artifacts/migration-scrub-decisions.md).
