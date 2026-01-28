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
#   1. Updates version in csproj and manifest.yml
#   2. Builds the GHA
#   3. Commits and pushes to GitHub with a version tag
#   4. Builds and pushes the Yak package
#
# Prerequisites:
#   - dotnet CLI
#   - git
#   - Rhino 8 installed (for yak CLI)
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CSPROJ="$PROJECT_ROOT/src/Cordyceps/Cordyceps.csproj"
MANIFEST="$PROJECT_ROOT/manifest.yml"
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

    # Try to get user info by attempting an operation that requires auth
    # The 'yak search' command works without auth, but 'yak push' requires it
    # We can check by looking for the token file or trying a test

    local token_found=false

    # Check for token file based on platform
    case "$PLATFORM" in
        mac)
            # macOS: Yak stores token in ~/Documents/.mcneel/yak.yml
            if [[ -f "$HOME/Documents/.mcneel/yak.yml" ]]; then
                token_found=true
            fi
            # Also check alternate locations
            if [[ -f "$HOME/.mcneel/yak.yml" ]]; then
                token_found=true
            fi
            ;;
        windows)
            # Windows: check Documents\.mcneel and AppData
            local docs_path
            docs_path=$(cygpath -u "$USERPROFILE/Documents" 2>/dev/null || echo "$USERPROFILE/Documents")
            if [[ -f "$docs_path/.mcneel/yak.yml" ]]; then
                token_found=true
            fi
            # Also check AppData
            local appdata_path
            appdata_path=$(cygpath -u "$APPDATA" 2>/dev/null || echo "$APPDATA")
            if [[ -f "$appdata_path/McNeel/yak.yml" ]]; then
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
    # Split by dots, increment last part
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
        # BSD sed requires an argument after -i
        sed -i '' "$pattern" "$file"
    else
        # GNU sed (Linux, Git Bash on Windows)
        sed -i "$pattern" "$file"
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

# Note: manifest.yml uses $version placeholder - version is passed to yak build

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
        # Copy icon for Yak package (referenced as icon.png in manifest)
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
        # Pass version from csproj - manifest.yml uses $version placeholder
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
        git add "$CSPROJ" "$RELEASES_DIR/Cordyceps.gha"
        git commit -m "Release v$version

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude Opus 4.5 <noreply@anthropic.com>"

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

# Push to Yak
yak_push() {
    local version="$1"
    log_info "Pushing to Yak package manager..."

    if [[ "$DRY_RUN" == true ]]; then
        log_info "[DRY-RUN] Would run: yak push cordyceps-$version-any.yak"
        return
    fi

    # Find the built .yak file
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
    if [[ "$DRY_RUN" == true ]]; then
        log_warn "DRY RUN MODE - No changes will be made"
        echo ""
    fi

    # Execute release steps
    update_csproj_version "$NEW_VERSION"
    build_gha
    prepare_dist
    build_yak "$NEW_VERSION"
    git_commit_and_tag "$NEW_VERSION"
    git_push "$NEW_VERSION"
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
