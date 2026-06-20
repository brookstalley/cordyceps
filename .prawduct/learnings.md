# Learnings

Accumulated wisdom from building this product.

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
