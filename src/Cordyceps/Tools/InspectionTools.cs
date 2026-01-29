using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Inspection and diagnostic operations
    /// </summary>
    [McpServerToolType]
    public class InspectionTools
    {
        private const int MAX_TRACE_DEPTH = 50;

        private static readonly HashSet<string> GeometryTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "point", "point3d", "vector", "vector3d", "plane",
            "line", "curve", "circle", "arc", "polyline",
            "surface", "brep", "mesh", "box", "sphere"
        };

        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public InspectionTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Get the status of all components on the canvas (OK, ERROR, WARNING, DISCONNECTED)")]
        public string GhGetCanvasStatus(
            [Description("Filter by category (e.g., 'Curve', 'Kangaroo'). Use get_categories to see valid values.")] string category = null)
        {
            _server?.RecordCommand("get_canvas_status");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure component IDs to filter (Cordyceps + connected components)
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                var components = new List<object>();
                int okCount = 0, errorCount = 0, warningCount = 0, disconnectedCount = 0;

                foreach (var obj in doc.Objects)
                {
                    if (obj is IGH_Component comp)
                    {
                        // Skip Cordyceps infrastructure (the component and anything connected to it)
                        if (ToolHelpers.IsCordycepsInfrastructure(comp, infraIds))
                            continue;

                        // Apply category filter if specified
                        if (!string.IsNullOrEmpty(category))
                        {
                            var proxy = Instances.ComponentServer.ObjectProxies
                                .FirstOrDefault(p => p.Guid == comp.ComponentGuid);
                            var compCategory = proxy?.Desc.Category ?? comp.Category;
                            if (!string.Equals(compCategory, category, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }

                        string status = "OK";
                        string statusMessage = "";

                        // Check runtime messages
                        if (comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error)
                        {
                            status = "ERROR";
                            statusMessage = string.Join("; ", comp.RuntimeMessages(GH_RuntimeMessageLevel.Error));
                            errorCount++;
                        }
                        else if (comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning)
                        {
                            status = "WARNING";
                            statusMessage = string.Join("; ", comp.RuntimeMessages(GH_RuntimeMessageLevel.Warning));
                            warningCount++;
                        }
                        else
                        {
                            // Check for disconnected required inputs
                            bool hasDisconnected = false;
                            var disconnectedInputs = new List<string>();

                            foreach (var input in comp.Params.Input)
                            {
                                if (!input.Optional && input.SourceCount == 0 && input.VolatileDataCount == 0)
                                {
                                    hasDisconnected = true;
                                    disconnectedInputs.Add(input.Name);
                                }
                            }

                            if (hasDisconnected)
                            {
                                status = "DISCONNECTED";
                                statusMessage = $"Missing inputs: {string.Join(", ", disconnectedInputs)}";
                                disconnectedCount++;
                            }
                            else
                            {
                                okCount++;
                            }
                        }

                        // Use nickname if set and different from name, otherwise use name
                        string displayName = !string.IsNullOrEmpty(comp.NickName) && comp.NickName != comp.Name
                            ? comp.NickName
                            : comp.Name;

                        components.Add(new
                        {
                            id = comp.InstanceGuid.ToString(),
                            displayName,
                            name = comp.Name,
                            nickname = comp.NickName,
                            status,
                            message = statusMessage,
                            inputCount = comp.Params.Input.Count,
                            outputCount = comp.Params.Output.Count
                        });
                    }
                }

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["summary"] = new
                    {
                        total = components.Count,
                        ok = okCount,
                        error = errorCount,
                        warning = warningCount,
                        disconnected = disconnectedCount
                    },
                    ["components"] = components
                };
                if (!string.IsNullOrEmpty(category))
                    result["filteredBy"] = category;

                return JsonConvert.SerializeObject(result);
            });
        }

        [McpServerTool, Description("List all component categories with counts and plugin info. Use to discover valid category values for filtering.")]
        public string GhGetCategories()
        {
            _server?.RecordCommand("get_categories");
            return _context.ExecuteOnUiThread(() =>
            {
                // Collect all categories from loaded component proxies
                var categoryStats = new Dictionary<string, CategoryInfo>(StringComparer.OrdinalIgnoreCase);

                foreach (var proxy in Instances.ComponentServer.ObjectProxies)
                {
                    if (proxy?.Desc?.Category == null) continue;

                    var category = proxy.Desc.Category;
                    if (!categoryStats.TryGetValue(category, out var info))
                    {
                        info = new CategoryInfo { Name = category };
                        categoryStats[category] = info;
                    }
                    info.ComponentCount++;
                }

                // Build result with plugin info
                var categories = categoryStats.Values
                    .OrderBy(c => c.Name)
                    .Select(c =>
                    {
                        var pluginInfo = PluginRegistry.Instance.GetPluginForCategory(c.Name);
                        var isBuiltIn = IsBuiltInCategory(c.Name);

                        return new
                        {
                            name = c.Name,
                            componentCount = c.ComponentCount,
                            isBuiltIn,
                            plugin = pluginInfo != null ? new
                            {
                                name = pluginInfo.Name,
                                author = pluginInfo.Author,
                                documentationUrl = pluginInfo.DocumentationUrl
                            } : null
                        };
                    })
                    .ToList();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = categories.Count,
                    categories
                });
            });
        }

        private static bool IsBuiltInCategory(string category)
        {
            // Built-in Grasshopper categories
            var builtIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Params", "Maths", "Sets", "Vector", "Curve", "Surface", "Mesh",
                "Intersect", "Transform", "Display", "Kangaroo2", "Galapagos"
            };
            return builtIn.Contains(category);
        }

        private class CategoryInfo
        {
            public string Name { get; set; }
            public int ComponentCount { get; set; }
        }

        [McpServerTool, Description("Get all disconnected (unconnected required) inputs across components")]
        public string GhGetDisconnectedInputs(
            [Description("Filter by component type: 'all', 'script', or component name")] string type = "all")
        {
            _server?.RecordCommand("get_disconnected_inputs");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure component IDs to filter
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                var disconnected = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (obj is IGH_Component comp)
                    {
                        // Skip Cordyceps infrastructure
                        if (ToolHelpers.IsCordycepsInfrastructure(comp, infraIds))
                            continue;

                        // Filter by type
                        bool include = type == "all"
                            || (type == "script" && (comp.Name.Contains("Script") || comp.GetType().Name.Contains("Script")))
                            || comp.Name.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!include) continue;

                        foreach (var input in comp.Params.Input)
                        {
                            if (!input.Optional && input.SourceCount == 0 && input.VolatileDataCount == 0)
                            {
                                disconnected.Add(new
                                {
                                    componentId = comp.InstanceGuid.ToString(),
                                    componentName = comp.Name,
                                    componentNickname = comp.NickName,
                                    inputName = input.Name,
                                    inputNickname = input.NickName,
                                    inputIndex = comp.Params.Input.IndexOf(input),
                                    inputType = input.TypeName
                                });
                            }
                        }
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = disconnected.Count,
                    disconnected
                });
            });
        }

        [McpServerTool, Description("Trace the data flow upstream or downstream from a component")]
        public string GhTraceDataFlow(
            [Description("Component GUID to trace from")] string id,
            [Description("Direction: 'upstream' or 'downstream'")] string direction = "upstream")
        {
            _server?.RecordCommand("trace_data_flow");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var traced = new List<object>();
                var visited = new HashSet<Guid>();

                if (direction.ToLowerInvariant() == "upstream")
                {
                    TraceUpstream(component, traced, visited, 0);
                }
                else
                {
                    TraceDownstream(component, traced, visited, 0);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    startComponent = new
                    {
                        id = component.InstanceGuid.ToString(),
                        name = component.Name
                    },
                    direction,
                    count = traced.Count,
                    trace = traced
                });
            });
        }

        [McpServerTool, Description("Get the output values/data from a component")]
        public string GhGetComponentOutputs(
            [Description("Component GUID")] string id)
        {
            _server?.RecordCommand("get_component_outputs");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var outputs = new List<object>();

                if (obj is IGH_Component comp)
                {
                    foreach (var output in comp.Params.Output)
                    {
                        var data = new List<string>();
                        foreach (var item in output.VolatileData.AllData(true))
                        {
                            data.Add(item?.ToString() ?? "null");
                        }

                        outputs.Add(new
                        {
                            name = output.Name,
                            nickname = output.NickName,
                            type = output.TypeName,
                            dataCount = output.VolatileDataCount,
                            branchCount = output.VolatileData.PathCount,
                            recipientCount = output.Recipients.Count,
                            // Limit data preview to first 10 items
                            preview = data.Take(10).ToList(),
                            truncated = data.Count > 10
                        });
                    }
                }
                else if (obj is IGH_Param param)
                {
                    var data = new List<string>();
                    foreach (var item in param.VolatileData.AllData(true))
                    {
                        data.Add(item?.ToString() ?? "null");
                    }

                    outputs.Add(new
                    {
                        name = param.Name,
                        nickname = param.NickName,
                        type = param.TypeName,
                        dataCount = param.VolatileDataCount,
                        branchCount = param.VolatileData.PathCount,
                        recipientCount = param.Recipients.Count,
                        preview = data.Take(10).ToList(),
                        truncated = data.Count > 10
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = obj.InstanceGuid.ToString(),
                    name = obj.Name,
                    outputs
                });
            });
        }

[McpServerTool, Description("Get all debug report outputs (dbg_Report, out, Report) from script components")]
        public string GhGetDebugReports()
        {
            _server?.RecordCommand("get_debug_reports");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var reports = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (!(obj is IGH_Component comp)) continue;

                    // Filter to script components
                    string typeName = comp.GetType().Name;
                    if (!typeName.Contains("Script") && !typeName.Contains("CSharp") && !typeName.Contains("Python"))
                        continue;

                    // Find dbg_Report or similar output
                    var reportParam = comp.Params.Output.FirstOrDefault(p =>
                        p.Name.Contains("Report") || p.Name.Contains("report") ||
                        p.Name == "dbg_Report" || p.Name == "out");

                    if (reportParam != null && reportParam.VolatileDataCount > 0)
                    {
                        var reportData = new List<string>();
                        foreach (var item in reportParam.VolatileData.AllData(true))
                        {
                            var val = item?.ToString();
                            if (val != null)
                                reportData.Add(val);
                        }

                        if (reportData.Count > 0)
                        {
                            reports.Add(new
                            {
                                componentId = comp.InstanceGuid.ToString(),
                                componentName = comp.NickName ?? comp.Name,
                                paramName = reportParam.Name,
                                report = string.Join("\n", reportData)
                            });
                        }
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = reports.Count,
                    reports
                });
            });
        }

        [McpServerTool, Description("Get geometry information from a component's output (bounding box, counts, validity)")]
        public string GhGetGeometry(
            [Description("Component GUID")] string id)
        {
            _server?.RecordCommand("get_geometry");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var outputs = new List<object>();
                IEnumerable<IGH_Param> outputParams = null;

                if (obj is IGH_Component comp)
                {
                    outputParams = comp.Params.Output;
                }
                else if (obj is IGH_Param param)
                {
                    outputParams = new[] { param };
                }

                if (outputParams != null)
                {
                    foreach (var output in outputParams)
                    {
                        var geometryInfo = new List<object>();

                        foreach (var data in output.VolatileData.AllData(true))
                        {
                            var info = SerializeGeometryInfo(data);
                            if (info != null)
                                geometryInfo.Add(info);
                        }

                        if (geometryInfo.Count > 0)
                        {
                            outputs.Add(new
                            {
                                name = output.Name,
                                type = output.TypeName,
                                count = geometryInfo.Count,
                                // Limit to first 10 items
                                items = geometryInfo.Take(10).ToList(),
                                truncated = geometryInfo.Count > 10
                            });
                        }
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = obj.InstanceGuid.ToString(),
                    name = obj.Name,
                    outputs
                });
            });
        }

        private object SerializeGeometryInfo(object val)
        {
            if (val == null) return null;

            // Handle GH wrapper types
            if (val is Grasshopper.Kernel.Types.IGH_Goo goo)
            {
                val = goo.ScriptVariable();
                if (val == null) return new { type = goo.GetType().Name, value = "null" };
            }

            // Primitives and strings
            if (val is string s)
                return new { type = "string", value = s.Length > 200 ? s.Substring(0, 200) + "..." : s };
            if (val is int || val is double || val is float || val is bool || val is long)
                return new { type = val.GetType().Name, value = val };

            // Rhino geometry types
            if (val is Rhino.Geometry.Point3d pt)
                return new { type = "Point3d", x = pt.X, y = pt.Y, z = pt.Z };
            if (val is Rhino.Geometry.Vector3d vec)
                return new { type = "Vector3d", x = vec.X, y = vec.Y, z = vec.Z };
            if (val is Rhino.Geometry.Plane plane)
                return new
                {
                    type = "Plane",
                    origin = new { x = plane.Origin.X, y = plane.Origin.Y, z = plane.Origin.Z },
                    normal = new { x = plane.Normal.X, y = plane.Normal.Y, z = plane.Normal.Z }
                };

            if (val is Rhino.Geometry.Mesh mesh)
            {
                var bbox = mesh.GetBoundingBox(true);
                return new
                {
                    type = "Mesh",
                    faces = mesh.Faces.Count,
                    vertices = mesh.Vertices.Count,
                    isValid = mesh.IsValid,
                    isClosed = mesh.IsClosed,
                    bbox = FormatBoundingBox(bbox)
                };
            }

            if (val is Rhino.Geometry.Curve curve)
            {
                var bbox = curve.GetBoundingBox(true);
                return new
                {
                    type = "Curve",
                    length = curve.GetLength(),
                    isClosed = curve.IsClosed,
                    bbox = FormatBoundingBox(bbox)
                };
            }

            if (val is Rhino.Geometry.Brep brep)
            {
                var bbox = brep.GetBoundingBox(true);
                return new
                {
                    type = "Brep",
                    faces = brep.Faces.Count,
                    edges = brep.Edges.Count,
                    isValid = brep.IsValid,
                    isSolid = brep.IsSolid,
                    bbox = FormatBoundingBox(bbox)
                };
            }

            if (val is Rhino.Geometry.Surface surface)
            {
                var bbox = surface.GetBoundingBox(true);
                return new
                {
                    type = "Surface",
                    bbox = FormatBoundingBox(bbox)
                };
            }

            // Fallback
            return new { type = val.GetType().Name, value = val.ToString() };
        }

        private object FormatBoundingBox(Rhino.Geometry.BoundingBox bbox)
        {
            return new
            {
                min = new { x = bbox.Min.X, y = bbox.Min.Y, z = bbox.Min.Z },
                max = new { x = bbox.Max.X, y = bbox.Max.Y, z = bbox.Max.Z }
            };
        }

        [McpServerTool, Description("Get debug log entries from the Cordyceps log buffer")]
        public string GhGetDebugLog(
            [Description("Maximum number of entries to return (default 100)")] int limit = 100,
            [Description("Clear the log buffer after retrieval")] bool clear = false)
        {
            _server?.RecordCommand("get_debug_log");

            if (limit <= 0) limit = 100;

            var entries = DebugLog.GetEntries(clear);

            // Take only the most recent entries up to limit
            var recentEntries = entries.Count > limit
                ? entries.GetRange(entries.Count - limit, limit)
                : entries;

            var logLines = new List<object>();
            foreach (var entry in recentEntries)
            {
                logLines.Add(new
                {
                    timestamp = entry.Timestamp.ToString("HH:mm:ss.fff"),
                    level = entry.Level,
                    message = entry.Message
                });
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                entries = logLines,
                count = logLines.Count,
                totalBuffered = entries.Count,
                cleared = clear
            });
        }

        [McpServerTool, Description("Clear the debug log buffer")]
        public string GhClearDebugLog()
        {
            _server?.RecordCommand("clear_debug_log");
            DebugLog.Clear();
            return JsonConvert.SerializeObject(new { success = true, message = "Debug log cleared" });
        }

        [McpServerTool, Description("Check if a component type is deprecated and get upgrade information")]
        public string GhCheckDeprecation(
            [Description("Component name or GUID to check")] string component)
        {
            _server?.RecordCommand("check_deprecation");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!RequestValidator.ValidateRequired(component, "component", out var compError))
                {
                    return ToolHelpers.ErrorResponse(compError);
                }

                // Check if it's a GUID
                Guid? componentGuid = null;
                if (Guid.TryParse(component, out Guid parsed))
                {
                    componentGuid = parsed;
                }
                else
                {
                    // Look up by name
                    var proxy = Instances.ComponentServer.ObjectProxies
                        .FirstOrDefault(p => p.Desc.Name.Equals(component, StringComparison.OrdinalIgnoreCase));
                    if (proxy != null)
                    {
                        componentGuid = proxy.Guid;
                    }
                }

                if (!componentGuid.HasValue)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        found = false,
                        message = $"Component '{component}' not found"
                    });
                }

                var isDeprecated = DeprecationRegistry.Instance.IsDeprecated(componentGuid.Value);
                var upgradeInfo = DeprecationRegistry.Instance.GetUpgradeInfo(componentGuid.Value);

                // Also check if hidden
                var proxy2 = Instances.ComponentServer.ObjectProxies
                    .FirstOrDefault(p => p.Guid == componentGuid.Value);
                var isHidden = proxy2?.Exposure == GH_Exposure.hidden;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    found = true,
                    componentName = proxy2?.Desc?.Name ?? component,
                    componentGuid = componentGuid.Value.ToString(),
                    deprecated = isDeprecated,
                    hidden = isHidden,
                    upgrade = upgradeInfo != null ? new
                    {
                        toName = upgradeInfo.ToName,
                        toGuid = upgradeInfo.ToGuid.ToString(),
                        version = upgradeInfo.Version.ToString("yyyy-MM-dd")
                    } : null,
                    message = isDeprecated
                        ? (upgradeInfo != null
                            ? $"Component is deprecated. Use '{upgradeInfo.ToName}' instead."
                            : "Component is deprecated/hidden. Check for alternatives.")
                        : "Component is current and supported."
                });
            });
        }

        [McpServerTool, Description("Suggest compatible connections for a component output")]
        public string GhSuggestConnections(
            [Description("Source component GUID")] string sourceId,
            [Description("Source output parameter name or index")] string sourceParam = "0")
        {
            _server?.RecordCommand("suggest_connections");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, sourceId, out var doc, out var sourceObj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure IDs to filter from suggestions
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                // Get the output parameter
                IGH_Param outputParam = null;
                if (sourceObj is IGH_Component sourceComp)
                {
                    if (int.TryParse(sourceParam, out int idx) && idx >= 0 && idx < sourceComp.Params.Output.Count)
                    {
                        outputParam = sourceComp.Params.Output[idx];
                    }
                    else
                    {
                        outputParam = sourceComp.Params.Output.FirstOrDefault(p =>
                            p.Name.Equals(sourceParam, StringComparison.OrdinalIgnoreCase) ||
                            p.NickName.Equals(sourceParam, StringComparison.OrdinalIgnoreCase));
                    }

                    if (outputParam == null)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = $"Output parameter not found: {sourceParam}" });
                    }
                }
                else if (sourceObj is IGH_Param param)
                {
                    outputParam = param;
                }
                else
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Source is not a component or parameter" });
                }

                var outputType = outputParam.TypeName;
                var suggestions = new List<object>();

                // Find components on canvas with compatible inputs
                foreach (var obj in doc.Objects)
                {
                    if (obj == sourceObj) continue;
                    if (!(obj is IGH_Component targetComp)) continue;

                    // Skip infrastructure components
                    if (ToolHelpers.IsCordycepsInfrastructure(targetComp, infraIds))
                        continue;

                    foreach (var input in targetComp.Params.Input)
                    {
                        // Check type compatibility
                        var compatibility = GetTypeCompatibility(outputType, input.TypeName);
                        if (compatibility > 0)
                        {
                            suggestions.Add(new
                            {
                                targetId = targetComp.InstanceGuid.ToString(),
                                targetName = targetComp.Name,
                                targetNickname = targetComp.NickName,
                                inputName = input.Name,
                                inputNickname = input.NickName,
                                inputIndex = targetComp.Params.Input.IndexOf(input),
                                inputType = input.TypeName,
                                inputAccess = input.Access.ToString(),
                                compatibility = compatibility >= 100 ? "exact" : compatibility >= 50 ? "compatible" : "convertible",
                                isConnected = input.SourceCount > 0
                            });
                        }
                    }
                }

                // Sort by compatibility (descending) and whether already connected
                var sorted = suggestions
                    .OrderByDescending(s => ((dynamic)s).compatibility == "exact" ? 3 : ((dynamic)s).compatibility == "compatible" ? 2 : 1)
                    .ThenBy(s => ((dynamic)s).isConnected)
                    .Take(20)
                    .ToList();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    sourceComponent = sourceObj.Name,
                    outputParameter = outputParam.Name,
                    outputType = outputType,
                    suggestionCount = sorted.Count,
                    suggestions = sorted
                });
            });
        }

        [McpServerTool, Description("Get detailed documentation for a component type")]
        public string GhGetComponentDocumentation(
            [Description("Component name or GUID")] string component)
        {
            _server?.RecordCommand("get_component_documentation");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!RequestValidator.ValidateRequired(component, "component", out var compError))
                {
                    return ToolHelpers.ErrorResponse(compError);
                }

                // Find the component proxy
                IGH_ObjectProxy proxy = null;

                if (Guid.TryParse(component, out Guid guid))
                {
                    proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == guid);
                }
                else
                {
                    proxy = Instances.ComponentServer.ObjectProxies
                        .FirstOrDefault(p => p.Desc.Name.Equals(component, StringComparison.OrdinalIgnoreCase));

                    if (proxy == null)
                    {
                        // Fuzzy match
                        proxy = Instances.ComponentServer.ObjectProxies
                            .FirstOrDefault(p => p.Desc.Name.IndexOf(component, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                }

                if (proxy == null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"Component not found: {component}"
                    });
                }

                // Get plugin info if available
                var pluginInfo = PluginRegistry.Instance.GetPluginForComponent(proxy);

                // Get deprecation info
                var isDeprecated = DeprecationRegistry.Instance.IsDeprecated(proxy.Guid);
                var upgradeInfo = DeprecationRegistry.Instance.GetUpgradeInfo(proxy.Guid);

                // Try to create an instance to get parameter details
                var inputs = new List<object>();
                var outputs = new List<object>();

                try
                {
                    var instance = proxy.CreateInstance();
                    if (instance is IGH_Component comp)
                    {
                        foreach (var input in comp.Params.Input)
                        {
                            inputs.Add(new
                            {
                                name = input.Name,
                                nickname = input.NickName,
                                description = input.Description,
                                type = input.TypeName,
                                access = input.Access.ToString(),
                                optional = input.Optional
                            });
                        }

                        foreach (var output in comp.Params.Output)
                        {
                            outputs.Add(new
                            {
                                name = output.Name,
                                nickname = output.NickName,
                                description = output.Description,
                                type = output.TypeName
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Warn($"Could not instantiate {proxy.Desc.Name} for docs: {ex.Message}");
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    name = proxy.Desc.Name,
                    description = proxy.Desc.Description,
                    category = proxy.Desc.Category,
                    subCategory = proxy.Desc.SubCategory,
                    guid = proxy.Guid.ToString(),
                    deprecated = isDeprecated,
                    hidden = proxy.Exposure == GH_Exposure.hidden,
                    upgrade = upgradeInfo != null ? new
                    {
                        toName = upgradeInfo.ToName,
                        toGuid = upgradeInfo.ToGuid.ToString()
                    } : null,
                    plugin = pluginInfo != null ? new
                    {
                        name = pluginInfo.Name,
                        author = pluginInfo.Author,
                        documentationUrl = pluginInfo.DocumentationUrl
                    } : null,
                    inputs,
                    outputs
                });
            });
        }

        #region Helper Methods

        /// <summary>
        /// Get type compatibility score (0 = incompatible, 50 = convertible, 100 = exact match)
        /// </summary>
        private int GetTypeCompatibility(string outputType, string inputType)
        {
            if (string.IsNullOrEmpty(outputType) || string.IsNullOrEmpty(inputType))
                return 0;

            // Normalize type names
            outputType = outputType.ToLowerInvariant();
            inputType = inputType.ToLowerInvariant();

            // Exact match
            if (outputType == inputType) return 100;

            // Generic types accept anything
            if (inputType == "generic" || inputType == "goo" || inputType == "object")
                return 75;

            // Number conversions
            if ((outputType == "integer" || outputType == "int") && (inputType == "number" || inputType == "double"))
                return 90;
            if ((outputType == "number" || outputType == "double") && (inputType == "integer" || inputType == "int"))
                return 50; // Lossy conversion

            // Geometry hierarchy
            if (inputType == "geometry" || inputType == "geometrybase")
            {
                if (IsGeometryType(outputType)) return 80;
            }

            // Curve hierarchy
            if (inputType == "curve")
            {
                if (outputType == "line" || outputType == "circle" || outputType == "arc" ||
                    outputType == "polyline" || outputType == "nurbs curve")
                    return 90;
            }

            // Surface/Brep
            if (inputType == "brep" && outputType == "surface")
                return 90;
            if (inputType == "surface" && outputType == "brep")
                return 50; // May fail

            // Point/Vector interchangeable
            if ((outputType == "point" || outputType == "point3d") &&
                (inputType == "vector" || inputType == "vector3d"))
                return 70;
            if ((outputType == "vector" || outputType == "vector3d") &&
                (inputType == "point" || inputType == "point3d"))
                return 70;

            return 0;
        }

        private bool IsGeometryType(string typeName) => GeometryTypes.Contains(typeName);

        private void TraceUpstream(IGH_DocumentObject obj, List<object> traced, HashSet<Guid> visited, int depth)
        {
            if (visited.Contains(obj.InstanceGuid)) return;
            if (depth > MAX_TRACE_DEPTH) return; // Prevent infinite loops

            visited.Add(obj.InstanceGuid);

            IEnumerable<IGH_Param> inputs = null;

            if (obj is IGH_Component comp)
            {
                inputs = comp.Params.Input;
            }
            else if (obj is IGH_Param param)
            {
                inputs = new[] { param };
            }

            if (inputs == null) return;

            foreach (var input in inputs)
            {
                foreach (var source in input.Sources)
                {
                    var sourceObj = source.Attributes.GetTopLevel.DocObject;

                    traced.Add(new
                    {
                        id = sourceObj.InstanceGuid.ToString(),
                        name = sourceObj.Name,
                        nickname = sourceObj.NickName,
                        depth = depth + 1,
                        connectedVia = new
                        {
                            fromOutput = source.Name,
                            toInput = input.Name
                        }
                    });

                    TraceUpstream(sourceObj, traced, visited, depth + 1);
                }
            }
        }

        private void TraceDownstream(IGH_DocumentObject obj, List<object> traced, HashSet<Guid> visited, int depth)
        {
            if (visited.Contains(obj.InstanceGuid)) return;
            if (depth > MAX_TRACE_DEPTH) return;

            visited.Add(obj.InstanceGuid);

            IEnumerable<IGH_Param> outputs = null;

            if (obj is IGH_Component comp)
            {
                outputs = comp.Params.Output;
            }
            else if (obj is IGH_Param param)
            {
                outputs = new[] { param };
            }

            if (outputs == null) return;

            foreach (var output in outputs)
            {
                foreach (var recipient in output.Recipients)
                {
                    var recipientObj = recipient.Attributes.GetTopLevel.DocObject;

                    traced.Add(new
                    {
                        id = recipientObj.InstanceGuid.ToString(),
                        name = recipientObj.Name,
                        nickname = recipientObj.NickName,
                        depth = depth + 1,
                        connectedVia = new
                        {
                            fromOutput = output.Name,
                            toInput = recipient.Name
                        }
                    });

                    TraceDownstream(recipientObj, traced, visited, depth + 1);
                }
            }
        }

        #endregion
    }
}
