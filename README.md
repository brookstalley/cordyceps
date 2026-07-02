# Cordyceps

**MCP server for Grasshopper.** Give AI agents or scripts direct control over your parametric design canvas and Rhino rendering tools.

[Model Context Protocol (MCP)](https://modelcontextprotocol.io/) provides a standardized interface for controlling applications — whether from AI assistants or your own code.

## Features

- **Full Grasshopper control** — add components, wire connections, set values, manage groups
- **Rhino integration** — bake geometry, manage layers, apply PBR materials, render scenes
- **Natural language** — describe what you want and let AI build it
- **Direct scripting** — call tools from Python or any MCP client, no AI required

## Requirements

- **Rhino 8.21+** (requires .NET 8)
- **For AI use**: Claude Desktop, Claude Code, Cursor, VS Code, or any MCP-compatible assistant
- **For scripting**: Any MCP client library ([Python](https://github.com/modelcontextprotocol/python-sdk), [TypeScript](https://github.com/modelcontextprotocol/typescript-sdk), etc.)

## Installation

### Rhino Package Manager (recommended)

1. In Rhino 8, run the **`PackageManager`** command (or *Tools → Package Manager*)
2. Search for **Cordyceps** and click **Install**
3. Restart Rhino

The Package Manager downloads the plugin, places it in the right folder, and unblocks it for you — and future updates are one click.

### Manual install

1. **[Download Cordyceps.gha](https://github.com/brookstalley/cordyceps/raw/main/releases/Cordyceps.gha)** (or grab it from the [latest release](https://github.com/brookstalley/cordyceps/releases/latest))

2. Copy to your Grasshopper components folder:
   *File → Special Folders → Components Folder*

3. Unblock the file so Rhino will load it:
   - **Windows**: right-click → Properties → check "Unblock" → OK
   - **macOS**: clear the quarantine flag (e.g. `xattr -dr com.apple.quarantine <path-to-Cordyceps.gha>`)

4. Restart Rhino

## Usage

1. Drop the **Cordyceps** component on your canvas (*Params → Util → Cordyceps*)

   The server starts on port 26929 by default. Optional inputs:
   - **HttpPort**: Change the HTTP port
   - **DebugLevel**: Set to 1+ to see request/response traffic in Rhino

2. Configure your MCP client:

   <details>
   <summary><strong>Claude Desktop</strong></summary>

   Claude Desktop uses stdio transport, so it needs the `mcp-remote` bridge. Requires [Node.js](https://nodejs.org/).

   Add to your config file:
   - macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
   - Windows: `%APPDATA%\Claude\claude_desktop_config.json`

   ```json
   {
     "mcpServers": {
       "cordyceps": {
         "command": "npx",
         "args": ["-y", "mcp-remote", "http://127.0.0.1:26929/mcp"]
       }
     }
   }
   ```

   Restart Claude Desktop after saving.
   </details>

   <details>
   <summary><strong>Claude Code</strong></summary>

   ```
   claude mcp add --transport http cordyceps http://127.0.0.1:26929/mcp
   ```
   </details>

   <details>
   <summary><strong>Cursor, VS Code, and other HTTP clients</strong></summary>

   ```json
   {
     "mcpServers": {
       "cordyceps": {
         "type": "streamable-http",
         "url": "http://127.0.0.1:26929/mcp"
       }
     }
   }
   ```
   </details>

3. Start building — describe what you want in natural language

## Example

```
Make an animated GIF that shows the full journey from parametric modeling
to photorealistic render. The subject is a small collection of geometric
forms — maybe five or six objects with varied shapes, scales, and
proportions. Arrange them as a pleasing composition.
```

![Cordyceps Showcase](images/cordyceps_showcase.gif)

The AI builds everything autonomously — creating parametric geometry in Grasshopper, baking to Rhino, applying PBR materials, configuring the render environment and lighting, and capturing a smooth orbiting animation.

## Scripting

Call tools directly from Python or any MCP client library:

```python
from mcp import ClientSession

async with ClientSession(transport) as session:
    # Add a slider and circle
    slider = await session.call_tool('gh_canvas', {
        'action': 'add', 'type': 'Number Slider', 'x': 50, 'y': 50
    })
    circle = await session.call_tool('gh_canvas', {
        'action': 'add', 'type': 'Curve/Circle', 'x': 200, 'y': 50
    })

    # Connect slider output to circle radius
    await session.call_tool('gh_wire', {
        'action': 'connect',
        'sourceId': slider['id'], 'sourceParam': '0',
        'targetId': circle['id'], 'targetParam': 'R'
    })
```

See the [MCP Python SDK](https://github.com/modelcontextprotocol/python-sdk) for transport setup and client details.

## Tools

Cordyceps provides **7 tools with over 100 actions** — consolidated to minimize context window usage. Related operations are grouped under a single tool with an `action` parameter.

### Grasshopper

| Tool | Description |
|------|-------------|
| `gh_canvas` | Components, values, groups, baking, variable parameters |
| `gh_wire` | Connection management |
| `gh_document` | Save, clear, snapshots (max 20 kept, oldest evicted), solver control, canvas capture (undo/redo are disabled — use snapshots) |
| `gh_script` | Script component configuration |
| `gh_inspect` | Status, outputs, data tracing, debugging |

### Rhino

| Tool | Description |
|------|-------------|
| `rhino_scene` | Objects, layers, selection, visibility |
| `rhino_render` | Viewport, camera, materials, lighting, environments, render |

## Documentation

Cordyceps exposes guides and patterns to MCP clients as resources. Your AI assistant can read these automatically when it needs guidance on data trees, type systems, component patterns, or rendering workflows.

Browse the documentation directly: [`src/Cordyceps/Knowledge/`](src/Cordyceps/Knowledge/)

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Plugin won't load | Verify Rhino 8.21+. If you installed manually, unblock the .gha file (Windows) or clear its quarantine flag (macOS) — the Package Manager does this for you. |
| Can't connect | Ensure Cordyceps component is on canvas. Check the port. |
| Claude Desktop can't connect | Ensure Node.js is installed. Check Rhino is running with Cordyceps. Restart Claude Desktop after config changes. |
| Component not found | Use `gh_canvas(action='search', query='...')` to find exact names. |
| No command output | Set DebugLevel input to 1 to see traffic in Rhino command history. |

## Building

```bash
dotnet build src/Cordyceps/Cordyceps.csproj -c Release
```

Only Release builds are supported — a Debug build (the `dotnet build` default) fails with an error.

---

[Changelog](CHANGELOG.md) · Inspired by [grasshopper-mcp](https://github.com/alfredatnycu/grasshopper-mcp) by Alfred Chen · MIT License
