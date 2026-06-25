#!/bin/bash
#
# Cordyceps Release Script — gitflow two-step (Cross-platform: macOS, Windows via Git Bash)
#
# Under gitflow, `main` is strict-protected: nothing pushes to it directly. A release is
# therefore split in two around a develop->main PR (branch protection guards branches, not
# tags — so the publish half pushes only the version tag, which is allowed):
#
#   ./scripts/release.sh prep [VERSION]      # on develop: bump version + CHANGELOG, build the
#                                            #   .gha, commit "Release vX.Y.Z", push develop, and
#                                            #   open a develop->main PR. Then merge that PR.
#   ./scripts/release.sh publish [VERSION]   # on main (after the prep PR merged + pulled): build
#                                            #   the yak package, tag vX.Y.Z, push the tag, publish
#                                            #   the GitHub Release, and push to yak.
#
#   ./scripts/release.sh prep --dry-run      # preview either half without making changes
#   ./scripts/release.sh --help
#
# Full walk-through: docs/release-process.md. Don't run the yak/gh commands by hand — this
# script keeps the csproj version, manifest version, GitHub tag, and yak package in lockstep.
#
# Prerequisites:
#   prep:    dotnet CLI, git push access to origin, gh CLI (authenticated)
#   publish: git push access, gh CLI (authenticated), Rhino 8 (for the yak CLI), yak login
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CSPROJ="$PROJECT_ROOT/src/Cordyceps/Cordyceps.csproj"
MANIFEST="$PROJECT_ROOT/manifest.yml"
CHANGELOG="$PROJECT_ROOT/CHANGELOG.md"
README="$PROJECT_ROOT/README.md"
RELEASES_DIR="$PROJECT_ROOT/releases"
DIST_DIR="$PROJECT_ROOT/dist"

# gitflow branches
INTEGRATION_BRANCH="develop"   # where prep runs and the release PR is opened from
RELEASE_BRANCH="main"          # where publish runs; strict-protected (PR + tag only)

DRY_RUN=false
SUBCOMMAND=""
NEW_VERSION=""
YAK=""

# Colors for output (works in most terminals including Windows Terminal)
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[OK]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

usage() {
    echo "Usage: $0 <prep|publish> [VERSION] [--dry-run]"
    echo ""
    echo "Commands:"
    echo "  prep [VERSION]     On '$INTEGRATION_BRANCH': bump version + CHANGELOG, build the .gha,"
    echo "                     commit 'Release vX.Y.Z', push, and open a '$INTEGRATION_BRANCH'->'$RELEASE_BRANCH' PR."
    echo "                     Omit VERSION to auto-increment the patch (e.g. 1.4.12 -> 1.4.13)."
    echo "  publish [VERSION]  On '$RELEASE_BRANCH' (after the prep PR is merged + pulled): build the yak"
    echo "                     package, tag vX.Y.Z, push the tag, publish the GitHub Release, push to yak."
    echo "                     Omit VERSION to use the version already on '$RELEASE_BRANCH' (csproj)."
    echo ""
    echo "Options:"
    echo "  --dry-run          Show what would happen without making changes"
    echo "  --help, -h         Show this help"
    echo ""
    echo "Supported platforms: macOS, Windows (Git Bash). Run from the repo root."
    echo "Full process: docs/release-process.md"
}

# Find Yak CLI dynamically
find_yak() {
    # Check if already in PATH
    if command -v yak &> /dev/null; then
        YAK="yak"
        return 0
    fi

    local yak_paths=()

    case "$(uname -s)" in
        Darwin*)
            PLATFORM="mac"
            # Search for Rhino installations on macOS
            yak_paths=(
                "/Applications/Rhino 8.app/Contents/Resources/bin/yak"
                "/Applications/Rhino 7.app/Contents/Resources/bin/yak"
                "$HOME/Applications/Rhino 8.app/Contents/Resources/bin/yak"
            )
            # Also search with mdfind (Spotlight)
            local spotlight_yak
            spotlight_yak=$(mdfind -name "yak" 2>/dev/null | grep -i "rhino" | grep "/bin/yak$" | head -1)
            if [[ -n "$spotlight_yak" ]]; then
                yak_paths+=("$spotlight_yak")
            fi
            ;;
        MINGW*|MSYS*|CYGWIN*)
            PLATFORM="windows"
            # Search for Rhino installations on Windows (Git Bash paths)
            yak_paths=(
                "/c/Program Files/Rhino 8/System/yak.exe"
                "/c/Program Files/Rhino 7/System/yak.exe"
                "/c/Program Files (x86)/Rhino 8/System/yak.exe"
            )
            # Try using PROGRAMFILES env var
            if [[ -n "$PROGRAMFILES" ]]; then
                # Convert Windows path to Unix-style for Git Bash
                local pf_unix
                pf_unix=$(cygpath -u "$PROGRAMFILES" 2>/dev/null || echo "$PROGRAMFILES")
                yak_paths+=("$pf_unix/Rhino 8/System/yak.exe")
            fi
            ;;
        *)
            PLATFORM="unknown"
            ;;
    esac

    # Try each path
    for path in "${yak_paths[@]}"; do
        if [[ -f "$path" ]]; then
            YAK="$path"
            return 0
        fi
    done

    return 1
}

# Check if logged into Yak, login if needed
ensure_yak_login() {
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would check Yak login status"
        return 0
    fi

    log_info "Checking Yak authentication..."

    local token_found=false

    # Check for token file based on platform
    case "$PLATFORM" in
        mac)
            if [[ -f "$HOME/Documents/.mcneel/yak.yml" ]] || [[ -f "$HOME/.mcneel/yak.yml" ]]; then
                token_found=true
            fi
            ;;
        windows)
            local docs_path
            docs_path=$(cygpath -u "$USERPROFILE/Documents" 2>/dev/null || echo "$USERPROFILE/Documents")
            local appdata_path
            appdata_path=$(cygpath -u "$APPDATA" 2>/dev/null || echo "$APPDATA")
            if [[ -f "$docs_path/.mcneel/yak.yml" ]] || [[ -f "$appdata_path/McNeel/yak.yml" ]]; then
                token_found=true
            fi
            ;;
    esac

    if [[ "$token_found" == true ]]; then
        log_success "Yak authentication found"
        return 0
    fi

    # No token found, prompt for login
    log_warn "Yak authentication not found. Running 'yak login'..."
    log_info "A browser window will open for authentication."
    echo ""

    "$YAK" login

    if [[ $? -eq 0 ]]; then
        log_success "Yak login successful"
    else
        log_error "Yak login failed"
        exit 1
    fi
}

# Verify the GitHub CLI is installed and authenticated (used to open PRs and create the Release)
ensure_gh() {
    if ! command -v gh &> /dev/null; then
        log_error "GitHub CLI ('gh') not found - it opens the release PR and creates the GitHub Release."
        log_error "Install it (https://cli.github.com) and run 'gh auth login', then retry."
        exit 1
    fi

    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would verify 'gh' authentication"
        return 0
    fi

    if ! gh auth status &> /dev/null; then
        log_error "GitHub CLI is not authenticated. Run 'gh auth login', then retry."
        exit 1
    fi

    log_success "GitHub CLI authenticated"
}

# Verify dotnet is available (prep builds the .gha)
ensure_dotnet() {
    if ! command -v dotnet &> /dev/null; then
        log_error "dotnet CLI not found. Install .NET SDK."
        exit 1
    fi
}

# Require the current git branch to equal the expected one
require_branch() {
    local expected="$1"
    local current
    current=$(git -C "$PROJECT_ROOT" rev-parse --abbrev-ref HEAD)
    if [[ "$current" != "$expected" ]]; then
        log_error "This command must run on '$expected', but you are on '$current'."
        log_error "Run: git checkout $expected"
        exit 1
    fi
    log_success "On branch '$expected'"
}

# Require a clean working tree (no uncommitted changes)
require_clean_tree() {
    if [[ -n "$(git -C "$PROJECT_ROOT" status --porcelain)" ]]; then
        log_error "Working tree is not clean - commit or stash changes before releasing."
        git -C "$PROJECT_ROOT" status --short
        exit 1
    fi
    log_success "Working tree clean"
}

# Get current version from csproj (portable across BSD/GNU sed)
get_current_version() {
    sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$CSPROJ"
}

# Increment patch version (last number) - portable implementation
increment_patch() {
    local version="$1"
    local prefix="${version%.*}"
    local last="${version##*.}"
    local new_last=$((last + 1))
    echo "${prefix}.${new_last}"
}

# Validate version format (digits and dots)
validate_version() {
    local version="$1"
    if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+)*$ ]]; then
        log_error "Invalid version format: $version"
        log_error "Expected format: X.Y.Z or X.Y.Z.W (e.g., 1.0.1 or 1.0.0.5)"
        exit 1
    fi
}

# Portable sed in-place edit (handles BSD vs GNU sed differences)
sed_inplace() {
    local pattern="$1"
    local file="$2"

    if [[ "$PLATFORM" == "mac" ]]; then
        sed -i '' "$pattern" "$file"
    else
        sed -i "$pattern" "$file"
    fi
}

# Rename CHANGELOG's top "## [Unreleased]" section to "## [version] - DATE" (prep). If the
# version section already exists, leave it. If neither exists, abort - there are no notes.
rename_changelog_unreleased() {
    local version="$1"
    local today
    today=$(date +%Y-%m-%d)

    if grep -q "## \[$version\]" "$CHANGELOG"; then
        log_success "CHANGELOG.md already has a section for $version"
        return 0
    fi

    if ! grep -q "## \[Unreleased\]" "$CHANGELOG"; then
        log_error "CHANGELOG.md has neither a [Unreleased] nor a [$version] section."
        log_error "Add your notes under '## [Unreleased]' first (Keep a Changelog convention)."
        exit 1
    fi

    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would rename CHANGELOG '## [Unreleased]' -> '## [$version] - $today'"
    else
        sed_inplace "s|## \[Unreleased\]|## [$version] - $today|" "$CHANGELOG"
        log_success "Renamed CHANGELOG '## [Unreleased]' -> '## [$version] - $today'"
    fi
}

# Check CHANGELOG has an entry for version (publish-side guard)
check_changelog() {
    local version="$1"
    log_info "Checking CHANGELOG.md for version $version..."

    if grep -q "## \[$version\]" "$CHANGELOG"; then
        log_success "CHANGELOG.md has entry for $version"
        return 0
    else
        log_error "CHANGELOG.md missing entry for version $version"
        log_error "Expected a '## [$version]' section (the prep step adds it)."
        exit 1
    fi
}

# Check README for potential issues
check_readme() {
    local version="$1"
    log_info "Checking README.md..."

    local issues=0

    # Check for hardcoded old versions (common patterns)
    if grep -qE "version.*[0-9]+\.[0-9]+\.[0-9]+" "$README" 2>/dev/null; then
        local found_versions
        found_versions=$(grep -oE "[0-9]+\.[0-9]+\.[0-9]+" "$README" | sort -u | head -5)
        if [[ -n "$found_versions" ]]; then
            log_warn "README contains version numbers - verify they're correct:"
            echo "$found_versions" | while read v; do echo "  - $v"; done
        fi
    fi

    # Check download link points to releases
    if ! grep -q "releases/Cordyceps.gha" "$README"; then
        log_warn "README may be missing download link to releases/Cordyceps.gha"
        issues=$((issues + 1))
    fi

    if [[ $issues -eq 0 ]]; then
        log_success "README.md looks good"
    fi
}

# Update version in csproj
update_csproj_version() {
    local version="$1"
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would update $CSPROJ to version $version"
    else
        sed_inplace "s|<Version>[^<]*</Version>|<Version>$version</Version>|" "$CSPROJ"
        log_success "Updated csproj to version $version"
    fi
}

# Update version in the Yak manifest (copied into dist/ by prepare_dist)
update_manifest_version() {
    local version="$1"
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would update $MANIFEST to version $version"
    else
        sed_inplace "s|^version: .*|version: $version|" "$MANIFEST"
        log_success "Updated manifest to version $version"
    fi
}

# Build the GHA
build_gha() {
    log_info "Building Cordyceps..."
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would run: dotnet build -c Release"
    else
        dotnet build -c Release "$CSPROJ"
        log_success "Build completed"
    fi
}

# Prepare dist directory for Yak
prepare_dist() {
    log_info "Preparing distribution directory..."
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would create $DIST_DIR with GHA, manifest, and icon"
    else
        rm -rf "$DIST_DIR"
        mkdir -p "$DIST_DIR"
        cp "$RELEASES_DIR/Cordyceps.gha" "$DIST_DIR/"
        cp "$MANIFEST" "$DIST_DIR/"
        cp "$PROJECT_ROOT/src/Cordyceps/Resources/CordycepsIcon.png" "$DIST_DIR/icon.png"
        log_success "Distribution directory prepared"
    fi
}

# Build Yak package
build_yak() {
    local version="$1"
    log_info "Building Yak package..."
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would run: yak build --platform any --version $version"
    else
        local original_dir="$(pwd)"
        cd "$DIST_DIR"
        "$YAK" build --platform any --version "$version"
        cd "$original_dir"
        log_success "Yak package built"
    fi
}

# Commit the version bump + built .gha on the integration branch (prep)
commit_release() {
    local version="$1"
    log_info "Committing release bump..."
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would commit version bump + .gha as 'Release v$version' on $INTEGRATION_BRANCH"
        return 0
    fi

    # csproj + manifest (bumped) and the rebuilt .gha (the downloadable asset) ride to main
    # via the PR; CHANGELOG was renamed by rename_changelog_unreleased.
    git -C "$PROJECT_ROOT" add "$CSPROJ" "$MANIFEST" "$CHANGELOG" "$RELEASES_DIR/Cordyceps.gha"
    git -C "$PROJECT_ROOT" commit -m "Release v$version"
    log_success "Committed 'Release v$version'"
}

# Push the integration branch (prep)
push_integration_branch() {
    log_info "Pushing $INTEGRATION_BRANCH..."
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would push $INTEGRATION_BRANCH to origin"
    else
        git -C "$PROJECT_ROOT" push origin "$INTEGRATION_BRANCH"
        log_success "Pushed $INTEGRATION_BRANCH"
    fi
}

# Extract a CHANGELOG section body (everything under "## [version]" up to the next
# "## [" heading), dropping the heading line and any leading blank lines. Portable awk:
# index(...)==1 is a literal anchored match, so the bracketed version needs no escaping.
extract_changelog_notes() {
    local version="$1"
    awk -v ver="## [$version]" '
        index($0, ver) == 1 { f = 1; next }
        f && index($0, "## [") == 1 { exit }
        f { if (!started && $0 == "") next; started = 1; print }
    ' "$CHANGELOG"
}

# Open the develop->main release PR (prep). A release PR is an integration PR, not a feature
# PR, so it does not go through /prawduct:pr's feature gates (each feature was already reviewed
# on its own feature->develop PR). It does run CI (build-test) as a required check on main.
open_release_pr() {
    local version="$1"
    log_info "Opening $INTEGRATION_BRANCH -> $RELEASE_BRANCH release PR..."

    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would open PR $INTEGRATION_BRANCH -> $RELEASE_BRANCH titled 'Release v$version'"
        return 0
    fi

    # Re-use the existing PR if one is already open for this branch.
    local existing
    existing=$(gh pr list --head "$INTEGRATION_BRANCH" --base "$RELEASE_BRANCH" --state open \
        --json url --jq '.[0].url' 2>/dev/null || true)
    if [[ -n "$existing" ]]; then
        log_warn "A $INTEGRATION_BRANCH -> $RELEASE_BRANCH PR is already open: $existing"
        log_info "Merge it, then run: $0 publish $version"
        return 0
    fi

    local notes_file
    notes_file=$(mktemp)
    {
        echo "Release **v$version**."
        echo ""
        extract_changelog_notes "$version"
    } > "$notes_file"

    local pr_url
    if ! pr_url=$(gh pr create --base "$RELEASE_BRANCH" --head "$INTEGRATION_BRANCH" \
        --title "Release v$version" --body-file "$notes_file"); then
        rm -f "$notes_file"
        log_error "gh pr create failed - open the $INTEGRATION_BRANCH -> $RELEASE_BRANCH PR manually."
        exit 1
    fi
    rm -f "$notes_file"
    log_success "Release PR opened: $pr_url"
}

# Tag the release commit on main and push ONLY the tag (branch protection allows tag pushes)
tag_and_push() {
    local version="$1"
    log_info "Tagging v$version and pushing the tag..."
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would tag v$version on $RELEASE_BRANCH and push the tag (not the branch)"
        return 0
    fi

    if git -C "$PROJECT_ROOT" rev-parse "v$version" &> /dev/null; then
        log_warn "Tag v$version already exists locally - skipping tag creation"
    else
        git -C "$PROJECT_ROOT" tag -a "v$version" -m "Release v$version"
    fi
    git -C "$PROJECT_ROOT" push origin "v$version"
    log_success "Tag v$version pushed"
}

# Create the GitHub Release from the pushed tag, attaching the .gha and CHANGELOG notes
create_github_release() {
    local version="$1"
    log_info "Creating GitHub Release v$version..."

    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would create GitHub Release v$version with releases/Cordyceps.gha attached"
        return 0
    fi

    # A tag can be pushed without a Release object; re-running must not hard-fail.
    if gh release view "v$version" &> /dev/null; then
        log_warn "GitHub Release v$version already exists - skipping creation"
        return 0
    fi

    local notes_file
    notes_file=$(mktemp)
    extract_changelog_notes "$version" > "$notes_file"

    if [[ ! -s "$notes_file" ]]; then
        rm -f "$notes_file"
        log_error "Could not extract CHANGELOG notes for $version (empty section?)"
        exit 1
    fi

    # Tested condition: failure is handled here (not via set -e), so cleanup always runs.
    if ! gh release create "v$version" \
        "$RELEASES_DIR/Cordyceps.gha#Cordyceps.gha" \
        --title "v$version" \
        --notes-file "$notes_file" \
        --latest; then
        rm -f "$notes_file"
        log_error "gh release create failed for v$version (the tag was pushed; create the Release manually or re-run)"
        exit 1
    fi

    rm -f "$notes_file"
    log_success "GitHub Release v$version published"
}

# Push to Yak
yak_push() {
    local version="$1"
    log_info "Pushing to Yak package manager..."

    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would run: yak push cordyceps-$version-any.yak"
        return
    fi

    local yak_file
    yak_file=$(find "$DIST_DIR" -name "*.yak" 2>/dev/null | head -1)

    if [[ -z "$yak_file" ]]; then
        log_error "No .yak file found in $DIST_DIR"
        exit 1
    fi

    log_info "Pushing $yak_file..."
    "$YAK" push "$yak_file"
    log_success "Published to Yak package manager"
}

# ----------------------------------------------------------------------------
# prep: on develop, bump + CHANGELOG + build + commit + push + open release PR
# ----------------------------------------------------------------------------
do_prep() {
    echo ""
    echo "=========================================="
    echo "    Cordyceps Release - PREP"
    echo "=========================================="
    echo ""

    require_branch "$INTEGRATION_BRANCH"
    require_clean_tree
    ensure_dotnet
    ensure_gh   # opens the PR

    CURRENT_VERSION=$(get_current_version)
    log_info "Current version: $CURRENT_VERSION"

    if [[ -z "$NEW_VERSION" ]]; then
        NEW_VERSION=$(increment_patch "$CURRENT_VERSION")
        log_info "Auto-incrementing to: $NEW_VERSION"
    else
        validate_version "$NEW_VERSION"
        log_info "Setting version to: $NEW_VERSION"
    fi

    if [[ "$NEW_VERSION" == "$CURRENT_VERSION" ]]; then
        log_error "New version must differ from current version ($CURRENT_VERSION)"
        exit 1
    fi

    echo ""
    log_info "Running pre-flight checks..."
    # rename_changelog_unreleased already aborts when there are no notes (neither a
    # [version] nor an [Unreleased] section), so a separate check_changelog here would be
    # redundant in the real path and wrong under --dry-run (the rename only logs, so the
    # [version] section wouldn't exist yet). publish keeps its check_changelog.
    rename_changelog_unreleased "$NEW_VERSION"
    check_readme "$NEW_VERSION"
    echo ""

    if [[ "$DRY_RUN" == true ]]; then
        log_warn "DRY RUN MODE - No changes will be made"
        echo ""
    fi

    update_csproj_version "$NEW_VERSION"
    update_manifest_version "$NEW_VERSION"
    build_gha
    commit_release "$NEW_VERSION"
    push_integration_branch
    open_release_pr "$NEW_VERSION"

    echo ""
    echo "=========================================="
    if [[ "$DRY_RUN" == true ]]; then
        log_success "Dry run (prep) completed - no changes made"
    else
        log_success "Prep for v$NEW_VERSION completed!"
        echo ""
        echo "  Next: review + merge the release PR (CI 'build-test' must pass), then run:"
        echo "    $0 publish $NEW_VERSION"
    fi
    echo "=========================================="
    echo ""
}

# ----------------------------------------------------------------------------
# publish: on main (after the prep PR merged), build yak + tag + GH Release + yak push
# ----------------------------------------------------------------------------
do_publish() {
    echo ""
    echo "=========================================="
    echo "    Cordyceps Release - PUBLISH"
    echo "=========================================="
    echo ""

    # Find Yak CLI (publish needs it)
    if ! find_yak; then
        log_error "Yak CLI not found. Make sure Rhino 8 is installed."
        case "$(uname -s)" in
            Darwin*) log_error "Expected at: /Applications/Rhino 8.app/Contents/Resources/bin/yak" ;;
            MINGW*|MSYS*|CYGWIN*) log_error "Expected at: C:\\Program Files\\Rhino 8\\System\\yak.exe" ;;
        esac
        exit 1
    fi
    log_info "Platform: $PLATFORM"
    log_info "Yak CLI: $YAK"

    require_branch "$RELEASE_BRANCH"
    require_clean_tree
    ensure_gh
    ensure_yak_login

    # Version comes from main (the prep PR already bumped it); an explicit arg must match.
    CURRENT_VERSION=$(get_current_version)
    if [[ -z "$NEW_VERSION" ]]; then
        NEW_VERSION="$CURRENT_VERSION"
        log_info "Publishing version from $RELEASE_BRANCH csproj: $NEW_VERSION"
    else
        validate_version "$NEW_VERSION"
        if [[ "$NEW_VERSION" != "$CURRENT_VERSION" ]]; then
            log_error "Requested $NEW_VERSION but $RELEASE_BRANCH csproj is at $CURRENT_VERSION."
            log_error "Did the prep PR ($INTEGRATION_BRANCH -> $RELEASE_BRANCH) merge and did you 'git pull'?"
            exit 1
        fi
    fi

    echo ""
    log_info "Running pre-flight checks..."
    check_changelog "$NEW_VERSION"
    echo ""

    if [[ "$DRY_RUN" == true ]]; then
        log_warn "DRY RUN MODE - No changes will be made"
        echo ""
    fi

    prepare_dist
    build_yak "$NEW_VERSION"
    tag_and_push "$NEW_VERSION"
    create_github_release "$NEW_VERSION"
    yak_push "$NEW_VERSION"

    echo ""
    echo "=========================================="
    if [[ "$DRY_RUN" == true ]]; then
        log_success "Dry run (publish) completed - no changes made"
    else
        log_success "Release v$NEW_VERSION published!"
        echo ""
        echo "  GitHub: https://github.com/brookstalley/cordyceps/releases/tag/v$NEW_VERSION"
        echo "  Yak:    https://yak.rhino3d.com/packages/cordyceps"
    fi
    echo "=========================================="
    echo ""
}

# Parse arguments: <subcommand> [VERSION] [--dry-run]
while [[ $# -gt 0 ]]; do
    case $1 in
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        prep|publish)
            SUBCOMMAND="$1"
            shift
            ;;
        *)
            if [[ -z "$NEW_VERSION" ]]; then
                NEW_VERSION="$1"
            else
                log_error "Unexpected argument: $1"
                usage
                exit 1
            fi
            shift
            ;;
    esac
done

# Default PLATFORM for sed_inplace when publish's find_yak hasn't run (prep path)
case "$(uname -s)" in
    Darwin*) PLATFORM="${PLATFORM:-mac}" ;;
    MINGW*|MSYS*|CYGWIN*) PLATFORM="${PLATFORM:-windows}" ;;
    *) PLATFORM="${PLATFORM:-unknown}" ;;
esac

# Prerequisites common to both halves
if [[ ! -f "$CSPROJ" ]]; then
    log_error "Cannot find $CSPROJ"
    exit 1
fi

case "$SUBCOMMAND" in
    prep) do_prep ;;
    publish) do_publish ;;
    "")
        log_error "Missing command. Expected 'prep' or 'publish'."
        echo ""
        usage
        exit 1
        ;;
    *)
        log_error "Unknown command: $SUBCOMMAND"
        usage
        exit 1
        ;;
esac
