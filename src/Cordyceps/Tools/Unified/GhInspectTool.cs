using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified inspect tool - diagnostic and inspection operations
    /// </summary>
    [McpServerToolType]
    public class GhInspectTool
    {
        private const int MAX_TRACE_DEPTH = 50;
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_inspect",
            Description = "Diagnostic and inspection operations for debugging definitions",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["status"] = new ActionInfo
                {
                    Name = "status",
                    Description = "Get status of all components (OK, ERROR, WARNING, DISCONNECTED)",
                    Optional = new[] { "category" },
                    Example = "action='status' OR action='status', category='Curve'"
                },
                ["outputs"] = new ActionInfo
                {
                    Name = "outputs",
                    Description = "Get output values from a component",
                    Required = new[] { "id" },
                    Example = "action='outputs', id='abc-123'"
                },
                ["trace"] = new ActionInfo
                {
                    Name = "trace",
                    Description = "Trace data flow upstream or downstream",
                    Required = new[] { "id" },
                    Optional = new[] { "direction" },
                    Example = "action='trace', id='abc', direction='upstream'",
                    Tips = new[] { "direction: 'upstream' (default) or 'downstream'" }
                },
                ["disconnected"] = new ActionInfo
                {
                    Name = "disconnected",
                    Description = "Find all disconnected required inputs",
                    Optional = new[] { "type" },
                    Example = "action='disconnected' OR action='disconnected', type='script'"
                },
                ["geometry"] = new ActionInfo
                {
                    Name = "geometry",
                    Description = "Get geometry info (bbox, counts, validity)",
                    Required = new[] { "id" },
                    Example = "action='geometry', id='abc-123'"
                },
                ["log"] = new ActionInfo
                {
                    Name = "log",
                    Description = "Get debug log entries",
                    Optional = new[] { "limit", "clear" },
                    Example = "action='log', limit=50, clear=true"
                },
                ["reports"] = new ActionInfo
                {
                    Name = "reports",
                    Description = "Get debug reports from script components",
                    Example = "action='reports'"
                },
                ["categories"] = new ActionInfo
                {
                    Name = "categories",
                    Description = "List all component categories",
                    Example = "action='categories'"
                },
                ["docs"] = new ActionInfo
                {
                    Name = "docs",
                    Description = "Get documentation for a component type",
                    Required = new[] { "component" },
                    Example = "action='docs', component='Circle'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "Use 'status' to quickly identify errors and warnings",
                "Use 'trace' to understand data flow through your definition"
            }
        };

        public GhInspectTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Inspection operations. Actions: status|outputs|trace|disconnected|geometry|log|reports|categories|docs|help")]
        public string GhInspect(
            [Description("Action to perform")] string action,
            [Description("Component GUID")] string id = null,
            [Description("Category filter")] string category = null,
            [Description("Component type filter")] string type = null,
            [Description("Trace direction (upstream/downstream)")] string direction = "upstream",
            [Description("Component name or GUID for docs")] string component = null,
            [Description("Log entry limit")] int limit = 100,
            [Description("Clear log after retrieval (true/false)")] string clear = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("id", id),
                ("category", category),
                ("type", type),
                ("direction", direction),
                ("component", component),
                ("limit", limit != 100 ? (object)limit : null),
                ("clear", clear)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            bool clearBool = ToolHelpers.ParseBool(clear, false);

            return action.ToLowerInvariant() switch
            {
                "status" => ActionStatus(category),
                "outputs" => ActionOutputs(id),
                "trace" => ActionTrace(id, direction),
                "disconnected" => ActionDisconnected(type),
                "geometry" => ActionGeometry(id),
                "log" => ActionLog(limit, clearBool),
                "reports" => ActionReports(),
                "categories" => ActionCategories(),
                "docs" => ActionDocs(component),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionStatus(string category)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var components = new List<object>();
                int ok = 0, errors = 0, warnings = 0, disconnected = 0;

                foreach (var obj in doc.Objects)
                {
                    if (!(obj is IGH_Component comp)) continue;
                    if (ToolHelpers.IsCordycepsInfrastructure(comp, infraIds)) continue;

                    if (!string.IsNullOrEmpty(category))
                    {
                        var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == comp.ComponentGuid);
                        var compCat = proxy?.Desc.Category ?? comp.Category;
                        if (!string.Equals(compCat, category, StringComparison.OrdinalIgnoreCase)) continue;
                    }

                    string status = "OK";
                    string message = "";

                    if (comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error)
                    {
                        status = "ERROR";
                        message = string.Join("; ", comp.RuntimeMessages(GH_RuntimeMessageLevel.Error));
                        errors++;
                    }
                    else if (comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning)
                    {
                        status = "WARNING";
                        message = string.Join("; ", comp.RuntimeMessages(GH_RuntimeMessageLevel.Warning));
                        warnings++;
                    }
                    else
                    {
                        var missing = comp.Params.Input
                            .Where(i => !i.Optional && i.SourceCount == 0 && i.VolatileDataCount == 0)
                            .Select(i => i.Name).ToList();
                        if (missing.Count > 0)
                        {
                            status = "DISCONNECTED";
                            message = $"Missing: {string.Join(", ", missing)}";
                            disconnected++;
                        }
                        else ok++;
                    }

                    components.Add(new
                    {
                        id = comp.InstanceGuid.ToString(),
                        name = comp.NickName != comp.Name ? comp.NickName : comp.Name,
                        type = comp.Name,
                        status,
                        message
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    summary = new { total = components.Count, ok, error = errors, warning = warnings, disconnected },
                    components
                });
            });
        }

        private string ActionOutputs(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var outputs = new List<object>();
                IEnumerable<IGH_Param> outParams = obj is IGH_Component comp ? comp.Params.Output
                    : obj is IGH_Param param ? new[] { param } : null;

                if (outParams != null)
                {
                    foreach (var output in outParams)
                    {
                        var data = output.VolatileData.AllData(true).Take(10).Select(d => d?.ToString() ?? "null").ToList();
                        outputs.Add(new
                        {
                            name = output.Name,
                            type = output.TypeName,
                            count = output.VolatileDataCount,
                            branches = output.VolatileData.PathCount,
                            preview = data,
                            truncated = output.VolatileDataCount > 10
                        });
                    }
                }

                return JsonConvert.SerializeObject(new { success = true, id, name = obj.Name, outputs });
            });
        }

        private string ActionTrace(string id, string direction)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var traced = new List<object>();
                var visited = new HashSet<Guid>();

                if (direction.ToLowerInvariant() == "downstream")
                    TraceDownstream(component, traced, visited, 0);
                else
                    TraceUpstream(component, traced, visited, 0);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    start = new { id = component.InstanceGuid.ToString(), name = component.Name },
                    direction,
                    count = traced.Count,
                    trace = traced
                });
            });
        }

        private string ActionDisconnected(string type)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var disconnected = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (!(obj is IGH_Component comp)) continue;
                    if (ToolHelpers.IsCordycepsInfrastructure(comp, infraIds)) continue;

                    bool include = string.IsNullOrEmpty(type) || type == "all"
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
                                componentName = comp.NickName ?? comp.Name,
                                inputName = input.Name,
                                inputIndex = comp.Params.Input.IndexOf(input),
                                inputType = input.TypeName
                            });
                        }
                    }
                }

                return JsonConvert.SerializeObject(new { success = true, count = disconnected.Count, disconnected });
            });
        }

        private string ActionGeometry(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var outputs = new List<object>();
                IEnumerable<IGH_Param> outParams = obj is IGH_Component comp ? comp.Params.Output
                    : obj is IGH_Param param ? new[] { param } : null;

                if (outParams != null)
                {
                    foreach (var output in outParams)
                    {
                        var items = output.VolatileData.AllData(true).Take(10).Select(d => SerializeGeometry(d)).Where(x => x != null).ToList();
                        if (items.Count > 0)
                        {
                            outputs.Add(new
                            {
                                name = output.Name,
                                type = output.TypeName,
                                count = items.Count,
                                items,
                                truncated = output.VolatileDataCount > 10
                            });
                        }
                    }
                }

                return JsonConvert.SerializeObject(new { success = true, id, name = obj.Name, outputs });
            });
        }

        private string ActionLog(int limit, bool clear)
        {
            var entries = DebugLog.GetEntries(clear);
            var recent = entries.Count > limit ? entries.GetRange(entries.Count - limit, limit) : entries;

            var logLines = recent.Select(e => new
            {
                time = e.Timestamp.ToString("HH:mm:ss.fff"),
                level = e.Level,
                message = e.Message
            }).ToList();

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = logLines.Count,
                total = entries.Count,
                cleared = clear,
                entries = logLines
            });
        }

        private string ActionReports()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var reports = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (!(obj is IGH_Component comp)) continue;
                    if (!comp.GetType().Name.Contains("Script") && !comp.GetType().Name.Contains("Python")) continue;

                    var reportParam = comp.Params.Output.FirstOrDefault(p =>
                        p.Name.Contains("Report") || p.Name == "out");

                    if (reportParam != null && reportParam.VolatileDataCount > 0)
                    {
                        var data = reportParam.VolatileData.AllData(true).Select(d => d?.ToString()).Where(s => s != null).ToList();
                        if (data.Count > 0)
                        {
                            reports.Add(new
                            {
                                componentId = comp.InstanceGuid.ToString(),
                                componentName = comp.NickName ?? comp.Name,
                                param = reportParam.Name,
                                report = string.Join("\n", data)
                            });
                        }
                    }
                }

                return JsonConvert.SerializeObject(new { success = true, count = reports.Count, reports });
            });
        }

        private string ActionCategories()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var cats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var proxy in Instances.ComponentServer.ObjectProxies)
                {
                    if (proxy?.Desc?.Category == null) continue;
                    cats.TryGetValue(proxy.Desc.Category, out int count);
                    cats[proxy.Desc.Category] = count + 1;
                }

                var categories = cats.OrderBy(k => k.Key).Select(k => new { name = k.Key, count = k.Value }).ToList();
                return JsonConvert.SerializeObject(new { success = true, count = categories.Count, categories });
            });
        }

        private string ActionDocs(string component)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                IGH_ObjectProxy proxy = null;
                if (Guid.TryParse(component, out Guid guid))
                    proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == guid);
                else
                    proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p =>
                        p.Desc.Name.Equals(component, StringComparison.OrdinalIgnoreCase));

                if (proxy == null)
                    return ToolHelpers.ErrorResponse($"Component not found: {component}");

                var inputs = new List<object>();
                var outputs = new List<object>();

                try
                {
                    var instance = proxy.CreateInstance();
                    if (instance is IGH_Component comp)
                    {
                        foreach (var i in comp.Params.Input)
                            inputs.Add(new { name = i.Name, type = i.TypeName, optional = i.Optional, description = i.Description });
                        foreach (var o in comp.Params.Output)
                            outputs.Add(new { name = o.Name, type = o.TypeName, description = o.Description });
                    }
                }
                catch { }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    name = proxy.Desc.Name,
                    description = proxy.Desc.Description,
                    category = proxy.Desc.Category,
                    subcategory = proxy.Desc.SubCategory,
                    guid = proxy.Guid.ToString(),
                    inputs,
                    outputs
                });
            });
        }

        #region Helper Methods

        private object SerializeGeometry(object val)
        {
            if (val == null) return null;
            if (val is Grasshopper.Kernel.Types.IGH_Goo goo)
                val = goo.ScriptVariable();
            if (val == null) return null;

            if (val is Rhino.Geometry.Point3d pt)
                return new { type = "Point3d", x = pt.X, y = pt.Y, z = pt.Z };
            if (val is Rhino.Geometry.Vector3d vec)
                return new { type = "Vector3d", x = vec.X, y = vec.Y, z = vec.Z };
            if (val is Rhino.Geometry.Mesh mesh)
                return new { type = "Mesh", faces = mesh.Faces.Count, vertices = mesh.Vertices.Count, valid = mesh.IsValid };
            if (val is Rhino.Geometry.Curve curve)
                return new { type = "Curve", length = curve.GetLength(), closed = curve.IsClosed };
            if (val is Rhino.Geometry.Brep brep)
                return new { type = "Brep", faces = brep.Faces.Count, edges = brep.Edges.Count, solid = brep.IsSolid };

            return new { type = val.GetType().Name, value = val.ToString() };
        }

        private void TraceUpstream(IGH_DocumentObject obj, List<object> traced, HashSet<Guid> visited, int depth)
        {
            if (visited.Contains(obj.InstanceGuid) || depth > MAX_TRACE_DEPTH) return;
            visited.Add(obj.InstanceGuid);

            IEnumerable<IGH_Param> inputs = obj is IGH_Component c ? c.Params.Input : obj is IGH_Param p ? new[] { p } : null;
            if (inputs == null) return;

            foreach (var input in inputs)
            {
                foreach (var source in input.Sources)
                {
                    var src = source.Attributes.GetTopLevel.DocObject;
                    traced.Add(new { id = src.InstanceGuid.ToString(), name = src.Name, depth = depth + 1, via = $"{source.Name}->{input.Name}" });
                    TraceUpstream(src, traced, visited, depth + 1);
                }
            }
        }

        private void TraceDownstream(IGH_DocumentObject obj, List<object> traced, HashSet<Guid> visited, int depth)
        {
            if (visited.Contains(obj.InstanceGuid) || depth > MAX_TRACE_DEPTH) return;
            visited.Add(obj.InstanceGuid);

            IEnumerable<IGH_Param> outputs = obj is IGH_Component c ? c.Params.Output : obj is IGH_Param p ? new[] { p } : null;
            if (outputs == null) return;

            foreach (var output in outputs)
            {
                foreach (var recipient in output.Recipients)
                {
                    var rec = recipient.Attributes.GetTopLevel.DocObject;
                    traced.Add(new { id = rec.InstanceGuid.ToString(), name = rec.Name, depth = depth + 1, via = $"{output.Name}->{recipient.Name}" });
                    TraceDownstream(rec, traced, visited, depth + 1);
                }
            }
        }

        #endregion
    }
}
