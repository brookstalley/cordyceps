# Build Plan — release-artifact-provenance

Branch: `fix/release-artifact-provenance` (off `develop`)
Size: medium · Type: bugfix / debt paydown
Critic mode: cumulative (single review — one coherent surface, ~7 files)

## Problem

Observable, from a real user stall: issue #29 reporter had no .NET toolchain and asked for a
`develop` build of `Cordyceps.gha`. There was no supported way to give them one, and the
obvious-looking sources were both wrong:

1. **CI publishes nothing.** `dotnet-ci.yml` builds and tests but never uploads an artifact.
   The reporter assumed otherwise ("hopefully that's just a matter of publishing the artifact")
   and waited a weekend on an artifact that did not exist.
2. **The tracked `releases/Cordyceps.gha` is refreshed only at release time**, so on `develop` it
   is the last *released* build. Had they found it and swapped it in, they would have tested code
   containing none of the fixes and reported a false negative on #27, #29 and #30.
3. **Any local Release build silently overwrites that tracked file** — the csproj `CopyToReleases`
   target writes into `releases/`, which is the path README publishes as the manual-install
   download. It dirtied the working tree twice while diagnosing this.

Underneath all three is one defect: **the binary that gets published is whatever happens to be
sitting in the working tree, not something the release process built.** `do_publish` calls
`prepare_dist` (which `cp`s `$RELEASES_DIR/Cordyceps.gha` into the Yak package *and* the GitHub
Release asset) without ever calling `build_gha`. Today that is safe only by accident: the file is
tracked, so `require_clean_tree` would catch a stray local build. That accident is the thing
holding the release surface together.

## Success

- `release.sh publish` ships a binary it built from the checked-out release commit, and works on
  a fresh clone.
- A local `dotnet build -c Release` cannot dirty the repo or stage an unreleased binary into the
  manual-install download path.
- The README manual-install link always resolves to a published release build.
- A build of `develop` is obtainable without a .NET toolchain.

## Out of scope

- The Yak publishing flow itself, `yak build`/`push`, manifest handling.
- Folding pre-releases into `release.sh` (today's `v1.5.0-rc.1` was cut by hand; a repeatable
  pre-release command is a separate ask — filed to backlog rather than built here).
- Rewriting history to drop the old `.gha` blobs. Untracking stops future churn; the existing
  blobs stay. Repo size is not a reported problem.
- Anything in the #27/#29/#30 product code.

## Requirements confidence

High. The failure is observed, not hypothesized, and every claim below was checked against the
code or the live GitHub API rather than recalled:

- `do_publish` (`scripts/release.sh`) has no `build_gha` call — read.
- `prepare_dist` copies from `$RELEASES_DIR` — read (line ~383).
- `create_github_release` attaches `$RELEASES_DIR/Cordyceps.gha#Cordyceps.gha` — read (~530), so
  the release asset is *already* named `Cordyceps.gha`.
- `https://github.com/brookstalley/cordyceps/releases/latest/download/Cordyceps.gha` → HTTP 200
  today, resolving to v1.4.12. `/releases/latest` excludes pre-releases, so `v1.5.0-rc.1` is not
  served by it — verified against the API after publishing the rc.
- README's manual-install link is pinned to `raw/main/...`, not the current branch — read.

## Chunks

### Chunk 01 — Publish builds what it ships

`do_publish` calls `build_gha` before `prepare_dist`. Provenance becomes explicit: the artifact
in the Yak package and the GitHub Release is compiled from the commit being released.

This is the prerequisite for chunk 02 — once the file is untracked, `require_clean_tree` no
longer guards it, and on a fresh clone of `main` it would not exist at all.

**Done when:** `publish --dry-run` reports the build step; a publish run on a tree with no
`releases/` directory succeeds.

### Chunk 02 — Untrack the built artifact

- `git rm --cached releases/Cordyceps.gha`; add `releases/` to `.gitignore`.
- Drop the `.gha` from `commit_release`'s `git add` (and correct the comment that says it "rides
  to main").
- Repoint README manual-install at `https://github.com/brookstalley/cordyceps/releases/latest/download/Cordyceps.gha`.
- Update `check_readme`'s guard, which greps for the literal `releases/Cordyceps.gha` and would
  otherwise warn on every prep forever.

**Done when:** a `dotnet build -c Release` leaves `git status` clean; README's link resolves to
the current release asset.

### Chunk 03 — CI publishes a downloadable build

`dotnet-ci.yml` uploads the built `.gha` via `actions/upload-artifact` on `develop`/`main` pushes,
so a build of any commit is obtainable without a toolchain.

Known limits, to be stated in the docs rather than discovered: Actions artifacts expire (90 days
default) and require a GitHub login to download. They are a convenience for testers, not the
distribution channel — that stays GitHub Releases + Yak.

**Done when:** the workflow uploads the artifact and the run page offers it.

### Chunk 04 — Documentation audit

Per CLAUDE.md's mandatory documentation audit: `docs/release-process.md` (publish now builds; the
artifact is no longer committed), `CLAUDE.md` (its Publishing section names "the downloadable
`releases/Cordyceps.gha`"), README manual-install wording, and a CHANGELOG entry under
`## [Unreleased]`.

Also retire the learning this change makes obsolete. `.prawduct/learnings.md` carries "Building
dirties the tracked `releases/Cordyceps.gha` binary", whose rule is *"run `git checkout --
releases/Cordyceps.gha` after building"* — a standing manual workaround for precisely the defect
chunk 02 removes. Leaving it would have every future session performing a no-op ritual against a
gitignored file. Rewrite it as the resolved fact (why the artifact is untracked, where the
download comes from) rather than deleting the history of it.

Two pre-existing inconsistencies surfaced while reading, fixed here rather than left (no
"pre-existing" exception):

- `project-preferences.md` records **Parallelization: xUnit default**, but
  `src/Cordyceps.Tests/AssemblyInfo.cs` sets `DisableTestParallelization = true` deliberately, to
  keep timing-sensitive tests off a 2-core CI runner where `build-test` — the required check for
  `main` — flaked twice. The preferences line is stale and contradicts a learning.
- `release.sh`'s `check_readme` greps for a literal path that chunk 02 removes (covered there,
  noted here so the audit is complete).

**Done when:** no doc still describes the artifact as committed to the repo, and no learning
instructs a reader to clean up after a build that no longer dirties anything.

## Verification

- Full C# suite green (`dotnet test ... -c Release`) — this change must not touch it; 550/550 is
  the pre-change baseline.
- `bash -n scripts/release.sh` and `shellcheck` if available.
- **Scratch-clone dry-run**: clone to the scratchpad, create local `develop`/`main`, apply the
  change, and run both `prep --dry-run` and `publish --dry-run` there. `require_branch` blocks
  running them from a feature branch in the real checkout, and dry-run pushes nothing, so a
  scratch clone is the honest way to exercise the real code paths.
- `git status` clean after a Release build.

## Recorded decisions

**Existing `raw/main/releases/Cordyceps.gha` links break.** Deleting the tracked blob means any
link of the form `https://github.com/brookstalley/cordyceps/raw/main/releases/Cordyceps.gha` —
the README's own link until this change, so plausibly copied into forum posts, bookmarks and
third-party install notes — now 404s. Weighed and accepted:

- GitHub serves no redirect for a deleted path, so there is no way to keep the old URL alive
  short of continuing to track the binary, which is the defect being fixed.
- A 404 is a loud failure. The alternative the old link produced — silently serving a build that
  is not the release you think it is — is the failure mode that nearly cost three false-negative
  bug reports.
- The README, the Rhino Package Manager path, and every GitHub Release page continue to work, and
  `/releases/latest/download/Cordyceps.gha` is the stable replacement.
- Old *release tags* are unaffected: their assets are attached to the Release objects, not to the
  tracked path.

**History is not rewritten.** The 56 historical `.gha` blobs (~27.4 MiB) stay. Untracking stops
future churn; a rewrite would break every existing clone and commit reference for a repo-size
problem nobody has reported.

## Verification results (2026-08-25)

- C# suite: 550/550 green, recorded via `test-evidence record`. The suite is a **regression guard
  only** — this change is shell/YAML/docs and adds no C# tests. Do not read green as evidence the
  release paths work; the items below are that evidence.
- `bash -n scripts/release.sh` passes. `shellcheck` is not installed on this machine, so the
  static-analysis pass was not run — flagged rather than claimed.
- Scratch clone (`--no-hardlinks`, origin removed, local `develop`/`main` at the change):
  - `prep --dry-run` — green; `check_readme` reports "README.md looks good" against the new
    release-asset URL, and the commit step now announces a version bump with no `.gha`.
  - `publish --dry-run` — green; "Building Cordyceps..." now precedes "Preparing distribution
    directory...".
  - Real `dotnet build -c Release` **in a clone with no `releases/` directory** produces
    `releases/Cordyceps.gha` (698,368 bytes) and leaves `git status --untracked-files=all` empty.
    This is the case that was broken before: publish would have hit a bare `cp` failure here.
  - `prepare_dist` exercised verbatim (extracted from the real script) both ways: with an empty
    releases dir it exits 1 with the named error; with the built artifact it populates `dist/`
    with `.gha` + `manifest.yml` + `icon.png`.
- Not verified: the CI upload step. YAML parses and the step list is correct, but
  `actions/upload-artifact` cannot run until the branch is pushed. Confirm on the first CI run.

## Status

- [x] Chunk 01: Publish builds what it ships
- [x] Chunk 02: Untrack the built artifact
- [x] Chunk 03: CI publishes a downloadable build
- [x] Chunk 04: Documentation audit
