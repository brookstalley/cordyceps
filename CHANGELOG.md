# Changelog

All notable changes to Cordyceps will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

**Unified Tool Architecture**
- Consolidated ~25+ individual tools into 12 action-based tools (90 total actions) for reduced context window usage
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

## [0.1.0] - 2024-12-01

### Added

- Initial release
- MCP server with HTTP/SSE transport
- Grasshopper canvas manipulation tools
- Component wiring and value management
- Script component support (C#, Python)
- Document operations (save, clear, undo/redo, snapshots)
- Canvas and viewport capture
- Knowledge resources for LLM guidance
