---
runbook: ship-a-release
tier: 2
owner: Cordyceps maintainer
last_verified: null
verified_by: null
triggers:
  - CHANGELOG has shippable notes under `## [Unreleased]` and you want them in users' hands
  - A reporter has verified a fix on a pre-release and is waiting for a stable build
---

# Ship a Cordyceps release

A release lands in **four places** and is only done when all four agree: the `main` branch, a
`vX.Y.Z` git tag, a GitHub Release with `Cordyceps.gha` attached, and the Yak package.
`scripts/release.sh` does all four. This runbook is the sequence around it — the checks it does not
do, and the announcing it cannot do.

## When to use this

Cutting a stable `X.Y.Z` release from `develop`.

**Not this:** giving a tester a build without releasing. That is a pre-release or a CI artifact —
see `docs/release-process.md` → "Getting a build without releasing". Do not run `publish` for it;
a Yak push cannot be taken back.

For what each script step does internally and why the flow is split in two, read
`docs/release-process.md`. This runbook does not restate it.

## Prerequisites

Missing any of these strands you mid-release, and `publish` is the half that strands worst.

- `dotnet`, `git` push access, and `gh` authenticated (`gh auth status`)
- **Rhino 8 installed** — it supplies the `yak` CLI at
  `/Applications/Rhino 8.app/Contents/Resources/bin/yak` (macOS)
- **Yak login** — `yak login` once; the token persists at `~/Documents/.mcneel/yak.yml`

`scripts/release.sh` checks only that the token *file exists*, not that it is still valid. An
expired token therefore passes its check and fails at `yak push` — the very last action of
`publish`, after the tag and the GitHub Release are already live. Probe it before you start:

    /Applications/Rhino\ 8.app/Contents/Resources/bin/yak owner list cordyceps

**Expected:** `brooks`. If it errors, run `yak login` before step 1.

> 🚧 **UNVERIFIED** — this command succeeds with a valid token, but it has not been observed
> failing against an expired one, so it may be a public read that proves nothing.
> Confirm the next time a token actually expires; until then treat a pass as weak evidence.

## Phase A — Pre-flight (on `develop`)

1. Get onto a clean, current `develop`:
   `git checkout develop && git pull && git status --porcelain`
   **Expected:** the `status` line prints **nothing**.
   **If it prints anything:** commit or stash first — `prep` refuses a dirty tree.

2. Run the suite:
   `dotnet test src/Cordyceps.Tests/Cordyceps.Tests.csproj -c Release`
   **Expected:** `Failed: 0` on the summary line.
   **If anything failed:** stop. Fix it on `develop` first — the release PR does **not** re-run
   feature review gates, so this is the last gate that catches it.

3. Check `## [Unreleased]` has no duplicate subsection headings:
   `awk '/^## \[Unreleased\]/{u=1;next} /^## \[/{u=0} u&&/^### /' CHANGELOG.md | sort | uniq -d`
   **Expected:** **no output.**
   **If a heading is listed twice:** merge those blocks now. `prep` renames the section verbatim, so
   duplicates ship into the released changelog permanently.

4. List the issues this release closes and confirm each is verified by its reporter:
   `gh issue list --state open`
   **Expected:** you can name, for each, the comment where the reporter confirmed the fix.
   Write the numbers down — step 16 needs them.
   *An issue nobody confirmed is not release-blocking, but you are about to tell its reporter it is
   fixed. Know which ones those are.*

## Phase B — Prep and merge

5. Preview the bump:
   `./scripts/release.sh prep X.Y.Z --dry-run`
   **Expected:** it reports the current version and `X.Y.Z` as the new one, and exits without
   touching anything.

6. Run it for real:
   `./scripts/release.sh prep X.Y.Z`
   **Expected:** it ends by printing the URL of a new `develop → main` PR.
   **If `gh pr create` failed:** the commit is already pushed to `develop` — open the PR by hand
   rather than re-running `prep`, which would try to bump the version a second time.

7. Wait for the required check:
   `gh pr checks <PR#> --watch`
   **Expected:** `build-test` reports `pass`.
   **If it fails:** fix on `develop` and push; the PR updates in place.

8. Merge it — **`--merge`, never squash**, so `develop` and `main` stay tree-aligned:
   `gh pr merge <PR#> --merge`
   **Expected:** `Merged pull request #<n>`.
   **If GitHub rejects `--merge`:** stop and fix the repo setting. Do not fall back to `--squash`;
   it forks `main` from `develop` and every later release inherits the divergence.

## Phase C — Publish

9. Move to the merged release commit:
   `git checkout main && git pull && git status --porcelain`
   **Expected:** no output from `status`, and `grep '<Version>' src/Cordyceps/Cordyceps.csproj`
   shows `X.Y.Z`.

10. Preview the publish:
    `./scripts/release.sh publish X.Y.Z --dry-run`
    **Expected:** it names the tag, the Release, and the `.yak` it would push, and changes nothing.

> ⚠️ **IRREVERSIBLE — step 11 pushes to Yak.**
> A published Yak version cannot be replaced, overwritten, or unpublished. A mistake is fixed only
> by burning the version number and publishing another. The tag and the GitHub Release are both
> reversible; Yak is not.
> **Proceed only if:** steps 2 and 10 both passed on this exact commit.
> **Abort if:** the dry run named a version you did not expect. Aborting here costs nothing.

11. Publish:
    `./scripts/release.sh publish X.Y.Z`
    **Expected:** it pushes the tag, creates the Release, and reports the Yak push succeeded.
    **If it failed after the tag was pushed:** re-run the same command. It reconciles an existing
    Release rather than skipping it (re-attaches the `.gha`, re-marks it latest) — but re-running
    after a *successful* Yak push will fail at Yak, which is correct and harmless.

## Phase D — Verify users can actually get it

The script reports its own success. These three check the systems it wrote to.

12. The download link in the README resolves to the new asset:
    `curl -sS -o /dev/null -w '%{http_code}\n' -L https://github.com/brookstalley/cordyceps/releases/latest/download/Cordyceps.gha`
    **Expected:** `200`.
    **If `404`:** the Release exists with no asset attached, or is not marked latest — re-run
    step 11 to reconcile it.

13. The Release carries the binary:
    `gh release view vX.Y.Z --json assets --jq '.assets[].name'`
    **Expected:** `Cordyceps.gha`.

14. Yak has the new version:
    `/Applications/Rhino\ 8.app/Contents/Resources/bin/yak search cordyceps`
    **Expected:** `cordyceps (X.Y.Z)`.
    *This is what Rhino's Package Manager serves. It can lag a few minutes.*

## Phase E — Close out

15. Tag the change-log entries this release shipped — add `release=vX.Y.Z` to the
    `<!-- prawduct: ... -->` line of every entry that has no `release=` yet, then:
    `prawduct-hook plan-backfill --apply`
    **Expected:** `check-releasability` no longer lists those entries as pending.
    *Never write a placeholder `release=` value — its absence is the release-pending state.*

16. Close each issue from step 4, naming the release:
    `gh issue close <N> --comment "Shipped in vX.Y.Z: <link>"`
    **Expected:** `gh issue list --state open` no longer shows them.
    *Reporters who verified a pre-release are waiting on exactly this; several may have rolled back
    to the last stable build until they see it.*

17. If any change in this release still needs a human in a live Rhino, append it to
    `.prawduct/operator-verification.md` as a `## VRF-<id>` entry with `**Status:** pending`.

## Done when

`yak search cordyceps` reports `X.Y.Z`, the `curl` in step 12 returns `200`, and
`gh issue list --state open` shows nothing that this release fixed.

## If this doesn't work

- **`prep` aborts with "CHANGELOG.md has neither a [Unreleased] nor a [X.Y.Z] section"** — there are
  no notes to ship. Write them under `## [Unreleased]` and re-run.
- **`publish` aborts with "Requested X.Y.Z but main csproj is at ..."** — the release PR is not
  merged, or `main` is not pulled. Return to step 8.
- **Yak push failed but the tag and Release are live** — users can download from GitHub; the Package
  Manager is stale. Re-running `publish` will fail at the tag step, so push the package by hand:
  `yak push dist/*.yak`, then re-check step 14.
- **Anything else, or the version is already burned on Yak** — stop and pick it up with a fresh
  version number. There is no rush that justifies improvising here; nothing is on fire.
