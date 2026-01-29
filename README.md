# Cordyceps

**MCP server for Grasshopper.** Give AI agents or scripts direct control over your parametric design canvas.

## Requirements

- **Rhino 8.21+** (requires .NET 8)
- **MCP client with Streamable HTTP**: Claude Code, Cursor, VS Code Copilot, or any compatible client

## Quick Start

**[Download Cordyceps.gha](https://github.com/brookstalley/cordyceps/raw/main/releases/Cordyceps.gha)**

**Install**: Copy `Cordyceps.gha` to your Grasshopper components folder. In Grasshopper: *File → Special Folders → Components Folder*.

* You may need to unblock the file before running. Windows: right click Cordyceps.gha -> properties -> Unblock. 

**Start**: Drop the Cordyceps component on your canvas (*Params → Util → Cordyceps*). The server starts on port 26929 by default—change it via the Port input if needed.

**Connect**: Configure your MCP client:

*Claude Code (command line):*
```cmd
claude mcp add --transport http http://127.0.0.1/mcp
```

*Most others (config file):*
```json
{
  "mcpServers": {
    "grasshopper": {
      "type": "streamable-http",
      "url": "http://127.0.0.1:26929/mcp"
    }
  }
}
```

## Usage

**Natural language**: Tell an AI what you want—*"Create a radial array of cylinders with sliders for count and radius"*—and it builds the definition using MCP tools.

**Scripting**: Call tools directly from Python or any MCP client:

```python
async with ClientSession(transport) as session:
    slider = await session.call_tool('gh_add_component', {'type': 'Number Slider', 'x': 50, 'y': 50})
    circle = await session.call_tool('gh_add_component', {'type': 'Curve/Circle', 'x': 200, 'y': 50})
    await session.call_tool('gh_connect', {
        'sourceId': slider['id'], 'sourceParam': '0',
        'targetId': circle['id'], 'targetParam': 'R'
    })
```

## Example

Here's what happens when you give Claude this natural language prompt:

> Create an array of cylinders radiating out from the origin on the XY plane, with settings for number of cylinders, length and diameter of the cylinders, and distance from origin. Then make copies of the whole array in Z, with additional settings for number of copies and distance between copies.

![Radial Cylinder Array Animation](images/cylinder_array_build.gif)

The AI interprets the request and builds the complete Grasshopper definition step by step—adding sliders for parameters, dividing a circle to get radial positions, creating direction vectors, generating line axes for each cylinder, piping those lines into solid cylinders, and copying the array in Z.

## Tools

### Grasshopper Canvas

**Discovery**: `gh_get_categories`, `gh_search_components`, `gh_get_component_documentation`, `gh_check_deprecation`, `gh_suggest_connections`

**Canvas**: `gh_add_component`, `gh_delete` (single or bulk via `ids` array), `gh_move` (single or bulk via `moves` array), `gh_rename`, `gh_get_all_components`, `gh_get_component_info`, `gh_find_by_nickname`, `gh_get_bounds`, `gh_validate_layout`, `gh_manage_inputs`

**Wiring**: `gh_connect` (single or bulk via `connections` array), `gh_disconnect`, `gh_clear_inputs`, `gh_validate_connection`, `gh_get_connections`

**Values**: `gh_set_value`, `gh_set_slider`, `gh_configure_value_list`, `gh_add_constant`, `gh_set_preview` (single or bulk via `ids` array), `gh_set_enabled` (single or bulk via `ids` array), `gh_bake`

**Scripts**: `gh_set_script_code`, `gh_get_script_code`, `gh_get_script_info`, `gh_configure_script`

**Groups**: `gh_create_group`, `gh_add_to_group`, `gh_remove_from_group`, `gh_delete_group`, `gh_rename_group`, `gh_set_group_color`, `gh_move_group`, `gh_get_all_groups`

**Inspection**: `gh_get_canvas_status`, `gh_get_disconnected_inputs`, `gh_trace_data_flow`, `gh_get_component_outputs`, `gh_get_geometry`, `gh_get_debug_reports`, `gh_get_debug_log`, `gh_clear_debug_log`

**Capture**: `gh_capture_canvas`, `gh_capture_canvas_region`, `gh_get_available_views`

**Documents**: `gh_get_document_info`, `gh_save_document`, `gh_clear_document`, `gh_set_solver_enabled`, `gh_recompute_solution`, `gh_undo`, `gh_redo`

**Snapshots**: `gh_snapshot`, `gh_revert_snapshot`, `gh_list_snapshots`, `gh_delete_snapshot`

**Execution**: `rhino_execute_script`, `rhino_run_python`, `rhino_create_macro`, `rhino_run_macro`, `rhino_list_macros`, `rhino_delete_macro`

### Rhino Document

**Objects**: `rhino_get_objects`, `rhino_select_objects`, `rhino_deselect_all`, `rhino_set_object_layer`, `rhino_set_object_name`, `rhino_hide_objects`, `rhino_show_objects`, `rhino_delete_objects`

**Layers**: `rhino_get_layers`, `rhino_create_layer`, `rhino_set_layer_properties`, `rhino_delete_layer`

**Materials**: `rhino_get_materials`, `rhino_create_material`, `rhino_apply_material`, `rhino_delete_material`

**Environments**: `rhino_get_environments`, `rhino_get_current_environment`, `rhino_set_current_environment`, `rhino_create_environment`, `rhino_delete_environment`

**Render Settings**: `rhino_get_render_settings`, `rhino_set_render_settings`, `rhino_get_ground_plane`, `rhino_set_ground_plane`, `rhino_get_sun`, `rhino_set_sun`, `rhino_get_skylight`, `rhino_set_skylight`

**Viewport**: `rhino_get_display_modes`, `rhino_set_display_mode`, `rhino_get_camera`, `rhino_set_camera`, `rhino_zoom_extents`, `rhino_zoom_objects`, `rhino_get_render_status`, `rhino_wait_for_render`, `capture_viewport`

## Resources

MCP resources provide documentation to clients. Source files are in [`src/Cordyceps/Knowledge/`](src/Cordyceps/Knowledge/).

**Guides:**
- `gh://docs/getting-started` — [GettingStartedGuide.md](src/Cordyceps/Knowledge/GettingStartedGuide.md) — Workflow and key concepts
- `gh://docs/data-trees` — [DataTreesGuide.md](src/Cordyceps/Knowledge/DataTreesGuide.md) — Grasshopper's data tree system
- `gh://docs/type-system` — [TypeSystemGuide.md](src/Cordyceps/Knowledge/TypeSystemGuide.md) — Type compatibility and coercion
- `gh://docs/best-practices` — [BestPracticesGuide.md](src/Cordyceps/Knowledge/BestPracticesGuide.md) — Patterns and recommendations
- `gh://docs/component-patterns` — [ComponentPatternsGuide.md](src/Cordyceps/Knowledge/ComponentPatternsGuide.md) — Common component combinations
- `gh://docs/canvas-layout` — [CanvasLayoutGuide.md](src/Cordyceps/Knowledge/CanvasLayoutGuide.md) — Spacing and layout conventions
- `gh://docs/geometry-orientation` — [GeometryOrientationGuide.md](src/Cordyceps/Knowledge/GeometryOrientationGuide.md) — Planes and orientation
- `gh://docs/mcp-testing` — [McpTestingGuide.md](src/Cordyceps/Knowledge/McpTestingGuide.md) — Test and validate MCP server functionality
- `gh://docs/rendering` — [RenderingGuide.md](src/Cordyceps/Knowledge/RenderingGuide.md) — Rhino rendering pipeline (bake, materials, viewport, capture)

**Patterns:**
- `gh://patterns/linear-array` — [LinearArray.md](src/Cordyceps/Knowledge/Patterns/LinearArray.md) — Copies along a line
- `gh://patterns/grid-array` — [GridArray.md](src/Cordyceps/Knowledge/Patterns/GridArray.md) — 2D/3D grid of copies

**Dynamic:**
- `gh://component/{name}` — Documentation for any Grasshopper component

## Testing

To validate Cordyceps is working correctly, ask your AI assistant to "test the MCP server" or "help me test Grasshopper". It will read the comprehensive test instructions at `gh://docs/mcp-testing` and run through all functionality areas, reporting any issues found.

Test coverage includes: component management, wiring, values, groups, scripts, inspection, document operations, Rhino objects/layers/materials, render environments and settings, viewport control, and infrastructure protection.

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Plugin won't load | Verify Rhino 8.21+. Unblock the .gha file (Windows) or clear quarantine (macOS). |
| Can't connect | Ensure Cordyceps component is on canvas. Check the port. |
| Component not found | Use `gh_search_components` to find exact names. |

## Building

```bash
dotnet build src/Cordyceps/Cordyceps.csproj
```

## Acknowledgments

Inspired by [grasshopper-mcp](https://github.com/alfredatnycu/grasshopper-mcp) by Alfred Chen.

## License

MIT
