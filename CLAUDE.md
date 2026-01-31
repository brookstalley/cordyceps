# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cordyceps is a Grasshopper plugin that exposes Grasshopper and Rhino functionality via the Model Context Protocol (MCP). It allows AI assistants to programmatically control Grasshopper (adding components, creating connections, configuring scripts) and Rhino (managing objects, layers, materials, rendering).

## Build Commands

```bash
# Build the plugin (Release configuration required)
dotnet build src/Cordyceps/Cordyceps.csproj -c Release

# The built .gha file is automatically copied to releases/
```

The project targets .NET 8.0 and outputs a Grasshopper plugin (`.gha` file). Debug builds are blocked—always use `-c Release`.

## Architecture

### Core Components

**McpServer.cs** - HTTP/SSE server implementing the MCP protocol. Listens on a configurable port (default 26929), handles JSON-RPC requests, and manages SSE sessions for streaming responses. Discovers tools via reflection using `[McpServerToolType]` and `[McpServerTool]` attributes.

**CordycepsComponent.cs** - The Grasshopper component that users drop on the canvas. Manages the MCP server lifecycle - starts the server when placed, stops when removed. Outputs server status and last command received.

**Core/GrasshopperContext.cs** - Thread-safe wrapper for Grasshopper document access. All Grasshopper operations must run on the UI thread; this class provides `ExecuteOnUiThread()` methods that marshal calls correctly.

**Core/ComponentRegistry.cs** - Resolves component type names to actual Grasshopper components. Handles aliases (e.g., "python" -> "Python 3 Script") and supports creation by name or GUID.

**Core/UnifiedToolHelpers.cs** - Shared utilities for the unified tool architecture including action dispatch, help generation, and standardized response formatting.

**Core/DebugLog.cs** - Centralized logging with Info, Warn, Error, Debug levels. Logs are retrievable via `gh_inspect(action='log')`.

### Tool Classes (in Tools/Unified/)

Each tool class is marked with `[McpServerToolType]` and contains a single method marked with `[McpServerTool]`. The method name is converted to snake_case for the MCP tool name (e.g., `GhCanvas` -> `gh_canvas`). Each tool uses an `action` parameter to dispatch to different operations.

**Grasshopper Tools (5):**
- **GhCanvasTool** (`gh_canvas`) - Components, values, groups: add, delete, move, find, search, list, bake, get/set values, group management
- **GhWireTool** (`gh_wire`) - Connect/disconnect components, bulk wiring, validate connections
- **GhDocumentTool** (`gh_document`) - Save, clear documents; snapshots; solver control; capture canvas/viewport
- **GhScriptTool** (`gh_script`) - Configure C#/Python script components
- **GhInspectTool** (`gh_inspect`) - Get component status, trace data flow, retrieve debug output

**Rhino Tools (2):**
- **RhinoSceneTool** (`rhino_scene`) - Object management, selection, layers (full CRUD), visibility
- **RhinoRenderTool** (`rhino_render`) - Display modes, camera, render settings, materials, environments

### Adding New Tools

1. Create a method in an existing tool class (or create a new class with `[McpServerToolType]`)
2. Add `[McpServerTool]` attribute to the method
3. Add `[Description("...")]` attributes to the method and each parameter
4. All parameters should be primitive types (string, int, double, bool)
5. Return a JSON-serialized string with `success` field

Example:
```csharp
[McpServerTool, Description("Brief description of what this tool does")]
public string MyNewTool(
    [Description("Parameter description")] string param1,
    [Description("Optional param description")] int param2 = 0)
{
    return _context.ExecuteOnUiThread(() =>
    {
        // Implementation
        return JsonConvert.SerializeObject(new { success = true, ... });
    });
}
```

## Key Patterns

- All Grasshopper document access must go through `_context.ExecuteOnUiThread()`
- Tool methods return JSON strings (use Newtonsoft.Json for serialization)
- Component parameters can be referenced by name or index (0-based)
- Use `Core.DebugLog` for logging (Info, Warn, Error, Debug levels)

## Dependencies

- Grasshopper 8.0+ (Rhino 8)
- Newtonsoft.Json for JSON serialization
- .NET 8.0

## Publishing

To publish a new version to yak:

```bash
# Update version in src/Cordyceps/Cordyceps.csproj, CHANGELOG.md, and manifest.yml
# Build, then from the dist/ folder:
"/Applications/Rhino 8.app/Contents/Resources/bin/yak" build --platform any
"/Applications/Rhino 8.app/Contents/Resources/bin/yak" push cordyceps-X.Y.Z-rh8_0-any.yak
```

The `dist/` folder should contain only: `Cordyceps.gha`, `manifest.yml`, and `icon.png`.
