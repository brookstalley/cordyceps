using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Cordyceps.Core;
using Cordyceps.Prompts;
using Cordyceps.Resources;
using Rhino;

namespace Cordyceps
{
    /// <summary>
    /// MCP Server using HttpListener with Streamable HTTP transport.
    /// Implements the MCP 2025-06-18 specification with stateless mode.
    /// Discovers tools via reflection on [McpServerTool] attributes.
    /// </summary>
    public class McpServer : IDisposable
    {
        // Configuration constants
        private const int DEFAULT_PORT = 26929;
        private const int SHUTDOWN_TIMEOUT_SECONDS = 2;
        private const int LOG_TRUNCATE_LENGTH = 200;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _listenerTask;
        private int _port;
        private GrasshopperContext _context;
        private readonly List<ToolInfo> _tools = new List<ToolInfo>();
        private bool _disposed;

        /// <summary>
        /// Whether the server is currently running
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Total number of commands received
        /// </summary>
        public int CommandCount { get; private set; }

        /// <summary>
        /// Last command received (type and timestamp)
        /// </summary>
        public string LastCommand { get; private set; } = "(none)";

        /// <summary>
        /// Current port the server is listening on
        /// </summary>
        public int Port => _port;

        /// <summary>
        /// Start the MCP server on the specified port
        /// </summary>
        public void Start(int port = DEFAULT_PORT)
        {
            if (IsRunning)
            {
                RhinoApp.WriteLine("Cordyceps: MCP server already running");
                return;
            }

            _port = port;
            _cts = new CancellationTokenSource();

            try
            {
                // Create context for tool execution
                _context = new GrasshopperContext();

                // Discover tools from assembly
                DiscoverTools();
                RhinoApp.WriteLine($"Cordyceps: Discovered {_tools.Count} tools");

                // Create and start HttpListener (bind to both IPv4 and IPv6)
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();

                // Start listening in background
                _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);

                IsRunning = true;
                RhinoApp.WriteLine($"Cordyceps: MCP server started on http://127.0.0.1:{_port}/mcp");
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Cordyceps: Failed to start MCP server: {ex.Message}");
                RhinoApp.WriteLine($"Cordyceps: Stack trace: {ex.StackTrace}");
                _cts?.Dispose();
                _cts = null;
                _listener?.Close();
                _listener = null;
            }
        }

        /// <summary>
        /// Stop the MCP server
        /// </summary>
        public void Stop()
        {
            if (!IsRunning) return;

            RhinoApp.WriteLine("Cordyceps: Stopping MCP server...");

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();

                try { _listenerTask?.Wait(TimeSpan.FromSeconds(SHUTDOWN_TIMEOUT_SECONDS)); }
                catch (AggregateException) { }
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Cordyceps: Error stopping server: {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _listener = null;
                _listenerTask = null;
                _context = null;
                IsRunning = false;
                RhinoApp.WriteLine("Cordyceps: MCP server stopped");
            }
        }

        /// <summary>
        /// Discover tools via reflection on [McpServerTool] attributes
        /// </summary>
        private void DiscoverTools()
        {
            _tools.Clear();
            var assembly = Assembly.GetExecutingAssembly();

            foreach (var type in assembly.GetTypes())
            {
                // Look for types with [McpServerToolType] attribute
                var toolTypeAttr = type.GetCustomAttribute<McpServerToolTypeAttribute>();
                if (toolTypeAttr == null) continue;

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                    if (toolAttr == null) continue;

                    var descAttr = method.GetCustomAttribute<DescriptionAttribute>();
                    var toolName = ConvertToSnakeCase(method.Name);

                    var parameters = new List<ToolParameter>();
                    foreach (var param in method.GetParameters())
                    {
                        var paramDesc = param.GetCustomAttribute<DescriptionAttribute>();
                        parameters.Add(new ToolParameter
                        {
                            Name = param.Name,
                            Description = paramDesc?.Description ?? "",
                            Type = GetJsonType(param.ParameterType),
                            IsRequired = !param.HasDefaultValue
                        });
                    }

                    _tools.Add(new ToolInfo
                    {
                        Name = toolName,
                        Description = descAttr?.Description ?? "",
                        DeclaringType = type,
                        Method = method,
                        Parameters = parameters
                    });
                }
            }
        }

        private static string ConvertToSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    sb.Append('_');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }

        private static string GetJsonType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float))
                return "number";
            if (type == typeof(bool)) return "boolean";
            return "string";
        }

        /// <summary>
        /// Main HTTP listener loop
        /// </summary>
        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                try
                {
                    var httpContext = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequestAsync(httpContext, ct), ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        RhinoApp.WriteLine($"Cordyceps: Listener error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handle an incoming HTTP request (Streamable HTTP transport)
        /// </summary>
        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var path = request.Url?.AbsolutePath ?? "/";
                RhinoApp.WriteLine($"Cordyceps: {request.HttpMethod} {path} from {request.RemoteEndPoint}");

                // Security: Validate Origin header (DNS rebinding protection)
                if (!ValidateOrigin(request, response))
                    return;

                // CORS preflight
                if (request.HttpMethod == "OPTIONS")
                {
                    HandleCorsPreFlight(response);
                    return;
                }

                // Streamable HTTP endpoint (MCP 2025-06-18)
                if (path == "/mcp")
                {
                    if (request.HttpMethod == "POST")
                    {
                        await HandleMcpPostAsync(context);
                    }
                    else
                    {
                        // GET and DELETE return 405 in stateless mode
                        response.StatusCode = 405;
                        response.Close();
                    }
                    return;
                }

                // Health check endpoint (keep for debugging)
                if (path == "/" || path == "/health")
                {
                    await HandleHealthCheckAsync(response);
                    return;
                }

                response.StatusCode = 404;
                response.Close();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Cordyceps: Request error: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch (Exception closeEx)
                {
                    Core.DebugLog.Warn($"Failed to close error response: {closeEx.Message}");
                }
            }
        }

        /// <summary>
        /// Validate Origin header to prevent DNS rebinding attacks
        /// </summary>
        private bool ValidateOrigin(HttpListenerRequest request, HttpListenerResponse response)
        {
            var origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin))
            {
                try
                {
                    var originUri = new Uri(origin);
                    if (originUri.Host != "127.0.0.1" && originUri.Host != "localhost")
                    {
                        RhinoApp.WriteLine($"Cordyceps: Rejected request from non-localhost origin: {origin}");
                        response.StatusCode = 403;
                        response.Close();
                        return false;
                    }
                }
                catch (UriFormatException)
                {
                    RhinoApp.WriteLine($"Cordyceps: Rejected request with malformed origin: {origin}");
                    response.StatusCode = 403;
                    response.Close();
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Handle CORS preflight requests
        /// </summary>
        private void HandleCorsPreFlight(HttpListenerResponse response)
        {
            response.Headers.Add("Access-Control-Allow-Origin", "http://localhost");
            response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");
            response.Headers.Add("Access-Control-Max-Age", "86400");
            response.StatusCode = 204;
            response.Close();
        }

        /// <summary>
        /// Handle health check requests
        /// </summary>
        private async Task HandleHealthCheckAsync(HttpListenerResponse response)
        {
            response.StatusCode = 200;
            response.ContentType = "application/json";
            var health = JsonSerializer.Serialize(new { status = "ok", server = "Cordyceps MCP", transport = "streamable-http" });
            var bytes = Encoding.UTF8.GetBytes(health);
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        /// <summary>
        /// Handle POST /mcp requests (Streamable HTTP transport)
        /// </summary>
        private async Task HandleMcpPostAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Validate Accept header per MCP spec (must include both JSON and SSE)
                var accept = request.Headers["Accept"] ?? "";
                if (!accept.Contains("application/json") || !accept.Contains("text/event-stream"))
                {
                    RhinoApp.WriteLine($"Cordyceps: Invalid Accept header: {accept}");
                    response.StatusCode = 406; // Not Acceptable
                    response.Close();
                    return;
                }

                // Add CORS header for actual requests
                response.Headers.Add("Access-Control-Allow-Origin", "http://localhost");

                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var json = await reader.ReadToEndAsync();

                RhinoApp.WriteLine($"Cordyceps: Received: {json.Substring(0, Math.Min(LOG_TRUNCATE_LENGTH, json.Length))}...");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                var hasId = root.TryGetProperty("id", out var id);
                var paramsEl = root.TryGetProperty("params", out var p) ? p : default;

                // JSON-RPC 2.0: Notifications (no id) MUST NOT receive a response
                bool isNotification = !hasId || id.ValueKind == JsonValueKind.Undefined;

                object result = null;
                string errorMessage = null;
                int errorCode = 0;

                try
                {
                    result = await DispatchMethodAsync(method, paramsEl);
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    errorCode = -32603;
                }

                // Notifications return 202 Accepted with empty body (per MCP Streamable HTTP spec)
                if (isNotification)
                {
                    RhinoApp.WriteLine($"Cordyceps: Notification '{method}' processed");
                    response.StatusCode = 202;  // Accepted
                    response.Close();
                    return;
                }

                // Build JSON-RPC response
                response.ContentType = "application/json";
                response.StatusCode = 200;

                var responseObj = new Dictionary<string, object> { ["jsonrpc"] = "2.0" };

                // Add id to response (required for non-notification responses)
                if (id.ValueKind == JsonValueKind.Number)
                    responseObj["id"] = id.GetInt32();
                else if (id.ValueKind == JsonValueKind.String)
                    responseObj["id"] = id.GetString();

                if (errorMessage != null)
                {
                    responseObj["error"] = new Dictionary<string, object>
                    {
                        ["code"] = errorCode,
                        ["message"] = errorMessage
                    };
                }
                else
                {
                    responseObj["result"] = result;
                }

                var responseJson = JsonSerializer.Serialize(responseObj, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                RhinoApp.WriteLine($"Cordyceps: Responding: {responseJson.Substring(0, Math.Min(LOG_TRUNCATE_LENGTH, responseJson.Length))}...");

                // Send response via HTTP
                var bytes = Encoding.UTF8.GetBytes(responseJson);
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                await response.OutputStream.FlushAsync();
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Cordyceps: JSON-RPC error: {ex.Message}");
                response.StatusCode = 400;
            }
            finally
            {
                response.Close();
            }
        }

        /// <summary>
        /// Dispatch a JSON-RPC method
        /// </summary>
        private async Task<object> DispatchMethodAsync(string method, JsonElement paramsEl)
        {
            switch (method)
            {
                case "initialize":
                    return HandleInitialize();
                case "initialized":
                case "notifications/initialized":
                    return new { };
                case "tools/list":
                    return HandleToolsList();
                case "tools/call":
                    return await HandleToolCallAsync(paramsEl);
                case "resources/list":
                    return HandleResourcesList();
                case "resources/read":
                    return HandleResourcesRead(paramsEl);
                case "prompts/list":
                    return HandlePromptsList();
                case "prompts/get":
                    return HandlePromptsGet(paramsEl);
                case "ping":
                    return new { };
                default:
                    throw new Exception($"Unknown method: {method}");
            }
        }

        private object HandleInitialize()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            return new
            {
                protocolVersion = "2025-06-18",
                capabilities = new
                {
                    tools = new { },
                    resources = new { subscribe = false, listChanged = false },
                    prompts = new { listChanged = false }
                },
                serverInfo = new { name = "Cordyceps", version },
                instructions = GetServerInstructions()
            };
        }

        private string GetServerInstructions()
        {
            return @"# Cordyceps MCP Server - Grasshopper Control

## IMPORTANT: Read Documentation First

Before working with Grasshopper, read these essential resources using `resources/read`:

- **`gh://docs/data-trees`** - CRITICAL: Grasshopper's data tree system is unintuitive. Read this first.
- **`gh://docs/canvas-layout`** - CRITICAL: Layout conventions for readable definitions.
- **`gh://docs/type-system`** - Type compatibility and coercion rules
- **`gh://docs/best-practices`** - Common mistakes and how to avoid them

For any specific component, read `gh://component/{name}` (e.g., `gh://component/Circle`) to get full parameter details.

## Component Disambiguation

Some component names are ambiguous. For example, 'Circle' matches both:
- **Circle (Curve)** - Creates circles from plane, radius, etc. (role: component)
- **Circle (Params/Geometry)** - Container to hold circle data (role: parameter)

When you call `add_component` with an ambiguous name, you'll get an `ambiguous_name` error with all matching components, including their GUIDs, categories, and I/O signatures.

### To resolve ambiguity:
- Use the GUID directly: `add_component(type: 'guid-here', ...)`
- Use category-qualified name: `add_component(type: 'Curve/Circle', ...)`

### Role Field
Every component response includes a `role` field:
- **component** - Processing component (takes inputs, produces outputs)
- **parameter** - Data container (Params category, non-Input subcategory)
- **input** - Interactive input (Params category, Input subcategory like Number Slider)

### Params Subcategories
Components in the 'Params' category fall into these subcategories:
- **Input** - Interactive controls: Number Slider, Boolean Toggle, Value List, etc.
- **Geometry** - Geometry containers: Point, Curve, Brep, Mesh, Surface, etc.
- **Primitive** - Value containers: Integer, Number, Text, Boolean, etc.
- **Util** - Data flow components: Relay, Cluster Input/Output, Data Dam, etc.
- **Rhino** - Viewport interaction: Plane, Box, Colour Gradient, etc.

Most of the time you want 'component' role items, not 'parameter' role items.

## Canvas Layout Guidelines (IMPORTANT)

Proper spacing prevents overlapping components and unreadable definitions:

### Component Dimensions
- **Number Sliders**: ~200px wide, 20px tall - need horizontal space!
- **Panels**: ~100px wide, expand with content
- **Standard components**: ~100px wide, 40-60px tall

### Spacing Conventions
- **Horizontal gaps**: 150px between columns
- **Vertical gaps**: 70px between stacked components
- **Groups**: Add 40px padding around contents

### Layout Pattern
```
x=50 (Inputs)  x=250 (Math)  x=400 (Transform)  x=700 (Output)
[Slider]        [Pi]          [Rotate]           [Geometry]
[Slider]        [Division]    [Move]
[Panel: 0]
[Panel: 1]
```

### Layout Tools
- `get_component_bounds(id)` - Get actual component dimensions
- `validate_layout()` - Check for overlaps and spacing issues
- `auto_space_components(mode)` - Auto-fix overlapping components
- `add_constant(value, x, y)` - Quick way to add constant panels

## Standard Workflow

1. **Plan first**: Use `suggest_pattern(description)` to find relevant patterns
2. **Read pattern**: If found, read the pattern resource (e.g., `gh://patterns/radial-array`)
3. **Disable solver**: `set_solver_enabled(enabled: false)`
4. **Add inputs** at x=50, stacked vertically with 70px gaps
5. **Add constants** below inputs if needed (0, 1, 2, Pi are common)
6. **Add processing** at x=250, 400, 550... (150px increments)
7. **Add outputs** at rightmost position
8. **Connect**: Use `bulk_connect` for efficiency
9. **Enable solver**: `set_solver_enabled(enabled: true)`
10. **Validate**: `get_canvas_status()` then `validate_layout()`
11. **Organize**: `add_to_group` for visual clarity

## Key Concepts

- **Data Trees**: Hierarchical data with paths like `{0;0;1}`. Mismatched trees cause cross-products.
- **Access Modes**: Item (single value), List (flat list), Tree (full hierarchy).
- **Type Coercion**: Grasshopper converts types automatically but some fail silently.

## Available Resources

### Documentation
- `gh://docs/data-trees` - Data tree fundamentals (READ THIS FIRST)
- `gh://docs/canvas-layout` - Layout best practices (READ THIS FIRST)
- `gh://docs/type-system` - Type compatibility matrix
- `gh://docs/best-practices` - Patterns and anti-patterns
- `gh://docs/component-patterns` - Common workflows

### Patterns (pre-built component workflows)
- `gh://patterns/radial-array` - Arrange objects in a circle
- `gh://patterns/linear-array` - Arrange objects in a line
- `gh://patterns/grid-array` - Create 2D grids

### Dynamic
- `gh://component/{name}` - Documentation for any component

## Avoiding Mistakes

1. **Layout first**: Plan positions before adding. Sliders need 200px width.
2. **Validate connections**: Use `validate_connection` before `connect_components`
3. **Check deprecation**: Use `search_components` to see if components are deprecated
4. **Name components**: Use `rename_component` for debugging
5. **Validate layout**: Always run `validate_layout()` after building
";
        }

        private object HandleToolsList()
        {
            var tools = _tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = new
                {
                    type = "object",
                    properties = t.Parameters.ToDictionary(
                        p => p.Name,
                        p => new { type = p.Type, description = p.Description }
                    ),
                    required = t.Parameters.Where(p => p.IsRequired).Select(p => p.Name).ToArray()
                }
            }).ToList();

            return new { tools };
        }

        private async Task<object> HandleToolCallAsync(JsonElement paramsEl)
        {
            var name = paramsEl.GetProperty("name").GetString();
            var arguments = paramsEl.TryGetProperty("arguments", out var a) ? a : default;

            RecordCommand(name);

            var tool = _tools.FirstOrDefault(t => t.Name == name);
            if (tool == null)
                throw new Exception($"Unknown tool: {name}");

            // Create tool instance manually (all tool classes take GrasshopperContext, McpServer)
            var instance = Activator.CreateInstance(tool.DeclaringType, _context, this);

            // Build method arguments
            var methodParams = tool.Method.GetParameters();
            var args = new object[methodParams.Length];

            for (int i = 0; i < methodParams.Length; i++)
            {
                var param = methodParams[i];
                if (arguments.ValueKind == JsonValueKind.Object &&
                    arguments.TryGetProperty(param.Name, out var argVal))
                {
                    args[i] = ConvertJsonValue(argVal, param.ParameterType);
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = param.DefaultValue;
                }
                else
                {
                    args[i] = param.ParameterType.IsValueType
                        ? Activator.CreateInstance(param.ParameterType)
                        : null;
                }
            }

            // Invoke the tool method
            var result = tool.Method.Invoke(instance, args);

            // Handle async methods
            if (result is Task task)
            {
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                result = resultProperty?.GetValue(task);
            }

            // Format as MCP tool result
            return new
            {
                content = new[] { new { type = "text", text = result?.ToString() ?? "" } },
                isError = false
            };
        }

        /// <summary>
        /// Handle resources/list request
        /// </summary>
        private object HandleResourcesList()
        {
            var resources = ResourceRegistry.Instance.ListResources();
            return new { resources };
        }

        /// <summary>
        /// Handle resources/read request
        /// </summary>
        private object HandleResourcesRead(JsonElement paramsEl)
        {
            var uri = paramsEl.TryGetProperty("uri", out var u) ? u.GetString() : null;

            if (string.IsNullOrEmpty(uri))
            {
                throw new Exception("Resource URI is required");
            }

            var content = ResourceRegistry.Instance.ReadResource(uri);

            if (content == null)
            {
                throw new Exception($"Resource not found: {uri}");
            }

            return new
            {
                contents = new[]
                {
                    new
                    {
                        uri = content.Uri,
                        mimeType = content.MimeType,
                        text = content.Text
                    }
                }
            };
        }

        /// <summary>
        /// Handle prompts/list request
        /// </summary>
        private object HandlePromptsList()
        {
            var prompts = PromptRegistry.Instance.ListPrompts();
            return new { prompts };
        }

        /// <summary>
        /// Handle prompts/get request
        /// </summary>
        private object HandlePromptsGet(JsonElement paramsEl)
        {
            var name = paramsEl.TryGetProperty("name", out var n) ? n.GetString() : null;

            if (string.IsNullOrEmpty(name))
            {
                throw new Exception("Prompt name is required");
            }

            // Extract arguments if provided
            var arguments = new Dictionary<string, string>();
            if (paramsEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsEl.EnumerateObject())
                {
                    arguments[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            var prompt = PromptRegistry.Instance.GetPrompt(name, arguments);

            if (prompt == null)
            {
                throw new Exception($"Prompt not found: {name}");
            }

            return new
            {
                description = prompt.Description,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new
                        {
                            type = "text",
                            text = prompt.Content
                        }
                    }
                }
            };
        }

        private static object ConvertJsonValue(JsonElement element, Type targetType)
        {
            if (targetType == typeof(string))
                return element.GetString();
            if (targetType == typeof(int))
                return element.GetInt32();
            if (targetType == typeof(long))
                return element.GetInt64();
            if (targetType == typeof(double))
                return element.GetDouble();
            if (targetType == typeof(bool))
                return element.GetBoolean();
            if (targetType == typeof(float))
                return (float)element.GetDouble();

            return element.GetString();
        }

        /// <summary>
        /// Record that a command was executed
        /// </summary>
        public void RecordCommand(string commandType)
        {
            CommandCount++;
            LastCommand = $"{commandType} @ {DateTime.Now:HH:mm:ss}";
            CordycepsComponent.RefreshComponent();
        }

        // Helper classes

        private class ToolInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public Type DeclaringType { get; set; }
            public MethodInfo Method { get; set; }
            public List<ToolParameter> Parameters { get; set; }
        }

        private class ToolParameter
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Type { get; set; }
            public bool IsRequired { get; set; }
        }

        #region IDisposable

        /// <summary>
        /// Dispose resources used by the MCP server
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose implementation
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                Stop();
            }

            _disposed = true;
        }

        #endregion
    }

    // Attributes for tool discovery (matching the MCP SDK pattern)

    /// <summary>
    /// Marks a class as containing MCP server tools
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class McpServerToolTypeAttribute : Attribute { }

    /// <summary>
    /// Marks a method as an MCP server tool
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class McpServerToolAttribute : Attribute { }
}
