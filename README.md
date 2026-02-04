# Cordyceps

**MCP server for Grasshopper.** Give AI agents direct control over your parametric design canvas and Rhino rendering tools.

[Model Context Protocol (MCP)](https://modelcontextprotocol.io/) enables AI assistants to control applications through a standardized interface.

## Features

- **Build definitions with natural language** — describe what you want and watch it appear on the canvas
- **Full Grasshopper control** — add components, create connections, configure values, manage groups
- **Rhino integration** — bake geometry, manage layers, apply PBR materials, control rendering
- **End-to-end automation** — from parametric modeling to raytraced renders, all via AI

## Requirements

- **Rhino 8.21+** (requires .NET 8)
- **MCP client**: Claude Desktop, Claude Code, Cursor, VS Code Copilot, or any compatible client

## Installation

1. **[Download Cordyceps.gha](https://github.com/brookstalley/cordyceps/raw/main/releases/Cordyceps.gha)**

2. Copy to your Grasshopper components folder:
   *File → Special Folders → Components Folder*

3. **Windows users**: Right-click the file → Properties → check "Unblock" → OK

## Usage

1. Drop the **Cordyceps** component on your canvas (*Params → Util → Cordyceps*)

   The server starts on port 26929 by default. Optional inputs:
   - **Port**: Change the HTTP port
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

> Make an animated GIF that shows the full journey from parametric modeling to photorealistic render. The subject is a small collection of geometric forms — maybe five or six objects with varied shapes, scales, and proportions. Arrange them as a pleasing composition.

![Cordyceps Showcase](images/cordyceps_showcase.gif)

The AI builds everything autonomously — creating parametric geometry in Grasshopper, baking to Rhino, applying PBR materials, configuring the render environment and lighting, and capturing a smooth orbiting animation.

## Tools

Cordyceps provides **7 tools with 110+ actions** — consolidated to minimize context window usage. Related operations are grouped under a single tool with an `action` parameter.

### Grasshopper

| Tool | Description |
|------|-------------|
| `gh_canvas` | Components, values, groups, baking, variable parameters |
| `gh_wire` | Connection management |
| `gh_document` | Save, clear, undo/redo, snapshots, canvas capture |
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
| Plugin won't load | Verify Rhino 8.21+. Unblock the .gha file (Windows) or clear quarantine (macOS). |
| Can't connect | Ensure Cordyceps component is on canvas. Check the port. |
| Claude Desktop can't connect | Ensure Node.js is installed. Check Rhino is running with Cordyceps. Restart Claude Desktop after config changes. |
| Component not found | Use `gh_canvas(action='search', query='...')` to find exact names. |
| No command output | Set DebugLevel input to 1 to see traffic in Rhino command history. |

## Building

```bash
dotnet build src/Cordyceps/Cordyceps.csproj
```

---

[Changelog](CHANGELOG.md) · Inspired by [grasshopper-mcp](https://github.com/alfredatnycu/grasshopper-mcp) by Alfred Chen · MIT License
