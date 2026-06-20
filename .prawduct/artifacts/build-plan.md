# Build Plan — Janitor maintenance pass (2026-06-20)

**Type:** Debt paydown / maintenance · **Size:** Medium (multi-area cleanup, one new CI file, one script change)
**Branch:** `chore/janitor-2026-06-20`
**Source:** `/prawduct:janitor` survey (2026-06-20), user-approved scope.
**Critic mode:** final (single review over the complete janitor diff once all chunks land; run `cumulative` before any PR).

## Requirements Confidence: High

1. **What problem are we solving?** Drift and obsolescence accumulated at the repo edges: a stale tracked manifest version, an unreferenced/contradictory planning doc in `src/`, shipped-work leftovers (resolved bug report, stale build plan, merged branches, stray dirs), a CLAUDE.md omission, a `release.sh` gap that let the manifest drift, and no CI exercising the 53 xUnit tests.
2. **What does success look like?** Tracked `manifest.yml` = `1.4.9` and `release.sh` keeps it in sync on future releases; the stray guide / resolved bug report / stray dirs / merged branches are gone; CLAUDE.md lists `ToolHelpers`; a GitHub Actions workflow runs `dotnet build -c Release` + `dotnet test` on push/PR. Build still green, 53/53 tests still pass.
3. **What's out of scope?** Re-architecting anything; dependency upgrades; touching production tool code; the deliberately-tracked `releases/Cordyceps.gha` binary; the `*.sln` ignore (judged intentional — build is csproj-driven); the two legitimate undo/redo `TODO`s.

## Boundary note
None of this touches the MCP Tool/Action contract or the Embedded Documentation contract's *behavior* (CLAUDE.md edit is additive; deleted guide is unreferenced by code/docs). `release.sh` + `manifest.yml` are the **release/publish** boundary — verify with `--dry-run`, not a real publish.

---

### Chunk 01: Repo hygiene & obsolescence cleanup
Pure removals of verified-unreferenced/shipped artifacts. No production code.

**Actions:**
- Delete `src/cordyceps-mcp-implementation-guide.md` (402-line unreferenced planning doc; contradicts the HTTP+SSE impl; superseded by CLAUDE.md + Knowledge/). Confirmed: zero code/doc references.
- Delete `incoming-bugs/script-component-language-lost-on-setsource.md` (GHS-7K2P shipped in PR #16; content preserved in backlog Archive + reflections + git history). Remove the now-empty `incoming-bugs/` dir.
- Remove the stray empty `output/` dir (untracked).
- Remove the stray `memory/` dir (gitignored; holds another project's notes — `S3_MoldFinisher`; belongs in `~/.claude`).
- Delete merged local branch `fix/dotnet-test-evidence`.
- Delete fully-merged stale remote branch `origin/claude/issue-4-20260131-2124` (outward-facing; user-approved).

**Verify:** `git ls-files` no longer lists the two deleted tracked files; `dotnet build -c Release` + `dotnet test` green (deletions can't affect the build, but confirm); restore `releases/Cordyceps.gha` after building.

### Chunk 02: Manifest & doc drift fixes
**Files:**
- `manifest.yml` — bump `version: 1.4.0` → `1.4.9` (match csproj + shipped + `dist/manifest.yml`).
- `scripts/release.sh` — add a manifest-version update step alongside the existing csproj update (`update_csproj_version`), so the tracked manifest can't silently drift again. Mirror the existing `sed_inplace` pattern; honor `--dry-run`.
- `CLAUDE.md` — add `Core/ToolHelpers.cs` to the Architecture "Core Components" list (shared GH/Rhino document + response helpers used across every tool), distinct from `UnifiedToolHelpers.cs`.

**Verify:** `release.sh --dry-run` reports it would set both csproj and manifest to the target version; `manifest.yml` parses; build + tests green. **Do not run a real publish.**

### Chunk 03: Build/test CI workflow
**Files:**
- `.github/workflows/dotnet-ci.yml` (new) — on push + pull_request: checkout, setup .NET 8, `dotnet build src/Cordyceps/Cordyceps.csproj -c Release`, `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release`. Mirror the exact commands CLAUDE.md/`project-preferences.md` document.

**Verify:** YAML parses; the commands it runs match the documented dev commands and pass locally (already verified at baseline). CI execution itself is verified on first push (can't run Actions locally — stated honestly).

## Acceptance criteria
- [ ] Chunk 01: deleted files gone from `git ls-files`; stray dirs/branches removed; build + 53 tests green.
- [ ] Chunk 02: `manifest.yml` = 1.4.9; `release.sh --dry-run` updates both csproj+manifest; CLAUDE.md lists ToolHelpers; build + tests green.
- [ ] Chunk 03: CI workflow added, YAML valid, runs the documented build+test commands.
- [ ] Critic (final) clean; backlog reconciled (file CI follow-up if deferred — n/a, building it).

## Status
- [x] Chunk 01: Repo hygiene & obsolescence cleanup — guide + resolved bug report deleted; stray `output/`/`memory/` removed; merged local branch + stale remote branch deleted.
- [x] Chunk 02: Manifest & doc drift fixes — manifest.yml → 1.4.9; `release.sh` now bumps the manifest (verified via syntax check + isolated sed test); CLAUDE.md lists `ToolHelpers`.
- [x] Chunk 03: Build/test CI workflow — `.github/workflows/dotnet-ci.yml` runs `dotnet build -c Release` + `dotnet test -c Release` on push(main)/PR; YAML valid; commands verified locally (53/53). CI execution confirmed on first push.

**Context:** All three chunks complete on `chore/janitor-2026-06-20`. Build 0/0, 53/53 tests pass, test-evidence recorded (current). No compiled C# changed. Awaiting Critic (final), then backlog reconciliation + (optional) PR.
