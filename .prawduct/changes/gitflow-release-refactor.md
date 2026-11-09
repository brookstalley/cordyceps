---
schema: 1
type: chore
scope: gitflow-release-refactor
date: 2026-06-24
title: gitflow + two-step release (prep/publish)
---
**Why:** Adopted gitflow — `develop` is now the default/integration branch and `main` is the
release surface, to be strict-protected (require PR + `build-test`, no bypass). The old
`scripts/release.sh` pushed the `Release vX.Y.Z` commit **directly to `main`**, which strict
protection rejects — breaking releases. Split it into `prep` (on develop: bump version + CHANGELOG,
build `.gha`, commit, push, open a `develop→main` PR) and `publish` (on main, after the PR merges:
build the yak package, **push only the `vX.Y.Z` tag** — branch protection guards branches not tags —
create the GitHub Release, push to yak). Also: `base_branch: develop` in project-state.yaml so the
prawduct gates/`resolve-base` diff against develop; CI runs on `develop` pushes; untracked the
gitignored-yet-tracked `dist/manifest.yml` (restamped every release). A bash release script isn't
unit-testable — syntax + branch-guard + dispatch were verified statically; the next real release is
operator-verified (VRF-006). Also disabled xUnit test parallelization
(`src/Cordyceps.Tests/AssemblyInfo.cs`) — a pre-existing flaky timing test
(`InFlightRequestsTests.Count_…`) starved its removal continuation under parallel execution on the
2-core CI runner, and `build-test` must be reliable since it becomes the required main-protection
check. Docs: `docs/release-process.md` rewritten, `CLAUDE.md` Publishing updated. Strict main
protection itself is applied as a separate step after this lands.

Legacy tags: status=merged

## Consumer note
none: migrated from the legacy change log, which held no consumer note; replace this line with the note if consumers need one
