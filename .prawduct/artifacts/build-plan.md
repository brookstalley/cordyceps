# Build Plan — gitflow-release-refactor

Branch: `feature/gitflow-release-refactor` (off `develop`)
Scope: adopt gitflow (develop=integration, main=release surface, strict-protected) and
refactor the release so it works when main rejects direct pushes.
Critic mode: cumulative (gates the develop PR) after all chunks.

## Context / decisions (already made)
- v1.4.12 shipped via the old trunk flow; `develop` created from post-release main and is now
  the GitHub **default branch**. This work lands on `develop` via a feature PR.
- Release model (user-chosen): **lean develop→main PR**. `release.sh` splits into `prep` +
  `publish`. No release branches, no back-merge (the develop→main merge is `--no-ff`, keeping
  main's tree == develop's; main only ever gains merge commits + tags).
- Strict main protection (require PR + `build-test`, block force-push/delete, **no bypass**) is
  **Phase 2c — applied AFTER this PR merges**, so the documented+actual release flow is in place
  before main locks. Not in this plan.
- Key enabler: branch protection guards *branches*, not *tags* — so `publish` pushing only the
  `vX.Y.Z` tag (not the main branch) works under strict protection.

## Confidence check
1. Problem: `release.sh` does `git push origin main` (the Release commit) — strict-protected main
   will reject it, breaking releases. prawduct's gates also still treat `origin/main` as the base.
2. Success: a release is cuttable end-to-end under strict main protection via `prep` (develop-side
   bump+CHANGELOG+.gha+PR) then `publish` (main-side tag+GH Release+yak); prawduct gates use
   `develop` as base; `--dry-run` previews both halves with no side effects.
3. Out of scope: applying main protection (Phase 2c); changing the yak/GH-release mechanics
   themselves; CI matrix changes beyond adding the `develop` push trigger.

## Verify
- `release.sh prep --dry-run` and `release.sh publish --dry-run` preview every step, no changes.
- `prawduct-hook resolve-base` returns `develop` after the config change.
- Operator-verified at the next real release (a bash release script can't be unit-tested; the
  next `1.4.13` cut is the live verification) — enqueue VRF.

## Chunks

### Chunk 01: gitflow config + CI develop-trigger + untrack dist/manifest.yml
- [ ] `.prawduct/project-state.yaml`: add top-level `base_branch: develop` (resolve-base / gates).
- [ ] `.github/workflows/dotnet-ci.yml`: push trigger `branches: [main, develop]`.
- [ ] `git rm --cached dist/manifest.yml` — it's gitignored (`.gitignore:6 dist/`) yet tracked, so
      every release restamps it and dirties the tree. Untrack; `.gitignore` already covers it.

### Chunk 02: release.sh → prep/publish (lean develop→main model)
- [ ] Restructure `scripts/release.sh` around a subcommand: `prep [X.Y.Z]` and `publish [X.Y.Z]`
      (keep `--dry-run`, `--help`). Shared helpers (find_yak, version parse, changelog/readme
      checks, prepare_dist, extract_changelog_notes) stay.
- [ ] `prep`: guard on-branch==`develop` + clean tree; resolve version (auto-patch from csproj or
      arg); bump csproj+manifest; rename CHANGELOG `[Unreleased]`→`[X.Y.Z] - DATE`; build the
      `.gha` (Release); commit `Release vX.Y.Z` (csproj, manifest, CHANGELOG, releases/Cordyceps.gha);
      push develop; `gh pr create` develop→main. STOPS for human merge.
- [ ] `publish`: guard on-branch==`main` + pulled + clean; verify csproj version==X.Y.Z and
      CHANGELOG has `[X.Y.Z]`; prepare_dist; yak build; `git tag -a vX.Y.Z` + **push only the tag**;
      `gh release create` (.gha + notes); yak push. No version bump, no branch push.
- [ ] Preserve the early prerequisite checks (gh auth, yak login/CLI) in both halves as relevant.
- [ ] Enqueue VRF in `.prawduct/operator-verification.md` (next real release is the live test).

### Chunk 03: docs
- [ ] `docs/release-process.md`: rewrite for gitflow two-step (prep → PR → publish); correct the
      "status=shipped/release= … not used here" note (it IS the model now).
- [ ] `CLAUDE.md` Publishing section: two-step flow + gitflow (develop default, main release surface).
- [ ] CHANGELOG `[Unreleased]` entry; `.prawduct/change-log.md` statusless entry (PR gate needs it).

## Status
- [ ] Chunk 01
- [ ] Chunk 02
- [ ] Chunk 03
Context: Chunk 01 done (base_branch=develop, CI develop trigger, dist/manifest.yml untracked;
resolve-base→origin/develop verified). Chunk 02 done (release.sh prep/publish rewrite; syntax +
branch-guard + dispatch verified, mutating flows dry-run-guarded → VRF-006 enqueued). Next:
Chunk 03 (docs: release-process.md, CLAUDE.md Publishing, CHANGELOG + change-log entry), then
cumulative Critic, then PR → develop. Phase 2c (protect main strict) follows after this merges.
