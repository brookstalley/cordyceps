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
        // In-flight request handlers, tracked so Stop() can drain them before tearing down
        // shared state instead of detaching them mid-request.
        private readonly Core.InFlightRequests _inFlight = new Core.InFlightRequests();
        private bool _disposed;
        private DateTime _startTime;

        /// <summary>
        /// Whether the server is currently running
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Actionable reason the last <see cref="Start"/> failed (e.g. the port is held by another
        /// application), or <c>null</c> if the server started cleanly. Lets the host component
        /// surface a real cause instead of a bare "NOT RUNNING".
        /// </summary>
        public string StartError { get; private set; }

        /// <summary>
        /// Thread-safe activity counters. <see cref="RecordCommand"/> runs on concurrent HTTP
        /// worker threads while the status/health readers run elsewhere, so the counter and
        /// last-command label go through <see cref="Core.CommandStats"/> rather than unsynchronized
        /// fields (which lost increments and risked stale reads).
        /// </summary>
        private readonly Core.CommandStats _commandStats = new Core.CommandStats();

        /// <summary>
        /// Total number of commands received
        /// </summary>
        public int CommandCount => _commandStats.Count;

        /// <summary>
        /// Last command received (type and timestamp)
        /// </summary>
        public string LastCommand => _commandStats.Last;

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
                Core.DebugLog.WriteLine("MCP server already running", "INFO", 1);
                return;
            }

            _port = port;
            _cts = new CancellationTokenSource();
            StartError = null;

            try
            {
                // Create context for tool execution
                _context = new GrasshopperContext();

                // Discover tools from assembly
                DiscoverTools();
                Core.DebugLog.WriteLine($"Discovered {_tools.Count} tools", "INFO", 1);

                // Create and start HttpListener (bind to both IPv4 and IPv6)
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();

                // Start listening in background
                _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);

                IsRunning = true;
                _startTime = DateTime.UtcNow;
                Core.DebugLog.WriteLine($"MCP server started on http://127.0.0.1:{_port}/mcp", "INFO", 0);
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- startup boundary: any failure (bind/reflection) must leave the server cleanly not-running with an actionable reason, never crash the host component's solve
            {
                // Surface an actionable reason so the host component can show *why* the server is
                // not running rather than a bare "NOT RUNNING". A bind failure (port held by a
                // non-Cordyceps process) arrives as HttpListenerException.
                StartError = ex is HttpListenerException
                    ? $"MCP server could not start on port {port}: {ex.Message}. The port may be in use by another application — choose a different port (the HttpPort input) or close the process holding it."
                    : $"MCP server failed to start on port {port}: {ex.Message}";

                Core.DebugLog.WriteLine($"Failed to start MCP server: {ex.Message}", "ERROR", 0);
                Core.DebugLog.WriteLine($"Stack trace: {ex.StackTrace}", "ERROR", 1);
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

            Core.DebugLog.WriteLine("Stopping MCP server...", "INFO", 1);

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener?.Close();

                try { _listenerTask?.Wait(TimeSpan.FromSeconds(SHUTDOWN_TIMEOUT_SECONDS)); }
                catch (AggregateException ex)
                {
                    Core.DebugLog.WriteLine($"Shutdown exception (expected): {ex.InnerException?.Message}", "DEBUG", 1);
                }

                // Drain in-flight request handlers within the shutdown budget so they finish
                // against a still-valid context before the finally block releases it. A handler
                // that outlives the budget is detached; it reads the context defensively (see
                // HandleToolCallAsync) and the Chunk-01 document-lock timeout bounds any wedged
                // work, so this wait stays finite.
                if (!_inFlight.DrainWithin(TimeSpan.FromSeconds(SHUTDOWN_TIMEOUT_SECONDS)))
                    Core.DebugLog.WriteLine($"Shutdown: {_inFlight.Count} request(s) still in flight after {SHUTDOWN_TIMEOUT_SECONDS}s; detaching", "WARN", 1);
            }
            catch (Exception ex)
            {
                Core.DebugLog.WriteLine($"Error stopping server: {ex.Message}", "ERROR", 0);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _listener = null;
                _listenerTask = null;
                _context = null;
                IsRunning = false;
                Core.DebugLog.WriteLine("MCP server stopped", "INFO", 0);
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

        private static string GetJsonType(Type type) => Core.JsonTypeConverter.GetJsonType(type);

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
                    // Track the handler so Stop() can drain it instead of detaching it.
                    var handlerTask = Task.Run(() => HandleRequestAsync(httpContext, ct), ct);
                    _inFlight.Track(handlerTask);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Core.DebugLog.WriteLine($"Listener error: {ex.Message}", "ERROR", 1);
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
                Core.DebugLog.WriteLine($"{request.HttpMethod} {path} from {request.RemoteEndPoint}", "INFO", 1);

                // Security: Validate Origin header (DNS rebinding protection)
                var validatedOrigin = ValidateOrigin(request, response, out bool isValid);
                if (!isValid)
                    return;

                // CORS preflight
                if (request.HttpMethod == "OPTIONS")
                {
                    HandleCorsPreFlight(response, validatedOrigin);
                    return;
                }

                // Streamable HTTP endpoint (MCP 2025-06-18)
                if (path == "/mcp")
                {
                    if (request.HttpMethod == "POST")
                    {
                        await HandleMcpPostAsync(context, validatedOrigin);
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
                Core.DebugLog.WriteLine($"Request error: {ex.Message}", "ERROR", 1);
                try
                {
                    response.StatusCode = 500;
                    response.Close();
                }
                catch (Exception closeEx)
                {
                    Core.DebugLog.WriteLine($"Failed to close error response: {closeEx.Message}", "WARN", 1);
                }
            }
        }

        /// <summary>
        /// Validate Origin header to prevent DNS rebinding attacks.
        /// Returns the validated origin (or null if no origin header present).
        /// </summary>
        private string ValidateOrigin(HttpListenerRequest request, HttpListenerResponse response, out bool isValid)
        {
            isValid = true;
            var origin = request.Headers["Origin"];
            if (!string.IsNullOrEmpty(origin))
            {
                try
                {
                    var originUri = new Uri(origin);
                    if (originUri.Host != "127.0.0.1" && originUri.Host != "localhost")
                    {
                        Core.DebugLog.WriteLine($"Rejected request from non-localhost origin: {origin}", "WARN", 1);
                        response.StatusCode = 403;
                        response.Close();
                        isValid = false;
                        return null;
                    }
                    return origin; // Return the validated origin for CORS header
                }
                catch (UriFormatException)
                {
                    Core.DebugLog.WriteLine($"Rejected request with malformed origin: {origin}", "WARN", 1);
                    response.StatusCode = 403;
                    response.Close();
                    isValid = false;
                    return null;
                }
            }
            return null; // No origin header present
        }

        /// <summary>
        /// Handle CORS preflight requests
        /// </summary>
        private void HandleCorsPreFlight(HttpListenerResponse response, string origin)
        {
            // Echo back the validated origin, or use wildcard if no origin header
            response.Headers.Add("Access-Control-Allow-Origin", origin ?? "*");
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

            // Get active document name if available
            string documentName = null;
            try
            {
                var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                documentName = doc?.DisplayName;
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- best-effort status read; the health endpoint must still report if the document name is unavailable
            {
                Core.DebugLog.Debug($"Could not read active document name for status: {ex.Message}");
            }

            var uptimeSeconds = (int)(DateTime.UtcNow - _startTime).TotalSeconds;

            var health = JsonSerializer.Serialize(new
            {
                status = "ok",
                server = "Cordyceps MCP",
                transport = "streamable-http",
                toolCount = _tools.Count,
                commandCount = CommandCount,
                uptimeSeconds,
                activeDocument = documentName
            });
            var bytes = Encoding.UTF8.GetBytes(health);
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.Close();
        }

        /// <summary>
        /// Handle POST /mcp requests (Streamable HTTP transport)
        /// </summary>
        private async Task HandleMcpPostAsync(HttpListenerContext context, string origin)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Validate Accept header - require application/json (SSE optional since we're stateless)
                var accept = request.Headers["Accept"] ?? "";
                if (!accept.Contains("application/json"))
                {
                    Core.DebugLog.WriteLine($"Invalid Accept header (must include application/json): {accept}", "WARN", 1);
                    response.StatusCode = 406; // Not Acceptable
                    response.Close();
                    return;
                }

                // Add CORS header for actual requests (echo validated origin)
                response.Headers.Add("Access-Control-Allow-Origin", origin ?? "*");

                // Check request body size limit (10MB) to prevent memory exhaustion
                const long MAX_BODY_SIZE = 10 * 1024 * 1024;
                if (request.ContentLength64 > MAX_BODY_SIZE)
                {
                    Core.DebugLog.WriteLine($"Request body too large: {request.ContentLength64} bytes (max {MAX_BODY_SIZE})", "WARN", 1);
                    response.StatusCode = 413; // Payload Too Large
                    response.Close();
                    return;
                }

                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var json = await reader.ReadToEndAsync();

                Core.DebugLog.WriteLine($"Received: {json.Substring(0, Math.Min(LOG_TRUNCATE_LENGTH, json.Length))}...", "INFO", 1);

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
                    Core.DebugLog.WriteLine($"Notification '{method}' processed", "INFO", 1);
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

                Core.DebugLog.WriteLine($"Responding: {responseJson.Substring(0, Math.Min(LOG_TRUNCATE_LENGTH, responseJson.Length))}...", "INFO", 1);

                // Send response via HTTP
                var bytes = Encoding.UTF8.GetBytes(responseJson);
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
                await response.OutputStream.FlushAsync();
            }
            catch (Exception ex)
            {
                Core.DebugLog.WriteLine($"JSON-RPC error: {ex.Message}", "ERROR", 1);
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
            return @"Cordyceps: MCP control of Grasshopper (parametric 3D design).

READ FIRST: gh://docs/getting-started (use resources/read)

UNIFIED TOOLS (use action='help' for details):
- gh_canvas: add, delete, move, rename, find, search, list, info, bounds, validate, constant, bake, zoom, view, get, set, config, preview, enable, group_create, group_delete, group_add, group_remove, group_list, group_rename, group_color, group_move, zoomable
- gh_wire: connect, disconnect, list, clear, validate
- gh_document: info, save, clear, solver, recompute, undo, redo, snapshot, revert, snapshots, capture_canvas, capture_viewport, capture_region, capture_views
- gh_script: get, set, configure, info
- gh_inspect: status, outputs, trace, disconnected, geometry, log, reports, categories, docs
- rhino_scene: objects, select, deselect, set_layer, set_name, layers, layer_create, layer_set, layer_delete, hide, show, delete, set_color, bbox, place_image, script
- rhino_render: display, camera, zoom, modes, render, settings, ground, sun, skylight, view_save, view_load, view_list, view_delete, light_add, light_list, light_set, light_delete, material_list, material_library, material_instantiate, material_create, material_texture, material_apply, material_delete, env_list, env_current, env_set, env_create, env_delete

Key points:
- Disable solver: gh_document(action='solver', enabled=false)
- Ambiguous names: use GUID or Category/Name format
- Spacing: 150px horizontal, 70px vertical
- CRITICAL: Inside a cluster editor, NEVER advise the user to press F5 or use Grasshopper's native recompute. It will destroy cluster inputs. Use gh_document(action='recompute') instead — it is cluster-safe.

VERIFY PERIODICALLY: After completing a section of work, run gh_inspect(action='status')
to catch errors in components you didn't directly modify. Changes can break downstream components.

Resources: gh://docs/getting-started, gh://docs/data-trees, gh://docs/common-errors, gh://component/{name}, gh://patterns/*
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

            // Capture the context once. Stop() releases _context during teardown; a handler still
            // in flight then would otherwise hand a null context to the tool and NRE. Capturing a
            // local (the field read is atomic) and guarding it turns that race into a clean,
            // structured "shutting down" result.
            var ctx = _context;
            if (ctx == null)
            {
                var shuttingDown = Core.McpResultFormatter.FormatExceptionResult(
                    new InvalidOperationException("MCP server is shutting down; the request was not processed."));
                return new
                {
                    content = new[] { new { type = "text", text = shuttingDown } },
                    isError = true
                };
            }

            RecordCommand(name);

            var tool = _tools.FirstOrDefault(t => t.Name == name);
            if (tool == null)
                throw new Exception($"Unknown tool: {name}");

            // Create tool instance (all tool classes take GrasshopperContext)
            var instance = Activator.CreateInstance(tool.DeclaringType, ctx);

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
                    // Required parameter is missing - throw a clear error
                    throw new Exception($"Missing required parameter: {param.Name}");
                }
            }

            object result;
            try
            {
                // Invoke the tool method
                result = tool.Method.Invoke(instance, args);

                // Handle async methods
                if (result is Task task)
                {
                    await task;
                    var resultProperty = task.GetType().GetProperty("Result");
                    result = resultProperty?.GetValue(task);
                }
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- tool-invocation boundary: any tool-body failure must become a structured {success:false} result, not a JSON-RPC protocol error (project error-handling contract)
            {
                // Log the full exception (type + stack) operator-side for root-causing; the client
                // payload stays message-only via FormatExceptionResult.
                Core.DebugLog.WriteLine($"Tool '{name}' threw: {ex}", "ERROR", 1);
                var errorText = Core.McpResultFormatter.FormatExceptionResult(ex);
                return new
                {
                    content = new[] { new { type = "text", text = errorText } },
                    isError = true
                };
            }

            // Format as MCP tool result. isError reflects the tool's own success flag
            // (see Core.McpResultFormatter) so clients see a failed tool result as an error.
            var resultText = result?.ToString() ?? "";
            return new
            {
                content = new[] { new { type = "text", text = resultText } },
                isError = Core.McpResultFormatter.IsErrorResult(resultText)
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

        private static object ConvertJsonValue(JsonElement element, Type targetType) => Core.JsonTypeConverter.ConvertJsonValue(element, targetType);

        /// <summary>
        /// Record that a command was executed
        /// </summary>
        public void RecordCommand(string commandType)
        {
            _commandStats.Record($"{commandType} @ {DateTime.Now:HH:mm:ss}");
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
