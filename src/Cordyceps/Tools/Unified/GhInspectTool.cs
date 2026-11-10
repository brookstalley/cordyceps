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
    public partial class GhInspectTool
    {
        private const int MAX_TRACE_DEPTH = 50;
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_inspect",
            Description = "Diagnostic and inspection operations for debugging definitions",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["connection"] = new ActionInfo
                {
                    Name = "connection",
                    Description = "Is the bridge alive, busy, or blocked? Answers from cached state without touching the Grasshopper document, so it replies even while the Rhino UI thread is wedged",
                    Example = "action='connection'",
                    Tips = new[]
                    {
                        "Use this when a call times out or goes quiet: it is the only action guaranteed to answer.",
                        "rhino.ui='blocked' + grasshopper.solving=true means BUSY — wait and retry.",
                        "rhino.modal_inferred=true means a modal dialog is open in Rhino and only a human can clear it — stop retrying and say so.",
                        "Every other tool response already carries a compact 'status' block; use this action when you need the full picture or the last call returned nothing."
                    }
                },
                ["status"] = new ActionInfo
                {
                    Name = "status",
                    Description = "Get status of all components (OK, ERROR, WARNING, DISCONNECTED)",
                    Optional = new[] { "category" },
                    Example = "action='status' OR action='status', category='Curve'",
                    Tips = new[] { "Returns success=false with a busy/blocked status instead of hanging when the Rhino UI thread is not responding — use action='connection' to see why." }
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
                    Example = "action='log', limit=50, clear=true",
                    Tips = new[] { "limit: most recent entries to return (default 100 when omitted or 0). A negative limit is an error." }
                },
                ["tree"] = new ActionInfo
                {
                    Name = "tree",
                    Description = "See the SHAPE of a data tree without pulling its data: branch paths, items per branch, nulls, depth. Diagnoses mismatched trees cheaply",
                    Optional = new[] { "id", "ids", "param", "side", "limit" },
                    Example = "action='tree', id='abc12345' OR action='tree', ids='[\"abc12345\", {\"id\": \"def67890\", \"param\": \"Result\"}]', side='input'",
                    Tips = new[]
                    {
                        "Give exactly one of id (one object) or ids (a JSON array of 1-50 entries, each an id string or {\"id\": \"...\", \"param\": \"...\"} with no other keys).",
                        "param (only with id): one port, by name, nickname or 0-based index. Without it every port on the side is read.",
                        "side: 'output' (default) or 'input'. side='input' shows the tree as the component receives it; with no param, a note names inputs whose branch counts or paths differ.",
                        "A parameter object (slider, panel, free parameter) has one tree, so param and side do not apply to it; its side reads 'parameter'.",
                        "limit: path groups per tree, 1-500 (default 20 when omitted or 0). An out-of-range value is an error, never adjusted.",
                        "paths are runs: '{0;0}..{0;9}: 1 each' is ten consecutive branches of one item; '{0;3}: 4' is one branch of four. items includes nulls; invalid counts non-null items whose IsValid is false.",
                        "A group is an error. Trees that resolve are returned alongside errors (success=false). solver appears only when the solver is off; a tree whose owner has not computed says why in note."
                    }
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
                    Description = "Get documentation for a component type (name/category/inputs/outputs)",
                    Required = new[] { "component" },
                    Example = "action='docs', component='Curve/Circle'",
                    Tips = new[] { "component is resolved exactly as gh_canvas(action='add') resolves type: exact name (case ignored), 'Category/Name', 'Category/Subcategory/Name', or GUID. An ambiguous or unknown name is an error listing the matches or suggestions, each with a 'use' spelling.", "If the component proxy can't be introspected, the response sets paramsUnavailable=true with a note — empty inputs/outputs then mean 'introspection failed', not 'no parameters'." }
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
                "Use 'trace' to understand data flow through your definition",
                "Use 'connection' when a call is slow or silent — it distinguishes a busy solver from a host blocked by a modal dialog"
            }
        };

        public GhInspectTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Inspection operations. Actions: connection|status|outputs|trace|disconnected|geometry|log|tree|reports|categories|docs|help")]
        public string GhInspect(
            [Description("Action to perform")] string action,
            [Description("Object: GUID, 8+ hex GUID prefix, or unique nickname")] string id = null,
            [Description("Category filter")] string category = null,
            [Description("Component type filter")] string type = null,
            [Description("Trace direction (upstream/downstream)")] string direction = "upstream",
            [Description("Component type for docs: exact name, 'Category/Name', or GUID (never matched partially)")] string component = null,
            [Description("log: most recent entries (default 100); tree: path groups per tree, 1-500 (default 20). 0 or omitted means the action's default; negative is an error")] int limit = 0,
            [Description("Clear log after retrieval (true/false)")] string clear = null,
            [Description("tree: JSON array of 1-50 objects to read, each an id string or {\"id\": \"...\", \"param\": \"...\"}. Use instead of id")] string ids = null,
            [Description("tree, with id: one port by name, nickname or 0-based index (default: every port on the side)")] string param = null,
            [Description("tree: 'output' (default) or 'input'")] string side = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("id", id),
                ("category", category),
                ("type", type),
                ("direction", direction),
                ("component", component),
                ("limit", limit != 0 ? (object)limit : null),
                ("clear", clear),
                ("ids", ids),
                ("param", param),
                ("side", side)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            bool clearBool = ToolHelpers.ParseBool(clear, false);

            return action.ToLowerInvariant() switch
            {
                "connection" => ActionConnection(),
                "status" => ActionStatus(category),
                "outputs" => ActionOutputs(id),
                "trace" => ActionTrace(id, direction),
                "disconnected" => ActionDisconnected(type),
                "geometry" => ActionGeometry(id),
                "log" => ActionLog(limit, clearBool),
                "tree" => ActionTree(id, ids, param, side, limit),
                "reports" => ActionReports(),
                "categories" => ActionCategories(),
                "docs" => ActionDocs(component),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        /// <summary>
        /// The connection probe. Deliberately does NOT call <c>_context.ExecuteOnUiThread</c>: that
        /// is what takes the document lock and marshals onto the Rhino UI thread, and this is the
        /// one call that must answer <em>while that thread is wedged</em>. Everything it reports
        /// comes from <see cref="SolverState"/>, which the UI thread writes and any thread can read.
        /// Keep it that way — a probe that can block is not a probe.
        /// </summary>
        private string ActionConnection()
            => StatusEnvelope.ProbeResult(SolverState.Shared.Derive());

        private string ActionStatus(string category)
        {
            // Enumerating component status needs the UI thread. If that thread is not responding,
            // marshaling would hang until the document lock's timeout (or, behind a modal dialog,
            // until a human intervenes) — the unbounded silence this feature exists to remove.
            // Answer immediately with the reason instead. Checked BEFORE marshaling, from cached
            // state only.
            var liveness = SolverState.Shared.Derive();
            if (liveness.Ui == UiLiveness.Blocked)
                return StatusEnvelope.BusyResult(liveness);

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

                    // The one health rule outline shares (ObjectHealth); the strings and joins are status's own.
                    var health = Core.Canvas.CanvasReader.Health(comp);
                    string status = health.StatusText;
                    string message = health.Message;
                    switch (health.Status)
                    {
                        case Core.Canvas.HealthStatus.Error: errors++; break;
                        case Core.Canvas.HealthStatus.Warning: warnings++; break;
                        case Core.Canvas.HealthStatus.Disconnected: disconnected++; break;
                        default: ok++; break;
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

                return JsonConvert.SerializeObject(new { success = true, id = obj.InstanceGuid.ToString(), name = obj.Name, outputs });
            });
        }

        private string ActionTrace(string id, string direction)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var traced = new List<object>();
                var visited = new HashSet<Guid>();
                // Infrastructure stays invisible in trace results, like everywhere else.
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var effectiveDirection = direction?.ToLowerInvariant() ?? "upstream";

                if (effectiveDirection == "downstream")
                    TraceDownstream(component, traced, visited, 0, infraIds);
                else
                    TraceUpstream(component, traced, visited, 0, infraIds);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    start = new { id = component.InstanceGuid.ToString(), name = component.Name },
                    direction = effectiveDirection,
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

                return JsonConvert.SerializeObject(new { success = true, id = obj.InstanceGuid.ToString(), name = obj.Name, outputs });
            });
        }

        private string ActionLog(int limit, bool clear)
        {
            // Validate before GetEntries: clearing is a mutation and must not happen on a bad limit.
            if (!Core.DataTrees.LimitRule.TryResolveLog(limit, out var count, out var limitError))
                return ToolHelpers.ErrorResponse(limitError);

            var entries = DebugLog.GetEntries(clear);
            var recent = entries.Count > count ? entries.GetRange(entries.Count - count, count) : entries;

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
                // The same strict rule gh_canvas(action='add') uses, so docs describe exactly the
                // type an add with the same text would create.
                var resolution = ComponentRegistry.ResolveType(component);
                if (!resolution.IsResolved)
                    return JsonConvert.SerializeObject(Core.Naming.TypeNameRule.ErrorPayload(resolution,
                        argName: "component", consequence: "No documentation was returned."));

                var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == resolution.Match.Guid);
                if (proxy == null)
                    return ToolHelpers.ErrorResponse($"Component not found: {component}");

                var inputs = new List<object>();
                var outputs = new List<object>();

                bool paramsAvailable = ToolHelpers.WithProxyComponent(proxy, comp =>
                {
                    foreach (var i in comp.Params.Input)
                        inputs.Add(new { name = i.Name, type = i.TypeName, optional = i.Optional, description = i.Description });
                    foreach (var o in comp.Params.Output)
                        outputs.Add(new { name = o.Name, type = o.TypeName, description = o.Description });
                });

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["name"] = proxy.Desc.Name,
                    ["description"] = proxy.Desc.Description,
                    ["category"] = proxy.Desc.Category,
                    ["subcategory"] = proxy.Desc.SubCategory,
                    ["guid"] = proxy.Guid.ToString(),
                    ["inputs"] = inputs,
                    ["outputs"] = outputs
                };

                // When the proxy can't be instantiated, inputs/outputs are empty NOT because the
                // component has no params, but because introspection failed. Surface that so the
                // caller doesn't read an empty list as authoritative (CQ-7T4P; the failure itself is
                // already logged by WithProxyComponent per CQ-5J9N). Keys are absent on success, so
                // the success-path response shape is unchanged.
                if (!paramsAvailable)
                {
                    result["paramsUnavailable"] = true;
                    result["note"] = "Component parameters could not be introspected (proxy instantiation failed); the empty inputs/outputs lists do not mean the component has no parameters. See gh_inspect(action='log') for the failure detail.";
                }

                return JsonConvert.SerializeObject(result);
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

        private void TraceUpstream(IGH_DocumentObject obj, List<object> traced, HashSet<Guid> visited, int depth, HashSet<Guid> infraIds)
        {
            if (visited.Contains(obj.InstanceGuid) || depth > MAX_TRACE_DEPTH) return;
            visited.Add(obj.InstanceGuid);

            IEnumerable<IGH_Param> inputs = obj is IGH_Component c ? c.Params.Input : obj is IGH_Param p ? new[] { p } : null;
            if (inputs == null) return;

            foreach (var input in inputs)
            {
                foreach (var source in input.Sources)
                {
                    // Null-safe like ToolHelpers.GetActiveObjects: phantom params can lack
                    // attributes/owner. Infrastructure stays invisible to callers.
                    var src = source.Attributes?.GetTopLevel?.DocObject;
                    if (src == null) continue;
                    if (infraIds.Contains(src.InstanceGuid)) continue;
                    traced.Add(new { id = src.InstanceGuid.ToString(), name = src.Name, depth = depth + 1, via = $"{source.Name}->{input.Name}" });
                    TraceUpstream(src, traced, visited, depth + 1, infraIds);
                }
            }
        }

        private void TraceDownstream(IGH_DocumentObject obj, List<object> traced, HashSet<Guid> visited, int depth, HashSet<Guid> infraIds)
        {
            if (visited.Contains(obj.InstanceGuid) || depth > MAX_TRACE_DEPTH) return;
            visited.Add(obj.InstanceGuid);

            IEnumerable<IGH_Param> outputs = obj is IGH_Component c ? c.Params.Output : obj is IGH_Param p ? new[] { p } : null;
            if (outputs == null) return;

            foreach (var output in outputs)
            {
                foreach (var recipient in output.Recipients)
                {
                    // Null-safe like ToolHelpers.GetActiveObjects: phantom params can lack
                    // attributes/owner. Infrastructure stays invisible to callers.
                    var rec = recipient.Attributes?.GetTopLevel?.DocObject;
                    if (rec == null) continue;
                    if (infraIds.Contains(rec.InstanceGuid)) continue;
                    traced.Add(new { id = rec.InstanceGuid.ToString(), name = rec.Name, depth = depth + 1, via = $"{output.Name}->{recipient.Name}" });
                    TraceDownstream(rec, traced, visited, depth + 1, infraIds);
                }
            }
        }

        #endregion
    }
}
