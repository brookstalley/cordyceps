# Release Process

Cordyceps is distributed two ways, and a release publishes to both:

1. **GitHub** — a `Release vX.Y.Z` commit + `vX.Y.Z` tag on `main`, and the built
   `releases/Cordyceps.gha` (the file users download directly from the README link).
2. **Yak** — the [Rhino package manager](https://yak.rhino3d.com/packages/cordyceps),
   which is how Rhino's Package Manager (`_PackageManager`) finds and installs Cordyceps.

Both are driven by a single script: **`scripts/release.sh`**. Do not run the yak
commands by hand — the script keeps the csproj version, the manifest version, the
GitHub tag, and the published yak package all in lockstep.

## Prerequisites

- **.NET 8 SDK** (`dotnet` on PATH) — builds the `.gha`.
- **Git push access** to `origin` — the script pushes the release commit and tag to `main`.
- **Rhino 8 installed** — provides the `yak` CLI. The script locates it automatically
  (`/Applications/Rhino 8.app/Contents/Resources/bin/yak` on macOS;
  `C:\Program Files\Rhino 8\System\yak.exe` on Windows/Git Bash), or uses `yak` if it's on PATH.
- **Yak authentication** — a token at `~/Documents/.mcneel/yak.yml` (macOS) or
  `…/Documents/.mcneel/yak.yml` / `%APPDATA%\McNeel\yak.yml` (Windows). If no token is
  found, the script runs `yak login`, which opens a browser. Authenticate once and the
  token persists.

The script supports macOS and Windows (via Git Bash). Run it from the repo root.

## Before you release

1. **Land the work on `main` first.** Releases are cut from `main`; merge the feature
   PR(s) before releasing (see `/prawduct:pr`). Under Prawduct, a merged change-log entry
   sits at `status=merged` — that is the correct, terminal state for this trunk repo
   (the `status=shipped` / `release=` flip is the gitflow path and is not used here).
2. **Add the CHANGELOG entry.** `release.sh` refuses to run unless `CHANGELOG.md` has a
   `## [X.Y.Z]` section for the version being released. The convention is
   [Keep a Changelog](https://keepachangelog.com/): rename the top `## [Unreleased]`
   section to `## [X.Y.Z] - YYYY-MM-DD` and start a fresh empty `## [Unreleased]` above it.

## Cutting the release

```bash
# Auto-increment the patch number (e.g. 1.4.9 -> 1.4.10):
./scripts/release.sh

# Or set an explicit version:
./scripts/release.sh 1.4.10

# Preview every step without changing anything (no build, no commit, no push):
./scripts/release.sh --dry-run
```

Versioning is **semver `X.Y.Z`**. A `+0.0.1` patch release is the default
(`./scripts/release.sh` with no argument). The csproj `<Version>` is the source of truth
for the current version; the script reads it and increments the last component.

### What the script does, in order

1. Locates the `yak` CLI and confirms (or establishes) yak login.
2. Reads the current version from `src/Cordyceps/Cordyceps.csproj`; computes the new version.
3. **Pre-flight checks** — verifies `CHANGELOG.md` has a `## [new-version]` entry (hard
   fail if missing) and warns on stale README version references.
4. Updates `<Version>` in `Cordyceps.csproj` and `version:` in the root `manifest.yml`.
5. Builds the `.gha` in Release configuration (`dotnet build -c Release`). The build copies
   the artifact to `releases/Cordyceps.gha`.
6. Prepares the `dist/` directory — `Cordyceps.gha`, `manifest.yml`, and `icon.png` only.
7. Builds the yak package: `yak build --platform any --version X.Y.Z` (produces `dist/*.yak`).
8. Commits `csproj`, `manifest.yml`, `releases/Cordyceps.gha`, and `CHANGELOG.md` as
   `Release vX.Y.Z`, and creates an annotated `vX.Y.Z` tag.
9. Pushes the commit and the tag to `origin main`.
10. Pushes the package to yak: `yak push dist/cordyceps-X.Y.Z-any.yak`.

On success it prints the GitHub release tag URL and the yak package URL.

## Notes & cautions

- **A yak publish is effectively permanent.** Published versions cannot be silently
  replaced or unpublished — bump the version and publish again to ship a fix. Use
  `--dry-run` first if you are unsure.
- **The script pushes to `main` directly.** That is the intended trunk-based release flow;
  the reviewed code already landed via its PR, and this commit only bumps versions and the
  built artifact.
- **Two manifests exist.** The root `manifest.yml` is the source of truth (the script edits
  it); `dist/manifest.yml` is a generated copy. Edit only the root one.
- **`dist/` is regenerated** on every run — it is safe to delete between releases.
