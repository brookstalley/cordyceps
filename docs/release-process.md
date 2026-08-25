# Release Process

Cordyceps is distributed two ways, and a release publishes to both:

1. **GitHub** — a `Release vX.Y.Z` commit + `vX.Y.Z` tag on `main`, and a published
   **GitHub Release** (with the `.gha` attached as a downloadable asset and the CHANGELOG
   section as its notes). That asset is what the README's manual-install link resolves to,
   via `/releases/latest/download/Cordyceps.gha`. The `.gha` itself is a build output and is
   not tracked in git — `publish` compiles it from the release commit.
2. **Yak** — the [Rhino package manager](https://yak.rhino3d.com/packages/cordyceps),
   which is how Rhino's Package Manager (`_PackageManager`) finds and installs Cordyceps.

Both are driven by a single script: **`scripts/release.sh`**. Do not run the yak/gh commands
by hand — the script keeps the csproj version, the manifest version, the GitHub tag, and the
published yak package all in lockstep.

## Branch model (gitflow)

- **`develop`** is the integration branch and the GitHub **default branch**. Feature branches
  branch off `develop` and merge back via PRs (see `/prawduct:pr`).
- **`main`** is the **release surface** and is **strict-protected**: no direct pushes, a PR with
  the `build-test` check is required, and even the owner cannot bypass. The only things that reach
  `main` are (a) a `develop → main` release PR and (b) version **tags**.

Because `main` rejects direct pushes, a release is split in two around the release PR. The key
fact that makes this work: **branch protection guards *branches*, not *tags*** — so the publish
half can push the `vX.Y.Z` tag even though it can't push the `main` branch.

## Getting a build without releasing

Two ways to hand someone a `.gha` that is not a release — neither touches `main` or Yak:

- **CI artifact.** Every `build-test` run on `develop`/`main` (and every PR) uploads
  `Cordyceps.gha-<sha>` to its run page. Cheapest option, but artifacts expire (90 days) and the
  downloader needs a GitHub login.
- **Pre-release.** For an outside tester, publish a GitHub pre-release so the link is anonymous
  and durable:

  ```bash
  dotnet build src/Cordyceps/Cordyceps.csproj -c Release -p:Version=1.5.0-rc.1
  gh release create v1.5.0-rc.1 "releases/Cordyceps.gha#Cordyceps.gha" \
      --prerelease --target <sha> --title "..." --notes-file <notes>
  ```

  Build with `-p:Version=` rather than editing the csproj: `validate_version` rejects prerelease
  strings and `increment_patch` would turn a stored `1.5.0-rc.1` into `1.5.0-rc.2` on the next
  bare `prep`. The override still moves the assembly version (`1.5.0.0` vs a shipped `1.4.12.0`),
  so a tester can confirm which build they are running via the MCP `initialize` response.

  Pre-releases are skipped by `/releases/latest/download/`, so the README download link is
  unaffected. `scripts/release.sh` has no command for this yet — backlog `REL-6H4X`.

## Prerequisites

- **.NET 8 SDK** (`dotnet` on PATH) — both halves build the `.gha`.
- **Git push access** to `origin`.
- **GitHub CLI** (`gh` on PATH), authenticated — `prep` opens the release PR and `publish`
  creates the GitHub Release. Run `gh auth login` once (https://cli.github.com).
- **Rhino 8 installed** — provides the `yak` CLI (`publish` only). The script locates it
  automatically (`/Applications/Rhino 8.app/Contents/Resources/bin/yak` on macOS;
  `C:\Program Files\Rhino 8\System\yak.exe` on Windows/Git Bash), or uses `yak` if on PATH.
- **Yak authentication** (`publish` only) — a token at `~/Documents/.mcneel/yak.yml` (macOS) or
  `…/Documents/.mcneel/yak.yml` / `%APPDATA%\McNeel\yak.yml` (Windows). If none is found the
  script runs `yak login` (opens a browser). Authenticate once and the token persists.

The script supports macOS and Windows (via Git Bash). Run it from the repo root.

## Before you release

1. **Land the feature work on `develop` first** — each feature merges via its own
   `feature → develop` PR (`/prawduct:pr`), which is where its review gates run. The release PR
   itself is an *integration* PR, not a feature PR, so it does **not** re-run those gates.
2. **Keep the CHANGELOG up to date.** Add user-facing notes under a top `## [Unreleased]` section
   as you go ([Keep a Changelog](https://keepachangelog.com/) convention). `prep` renames
   `## [Unreleased]` → `## [X.Y.Z] - YYYY-MM-DD` for you; if there is no `[Unreleased]` section it
   aborts (there are no notes to ship).

## Cutting a release

Versioning is **semver `X.Y.Z`**; a `+0.0.1` patch is the default. The csproj `<Version>` is the
source of truth — `prep` reads it and increments the last component unless you pass a version.

**Step 1 — prep (on `develop`):**

```bash
git checkout develop && git pull
./scripts/release.sh prep            # auto-increment patch (1.4.12 -> 1.4.13)
./scripts/release.sh prep 1.4.13     # or set an explicit version
./scripts/release.sh prep --dry-run  # preview, no changes
```

`prep` bumps the version, renames the CHANGELOG section, builds the `.gha` (as a smoke test —
the binary is not committed), commits `Release vX.Y.Z` on `develop`, pushes, and opens a
`develop → main` PR.

**Step 2 — merge the release PR.** Review it on GitHub and merge once `build-test` is green.
Use a **merge commit** (not squash) so `develop` and `main` stay tree-aligned (no back-merge
needed). The merge is the one and only way the release commit reaches the protected `main`.

**Step 3 — publish (on `main`):**

```bash
git checkout main && git pull        # pull the merged release commit
./scripts/release.sh publish 1.4.13  # (or omit the version to use main's csproj)
./scripts/release.sh publish --dry-run
```

`publish` builds the `.gha` and the yak package, tags `vX.Y.Z` and **pushes only the tag**,
creates the GitHub Release (attaching the `.gha`), and pushes to yak.

### What each step does, in order

**`prep`** (on `develop`): guards branch + clean tree → resolves the version → renames
`CHANGELOG [Unreleased]` → bumps `csproj` + root `manifest.yml` → `dotnet build -c Release`
(verifies the bumped version compiles; the `.gha` lands in the gitignored `releases/`) → commits
`csproj`, `manifest.yml` and `CHANGELOG.md` as `Release vX.Y.Z` → pushes `develop` → opens the
`develop → main` PR with the CHANGELOG section as the body.

**`publish`** (on `main`, after the PR merged + pulled): guards branch + clean tree →
verifies `main`'s csproj is at the version → `dotnet build -c Release` → prepares `dist/`
(`.gha`, `manifest.yml`, `icon.png`) → `yak build` → tags `vX.Y.Z` and pushes **only the tag** →
`gh release create` (`.gha` + notes, `--latest`; skipped if it already exists) → `yak push`.

`publish` builds rather than reusing whatever is in `releases/`, so the binary it ships to both
GitHub and Yak is provably compiled from the commit being released, and a fresh clone of `main`
can publish.

## Governance bookkeeping (Prawduct)

Under gitflow, a feature's change-log entry carries **no `release=` tag** after its
`feature → develop` PR — that absence is the release-pending state. When the `develop → main`
release ships those entries, add `release=vX.Y.Z` to each. That tag is the whole release-time
edit: nothing is regenerated from it, and `prawduct-hook check-releasability` reads the *absence*
of `release=` to enumerate what is still pending, so never write a placeholder value.

Then archive the build plans this release shipped:

```bash
prawduct-hook plan-backfill --apply
```

There is no derived-view regeneration step. Build-plan `## Status` checkboxes are written by hand —
tick a box when its chunk's review passes, and nothing overwrites it. (`prawduct-hook regen-views`
still exists but is inert: it prints a deprecation warning and writes nothing.) Older change-log
entries carry a legacy `status=` key; it is no longer read by anything.

## Notes & cautions

- **A yak publish is effectively permanent.** Published versions cannot be silently replaced or
  unpublished — bump the version and publish again to ship a fix. Use `--dry-run` if unsure.
- **`publish` pushes a tag, never the `main` branch.** That is what lets a release coexist with
  strict branch protection; the release *commit* only ever reaches `main` through the merged PR.
- **Two manifests exist.** The root `manifest.yml` is the source of truth (the script edits it);
  `dist/manifest.yml` is a generated, gitignored copy. Edit only the root one.
- **`dist/` is regenerated** on every `publish` — safe to delete between releases.
