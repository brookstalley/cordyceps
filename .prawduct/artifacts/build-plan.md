# Build Plan — Fix: gh_script drops the script language directive (GHS-7K2P)

**Type:** Bugfix · **Size:** Medium (code + tests + test-infra + docs across the MCP-tool and documentation-contract boundaries)
**Branch:** `fix/gh-script-language-directive`
**Backlog:** GHS-7K2P · **Root cause doc:** `incoming-bugs/script-component-language-lost-on-setsource.md`
**Critic mode:** chunk (single-chunk bugfix); run `cumulative` before any PR.

## Requirements Confidence: High

1. **What problem are we solving?** `gh_script(action='set'|'configure')` calls `scriptComp.SetSource(code)`, which replaces the *entire* body — including the Rhino 8 language-specifier directive on line 1 (`#! python 3`, `// #! csharp`). If the caller's `code` omits the directive, the component loses its language association and fails at solve time with "Can not determine input code language", emitting no geometry.
2. **What does success look like?** After `gh_script(set)` with a directive-less body on a freshly-added script component, the component still knows its language and runs — no "Can not determine input code language". The fix is verified by unit tests on the pure directive logic; live behavior re-verified in Rhino.
3. **What's out of scope?** Changing how params/type-hints sync; the `mcp-remote` bridge; any non-script component; inventing a language picker. We do **not** add a `language` parameter to the tool (suggested fix #2) — preservation makes it unnecessary.

## Design Decision: preserve the existing directive (not infer-from-type)

The bug report's preferred fix (#1) says "prepend the directive that matches the component's current language." Two ways to know that language:
- **Infer from component type** → requires hardcoding the exact directive strings for Python 2/3 and C# (post-cutoff Rhino behavior; risk of guessing wrong, esp. C#'s `// #! csharp` comment form).
- **Preserve the directive already on the component's source** (chosen) → reads the actual first-line directive off the existing source and re-applies it when the new code lacks one. Zero hardcoded literals, handles Python 2/3 and C# uniformly, and a freshly-added component always carries its directive — so it fully covers the documented repro.

Rationale: more accurate (uses the component's real directive), lower-risk (no guessing fast-moving strings), and minimal (Principle 12 — no `language` param, no infer-table). Verified directive forms (web): Python `#! python 3` / `#! python 2`; C# `// #! csharp`. These literals appear only in **docs**, not in the preserve logic.
Trade-off accepted: a component whose directive was *already* stripped by a prior bad `set` (directive-less existing source) is not auto-recovered — re-adding such a component, or passing an explicit directive, fixes it. This recovery case is rare and out of scope; documented in CommonErrorsGuide.

### Chunk 01: Preserve the language directive + tests + docs

**Files:**
- `src/Cordyceps/Core/ScriptDirective.cs` (NEW) — pure, Grasshopper-free static helper:
  - `Preserve(existingSource, newCode)` → if `newCode` already has a line-1 directive, return it unchanged (caller wins); else if `existingSource` has one, prepend it; else return `newCode` unchanged.
  - `ExtractDirective(source)` / `HasDirective(code)` → first-line detection: trimmed line starts with `#!`, or starts with `//` and contains `#!` (C#). BOM/whitespace tolerant.
- `src/Cordyceps/Tools/Unified/GhScriptTool.cs` — at all 3 `SetSource(code)` sites (ActionSet ~164; ActionConfigure ~260, ~283) call `SetSource(ScriptDirective.Preserve(TryGetScriptSource(component), code))` via a small private wrapper.
- `src/Cordyceps.Tests/Cordyceps.Tests.csproj` — (a) link `ScriptDirective.cs` like `JsonTypeConverter.cs`; (b) add a `RollForward=Major` property so the net8.0 test host runs on the .NET 10-only machine (test-infra fix; shipping plugin csproj untouched — it must stay net8.0 for Rhino).
- `src/Cordyceps.Tests/ScriptDirectiveTests.cs` (NEW) — see Acceptance.

**Documentation audit (CLAUDE.md table):**
- `src/Cordyceps/Knowledge/CommonErrorsGuide.md` — add the "Can not determine input code language" row (auto-handled now; override with a line-1 directive).
- `GhScriptTool` `ActionInfo` (`set`, `configure` Tips) — note the language directive is auto-preserved and can be set explicitly.
- `src/Cordyceps/Knowledge/Prompts/SetupScriptComponent.md` + `src/Cordyceps/Knowledge/ComponentPatternsGuide.md` — one-line note that the language directive is preserved automatically (templates already show plain bodies, which now work).
- `McpServer.GetServerInstructions()` — no change (no action added/removed; behavior is "just works"). Noted for the audit.
- Root `CHANGELOG.md` — `[Unreleased] → Fixed` entry.

## Acceptance criteria
- [ ] Unit tests (xUnit) cover: preserve Python directive when body lacks one; preserve C# `// #! csharp`; respect a directive already in the new body (Python & C#); no-op when existing source has no directive / is null / empty; new code null/empty doesn't throw; exact Python 2-vs-3 preservation; directive only honored on line 1 (not later lines); BOM + CRLF tolerance.
- [ ] All tests pass (existing JsonTypeConverter tests + new ScriptDirective tests) via `dotnet test`.
- [ ] `dotnet build src/Cordyceps/Cordyceps.csproj -c Release` succeeds.
- [ ] Docs updated per the audit above.
- [ ] Critic (chunk) clean; backlog GHS-7K2P → shipped.

## Status
- [ ] Chunk 01: Preserve the language directive + tests + docs
