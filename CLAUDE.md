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

- All Grasshopper document access goes through `_context.ExecuteOnUiThread()`; Grasshopper must be
  touched on the UI thread.
- Component-type names resolve in `Core/Naming/TypeNameRule.cs` (never a partial match). Change
  resolution or aliases there, not in the `ComponentRegistry` adapter.
- Object and group ids resolve in `Core/Canvas/ObjectResolver.cs`. Change name resolution there,
  not at a call site.
- Decision logic goes in a host-free `Core/` file linked into `Cordyceps.Tests`, with tests.
- Tool methods return JSON strings with a `success` field (Newtonsoft.Json); log with
  `Core.DebugLog`. Component parameters can be referenced by name or 0-based index.

### Adding or modifying tools

1. Create a method in an existing tool class (or create a new class with `[McpServerToolType]`)
2. Add `[McpServerTool]` attribute to the method
3. Add `[Description("...")]` attributes to the method and each parameter
4. All parameters should be primitive types (string, int, double, bool)
5. Return a JSON-serialized string with `success` field

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

**There is no `.prawduct/backlog.md`.** The backlog lives in **GitHub Issues on
`brookstalley/cordyceps`** (cut over 2026-08-29). Use `/prawduct:backlog`, which routes there;
the plugin owns the item shape, labels and commands. Reading or filing needs network access and
an authenticated `gh` (`gh auth status`); an auth or network error is about your environment, not
the backlog.

Migrated items kept their `PFX-XXXX` ids as `id:PFX` aliases, so citations like `RSC-2H9K` still
resolve (pass `--repo brookstalley/cordyceps` with a bare id). Items filed after the cutover are
cited by issue number. The pre-cutover file is recoverable with
`git show "$(git rev-list -1 HEAD -- .prawduct/backlog.md)^:.prawduct/backlog.md"`; the mapping
and the migration's decisions are in
[`.prawduct/artifacts/migration-scrub-decisions.md`](.prawduct/artifacts/migration-scrub-decisions.md).
