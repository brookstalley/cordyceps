# Project Preferences

Developer preferences for how code is written in this project. Captured during discovery, updated as preferences evolve. Every session should read this before writing code.

## Language & Runtime

- **Language**: C#
- **Version**: .NET 8.0; targets Rhino 8.21+ / Grasshopper 8 (outputs a `.gha` plugin)
- **Package manager**: NuGet (via `dotnet` / MSBuild)

## Code Style

- **Naming**: PascalCase for types/methods/public members, camelCase for locals/params, `_camelCase` for private fields (standard C# conventions). MCP tool method names are auto-converted to snake_case for the tool name (`GhCanvas` → `gh_canvas`).
- **Formatting**: default C#/.NET conventions (4-space indent); no enforced formatter configured
- **Linting**: none beyond the C# compiler / built-in analyzers
- **Type annotations**: explicit C# types; nullable reference types not enabled project-wide
- **Imports**: `using` directives at top of file

## Testing

- **Framework**: xUnit (`src/Cordyceps.Tests/`, net8.0)
- **Style**: `[Fact]` / `[Theory]` with `[InlineData]` for table-driven cases; descriptive method names
- **Coverage expectations**: host-independent logic (type marshaling, conversions, schema mapping) is unit-tested; document-touching behavior is verified live in Rhino — the Grasshopper host cannot be exercised off the UI thread in a unit test
- **Testing strategies**: table-driven via `[Theory]`/`[InlineData]`; no property-based testing
- **Test location**: separate project `src/Cordyceps.Tests/`
- **Parallelization**: xUnit default

## Architecture Patterns

- **Data modeling**: anonymous objects serialized with Newtonsoft.Json; every tool method returns a JSON string with a `success` field
- **Error handling**: catch at the tool boundary and return `{ success: false, error }` JSON rather than throwing across the MCP boundary; runtime issues surface via `gh_inspect`
- **Async**: synchronous tool methods; all document work is marshaled onto the Rhino UI thread via `GrasshopperContext.ExecuteOnUiThread()`
- **File organization**: layer/area folders — `Core/`, `Tools/Unified/`, `Resources/`, `Prompts/`, `Knowledge/`

## Tooling

- **Key libraries**: Newtonsoft.Json (SDK-compatible JSON serialization); Grasshopper 8 SDK / RhinoCommon (host APIs); System.Text.Json used only in type-conversion code and tests
- **Dev commands**:
  - Build (Release required — Debug is blocked): `dotnet build src/Cordyceps/Cordyceps.csproj -c Release`
  - Test: `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj`
  - Publish: `yak build` / `yak push` from `dist/` (see CLAUDE.md → Publishing)

## Workflow

- **Branching**: feature-branches (default: feature-branches — create a branch for medium+ work, direct commits to protected branches only for trivial fixes; set to "direct" for solo projects where committing to main is OK)
- **Protected branches**: main (branches that should not receive direct commits unless branching is "direct")
- **PR creation**: wait_for_user (default: wait_for_user — only create PRs when explicitly asked; set to "automatic" to create PRs after Critic review passes)
- **PR merge**: wait_for_user (default: wait_for_user — present the PR for user review before merging; set to "automatic" to merge after CI passes and review is clean)
- **Commit attribution**: none (default: none — no `Co-Authored-By`, `Signed-off-by`, or "Generated with …" trailers on commits or PR bodies; set to "co-authored" to add a Claude `Co-Authored-By` trailer)

---

**What belongs here**: How you want code written. Conventions, tools, style preferences, workflow preferences.

**What doesn't belong here**: What to build (product-brief), system design (data-model, architecture), performance targets (nonfunctional-requirements), or deployment (operational-spec).

## Enforcement

Each preference above should be enforced by one of three mechanisms — assign the mechanism when you add the preference so it doesn't quietly become aspirational.

| Mechanism | Where it lives | What it catches | Trade-off |
|---|---|---|---|
| **Linter** | Project's configured linter (ruff, eslint, swiftlint, etc.) | Mechanical style/naming rules | Best tool when configured. If no linter, preferences in this category fall through to Critic. |
| **Test** | `tests/preferences/test_*.py` (or equivalent) | Structural rules with named exceptions (AST checks, config-presence checks) | Bakes the rule into CI; refuses to be silent. Cost: re-validate when the rule's shape changes. |
| **Critic** | `/critic` review (Goal 4: Project Preferences) | Judgment-required rules (semantic naming, "appropriate" anything, what counts as a "boundary") | No false-confidence test. Cost: requires reviewer per chunk; misses violations between reviews. |

| Preference | Mechanism | Enforcement artifact |
|---|---|---|
| Documentation audit on every change — server instructions, `action='help'` metadata, gh:// resources, prompt templates, and CHANGELOG kept in sync (CLAUDE.md) | Critic | `/critic` (Goal 4: Project Preferences) — verifies user-facing docs were updated |
| All Grasshopper/Rhino document access goes through `GrasshopperContext.ExecuteOnUiThread()` (no off-thread host calls) | Critic | `/critic` — judgment-required; verifies UI-thread marshaling at boundaries |
| Tool methods return `{ success, ... }` JSON and catch at the boundary (no throwing across the MCP boundary) | Critic | `/critic` — semantic; verifies the response contract |
| Release configuration required for builds (Debug blocked) | Build target | `src/Cordyceps/Cordyceps.csproj` (Debug build fails) |
| C# naming conventions (PascalCase types/methods, `_camelCase` private fields) | Critic | `/critic` — no analyzer configured, so semantic check |

**Rule for adding a new preference:** assign a mechanism. If the preference can be expressed as "every file/function/config matches pattern X with named exceptions" → write a test. If a linter rule already exists for it → configure the linter. If it requires understanding intent → assign to Critic. Never leave a preference unassigned.

**False-confidence guardrail:** if a generated test would pass on conforming code but couldn't reliably catch a real violation (e.g., greppy heuristics for semantic rules), prefer Critic over a weak test. A green test that doesn't actually check the rule is worse than no test.
