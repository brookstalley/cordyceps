# Changelog

All notable changes to Cordyceps will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- **PBR texture maps** - New `rhino_render(action='material_texture')` action to assign image-based texture maps to PBR material slots (base-color, roughness, metallic, bump, opacity, emission, displacement, ambient-occlusion, clearcoat, clearcoat-roughness). Supports UV tiling via `repeat` parameter and slot influence via `amount`. Omit `path` to remove a texture from a slot.
- **Material texture inspection** - `rhino_render(action='material_list')` now reports which PBR slots have textures assigned per material.

### Fixed

- **MCP error contract honored at the server boundary** - Tool results carrying `{"success": false, ...}` are now reported to MCP clients with the transport `isError` flag set to `true` (previously hardcoded `false`, so failures looked like successes). Additionally, an exception thrown inside any tool method is now returned as a normal tool result (`isError: true`, body `{"success": false, "error": "<message>"}`) instead of escaping as a JSON-RPC `-32603` protocol error — making error reporting uniform across all seven tools. Genuine protocol errors (unknown tool, missing required parameter) still surface as JSON-RPC errors.
- **Script language preserved on `gh_script(set/configure)`** - Setting a script body without the Rhino 8 language directive (`#! python 3`, `// #! csharp`) no longer strips the component's language. Previously this caused "Can not determine input code language" at solve time and the component emitted no geometry — which bit anyone following the plain-body examples in the docs. The component's existing directive is now preserved automatically; a directive included in the new code is respected as-is.
- **`gh_inspect(action='docs')` flags failed introspection** - When a component's proxy can't be instantiated, the response now sets `paramsUnavailable: true` with an explanatory `note` instead of returning `success: true` with silently-empty `inputs`/`outputs`. Callers can now distinguish "component has no parameters" from "introspection failed" (the failure is also logged, retrievable via `gh_inspect(action='log')`). The field is absent when introspection succeeds, so the success-path response is unchanged.

## [1.4.9] - 2026-03-26

### Fixed

- **Type marshaling** - MCP clients that send string-encoded numbers (e.g., `"300"` instead of `300`) no longer cause errors. `ConvertJsonValue` now performs cross-type coercion for all primitive types. Fixes #13.

### Changed

- **Accurate MCP schema types** - Numeric tool parameters (`lens`, `wait`, `timeout`, `azimuth`, `sunAltitude`, `intensity`, `latitude`, `longitude`, `groundAltitude`, `shadowIntensity`, `spotAngle`, `xMin`/`yMin`/`xMax`/`yMax`, `limit`, `padding`, `width`, `height`) are now declared with their proper numeric types instead of `string`. The MCP schema now reports `"type": "number"` or `"type": "integer"` for these parameters.
- **JSON Schema integer distinction** - `GetJsonType` now maps `int`/`long` to `"integer"` (was `"number"`) per JSON Schema specification.

## [1.4.8] - 2026-02-10

### Changed

- **Developer documentation** - Added mandatory documentation audit checklist to CLAUDE.md. Every code change must now verify that tool help metadata, server instructions, knowledge base guides, resource registry, prompt templates, and CHANGELOG are updated as needed. Also documented the full user-facing documentation system (Knowledge/, ResourceRegistry, PromptRegistry, server instructions) in the Architecture section.

## [1.4.7] - 2026-02-05

### Fixed

- **Cluster-safe solution expiration** - Replaced all `ExpireSolution(true)` and `NewSolution(true)` calls with `ExpireSolution(false)` across all tools. When the cluster editor is open, the active document is the cluster's internal document; triggering a solution on it causes the parent to recreate the cluster, orphaning the editor and severing all `GH_ClusterInputHook` connections. Using `ExpireSolution(false)` marks the component as needing recompute without triggering a destructive solution cycle. Affects: `gh_canvas` (set, config, enable, add, delete, constant, zoomable), `gh_wire` (connect, disconnect, clear), `gh_script` (set, configure).

- **Surgical script parameter sync** - `gh_script(action='set')` no longer calls `SetParametersFromScript()`, which rebuilds all parameters from scratch and destroys cluster input hooks. Instead, uses LCS-based (longest common subsequence) diffing to identify unchanged parameters and only adds/removes what actually changed via `IGH_VariableParameterComponent`. Parameters whose names haven't changed retain all their connections.

## [1.4.6] - 2026-02-05

### Fixed

- **Cluster document corruption** - Fixed critical bug where modifying components inside clusters (scripts, values, wires, etc.) would corrupt cluster inputs, turning them all to null. Two issues fixed: (1) Operations now use the component's owning document via `OnPingDocument()` rather than assuming the active canvas document. (2) Changed `NewSolution(false)` to `NewSolution(true)` for incremental recomputation - the `false` parameter clears ALL volatile data including cluster input proxies. Affects: `gh_script`, `gh_canvas` (set, config, enable, delete, zoomable), `gh_wire` (connect, disconnect, clear).

## [1.4.5] - 2026-02-05

### Changed

- **README reorganization** - Improved structure and aesthetics:
  - Added Features section highlighting key capabilities
  - Added brief MCP explanation with link to modelcontextprotocol.io
  - Separated Installation from Usage with cleaner flow
  - Added Scripting section with Python example (MCP as protocol, not just for AI)
  - Collapsible sections for client-specific configuration
  - Simplified tool tables
  - Consolidated footer with Changelog link
  - Removed verbose resource URI listings

- **Knowledge base optimization** - 49% token reduction (1648 → 835 lines):
  - Converted verbose prose to tables throughout
  - Removed redundant tool parameter listings (use `action='help'`)
  - Eliminated basic Grasshopper knowledge LLMs already have
  - Consolidated duplicate workflow instructions
  - Optimized for LLM consumption, not human reading

## [1.4.4] - 2026-01-31

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

### Changed

- **Partial class refactoring** - Large tool files split into partial classes for maintainability:
  - `GhCanvasTool` → Groups.cs, Values.cs, Zoomable.cs
  - `RhinoRenderTool` → Lights.cs, Materials.cs, Settings.cs, Views.cs
  - `RhinoSceneTool` → Layers.cs
  - `GhDocumentTool` → Capture.cs

### Fixed

- **Script component type hints** - `gh_script(action='configure')` now correctly applies type hints to component parameters. Previously all parameters remained as "Generic Data" regardless of the types specified. Supports common types: int, double, bool, string, Point3d, Vector3d, Curve, Mesh, Brep, and more. (Closes #7)
- **Null reference in group operations** - Fixed potential null reference when matching groups by name/nickname in `gh_canvas` group actions
- **Null document in render wait** - Fixed potential null reference in `rhino_render(action='render')` wait loop when no document is active
- **Null light geometry** - Fixed potential null reference when modifying lights via `rhino_render(action='light_set')`
- **Consistent layer response fields** - Standardized layer visibility/locked field names across all layer actions

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
