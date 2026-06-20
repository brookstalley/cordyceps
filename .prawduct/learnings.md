# Learnings

Accumulated wisdom from building this product.

## Code linked into `Cordyceps.Tests` must stay host-free — can't call `DebugLog`

The test project links specific `Core/*.cs` files individually (`RequestValidator`,
`UnifiedToolHelpers`, `ScriptDirective`, `McpResultFormatter`, `JsonTypeConverter`) to avoid
pulling in the Grasshopper/Rhino runtime. `Core/DebugLog.cs` uses `RhinoApp.WriteLine`, so it is
**host-coupled and not linkable**. Therefore: when de-silencing a `catch` (or adding any logging)
in one of those linked files, you can't add a `DebugLog` call — it won't compile in the test
project. **Narrow the catch to the expected exception type instead** (e.g. `GetParam` →
`FormatException`/`InvalidCastException`/`OverflowException`/`JsonException`). Narrowing is usually
the better fix anyway: unexpected errors surface instead of being swallowed.

## `views_enabled`: don't hand-flip build-plan `## Status` checkboxes

With `views_enabled: true` (project-state.yaml), the build-plan `## Status` boxes are a **derived
view** — they flip to `[x]` only via `prawduct-hook regen-views` from change-log `status=shipped`
tags at merge time. On an unmerged branch they stay `[ ]`. Hand-flipping them to `[x]` as a
local-progress gesture is view↔tag drift (the Critic flags it; regen-views would revert it).
**Track in-flight progress in the `## Status` Context prose**, not by editing the boxes. (Recurred:
the MCP-4R2K plan did it, then the backlog-batch plan copied it — Critic-caught both times.)

## Building dirties the tracked `releases/Cordyceps.gha` binary

`Cordyceps.csproj` has a post-build `CopyToReleases` target that copies the built `.gha`
into `releases/` (a *tracked* binary). So **any** `dotnet build`/`dotnet test` during
non-release work — including baseline verification and `prawduct-hook test-evidence record` —
restamps `releases/Cordyceps.gha` (it embeds a build timestamp via `SourceRevisionId`), leaving
it modified in `git status`. **Run `git checkout -- releases/Cordyceps.gha` after building** so a
rebuilt binary never lands in a non-release diff. The binary should only change via
`scripts/release.sh` at actual release time. (Mirror of the GHS-7K2P housekeeping snag with the
regenerable `.work-model-index.json`: discard regenerable build artifacts before committing.)

## Version lives in three places; keep them in lockstep

A release version must match across `src/Cordyceps/Cordyceps.csproj` `<Version>`, the tracked
root `manifest.yml`, and `CHANGELOG.md`. The root `manifest.yml` is the yak source-of-truth that
`scripts/release.sh` → `prepare_dist` copies into the (gitignored) `dist/`. It silently drifted to
1.4.0 while shipping 1.4.9 because `release.sh` only bumped the csproj — fixed by adding
`update_manifest_version` to the script (2026-06-20 janitor).
