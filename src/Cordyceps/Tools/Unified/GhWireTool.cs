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
    /// Unified wire tool - connection operations (connect, disconnect, list, validate, clear)
    /// </summary>
    [McpServerToolType]
    public class GhWireTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_wire",
            Description = "Connection (wiring) operations between components",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["connect"] = new ActionInfo
                {
                    Name = "connect",
                    Description = "Create wire(s) between components",
                    Required = new string[0],
                    Optional = new[] { "sourceId", "sourceParam", "targetId", "targetParam", "connections" },
                    Example = "action='connect', sourceId='a', sourceParam='0', targetId='b', targetParam='R'",
                    Tips = new[] { "Use param name or index (0-based)", "For bulk: connections='[{sourceId,sourceParam,targetId,targetParam},...]'" }
                },
                ["disconnect"] = new ActionInfo
                {
                    Name = "disconnect",
                    Description = "Remove a wire between components",
                    Required = new[] { "sourceId", "sourceParam", "targetId", "targetParam" },
                    Example = "action='disconnect', sourceId='a', sourceParam='0', targetId='b', targetParam='R'"
                },
                ["list"] = new ActionInfo
                {
                    Name = "list",
                    Description = "List all connections on canvas",
                    Optional = new[] { "componentId" },
                    Example = "action='list' OR action='list', componentId='abc' (filter by component)"
                },
                ["clear"] = new ActionInfo
                {
                    Name = "clear",
                    Description = "Clear all inputs on a component",
                    Required = new[] { "id" },
                    Example = "action='clear', id='abc-123'"
                },
                ["validate"] = new ActionInfo
                {
                    Name = "validate",
                    Description = "Check if a connection is valid before creating it",
                    Required = new[] { "sourceId" },
                    Optional = new[] { "sourceParam", "targetId", "targetParam" },
                    Example = "action='validate', sourceId='a', targetId='b'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "Parameters can be specified by name or 0-based index",
                "Disable solver before bulk wiring: gh_document(action='solver', enabled=false)"
            }
        };

        public GhWireTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Connection operations. Actions: connect|disconnect|list|clear|validate|help")]
        public string GhWire(
            [Description("Action to perform")] string action,
            [Description("Source component GUID")] string sourceId = null,
            [Description("Source output param name or index")] string sourceParam = null,
            [Description("Target component GUID")] string targetId = null,
            [Description("Target input param name or index")] string targetParam = null,
            [Description("JSON array of connections for bulk connect")] string connections = null,
            [Description("Component GUID for list filter or clear")] string id = null,
            [Description("Alias for id (for list filter)")] string componentId = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("sourceId", sourceId),
                ("sourceParam", sourceParam),
                ("targetId", targetId),
                ("targetParam", targetParam),
                ("connections", connections),
                ("id", id ?? componentId),
                ("componentId", componentId ?? id)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            return action.ToLowerInvariant() switch
            {
                "connect" => ActionConnect(sourceId, sourceParam, targetId, targetParam, connections),
                "disconnect" => ActionDisconnect(sourceId, sourceParam, targetId, targetParam),
                "list" => ActionList(id ?? componentId),
                "clear" => ActionClear(id ?? componentId),
                "validate" => ActionValidate(sourceId, sourceParam, targetId, targetParam),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionConnect(string sourceId, string sourceParam, string targetId, string targetParam, string connections)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                List<dynamic> connectionList;
                if (!string.IsNullOrEmpty(connections))
                {
                    try { connectionList = JsonConvert.DeserializeObject<List<dynamic>>(connections); }
                    catch (Exception ex) { return ToolHelpers.ErrorResponse($"Failed to parse connections JSON: {ex.Message}"); }
                }
                else if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(targetId))
                {
                    connectionList = new List<dynamic> { new { sourceId, sourceParam = sourceParam ?? "0", targetId, targetParam = targetParam ?? "0" } };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Provide sourceId+targetId for single, or connections array for bulk");
                }

                var results = new List<object>();
                int successCount = 0, failCount = 0;
                IGH_DocumentObject lastTarget = null;

                foreach (var conn in connectionList)
                {
                    string srcId = conn.sourceId?.ToString();
                    string srcParam = conn.sourceParam?.ToString() ?? "0";
                    string tgtId = conn.targetId?.ToString();
                    string tgtParam = conn.targetParam?.ToString() ?? "0";

                    if (string.IsNullOrEmpty(srcId) || string.IsNullOrEmpty(tgtId))
                    {
                        results.Add(new { success = false, error = "Missing sourceId or targetId" });
                        failCount++;
                        continue;
                    }

                    if (!Guid.TryParse(srcId, out Guid srcGuid))
                    {
                        results.Add(new { success = false, error = "Invalid source GUID" });
                        failCount++;
                        continue;
                    }
                    if (infraIds.Contains(srcGuid))
                    {
                        results.Add(new { success = false, error = "Protected: required for MCP server" });
                        failCount++;
                        continue;
                    }

                    if (!Guid.TryParse(tgtId, out Guid tgtGuid))
                    {
                        results.Add(new { success = false, error = "Invalid target GUID" });
                        failCount++;
                        continue;
                    }
                    if (infraIds.Contains(tgtGuid))
                    {
                        results.Add(new { success = false, error = "Protected: required for MCP server" });
                        failCount++;
                        continue;
                    }

                    var srcObj = doc.FindObject(srcGuid, true);
                    var tgtObj = doc.FindObject(tgtGuid, true);
                    if (srcObj == null || tgtObj == null)
                    {
                        results.Add(new { success = false, error = "Component not found" });
                        failCount++;
                        continue;
                    }

                    var sourceOutput = GetOutputParameter(srcObj, srcParam);
                    var targetInput = GetInputParameter(tgtObj, tgtParam);

                    if (sourceOutput == null)
                    {
                        var available = srcObj is IGH_Component c ? c.Params.Output.Select(p => p.Name).ToList() : new List<string>();
                        results.Add(new { success = false, error = $"Source output '{srcParam}' not found", availableOutputs = available });
                        failCount++;
                        continue;
                    }

                    if (targetInput == null)
                    {
                        var available = tgtObj is IGH_Component c ? c.Params.Input.Select(p => p.Name).ToList() : new List<string>();
                        results.Add(new { success = false, error = $"Target input '{tgtParam}' not found", availableInputs = available });
                        failCount++;
                        continue;
                    }

                    targetInput.AddSource(sourceOutput);
                    lastTarget = tgtObj;
                    results.Add(new
                    {
                        success = true,
                        source = new { id = srcId, name = srcObj.NickName ?? srcObj.Name, param = sourceOutput.Name },
                        target = new { id = tgtId, name = tgtObj.NickName ?? tgtObj.Name, param = targetInput.Name }
                    });
                    successCount++;
                }

                if (lastTarget is IGH_ActiveObject activeTarget)
                    activeTarget.ExpireSolution(false);

                if (connectionList.Count == 1)
                    return JsonConvert.SerializeObject(results[0]);

                return JsonConvert.SerializeObject(new { success = failCount == 0, total = connectionList.Count, succeeded = successCount, failed = failCount, results });
            });
        }

        private string ActionDisconnect(string sourceId, string sourceParam, string targetId, string targetParam)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, sourceId, out var doc, out var srcObj, out var error))
                    return ToolHelpers.ErrorResponse($"Source: {error}");

                if (!ToolHelpers.TryGetUnprotectedComponent(_context, targetId, out var tgtObj, out error))
                    return ToolHelpers.ErrorResponse($"Target: {error}");

                var sourceOutput = GetOutputParameter(srcObj, sourceParam);
                if (sourceOutput == null)
                    return ToolHelpers.ErrorResponse($"Source output not found: {sourceParam}");

                var targetInput = GetInputParameter(tgtObj, targetParam);
                if (targetInput == null)
                    return ToolHelpers.ErrorResponse($"Target input not found: {targetParam}");

                targetInput.RemoveSource(sourceOutput);
                if (tgtObj is IGH_ActiveObject activeDisc)
                    activeDisc.ExpireSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    disconnected = true,
                    source = new { id = sourceId, param = sourceOutput.Name },
                    target = new { id = targetId, param = targetInput.Name }
                });
            });
        }

        private string ActionList(string componentId)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                Guid? filterGuid = null;
                if (!string.IsNullOrEmpty(componentId))
                {
                    if (!Guid.TryParse(componentId, out Guid parsed))
                        return ToolHelpers.ErrorResponse("Invalid componentId format");
                    filterGuid = parsed;
                }

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var connections = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds)) continue;

                    IEnumerable<IGH_Param> inputs = obj is IGH_Component comp
                        ? comp.Params.Input
                        : obj is IGH_Param param ? new[] { param } : null;

                    if (inputs == null) continue;

                    foreach (var input in inputs)
                    {
                        foreach (var source in input.Sources)
                        {
                            var sourceObj = source.Attributes.GetTopLevel.DocObject;
                            if (ToolHelpers.IsCordycepsInfrastructure(sourceObj, infraIds)) continue;

                            if (filterGuid.HasValue &&
                                sourceObj.InstanceGuid != filterGuid.Value &&
                                obj.InstanceGuid != filterGuid.Value)
                                continue;

                            connections.Add(new
                            {
                                source = new { componentId = sourceObj.InstanceGuid.ToString(), name = sourceObj.Name, param = source.Name },
                                target = new { componentId = obj.InstanceGuid.ToString(), name = obj.Name, param = input.Name }
                            });
                        }
                    }
                }

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["count"] = connections.Count,
                    ["connections"] = connections
                };
                if (filterGuid.HasValue) result["filteredBy"] = componentId;

                return JsonConvert.SerializeObject(result);
            });
        }

        private string ActionClear(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                IEnumerable<IGH_Param> inputs = component is IGH_Component comp
                    ? comp.Params.Input
                    : component is IGH_Param param ? new[] { param } : null;

                if (inputs == null)
                    return ToolHelpers.ErrorResponse("Component has no inputs");

                int clearedCount = 0;
                var clearedInputs = new List<string>();

                foreach (var input in inputs)
                {
                    int sourceCount = input.SourceCount;
                    if (sourceCount > 0)
                    {
                        input.RemoveAllSources();
                        clearedCount += sourceCount;
                        clearedInputs.Add($"{input.Name} ({sourceCount})");
                    }
                }

                if (clearedCount > 0 && component is IGH_ActiveObject activeClear)
                    activeClear.ExpireSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    componentId = id,
                    componentName = component.Name,
                    clearedCount,
                    clearedInputs
                });
            });
        }

        private string ActionValidate(string sourceId, string sourceParam, string targetId, string targetParam)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!ToolHelpers.TryGetUnprotectedComponent(_context, sourceId, out var srcObj, out error))
                    return ToolHelpers.ErrorResponse($"Source: {error}");

                // If no target specified, list possible outputs from source
                if (string.IsNullOrEmpty(targetId))
                {
                    var outputs = new List<object>();
                    if (srcObj is IGH_Component srcComp)
                    {
                        foreach (var output in srcComp.Params.Output)
                        {
                            outputs.Add(new { name = output.Name, nickname = output.NickName, type = output.TypeName });
                        }
                    }
                    else if (srcObj is IGH_Param srcParam)
                    {
                        outputs.Add(new { name = srcParam.Name, nickname = srcParam.NickName, type = srcParam.TypeName });
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        sourceId,
                        sourceName = srcObj.NickName ?? srcObj.Name,
                        outputs,
                        hint = "Specify targetId to validate a specific connection"
                    });
                }

                if (!ToolHelpers.TryGetUnprotectedComponent(_context, targetId, out var tgtObj, out error))
                    return ToolHelpers.ErrorResponse($"Target: {error}");

                var sourceOutput = GetOutputParameter(srcObj, sourceParam ?? "0");
                var targetInput = GetInputParameter(tgtObj, targetParam ?? "0");

                if (sourceOutput == null)
                    return ToolHelpers.ErrorResponse($"Source output not found: {sourceParam ?? "0"}");
                if (targetInput == null)
                    return ToolHelpers.ErrorResponse($"Target input not found: {targetParam ?? "0"}");

                // Basic type compatibility check
                var sourceType = sourceOutput.TypeName?.ToLowerInvariant() ?? "";
                var targetType = targetInput.TypeName?.ToLowerInvariant() ?? "";
                var compatibility = AnalyzeTypeCompatibility(sourceType, targetType);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    valid = compatibility.Level != "incompatible",
                    source = new { id = sourceId, name = srcObj.NickName ?? srcObj.Name, param = sourceOutput.Name, type = sourceOutput.TypeName },
                    target = new { id = targetId, name = tgtObj.NickName ?? tgtObj.Name, param = targetInput.Name, type = targetInput.TypeName },
                    compatibility = new { level = compatibility.Level, message = compatibility.Message }
                });
            });
        }

        private (string Level, string Message) AnalyzeTypeCompatibility(string sourceType, string targetType)
        {
            if (string.IsNullOrEmpty(sourceType) || string.IsNullOrEmpty(targetType))
                return ("unknown", "Unable to determine types");

            if (sourceType == targetType)
                return ("exact", "Types match exactly");

            if (targetType.Contains("generic") || targetType == "goo" || targetType == "object")
                return ("compatible", "Target accepts any type");

            if ((sourceType == "integer" || sourceType == "int") && (targetType == "number" || targetType == "double"))
                return ("compatible", "Integer converts to Number");

            if (targetType == "geometry" || targetType == "geometrybase")
            {
                var geoTypes = new[] { "point", "curve", "surface", "brep", "mesh", "line", "circle" };
                if (geoTypes.Any(g => sourceType.Contains(g)))
                    return ("compatible", "Geometry type is compatible");
            }

            if (targetType == "curve")
            {
                var curveTypes = new[] { "line", "circle", "arc", "polyline" };
                if (curveTypes.Any(c => sourceType.Contains(c)))
                    return ("compatible", "Curve subtype is compatible");
            }

            return ("unknown", "Compatibility unknown, Grasshopper will attempt conversion");
        }

        private IGH_Param GetOutputParameter(IGH_DocumentObject obj, string paramSpec)
            => GetParameter(obj, paramSpec, isInput: false);

        private IGH_Param GetInputParameter(IGH_DocumentObject obj, string paramSpec)
            => GetParameter(obj, paramSpec, isInput: true);

        private IGH_Param GetParameter(IGH_DocumentObject obj, string paramSpec, bool isInput)
        {
            if (obj is IGH_Param param) return param;
            if (!(obj is IGH_Component comp)) return null;

            var list = isInput ? comp.Params.Input : comp.Params.Output;
            if (list.Count == 0) return null;

            if (int.TryParse(paramSpec, out int index) && index >= 0 && index < list.Count)
                return list[index];

            return list.FirstOrDefault(p =>
                    p.Name.Equals(paramSpec, StringComparison.OrdinalIgnoreCase) ||
                    p.NickName.Equals(paramSpec, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(p =>
                    p.Name.IndexOf(paramSpec, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
