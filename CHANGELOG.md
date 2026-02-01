# Changelog

All notable changes to Cordyceps will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.4.3] - 2026-01-31

### Added

- **Object display color** - New `rhino_scene(action='set_color', ids='[...]', color='#FF0000')` action to set per-object display colors
- **Bounding box calculation** - New `rhino_scene(action='bbox', ids='[...]')` action returns combined bounding box with min, max, center, and size
- **Standard view presets** - New `preset` parameter on `rhino_render(action='camera')` for quick standard views: top, bottom, front, back, left, right, perspective, iso_nw, iso_ne, iso_sw, iso_se
- **Named views** - Save and restore camera positions:
  - `rhino_render(action='view_save', name='MyView')` - save current view
  - `rhino_render(action='view_load', name='MyView')` - restore saved view
  - `rhino_render(action='view_list')` - list all named views
  - `rhino_render(action='view_delete', name='MyView')` - delete a named view
- **Scene lighting** - Full control over scene lights:
  - `rhino_render(action='light_add', type='point', location='10,10,20')` - create point, spot, or directional lights
  - `rhino_render(action='light_list')` - list all lights
  - `rhino_render(action='light_set', ids='[...]', intensity='2.0')` - modify light properties
  - `rhino_render(action='light_delete', ids='[...]')` - delete lights

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
- CordycepsComponent now includes a DebugLevel input (Info, Warn, Error, Debug)
- Each HTTP request logs level, method, and endpoint
- Debug mode provides detailed timing and parameter information

### Changed

- **Consolidated to 7 unified tools** with action-based dispatch:
  - `gh_canvas` - Component operations (add, delete, move, find, search, list, info, bounds, validate, constant, bake, zoom, view, get, set, config, preview, enable, group_*)
  - `gh_wire` - Connection operations (connect, disconnect, validate)
  - `gh_document` - Document operations (info, save, clear, solver, recompute, undo, redo, snapshot, revert, snapshots, capture_*)
  - `gh_script` - Script component operations (get, set, configure, info)
  - `gh_inspect` - Inspection operations (status, outputs, warnings, trace, debug, log)
  - `rhino_scene` - Scene operations (objects, select, deselect, set_layer, set_name, layers, layer_*, hide, show, delete, script)
  - `rhino_render` - Render operations (display, camera, zoom, modes, render, settings, ground, sun, skylight, material_*, env_*)

### Deprecated

- Individual tools (gh_add, gh_connect, gh_info, etc.) replaced by unified action-based tools

## [1.3.0] - 2026-01-29

### Added

- Rhino scene and render tools for object management and viewport control

## [1.2.0] - 2026-01-28

### Added

- Script component support (C# and Python)
- Capture tools for canvas and viewport screenshots

## [1.1.0] - 2026-01-27

### Added

- Group management tools
- Undo/redo support with snapshots

## [1.0.0] - 2026-01-26

### Added

- Initial release
- Core Grasshopper component operations
- MCP server implementation
