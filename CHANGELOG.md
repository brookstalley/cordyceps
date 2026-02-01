# Changelog

All notable changes to Cordyceps will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.4.3] - 2026-01-31

### Fixed

- **Script component type hints** - `gh_script(action='configure')` now correctly applies type hints to component parameters. Previously all parameters remained as "Generic Data" regardless of the types specified. Supports common types: int, double, bool, string, Point3d, Vector3d, Curve, Mesh, Brep, and more. (Closes #7)

## [1.4.2] - 2026-01-31

### Added

- **Zoomable parameter management** - New `gh_canvas(action='zoomable')` action for managing variable-count parameters (ZUI components like Merge, Addition, etc.). Supports adding, removing, and listing zoomable inputs/outputs.

### Fixed

- **Phantom objects in find/list actions** - Fixed bug where orphaned objects from undo history could appear in component queries. Added `IsActiveDocumentObject()` and `GetActiveObjects()` helpers to filter objects by verifying their document reference matches the current document.

## [1.4.1] - 2026-01-31

### Added

- **Common Errors Guide** (`gh://docs/common-errors`) - New resource with comprehensive error→solution reference

### Fixed

- **Deadlock prevention in RefreshComponent** - UI thread invocation now uses fire-and-forget pattern to avoid deadlock when UI thread is waiting on the component lock
- **Document mutex for concurrent requests** - Added SemaphoreSlim to GrasshopperContext to prevent race conditions when multiple HTTP requests try to modify the document simultaneously
- **Thread-safe DebugLevel property** - Added volatile modifier to prevent stale reads across HTTP worker and UI threads

### Changed

- **Prompt tool names updated** - All prompts now use unified tool names (gh_canvas, gh_wire, etc.) instead of deprecated individual tool names
- **Server instructions clarified** - Added "VERIFY PERIODICALLY" guidance to help LLMs catch collateral errors in components they didn't directly modify
- **Documentation cross-references** - Added links to common-errors guide from GettingStartedGuide and BestPracticesGuide
- **Fixed tool count** - GettingStartedGuide now correctly says "7 tools" instead of "8 tools"

## [1.4.0] - 2026-01-30

### Added

**DebugLevel Input Parameter**
- New `DebugLevel` integer input on the Cordyceps component (default: 0)
- Level 0: Only logs server start URL and stop messages to Rhino command history
- Level 1+: Logs all request/response traffic and detailed debugging info
- All messages are still captured internally and retrievable via `gh_inspect(action='log')` regardless of level

### Changed

**Tool Consolidation (12 → 7 tools)**

Further reduced tool count to minimize context window usage:

- Merged `gh_adjust` → `gh_canvas` (value operations: get, set, config, preview, enable)
- Merged `gh_group` → `gh_canvas` (group operations: group_create, group_delete, group_add, group_remove, group_list, group_rename, group_color, group_move)
- Merged `gh_capture` → `gh_document` (capture operations: capture_canvas, capture_viewport, capture_region, capture_views)
- Merged `rhino_material` → `rhino_render` (material operations: material_list, material_library, material_instantiate, material_create, material_apply, material_delete)
- Merged `rhino_environment` → `rhino_render` (environment operations: env_list, env_current, env_set, env_create, env_delete)

**Resulting 7 Tools:**
- `gh_canvas` - Components, values, groups (27 actions)
- `gh_wire` - Connections (5 actions)
- `gh_document` - Document operations and capture (14 actions)
- `gh_script` - Script components (4 actions)
- `gh_inspect` - Inspection and debugging (9 actions)
- `rhino_scene` - Objects and layers (13 actions)
- `rhino_render` - Viewport, materials, environments (20 actions)

**Knowledge Base**
- Refactored GettingStartedGuide to hub model (reduced from 218 to 88 lines)
- Updated RenderingGuide with new tool names

### Breaking Changes

**Tool names changed:** `gh_adjust`, `gh_group`, `gh_capture`, `rhino_material`, `rhino_environment` no longer exist. Their functionality is now available through actions on the remaining tools.

| Old Tool | New Location |
|----------|--------------|
| `gh_adjust(action='get')` | `gh_canvas(action='get')` |
| `gh_group(action='create')` | `gh_canvas(action='group_create')` |
| `gh_capture(action='canvas')` | `gh_document(action='capture_canvas')` |
| `rhino_material(action='create')` | `rhino_render(action='material_create')` |
| `rhino_environment(action='set')` | `rhino_render(action='env_set')` |

## [1.3.0] - 2026-01-30

### Added

**Unified Tool Architecture**
- Consolidated ~130+ individual tools into 12 action-based tools (90 total actions) for reduced context window usage
- Grasshopper tools: `gh_canvas`, `gh_wire`, `gh_adjust`, `gh_document`, `gh_group`, `gh_script`, `gh_inspect`, `gh_capture`
- Rhino tools: `rhino_scene`, `rhino_render`, `rhino_material`, `rhino_environment`

**Rhino Scene Management (`rhino_scene`)**
- Full object management: list, select, hide, show, delete objects
- Layer CRUD: create, list, modify, delete layers
- Object organization: move objects between layers, rename objects

**Rhino Rendering (`rhino_render`)**
- Display mode control (Shaded, Rendered, Raytraced, etc.)
- Camera positioning with location, target, and lens parameters
- Render settings: background styles, colors, gradients
- Ground plane, sun position, and skylight control
- Raytraced render progress monitoring with wait/timeout support

**PBR Materials (`rhino_material`)**
- Create custom PBR materials with color, metallic, roughness, transparency, emission, and IOR parameters
- Browse built-in material library (Metal, Glass, Plastic, Paint, Gem, Plaster, Emission, etc.)
- Instantiate materials from library with custom names and base colors
- Apply materials to objects, list, delete

**Render Environments (`rhino_environment`)**
- List, create, set, and delete render environments

**Baking Support**
- `gh_canvas(action='bake')` to bake Grasshopper geometry to Rhino

**Documentation**
- New `gh://docs/rendering` resource - complete rendering pipeline guide
- New patterns: GridArray, LinearArray

### Changed

- Updated Getting Started guide with new tool syntax
- Simplified MCP testing guide
- Improved CORS header handling with validated origin passthrough
- Better shutdown exception handling

### Fixed

- Multiple Cordyceps components on the canvas now properly detect port conflicts instead of interfering with each other

### Breaking Changes

**All tool names have changed** from individual verbs (e.g., `add_component`, `connect_components`) to a unified action-based pattern (e.g., `gh_canvas(action='add')`, `gh_wire(action='connect')`).

**Why this change?** MCP exposes tools to LLMs, and LLMs perform better with fewer, well-organized tools than with many specialized ones. The previous 25+ tools created decision fatigue and made it harder for LLMs to discover related functionality. The new 12-tool structure groups related operations logically:

- All component operations live under `gh_canvas`
- All wiring operations live under `gh_wire`
- All value/settings operations live under `gh_adjust`

This pattern reduces context window usage (fewer tool definitions to process) and matches how other MCP servers organize their tools.


