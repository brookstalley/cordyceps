---
paths:
  - "scripts/release.sh"
  - "manifest.yml"
  - "src/Cordyceps/Cordyceps.csproj"
  - "releases/**"
  - "CHANGELOG.md"
---
# Learnings — release

## The built `.gha` is a build output, not a tracked file — don't reintroduce it

`Cordyceps.csproj`'s post-build `CopyToReleases` target writes the built `.gha` into `releases/`
so Grasshopper can load a local build. `releases/` is **gitignored**: the binary embeds a build
timestamp via `SourceRevisionId`, so every `dotnet build`/`dotnet test` produces a different one,
and while it was tracked each of those restamped it into `git status` — every session needed a
`git checkout -- releases/Cordyceps.gha` ritual to keep a rebuilt binary out of an unrelated diff.

Two failures came from tracking it, and both are what the untracking prevents:

- The tracked copy only refreshed at release time, so on `develop` it was always the last
  *released* build. A tester who swapped it in would run code with none of the branch's fixes and
  report a false negative (this nearly happened on issues #27/#29/#30).
- `release.sh publish` shipped `releases/Cordyceps.gha` straight from the working tree into both
  the Yak package and the GitHub Release asset without building it. That was safe only because
  `require_clean_tree` would flag the tracked file if a local build had touched it.

`publish` now runs `build_gha` before `prepare_dist`, so the released binary is provably compiled
from the release commit and a fresh clone can publish. Users download the GitHub Release asset via
`/releases/latest/download/Cordyceps.gha`; CI also uploads a per-commit artifact for testers
without a toolchain. **If you find yourself wanting to commit a `.gha`, don't** — cut a
pre-release with the binary attached instead.

## Version lives in three places; keep them in lockstep

A release version must match across `src/Cordyceps/Cordyceps.csproj` `<Version>`, the tracked
root `manifest.yml`, and `CHANGELOG.md`. The root `manifest.yml` is the yak source-of-truth that
`scripts/release.sh` → `prepare_dist` copies into the (gitignored) `dist/`. It silently drifted to
1.4.0 while shipping 1.4.9 because `release.sh` only bumped the csproj — fixed by adding
`update_manifest_version` to the script (2026-06-20 janitor).
