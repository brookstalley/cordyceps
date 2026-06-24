#!/bin/bash
#
# Cordyceps Release Script (Cross-platform: macOS, Windows via Git Bash)
#
# Usage:
#   ./scripts/release.sh           # Auto-increment patch version (1.0.0.5 -> 1.0.0.6)
#   ./scripts/release.sh 1.0.1.0   # Set specific version
#   ./scripts/release.sh --dry-run # Show what would happen without making changes
#
# This script:
#   1. Verifies CHANGELOG.md has an entry for the new version
#   2. Checks README.md for stale version references
#   3. Updates version in csproj
#   4. Builds the GHA
#   5. Commits and pushes to GitHub with a version tag
#   6. Creates a GitHub Release with the .gha attached and CHANGELOG notes
#   7. Builds and pushes the Yak package
#
# Prerequisites:
#   - dotnet CLI
#   - git
#   - gh CLI (GitHub CLI), authenticated (gh auth login) - creates the GitHub Release
#   - Rhino 8 installed (for yak CLI)
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

DRY_RUN=false
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

# Verify the GitHub CLI is installed and authenticated (used to create the Release)
ensure_gh() {
    if ! command -v gh &> /dev/null; then
        log_error "GitHub CLI ('gh') not found - it creates the GitHub Release."
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

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [VERSION] [--dry-run]"
            echo ""
            echo "Options:"
            echo "  VERSION     Specific version to set (e.g., 1.0.1.0)"
            echo "  --dry-run   Show what would happen without making changes"
            echo ""
            echo "If no version is specified, the patch number is auto-incremented."
            echo ""
            echo "Supported platforms: macOS, Windows (Git Bash)"
            exit 0
            ;;
        *)
            NEW_VERSION="$1"
            shift
            ;;
    esac
done

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

# Check CHANGELOG has entry for version
check_changelog() {
    local version="$1"
    log_info "Checking CHANGELOG.md for version $version..."

    if grep -q "## \[$version\]" "$CHANGELOG"; then
        log_success "CHANGELOG.md has entry for $version"
        return 0
    else
        log_error "CHANGELOG.md missing entry for version $version"
        log_error "Please add a section: ## [$version] - $(date +%Y-%m-%d)"
        log_error ""
        log_error "Recent CHANGELOG entries:"
        head -20 "$CHANGELOG" | tail -15
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

# Git commit and tag
git_commit_and_tag() {
    local version="$1"
    log_info "Committing and tagging..."

    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would commit version bump and tag as v$version"
    else
        # Add required files (manifest bumped by update_manifest_version must be committed too)
        git add "$CSPROJ" "$MANIFEST" "$RELEASES_DIR/Cordyceps.gha"

        # Add CHANGELOG if it was modified
        if git diff --cached --quiet "$CHANGELOG" 2>/dev/null || git diff --quiet "$CHANGELOG" 2>/dev/null; then
            # Check if CHANGELOG has unstaged changes
            if ! git diff --quiet "$CHANGELOG" 2>/dev/null; then
                git add "$CHANGELOG"
                log_info "Including CHANGELOG.md in commit"
            fi
        fi

        git commit -m "Release v$version"

        git tag -a "v$version" -m "Release v$version"
        log_success "Committed and tagged as v$version"
    fi
}

# Push to GitHub
git_push() {
    local version="$1"
    log_info "Pushing to GitHub..."
    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would push commits and tags to origin"
    else
        git push origin main
        git push origin "v$version"
        log_success "Pushed to GitHub"
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

# Main execution
main() {
    echo ""
    echo "=========================================="
    echo "       Cordyceps Release Script"
    echo "=========================================="
    echo ""

    # Find Yak CLI
    if ! find_yak; then
        log_error "Yak CLI not found."
        log_error "Make sure Rhino 8 is installed."
        case "$(uname -s)" in
            Darwin*)
                log_error "Expected at: /Applications/Rhino 8.app/Contents/Resources/bin/yak"
                ;;
            MINGW*|MSYS*|CYGWIN*)
                log_error "Expected at: C:\\Program Files\\Rhino 8\\System\\yak.exe"
                ;;
        esac
        exit 1
    fi

    log_info "Platform: $PLATFORM"
    log_info "Yak CLI: $YAK"

    # Check prerequisites
    if [[ ! -f "$CSPROJ" ]]; then
        log_error "Cannot find $CSPROJ"
        exit 1
    fi

    if ! command -v dotnet &> /dev/null; then
        log_error "dotnet CLI not found. Install .NET SDK."
        exit 1
    fi

    # Verify GitHub CLI early (a half-done release with no GitHub Release is worse than failing here)
    ensure_gh

    # Check Yak login status early
    ensure_yak_login

    # Get current version
    CURRENT_VERSION=$(get_current_version)
    log_info "Current version: $CURRENT_VERSION"

    # Determine new version
    if [[ -z "$NEW_VERSION" ]]; then
        NEW_VERSION=$(increment_patch "$CURRENT_VERSION")
        log_info "Auto-incrementing to: $NEW_VERSION"
    else
        validate_version "$NEW_VERSION"
        log_info "Setting version to: $NEW_VERSION"
    fi

    # Verify version is actually different
    if [[ "$NEW_VERSION" == "$CURRENT_VERSION" ]]; then
        log_error "New version must be different from current version"
        exit 1
    fi

    echo ""

    # Pre-flight checks
    log_info "Running pre-flight checks..."
    check_changelog "$NEW_VERSION"
    check_readme "$NEW_VERSION"
    echo ""

    if [[ "$DRY_RUN" == true ]]; then
        log_warn "DRY RUN MODE - No changes will be made"
        echo ""
    fi

    # Execute release steps
    update_csproj_version "$NEW_VERSION"
    update_manifest_version "$NEW_VERSION"
    build_gha
    prepare_dist
    build_yak "$NEW_VERSION"
    git_commit_and_tag "$NEW_VERSION"
    git_push "$NEW_VERSION"
    create_github_release "$NEW_VERSION"
    yak_push "$NEW_VERSION"

    echo ""
    echo "=========================================="
    if [[ "$DRY_RUN" == true ]]; then
        log_success "Dry run completed - no changes made"
    else
        log_success "Release v$NEW_VERSION completed!"
        echo ""
        echo "  GitHub: https://github.com/brookstalley/cordyceps/releases/tag/v$NEW_VERSION"
        echo "  Yak:    https://yak.rhino3d.com/packages/cordyceps"
    fi
    echo "=========================================="
    echo ""
}

main
