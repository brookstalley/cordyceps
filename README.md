# Cordyceps

**MCP server for Grasshopper.** Give AI agents or scripts direct control over your parametric design canvas.

## Requirements

- **Rhino 8.21+** (requires .NET 8)
- **MCP client with Streamable HTTP**: Claude Code, Cursor, VS Code Copilot, or any compatible client

## Quick Start

**Install**: Copy `releases/Cordyceps.gha` to your Grasshopper components folder. In Grasshopper: *File → Special Folders → Components Folder*.

**Start**: Drop the Cordyceps component on your canvas (*Params → Util → Cordyceps*). The server starts on port 26929 by default—change it via the Port input if needed.

**Connect**: Configure your MCP client:

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
    slider = await session.call_tool('add_component', {'type': 'Number Slider', 'x': 50, 'y': 50})
    circle = await session.call_tool('add_component', {'type': 'Curve/Circle', 'x': 200, 'y': 50})
    await session.call_tool('connect_components', {
        'sourceId': slider['id'], 'sourceParam': '0',
        'targetId': circle['id'], 'targetParam': 'R'
    })
```

## Tools

**Canvas**: `add_component`, `delete_component`, `move_component`, `search_components`, `get_all_components`

**Wiring**: `connect_components`, `disconnect_components`, `bulk_connect`, `validate_connection`

**Values**: `set_component_value`, `configure_value_list`, `add_constant`

**Scripts**: `set_script_code`, `configure_script_component`

**Groups**: `create_group`, `add_to_group`, `move_group`, `get_all_groups`

**Inspection**: `get_canvas_status`, `get_disconnected_inputs`, `trace_data_flow`, `get_component_outputs`

**Documents**: `new_document`, `save_document`, `load_document`, `snapshot`, `revert_snapshot`

**Execution**: `set_solver_enabled`, `recompute_solution`, `bake_geometry`, `execute_script`

## Resources

MCP resources provide documentation to clients:

- `gh://docs/getting-started` — Workflow and key concepts
- `gh://docs/data-trees` — Grasshopper's data tree system
- `gh://component/{name}` — Documentation for any component

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Plugin won't load | Verify Rhino 8.21+. Unblock the .gha file (Windows) or clear quarantine (macOS). |
| Can't connect | Ensure Cordyceps component is on canvas. Check the port. |
| Component not found | Use `search_components` to find exact names. |

## Building

```bash
dotnet build src/Cordyceps/Cordyceps.csproj
```

## Acknowledgments

Inspired by [grasshopper-mcp](https://github.com/alfredatnycu/grasshopper-mcp) by Alfred Chen.

## License

MIT
