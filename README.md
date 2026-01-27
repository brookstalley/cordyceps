# Cordyceps 🍄

**Claude takes control of Grasshopper.**

Cordyceps is a Grasshopper plugin that gives AI agents (like Claude) direct control over your parametric design canvas via the [Model Context Protocol](https://modelcontextprotocol.io/). No Python bridge, no middleware, no fuss—just drop the plugin in Rhino and let your AI collaborator add components, wire connections, configure scripts, and build definitions alongside you.

> *"The fungus that controls the host."* — Named after the [parasitic fungus](https://en.wikipedia.org/wiki/Cordyceps) that manipulates insect behavior. Except here, the manipulation is consensual and helpful. Mostly.

## What's This All About?

Imagine telling Claude: *"Create a parametric facade pattern with hexagonal cells that vary in size based on attractor points"*—and watching it build the Grasshopper definition in real-time. That's Cordyceps.

It exposes **74 MCP tools** that let AI agents:
- Add and configure any Grasshopper component
- Create and manage wiring between components
- Set slider values, configure value lists, write script components
- Inspect canvas state, trace data flow, debug errors
- Organize definitions with groups and proper layout
- Bake geometry to Rhino
- And much more

## ⚠️ Requirements

**Rhino 8.21 or later** — Cordyceps requires .NET 8, which shipped with Rhino 8.21. Earlier versions won't load the plugin.

**An MCP client with SSE support** — Cordyceps is a *pure SSE (Server-Sent Events) MCP server*. It runs an HTTP server directly from the Grasshopper plugin. You'll need an MCP client that can connect to SSE endpoints. [Claude Code](https://docs.anthropic.com/en/docs/claude-code) works great.

**Not compatible with Claude Desktop's stdio mode** — Claude Desktop expects MCP servers to communicate via stdin/stdout as subprocess. Cordyceps runs inside Rhino's process. Use Claude Code or another SSE-capable client.

## Quick Start

### 1. Install the Plugin

Copy `Cordyceps.gha` from the `releases/` folder to your Grasshopper libraries folder:

**Windows:**
```
%APPDATA%\Grasshopper\Libraries\
```

**macOS:**
```
~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper (b45a29b1-4343-4035-989e-044e8580d9cf)/Libraries/
```

If Grasshopper complains about an unsigned plugin, right-click the file → Properties → Unblock (Windows) or clear the quarantine flag (macOS).

### 2. Start Rhino & Grasshopper

Launch Rhino 8, then open Grasshopper. Drop the **Cordyceps** component onto your canvas (find it under the Params tab, Util section, or search "Cordyceps").

When the component loads, you'll see a message in the Rhino command line:

```
Cordyceps: MCP server started on http://127.0.0.1:8080
Cordyceps: SSE endpoint: http://127.0.0.1:8080/sse
```

### 3. Connect Your MCP Client

For **Claude Code**, configure the MCP server in your settings:

```json
{
  "mcpServers": {
    "grasshopper": {
      "url": "http://127.0.0.1:8080/sse"
    }
  }
}
```

That's it. Claude can now manipulate Grasshopper.

## What Can Claude Actually Do?

Here are **real prompts** you can use (all actually work with Cordyceps):

### Basic Geometry
> "Add a circle with radius 5 centered at the origin"

> "Create a slider from 0 to 10 and connect it to a circle's radius"

### Parametric Patterns
> "Build a radial array of 12 boxes around a center point. Add a slider to control the radius."

> "Create a grid of points, 5x5, with adjustable X and Y spacing"

### Working with Scripts
> "Add a C# script component that takes a list of points and outputs their centroid"

> "Create a Python script that filters a list of numbers to only include values greater than a threshold input"

### Canvas Management
> "Group all the components on the left side of the canvas and label the group 'Input Parameters'"

> "Check the canvas for any disconnected inputs or components with errors"

> "Auto-space the components horizontally with 150px gaps"

### Debugging & Inspection
> "What components are producing errors? Show me the error messages."

> "Trace the data flow upstream from the Brep component—what's feeding into it?"

> "Get the geometry output from the Mesh component—how many faces does it have?"

### Complex Workflows
> "I want to create a Voronoi pattern on a surface. Let's start with a surface input parameter, sample points on it, and generate a 3D Voronoi."

> "Set up a sunlight analysis workflow: create a sun path component, connect it to a mesh shadow calculation, and output the results to a panel"

### What Claude Can't Do (Yet)
- Directly import external files (use Rhino commands via `execute_script`)
- Interact with the Rhino viewport camera
- Run Grasshopper clusters/user objects

## Available Tools

Cordyceps exposes these tool categories:

### Canvas Operations
| Tool | Description |
|------|-------------|
| `add_component` | Add any component by name/GUID, optionally set nickname |
| `delete_component` | Remove a component |
| `move_component` | Reposition on canvas |
| `bulk_move_components` | Move multiple components at once |
| `rename_component` | Change a component's nickname |
| `search_components` | Find available component types |
| `get_component_info` | Detailed component inspection |
| `get_component_by_nickname` | Find component(s) by nickname |
| `get_all_components` | List everything on canvas |

### Wiring
| Tool | Description |
|------|-------------|
| `connect_components` | Create a wire |
| `disconnect_components` | Remove a wire |
| `bulk_connect` | Multiple connections efficiently |
| `validate_connection` | Check type compatibility before connecting |
| `get_connections` | List all wires |
| `clear_component_inputs` | Remove all incoming wires |

### Values & Parameters
| Tool | Description |
|------|-------------|
| `set_component_value` | Set slider, panel, or param values |
| `configure_value_list` | Set up dropdown items |
| `add_constant` | Quick panel with preset value |
| `get_component_parameters` | Get input/output specs for a component type |

### Script Components
| Tool | Description |
|------|-------------|
| `set_script_code` | Set C#/Python source code |
| `configure_script_component` | Full control: inputs, outputs, types, code |

### Groups & Layout
| Tool | Description |
|------|-------------|
| `create_group` | Make a visual group |
| `add_to_group` | Add components to group |
| `remove_from_group` | Remove from group |
| `set_group_color` | Color the group |
| `validate_layout` | Check for overlaps |
| `auto_space_components` | Fix spacing automatically |

### Inspection & Debugging
| Tool | Description |
|------|-------------|
| `get_canvas_status` | Status of all components (OK/ERROR/WARNING) |
| `get_disconnected_inputs` | Find unconnected required inputs |
| `trace_data_flow` | Follow connections upstream/downstream |
| `get_component_outputs` | See actual output data |
| `get_geometry` | Bounding boxes, vertex counts, validity |
| `get_debug_reports` | Script component output/reports |

### Document Operations
| Tool | Description |
|------|-------------|
| `get_document_info` | Canvas metadata |
| `new_document` | Fresh canvas |
| `save_document` | Save to .gh/.ghx |
| `load_document` | Load from file |
| `clear_document` | Remove everything |

### Execution
| Tool | Description |
|------|-------------|
| `recompute_solution` | Force recalculation |
| `set_solver_enabled` | Pause/resume the solver |
| `execute_script` | Run Rhino commands |
| `run_gh_python` | Execute Python in Rhino |
| `bake_geometry` | Send geometry to Rhino document |

### Snapshots
| Tool | Description |
|------|-------------|
| `snapshot` | Save canvas state |
| `revert_snapshot` | Restore previous state |
| `list_snapshots` | View available snapshots |

## MCP Resources

Cordyceps also exposes documentation as MCP resources that Claude can read:

- `gh://docs/data-trees` — Understanding Grasshopper's data tree system
- `gh://docs/canvas-layout` — Best practices for component layout
- `gh://docs/type-system` — Type compatibility and coercion
- `gh://docs/best-practices` — Common patterns and gotchas
- `gh://patterns/radial-array` — Step-by-step radial array pattern
- `gh://patterns/linear-array` — Linear array pattern
- `gh://patterns/grid-array` — 2D grid pattern
- `gh://component/{name}` — Documentation for any component

## Troubleshooting

### "Component won't load"
- Verify you're running **Rhino 8.21+** (Help → About)
- Unblock the .gha file (Windows) or clear quarantine (macOS)
- Check Grasshopper preferences → Libraries for blocked files

### "Can't connect to MCP server"
- Ensure the Cordyceps component is on your canvas
- Check Rhino command line for "MCP server started" message
- Verify port 8080 isn't in use; if it is, the component output shows the actual port
- Make sure your MCP client uses **SSE mode**, not stdio

### "Connection times out"
- The component must remain on the canvas—removing it stops the server
- Only one Grasshopper document can have the server running at a time

### "Components aren't found"
- Some components require specific plugins (e.g., LunchBox, Kangaroo)
- Use `search_components` to find exact component names
- Check for deprecated components with `check_deprecation`

## Building from Source

```bash
# Build the plugin (Release only—Debug is blocked)
dotnet build src/Cordyceps/Cordyceps.csproj

# Output goes to releases/Cordyceps.gha
```

Requirements:
- .NET 8.0 SDK
- Rhino 8 (for Grasshopper references)

## Architecture

Cordyceps runs entirely within Rhino's process:

```
┌─────────────────────────────────────────────┐
│                   Rhino 8                   │
│  ┌───────────────────────────────────────┐  │
│  │              Grasshopper              │  │
│  │  ┌─────────────────────────────────┐  │  │
│  │  │     Cordyceps Component         │  │  │
│  │  │  ┌───────────────────────────┐  │  │  │
│  │  │  │      MCP Server (SSE)     │◄─┼──┼──┼── Claude / MCP Client
│  │  │  │      http://127.0.0.1:8080│  │  │  │
│  │  │  └───────────────────────────┘  │  │  │
│  │  │         │                       │  │  │
│  │  │         ▼                       │  │  │
│  │  │  [Tool Classes: Canvas, Wiring, │  │  │
│  │  │   Scripts, Values, Groups, ...] │  │  │
│  │  │         │                       │  │  │
│  │  │         ▼                       │  │  │
│  │  │  ┌───────────────────────────┐  │  │  │
│  │  │  │   GrasshopperContext      │  │  │  │
│  │  │  │   (UI Thread Marshalling) │  │  │  │
│  │  │  └───────────────────────────┘  │  │  │
│  │  └─────────────────────────────────┘  │  │
│  │                  │                    │  │
│  │                  ▼                    │  │
│  │         [Grasshopper Canvas]          │  │
│  └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
```

All Grasshopper operations execute on the UI thread via `GrasshopperContext.ExecuteOnUiThread()`. The MCP server handles HTTP/SSE on background threads, then marshals tool calls appropriately.

## Acknowledgments

Cordyceps builds on concepts from [grasshopper-mcp](https://github.com/alfredatnycu/grasshopper-mcp) by **Alfred Chen**. That project pioneered the idea of MCP-controlled Grasshopper using a Python bridge architecture. Cordyceps takes a different approach—running the MCP server directly inside the Rhino process—but the inspiration and some architectural ideas came from Alfred's excellent work.

Thanks also to:
- The **Rhino and Grasshopper team** at McNeel for building such an extensible platform
- **Anthropic** for creating Claude and the Model Context Protocol

## License

MIT License. See [LICENSE](LICENSE) for details.

---

*Now go build something parametric with your new AI collaborator.* 🎨
