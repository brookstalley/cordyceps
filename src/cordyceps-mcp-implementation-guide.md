# Cordyceps MCP Implementation Guide

> **Target Environment:** Rhino 8.27+ (.NET 8.0), Windows + macOS  
> **Goal:** Expose Grasshopper tools via MCP to standard clients (Claude Desktop, Cursor, VS Code Copilot, etc.)

## Architecture Overview

Cordyceps runs as a Grasshopper plugin inside Rhino's process. Because clients cannot spawn Rhino as a subprocess, **stdio transport is impossible**. The solution is **Streamable HTTP**—the MCP specification's current HTTP-based transport.

### Why Streamable HTTP?

| Transport | Viable? | Notes |
|-----------|---------|-------|
| stdio | ❌ No | Requires client to spawn server as subprocess |
| HTTP+SSE (legacy) | ⚠️ Deprecated | Being phased out per MCP spec |
| **Streamable HTTP** | ✅ Yes | Current spec standard (2025-06-18), single endpoint |

### Client Compatibility

| Client | Connection Method |
|--------|------------------|
| Claude Desktop | Via `mcp-remote` bridge (npx) |
| Cursor | Native Streamable HTTP support |
| VS Code GitHub Copilot | Native Streamable HTTP support |
| Continue | Native Streamable HTTP support |
| MCP Inspector | Native Streamable HTTP support |
| Any stdio-only client | Via `mcp-remote` bridge |

> **Note:** Claude.ai (web) cannot connect to localhost servers. It can only connect to publicly-accessible remote MCP servers. Cordyceps is a local tool, so Claude Desktop (with mcp-remote) is the appropriate Claude client.

---

## Component Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Rhino / Grasshopper                     │
├─────────────────────────────────────────────────────────────┤
│                    Cordyceps GHA Plugin                     │
│  ┌─────────────────────────────────────────────────────┐    │
│  │              HttpListener (port 3333)               │    │
│  │  ┌───────────────────────────────────────────────┐  │    │
│  │  │       StreamableHttpServerTransport          │  │    │
│  │  │  ┌─────────────────────────────────────────┐  │  │    │
│  │  │  │              McpServer                  │  │  │    │
│  │  │  │  ┌─────────────────────────────────┐    │  │  │    │
│  │  │  │  │    Grasshopper Tool Classes     │    │  │  │    │
│  │  │  │  │    [McpServerToolType]          │    │  │  │    │
│  │  │  │  └─────────────────────────────────┘    │  │  │    │
│  │  │  └─────────────────────────────────────────┘  │  │    │
│  │  └───────────────────────────────────────────────┘  │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │ HTTP (localhost:3333/mcp)
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  MCP Client (Claude Desktop via mcp-remote, Cursor, etc.)  │
└─────────────────────────────────────────────────────────────┘
```

---

## Implementation Details

### 1. NuGet Dependencies

```xml
<PackageReference Include="ModelContextProtocol.Core" Version="0.6.0-preview.1" />
```

Use **`ModelContextProtocol.Core`** only—not `ModelContextProtocol.AspNetCore`. The Core package:
- Has no ASP.NET Core dependency
- Includes `StreamableHttpServerTransport`
- Includes `McpServer` and tool registration infrastructure
- Targets .NET Standard 2.0 (compatible with .NET 8)

### 2. Server Lifecycle

```
Rhino Starts → GHA Loads → Start HttpListener → Ready for connections
                                    ↓
                           Accept connections until...
                                    ↓
GHA Unloads / Rhino Closes → Stop HttpListener → Dispose McpServer
```

**Key requirements:**
- Start HTTP server on a background thread (don't block Rhino's UI thread)
- Handle graceful shutdown when Rhino closes
- Port should be configurable (default: `3333`)
- Bind to `127.0.0.1` only (security requirement)

### 3. HTTP Request Handling (Per MCP Spec 2025-06-18)

Your `HttpListener` must route requests to `StreamableHttpServerTransport` following the spec:

#### POST /mcp (Primary message channel)

1. **Validate headers:**
   - `Accept` header must include both `application/json` and `text/event-stream`
   - Validate `Origin` header to prevent DNS rebinding attacks
   - Read `Mcp-Session-Id` header if present (stateful mode only)

2. **Parse request body** as JSON-RPC message

3. **Call transport:**
   ```csharp
   bool wroteResponse = await transport.HandlePostRequestAsync(
       message, 
       response.OutputStream, 
       cancellationToken);
   ```

4. **Set response based on return value:**
   - If `wroteResponse == true`: Response already written as SSE or JSON
   - If `wroteResponse == false`: Return `202 Accepted` with empty body (for notifications/responses)

5. **Set response headers:**
   - `Content-Type: text/event-stream` (if SSE) or `application/json`
   - `Mcp-Session-Id` (if stateful mode and session established)

#### GET /mcp (Optional SSE stream for server-initiated messages)

- Only needed for **stateful mode** where server sends unsolicited messages
- In **stateless mode** (`Stateless = true`), return `405 Method Not Allowed`
- If supported, call `transport.HandleGetRequestAsync(response.OutputStream, ct)`

#### DELETE /mcp (Session termination)

- Only relevant for **stateful mode**
- In **stateless mode**, return `405 Method Not Allowed`

### 4. Transport Configuration

```csharp
var transport = new StreamableHttpServerTransport
{
    Stateless = true  // Recommended for simplicity
};
```

**Stateless mode** (recommended for Cordyceps):
- No session management required
- Each request is independent
- GET endpoint disabled (throws `InvalidOperationException`)
- Cannot send unsolicited server→client messages
- Simpler implementation, fewer edge cases

**Stateful mode** (if needed later):
- Requires session ID management via `Mcp-Session-Id` header
- Supports server-initiated notifications
- More complex lifecycle management

### 5. McpServer Setup

The `McpServer` is the high-level abstraction that handles MCP protocol logic:

```csharp
var serverOptions = new McpServerOptions
{
    ServerInfo = new Implementation 
    { 
        Name = "Cordyceps", 
        Version = "1.0.0" 
    },
    Capabilities = new ServerCapabilities
    {
        Tools = new ToolsCapability()
    }
};

// Create server with transport
var server = McpServer.Create(transport, serverOptions);

// Register tools from assembly
server.WithToolsFromAssembly();  // Scans for [McpServerToolType] classes

// Or register manually
server.AddTool<GrasshopperTools>();
```

### 6. Tool Definitions

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

[McpServerToolType]
public class GrasshopperTools
{
    [McpServerTool]
    [Description("Gets the name of the current Grasshopper document")]
    public string GetDocumentName()
    {
        // Access Grasshopper API here
        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        return doc?.DisplayName ?? "No document open";
    }

    [McpServerTool]
    [Description("Lists all component nicknames in the current definition")]
    public string[] ListComponents()
    {
        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
        if (doc == null) return Array.Empty<string>();
        
        return doc.Objects
            .OfType<IGH_Component>()
            .Select(c => c.NickName)
            .ToArray();
    }

    [McpServerTool]
    [Description("Adds a panel component at the specified canvas location")]
    public string AddPanel(
        [Description("X coordinate on canvas")] double x,
        [Description("Y coordinate on canvas")] double y,
        [Description("Initial text content")] string content = "")
    {
        // Implementation with proper Grasshopper thread marshaling
        // ...
        return "Panel added successfully";
    }
}
```

**Tool method requirements:**
- Public methods with `[McpServerTool]` attribute
- `[Description]` on method explains what tool does (sent to LLM)
- `[Description]` on parameters explains each parameter (sent to LLM)
- Return types: primitives, strings, arrays, or objects (serialized as JSON)
- Can be async (`Task<T>` return type)

### 7. Security Requirements (Per MCP Spec)

> ⚠️ **These are MUST requirements from the spec, not optional.**

1. **Bind to localhost only:**
   ```csharp
   listener.Prefixes.Add("http://127.0.0.1:3333/mcp/");
   // NOT: http://+:3333/ or http://*:3333/ or http://0.0.0.0:3333/
   ```

2. **Validate Origin header:**
   ```csharp
   string origin = request.Headers["Origin"];
   if (!string.IsNullOrEmpty(origin))
   {
       var originUri = new Uri(origin);
       if (originUri.Host != "127.0.0.1" && originUri.Host != "localhost")
       {
           response.StatusCode = 403;
           return;
       }
   }
   ```

3. **Consider authentication** for production deployments (API key, etc.)

---

## What Users Do

### Claude Desktop Configuration

**Prerequisites:** 
- Node.js installed (for `npx` / `mcp-remote`)
- Rhino running with Cordyceps loaded

**Config file locations:**
- **macOS:** `~/Library/Application Support/Claude/claude_desktop_config.json`
- **Windows:** `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "cordyceps": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://127.0.0.1:3333/mcp"]
    }
  }
}
```

Restart Claude Desktop after saving.

### Cursor / VS Code Copilot / Continue

These clients support Streamable HTTP natively. Configuration varies by client:

**Cursor example:**
```json
{
  "mcpServers": {
    "cordyceps": {
      "type": "streamable-http",
      "url": "http://127.0.0.1:3333/mcp"
    }
  }
}
```

---

## What You Don't Ship

| Item | Reason |
|------|--------|
| `mcp-remote` | Users install via `npx` on-demand |
| Node.js | User prerequisite for Claude Desktop bridge |
| ASP.NET Core packages | Not needed—Core package is sufficient |
| Custom JSON-RPC parser | SDK handles all protocol details |

---

## Implementation Checklist

### Core Infrastructure
- [ ] Add `ModelContextProtocol.Core` NuGet package
- [ ] Create HTTP server class wrapping `System.Net.HttpListener`
- [ ] Implement request routing (POST, GET, DELETE)
- [ ] Wire requests to `StreamableHttpServerTransport.HandlePostRequestAsync()`
- [ ] Create and configure `McpServer` instance
- [ ] Implement server start/stop lifecycle tied to GHA loading

### Protocol Compliance
- [ ] Validate `Accept` header on POST requests
- [ ] Validate `Origin` header on all requests (DNS rebinding protection)
- [ ] Handle `202 Accepted` for notifications/responses (when no body written)
- [ ] Set correct `Content-Type` headers (JSON vs SSE)
- [ ] Return `405 Method Not Allowed` for GET/DELETE in stateless mode

### Tool Implementation
- [ ] Define tool classes with `[McpServerToolType]`
- [ ] Define tool methods with `[McpServerTool]` and `[Description]`
- [ ] Handle Grasshopper thread marshaling for UI operations
- [ ] Implement proper error handling and meaningful error messages

### Configuration & Polish
- [ ] Make port configurable (default 3333)
- [ ] Add logging for debugging connection issues
- [ ] Document user setup in README
- [ ] Handle edge cases (Rhino not ready, no document open, etc.)

### Testing
- [ ] Test with MCP Inspector (direct HTTP)
- [ ] Test with Claude Desktop via `mcp-remote`
- [ ] Test on Windows
- [ ] Test on macOS
- [ ] Test tool invocations with various parameter types

---

## Testing

### MCP Inspector (Recommended for Development)

```bash
npx @modelcontextprotocol/inspector --transport http http://127.0.0.1:3333/mcp
```

This connects directly to your endpoint and provides:
- Protocol-level debugging
- Tool discovery verification
- Interactive tool invocation
- Request/response inspection

### Manual Testing with curl

```bash
# Initialize connection
curl -X POST http://127.0.0.1:3333/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}'

# List tools
curl -X POST http://127.0.0.1:3333/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
```

---

## Platform Notes

### Windows
- `HttpListener` works natively in .NET 8
- No admin rights required for `http://127.0.0.1:port/` binding
- Admin required only for `http://+:port/` or `http://*:port/` (which you shouldn't use)

### macOS  
- `HttpListener` works in .NET 8 on macOS
- Rhino 8.27+ runs .NET 8 by default
- No special permissions needed for localhost

### Cross-Platform
- Always use `127.0.0.1` (not `localhost` string) for consistent behavior
- Test on both platforms—subtle differences in networking behavior
- File paths differ (`/` vs `\`)—use `Path.Combine()` for any file operations
