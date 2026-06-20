# Change Log — Cordyceps

<!-- Append new entries at the top. Each entry is a ## section.
     This file is separate from project-state.yaml to reduce merge conflicts
     when multiple branches add entries simultaneously.

     # Tagged entries (enabled by default; set `views_enabled: false` in project-state.yaml to opt out)

     With views enabled (the default), add a tag-line directly under each ##
     header to mark which build-plan chunks the entry shipped and which
     release it belongs to. `prawduct-hook regen-views` uses these tags to
     regenerate three derived views:
       * build-plan `## Status` block — checkboxes flip from `status=shipped`
       * `.prawduct/release-notes.md` — sections grouped by `release=`
       * `scope_rollups:` block in project-state.yaml — grouped by `scope=`
     Untagged entries are ignored by all three views.

     Format:

         ## YYYY-MM-DD: title (vN.M.P)

         <!-- prawduct: chunks=00,01,02 | release=v1.3.18 | status=shipped | scope=v1.4 -->

         **Why:** ...

     Recognized keys:
       chunks   - comma-separated chunk IDs (zero-padded, must match
                  build-plan.md ## Status headers exactly: `Chunk 00:`)
       release  - version string (used by release-notes view, future)
       status   - shipped | in-progress | deferred
                  `shipped` means MERGED TO MAINLINE — per-chunk timing.
                  Tag chunks `status=shipped` as soon as the merge commit lands;
                  inclusion in a tagged release is tracked separately via
                  `release=vN.M.P` (set when a release entry consolidates one
                  or more shipped chunks).
       scope    - rollup identifier (e.g., v1.4)

     With `views_enabled: true`, the Status checkboxes in build-plan.md are a
     derived view. Don't hand-edit them — add/update a tagged entry here and
     run `prawduct-hook regen-views`. -->

## 2026-06-20: Janitor maintenance pass

<!-- prawduct: type=maintenance | chunks=01,02,03 | scope=janitor-2026-06-20 -->

**Why:** periodic `/prawduct:janitor` survey + user-approved cleanup. Fixed release-metadata
drift (tracked `manifest.yml` was stale at 1.4.0 while shipping 1.4.9) and closed the gap that
let it drift — `scripts/release.sh` now bumps the manifest version, not just the csproj. Added
the first build/test CI (`.github/workflows/dotnet-ci.yml`: `dotnet build`/`dotnet test` on
push/PR) so the 53 xUnit tests run automatically. Removed obsolescence: a 402-line unreferenced
`src/` planning doc that contradicted the HTTP+SSE implementation, the shipped GHS-7K2P bug
report, stray `output/`/`memory/` dirs, and merged/stale branches. Documented
`Core/ToolHelpers.cs` in CLAUDE.md. No compiled C# changed; build 0/0, 53/53 tests pass. Not
user-facing (dev tooling + release plumbing), so no root CHANGELOG entry.

## 2026-06-20: Wire .NET/xUnit test evidence into the Prawduct gate (TST-9Q4M)

<!-- prawduct: type=tooling | chunks=01 | scope=gate-soundness -->

**Why:** `prawduct-hook test-evidence record` defaulted to pytest and could not run this
C#/xUnit repo, so no `.test-evidence.json` was ever produced and the freshness/Critic/PR
gates were unsound (every code chunk warned "no test evidence"). Added the
`JunitXml.TestLogger` package to `Cordyceps.Tests` and declared `test_command` in
project-state.yaml so the hook runs the real xUnit suite and records exact counts.
Verified end-to-end: `test-evidence record` → 53 passed / 0 failed @ HEAD; `test-status`
→ current. No user-facing change (dev tooling), so no root CHANGELOG entry.

## 2026-06-20: Fix gh_script dropping the script language directive (GHS-7K2P)

<!-- prawduct: type=bugfix | chunks=01 | scope=gh-script-language | status=merged -->

**Why:** `gh_script(set/configure)` replaced the whole script body via `SetSource`,
stripping the Rhino 8 language directive (`#! python 3`, `// #! csharp`) when the new
body omitted it — causing "Can not determine input code language" at solve time and no
geometry, which bit anyone following the plain-body examples in the docs. The
component's existing directive is now preserved automatically (a directive in the new
code is respected as-is). New pure helper `Core/ScriptDirective.cs` with 28 unit tests;
docs audited (CommonErrorsGuide, gh_script help, templates, root CHANGELOG).
