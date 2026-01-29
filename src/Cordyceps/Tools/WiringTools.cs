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
    /// Wiring (connection) operations
    /// </summary>
    [McpServerToolType]
    public class WiringTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public WiringTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Connect component outputs to inputs. Use single params or connections array. Example: gh_connect(sourceId='a', sourceParam='0', targetId='b', targetParam='R') or gh_connect(connections='[{...}]')")]
        public string GhConnect(
            [Description("Source component GUID (for single connection)")] string sourceId = null,
            [Description("Source output parameter name or index (for single connection)")] string sourceParam = null,
            [Description("Target component GUID (for single connection)")] string targetId = null,
            [Description("Target input parameter name or index (for single connection)")] string targetParam = null,
            [Description("JSON array of connections: [{sourceId, sourceParam, targetId, targetParam}, ...]")] string connections = null)
        {
            _server?.RecordCommand("gh_connect");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure IDs to filter
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                // Build connection list from either parameter style
                List<dynamic> connectionList;
                if (!string.IsNullOrEmpty(connections))
                {
                    try
                    {
                        connectionList = JsonConvert.DeserializeObject<List<dynamic>>(connections);
                    }
                    catch (Exception ex)
                    {
                        return ToolHelpers.ErrorResponse($"Failed to parse connections JSON: {ex.Message}");
                    }
                }
                else if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(targetId))
                {
                    connectionList = new List<dynamic> { new { sourceId, sourceParam = sourceParam ?? "0", targetId, targetParam = targetParam ?? "0" } };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Provide sourceId+targetId for single connection, or connections array for bulk");
                }

                if (connectionList == null || connectionList.Count == 0)
                    return ToolHelpers.ErrorResponse("No connections provided");

                var results = new List<object>();
                int successCount = 0;
                int failCount = 0;

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

                    if (!Guid.TryParse(srcId, out Guid srcGuid) || infraIds.Contains(srcGuid))
                    {
                        results.Add(new { success = false, error = "Source component not found" });
                        failCount++;
                        continue;
                    }

                    if (!Guid.TryParse(tgtId, out Guid tgtGuid) || infraIds.Contains(tgtGuid))
                    {
                        results.Add(new { success = false, error = "Target component not found" });
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

                    IGH_Param sourceOutput = GetOutputParameter(srcObj, srcParam);
                    IGH_Param targetInput = GetInputParameter(tgtObj, tgtParam);

                    if (sourceOutput == null)
                    {
                        var availableOutputs = srcObj is IGH_Component c ? c.Params.Output.Select(p => p.Name).ToList() : new List<string>();
                        results.Add(new { success = false, error = $"Source output '{srcParam}' not found", availableOutputs });
                        failCount++;
                        continue;
                    }

                    if (targetInput == null)
                    {
                        var availableInputs = tgtObj is IGH_Component c ? c.Params.Input.Select(p => p.Name).ToList() : new List<string>();
                        results.Add(new { success = false, error = $"Target input '{tgtParam}' not found", availableInputs });
                        failCount++;
                        continue;
                    }

                    targetInput.AddSource(sourceOutput);
                    results.Add(new
                    {
                        success = true,
                        source = new { id = srcId, name = srcObj.NickName ?? srcObj.Name, param = sourceOutput.Name },
                        target = new { id = tgtId, name = tgtObj.NickName ?? tgtObj.Name, param = targetInput.Name }
                    });
                    successCount++;
                }

                doc.NewSolution(true);

                // Simplified response for single connection
                if (connectionList.Count == 1)
                {
                    var result = results[0];
                    return JsonConvert.SerializeObject(result);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = failCount == 0,
                    total = connectionList.Count,
                    succeeded = successCount,
                    failed = failCount,
                    results
                });
            });
        }

        [McpServerTool, Description("Disconnect a wire between two components. Example: gh_disconnect(sourceId='a', sourceParam='0', targetId='b', targetParam='R')")]
        public string GhDisconnect(
            [Description("Source component GUID")] string sourceId,
            [Description("Source output parameter name or index")] string sourceParam,
            [Description("Target component GUID")] string targetId,
            [Description("Target input parameter name or index")] string targetParam)
        {
            _server?.RecordCommand("disconnect_components");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected methods - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, sourceId, out var doc, out var srcObj, out var error))
                    return ToolHelpers.ErrorResponse($"Source: {error}");

                if (!ToolHelpers.TryGetUnprotectedComponent(_context, targetId, out var tgtObj, out error))
                    return ToolHelpers.ErrorResponse($"Target: {error}");

                IGH_Param sourceOutput = GetOutputParameter(srcObj, sourceParam);
                if (sourceOutput == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Source output parameter not found: {sourceParam}" });
                }

                IGH_Param targetInput = GetInputParameter(tgtObj, targetParam);
                if (targetInput == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Target input parameter not found: {targetParam}" });
                }

                targetInput.RemoveSource(sourceOutput);
                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    disconnected = true,
                    source = new { id = sourceId, param = sourceOutput.Name },
                    target = new { id = targetId, param = targetInput.Name }
                });
            });
        }

        [McpServerTool, Description("Clear all connections to a component's inputs (useful before re-wiring). Example: gh_clear_inputs(id='abc-123')")]
        public string GhClearInputs(
            [Description("Component GUID")] string id)
        {
            _server?.RecordCommand("clear_component_inputs");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                IEnumerable<IGH_Param> inputs = null;
                if (component is IGH_Component comp)
                {
                    inputs = comp.Params.Input;
                }
                else if (component is IGH_Param param)
                {
                    inputs = new[] { param };
                }

                if (inputs == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Component has no inputs" });
                }

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

                if (clearedCount > 0)
                {
                    doc.NewSolution(true);
                }

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

        [McpServerTool, Description("Get all connections (wires) between components on the canvas. Example: gh_get_connections() or gh_get_connections(componentId='abc')")]
        public string GhGetConnections(
            [Description("Filter to connections involving this component ID (as source or target)")] string componentId = null)
        {
            _server?.RecordCommand("get_connections");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Validate componentId if provided
                Guid? filterGuid = null;
                if (!string.IsNullOrEmpty(componentId))
                {
                    if (!RequestValidator.ValidateGuid(componentId, "componentId", out Guid parsedGuid, out var guidError))
                        return ToolHelpers.ErrorResponse(guidError);
                    filterGuid = parsedGuid;
                }

                // Get infrastructure IDs to filter
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                var connections = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    // Skip infrastructure components
                    if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds))
                        continue;

                    IEnumerable<IGH_Param> inputs = null;

                    if (obj is IGH_Component comp)
                    {
                        inputs = comp.Params.Input;
                    }
                    else if (obj is IGH_Param param)
                    {
                        inputs = new[] { param };
                    }

                    if (inputs == null) continue;

                    foreach (var input in inputs)
                    {
                        foreach (var source in input.Sources)
                        {
                            var sourceObj = source.Attributes.GetTopLevel.DocObject;

                            // Skip connections from infrastructure components
                            if (ToolHelpers.IsCordycepsInfrastructure(sourceObj, infraIds))
                                continue;

                            // Apply component filter if specified
                            if (filterGuid.HasValue)
                            {
                                var sourceGuid = sourceObj.InstanceGuid;
                                var targetGuid = obj.InstanceGuid;
                                if (sourceGuid != filterGuid.Value && targetGuid != filterGuid.Value)
                                    continue;
                            }

                            connections.Add(new
                            {
                                source = new
                                {
                                    componentId = sourceObj.InstanceGuid.ToString(),
                                    componentName = sourceObj.Name,
                                    param = source.Name,
                                    paramIndex = GetParamIndex(source, false)
                                },
                                target = new
                                {
                                    componentId = obj.InstanceGuid.ToString(),
                                    componentName = obj.Name,
                                    param = input.Name,
                                    paramIndex = GetParamIndex(input, true)
                                }
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
                if (filterGuid.HasValue)
                    result["filteredBy"] = componentId;

                return JsonConvert.SerializeObject(result);
            });
        }

        private IGH_Param GetOutputParameter(IGH_DocumentObject obj, string paramSpec)
            => GetParameter(obj, paramSpec, isInput: false);

        private IGH_Param GetInputParameter(IGH_DocumentObject obj, string paramSpec)
            => GetParameter(obj, paramSpec, isInput: true);

        private IGH_Param GetParameter(IGH_DocumentObject obj, string paramSpec, bool isInput)
        {
            if (obj is IGH_Param param)
                return param;

            if (!(obj is IGH_Component comp))
                return null;

            var list = isInput ? comp.Params.Input : comp.Params.Output;
            if (list.Count == 0) return null;

            // Try index
            if (int.TryParse(paramSpec, out int index) && index >= 0 && index < list.Count)
                return list[index];

            // Try exact name match, then partial match
            return list.FirstOrDefault(p =>
                    p.Name.Equals(paramSpec, StringComparison.OrdinalIgnoreCase) ||
                    p.NickName.Equals(paramSpec, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(p =>
                    p.Name.IndexOf(paramSpec, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    p.NickName.IndexOf(paramSpec, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private int GetParamIndex(IGH_Param param, bool isInput)
        {
            if (!(param.Attributes.GetTopLevel.DocObject is IGH_Component comp))
                return 0;

            var list = isInput ? comp.Params.Input : comp.Params.Output;
            var index = list.ToList().FindIndex(p => p.InstanceGuid == param.InstanceGuid);
            return index >= 0 ? index : 0;
        }

        #region Connection Validation Helpers

        private (string Level, string Message) AnalyzeTypeCompatibility(string sourceType, string targetType)
        {
            if (string.IsNullOrEmpty(sourceType) || string.IsNullOrEmpty(targetType))
                return ("unknown", "Unable to determine types");

            // Exact match
            if (sourceType == targetType)
                return ("exact", "Types match exactly");

            // Generic types accept anything
            if (targetType.Contains("generic") || targetType == "goo" || targetType == "object")
                return ("compatible", "Target accepts any type");

            // Number conversions
            if ((sourceType == "integer" || sourceType == "int") && (targetType == "number" || targetType == "double"))
                return ("compatible", "Integer converts to Number");
            if ((sourceType == "number" || sourceType == "double") && (targetType == "integer" || targetType == "int"))
                return ("convertible", "Number to Integer may lose precision");

            // Geometry hierarchy
            if (targetType == "geometry" || targetType == "geometrybase")
            {
                var geometryTypes = new[] { "point", "curve", "surface", "brep", "mesh", "line", "circle", "arc" };
                if (geometryTypes.Any(g => sourceType.Contains(g)))
                    return ("compatible", "Geometry type is compatible");
            }

            // Curve hierarchy
            if (targetType == "curve")
            {
                var curveTypes = new[] { "line", "circle", "arc", "polyline", "nurbs" };
                if (curveTypes.Any(c => sourceType.Contains(c)))
                    return ("compatible", "Curve subtype is compatible");
            }

            // Surface/Brep
            if (targetType == "brep" && sourceType.Contains("surface"))
                return ("compatible", "Surface wraps as Brep");
            if (targetType == "surface" && sourceType.Contains("brep"))
                return ("convertible", "Brep to Surface may fail if multiple faces");

            // Point/Vector
            if ((sourceType.Contains("point") && targetType.Contains("vector")) ||
                (sourceType.Contains("vector") && targetType.Contains("point")))
                return ("convertible", "Point/Vector conversion uses coordinates");

            // Check for obvious mismatches
            var geometricTypes = new[] { "point", "curve", "surface", "brep", "mesh", "line", "plane", "vector", "circle" };
            var dataTypes = new[] { "text", "string", "number", "integer", "boolean", "bool" };

            bool sourceIsGeometric = geometricTypes.Any(g => sourceType.Contains(g));
            bool targetIsGeometric = geometricTypes.Any(g => targetType.Contains(g));
            bool sourceIsData = dataTypes.Any(d => sourceType.Contains(d));
            bool targetIsData = dataTypes.Any(d => targetType.Contains(d));

            if ((sourceIsGeometric && targetIsData) || (sourceIsData && targetIsGeometric))
                return ("incompatible", "Geometric and data types are not compatible");

            // Default: unknown compatibility, let Grasshopper try
            return ("unknown", "Compatibility unknown, Grasshopper will attempt conversion");
        }

        private (string Level, string Warning, string Suggestion) AnalyzeAccessCompatibility(IGH_Param source, IGH_Param target)
        {
            var sourceAccess = source.Access;
            var targetAccess = target.Access;

            // Same access mode is ideal
            if (sourceAccess == targetAccess)
                return ("exact", null, null);

            // Item to List/Tree - generally works but may not be what user expects
            if (sourceAccess == GH_ParamAccess.item && targetAccess == GH_ParamAccess.list)
                return ("compatible", "Source outputs items, target expects lists. Items will be wrapped as single-item lists.", null);

            if (sourceAccess == GH_ParamAccess.item && targetAccess == GH_ParamAccess.tree)
                return ("compatible", "Source outputs items, target expects tree. Items will be wrapped in simple tree structure.", null);

            // List to Item - each item processed separately
            if (sourceAccess == GH_ParamAccess.list && targetAccess == GH_ParamAccess.item)
                return ("compatible", "Source outputs lists, target expects items. Each list item will be processed separately.", null);

            // Tree to Item/List - flattening may occur
            if (sourceAccess == GH_ParamAccess.tree && targetAccess == GH_ParamAccess.item)
                return ("warning", "Source outputs tree, target expects items. Tree will be processed item-by-item, preserving branch structure.", "Consider if this data matching behavior is intended");

            if (sourceAccess == GH_ParamAccess.tree && targetAccess == GH_ParamAccess.list)
                return ("warning", "Source outputs tree, target expects lists. Each branch becomes a list.", "Check that branch structure matches expectations");

            return ("compatible", null, null);
        }

        #endregion

        [McpServerTool, Description("Validate if a connection between two components is possible before creating it. Example: gh_validate_connection(sourceId='a', targetId='b')")]
        public string GhValidateConnection(
            [Description("Source component GUID")] string sourceId,
            [Description("Source output parameter name or index (optional)")] string sourceParam = null,
            [Description("Target component GUID")] string targetId = null,
            [Description("Target input parameter name or index (optional)")] string targetParam = null)
        {
            _server?.RecordCommand("validate_connection");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (string.IsNullOrEmpty(sourceId))
                    return ToolHelpers.ErrorResponse("Missing sourceId");

                if (string.IsNullOrEmpty(targetId))
                    return ToolHelpers.ErrorResponse("Missing targetId");

                // Get infrastructure IDs to protect
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                if (!ToolHelpers.TryParseGuid(sourceId, out var srcGuid, out error))
                    return ToolHelpers.ErrorResponse($"Source: {error}");

                // Check if protected - report as "not found"
                if (infraIds.Contains(srcGuid))
                    return ToolHelpers.ErrorResponse($"Source: Component not found: {srcGuid}");

                if (!ToolHelpers.TryFindComponent(doc, srcGuid, out var srcObj, out error))
                    return ToolHelpers.ErrorResponse($"Source: {error}");

                if (!ToolHelpers.TryParseGuid(targetId, out var tgtGuid, out error))
                    return ToolHelpers.ErrorResponse($"Target: {error}");

                // Check if protected - report as "not found"
                if (infraIds.Contains(tgtGuid))
                    return ToolHelpers.ErrorResponse($"Target: Component not found: {tgtGuid}");

                if (!ToolHelpers.TryFindComponent(doc, tgtGuid, out var tgtObj, out error))
                    return ToolHelpers.ErrorResponse($"Target: {error}");

                // Get source output parameter
                IGH_Param sourceOutput = null;
                if (srcObj is IGH_Param srcParam)
                {
                    sourceOutput = srcParam;
                }
                else if (srcObj is IGH_Component srcComp)
                {
                    if (!string.IsNullOrEmpty(sourceParam))
                    {
                        sourceOutput = GetOutputParameter(srcObj, sourceParam);
                    }
                    if (sourceOutput == null && srcComp.Params.Output.Count > 0)
                    {
                        sourceOutput = srcComp.Params.Output[0];
                    }
                }

                if (sourceOutput == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Could not find source output parameter" });
                }

                // Get target input parameter
                IGH_Param targetInput = null;
                if (tgtObj is IGH_Param tgtParam)
                {
                    targetInput = tgtParam;
                }
                else if (tgtObj is IGH_Component tgtComp)
                {
                    if (!string.IsNullOrEmpty(targetParam))
                    {
                        targetInput = GetInputParameter(tgtObj, targetParam);
                    }
                    if (targetInput == null && tgtComp.Params.Input.Count > 0)
                    {
                        targetInput = tgtComp.Params.Input[0];
                    }
                }

                if (targetInput == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Could not find target input parameter" });
                }

                // Analyze type compatibility
                var sourceTypeName = sourceOutput.TypeName?.ToLowerInvariant() ?? "";
                var targetTypeName = targetInput.TypeName?.ToLowerInvariant() ?? "";

                var typeCompatibility = AnalyzeTypeCompatibility(sourceTypeName, targetTypeName);
                var accessCompatibility = AnalyzeAccessCompatibility(sourceOutput, targetInput);

                var warnings = new List<string>();
                var suggestions = new List<string>();

                // Type warnings
                if (typeCompatibility.Level == "incompatible")
                {
                    warnings.Add($"Types may be incompatible: {sourceOutput.TypeName} → {targetInput.TypeName}");
                    suggestions.Add("Consider adding a conversion component between them");
                }
                else if (typeCompatibility.Level == "convertible")
                {
                    warnings.Add($"Types differ: {sourceOutput.TypeName} → {targetInput.TypeName}. Grasshopper will attempt conversion.");
                }

                // Access mode analysis
                if (accessCompatibility.Warning != null)
                {
                    warnings.Add(accessCompatibility.Warning);
                    if (accessCompatibility.Suggestion != null)
                    {
                        suggestions.Add(accessCompatibility.Suggestion);
                    }
                }

                // Check data tree structure if both have data
                string treeWarning = null;
                if (sourceOutput.VolatileDataCount > 0)
                {
                    var sourceBranches = sourceOutput.VolatileData.PathCount;
                    if (sourceBranches > 1 && targetInput.Access == GH_ParamAccess.item)
                    {
                        treeWarning = $"Source has {sourceBranches} branches but target expects single items. Data will be processed per-item across all branches.";
                    }
                }

                bool isValid = typeCompatibility.Level != "incompatible";

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    valid = isValid,
                    sourceComponent = srcObj.NickName ?? srcObj.Name,
                    sourceParam = sourceOutput.Name,
                    sourceType = sourceOutput.TypeName,
                    sourceAccess = sourceOutput.Access.ToString(),
                    sourceBranchCount = sourceOutput.VolatileData?.PathCount ?? 0,
                    targetComponent = tgtObj.NickName ?? tgtObj.Name,
                    targetParam = targetInput.Name,
                    targetType = targetInput.TypeName,
                    targetAccess = targetInput.Access.ToString(),
                    targetOptional = targetInput.Optional,
                    typeCompatibility = typeCompatibility.Level,
                    accessCompatibility = accessCompatibility.Level,
                    warnings = warnings.Count > 0 ? warnings : null,
                    suggestions = suggestions.Count > 0 ? suggestions : null,
                    treeWarning
                });
            });
        }
    }
}
