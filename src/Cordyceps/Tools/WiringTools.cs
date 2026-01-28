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

        [McpServerTool, Description("Connect an output of one component to an input of another component")]
        public string ConnectComponents(
            [Description("Source component GUID")] string sourceId,
            [Description("Source output parameter name or index")] string sourceParam,
            [Description("Target component GUID")] string targetId,
            [Description("Target input parameter name or index")] string targetParam)
        {
            _server?.RecordCommand("connect_components");
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
                    // List available outputs for debugging
                    var availableOutputs = new List<string>();
                    if (srcObj is IGH_Component srcComp)
                    {
                        availableOutputs = srcComp.Params.Output.Select(p => p.Name).ToList();
                    }
                    Core.DebugLog.Warn($"Source output '{sourceParam}' not found. Available: [{string.Join(", ", availableOutputs)}]");
                    return JsonConvert.SerializeObject(new { success = false, error = $"Source output parameter not found: {sourceParam}", availableOutputs });
                }

                IGH_Param targetInput = GetInputParameter(tgtObj, targetParam);
                if (targetInput == null)
                {
                    // List available inputs for debugging
                    var availableInputs = new List<string>();
                    if (tgtObj is IGH_Component tgtComp)
                    {
                        availableInputs = tgtComp.Params.Input.Select(p => p.Name).ToList();
                    }
                    Core.DebugLog.Warn($"Target input '{targetParam}' not found on {tgtObj.NickName}. Available: [{string.Join(", ", availableInputs)}]");
                    return JsonConvert.SerializeObject(new { success = false, error = $"Target input parameter not found: {targetParam}", availableInputs });
                }

                targetInput.AddSource(sourceOutput);
                doc.NewSolution(true);

                Core.DebugLog.Debug($"Connected {srcObj.NickName}.{sourceOutput.Name} -> {tgtObj.NickName}.{targetInput.Name}");

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    source = new { id = sourceId, param = sourceOutput.Name },
                    target = new { id = targetId, param = targetInput.Name }
                });
            });
        }

        [McpServerTool, Description("Disconnect a wire between two components")]
        public string DisconnectComponents(
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

        [McpServerTool, Description("Clear all connections to a component's inputs (useful before re-wiring)")]
        public string ClearComponentInputs(
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

        [McpServerTool, Description("Get all connections (wires) between components on the canvas")]
        public string GetConnections()
        {
            _server?.RecordCommand("get_connections");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

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

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = connections.Count,
                    connections = connections
                });
            });
        }

        private IGH_Param GetOutputParameter(IGH_DocumentObject obj, string paramSpec)
        {
            List<IGH_Param> outputs = null;

            if (obj is IGH_Component comp)
            {
                outputs = comp.Params.Output.ToList();
            }
            else if (obj is IGH_Param param)
            {
                return param;
            }

            if (outputs == null || outputs.Count == 0) return null;

            if (int.TryParse(paramSpec, out int index))
            {
                if (index >= 0 && index < outputs.Count)
                {
                    return outputs[index];
                }
            }

            var byName = outputs.FirstOrDefault(p =>
                p.Name.Equals(paramSpec, StringComparison.OrdinalIgnoreCase) ||
                p.NickName.Equals(paramSpec, StringComparison.OrdinalIgnoreCase));

            if (byName != null) return byName;

            byName = outputs.FirstOrDefault(p =>
                p.Name.IndexOf(paramSpec, StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.NickName.IndexOf(paramSpec, StringComparison.OrdinalIgnoreCase) >= 0);

            return byName;
        }

        private IGH_Param GetInputParameter(IGH_DocumentObject obj, string paramSpec)
        {
            List<IGH_Param> inputs = null;

            if (obj is IGH_Component comp)
            {
                inputs = comp.Params.Input.ToList();
            }
            else if (obj is IGH_Param param)
            {
                return param;
            }

            if (inputs == null || inputs.Count == 0) return null;

            if (int.TryParse(paramSpec, out int index))
            {
                if (index >= 0 && index < inputs.Count)
                {
                    return inputs[index];
                }
            }

            var byName = inputs.FirstOrDefault(p =>
                p.Name.Equals(paramSpec, StringComparison.OrdinalIgnoreCase) ||
                p.NickName.Equals(paramSpec, StringComparison.OrdinalIgnoreCase));

            if (byName != null) return byName;

            byName = inputs.FirstOrDefault(p =>
                p.Name.IndexOf(paramSpec, StringComparison.OrdinalIgnoreCase) >= 0 ||
                p.NickName.IndexOf(paramSpec, StringComparison.OrdinalIgnoreCase) >= 0);

            return byName;
        }

        private int GetParamIndex(IGH_Param param, bool isInput)
        {
            var parent = param.Attributes.GetTopLevel.DocObject;
            if (parent is IGH_Component comp)
            {
                var list = isInput ? comp.Params.Input : comp.Params.Output;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].InstanceGuid == param.InstanceGuid)
                        return i;
                }
            }
            return 0;
        }

        [McpServerTool, Description("Create multiple connections at once efficiently. Each connection needs sourceId, sourceParam, targetId, targetParam.")]
        public string BulkConnect(
            [Description("JSON array of connection objects: [{sourceId, sourceParam, targetId, targetParam}, ...]")] string connections)
        {
            _server?.RecordCommand("bulk_connect");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure IDs to filter
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                List<dynamic> connectionList;
                try
                {
                    connectionList = JsonConvert.DeserializeObject<List<dynamic>>(connections);
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Failed to parse connections JSON: {ex.Message}" });
                }

                if (connectionList == null || connectionList.Count == 0)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No connections provided" });
                }

                var results = new List<object>();
                int successCount = 0;
                int failCount = 0;

                int connectionIndex = 0;
                foreach (var conn in connectionList)
                {
                    try
                    {
                        string sourceId = conn.sourceId?.ToString();
                        string sourceParam = conn.sourceParam?.ToString();
                        string targetId = conn.targetId?.ToString();
                        string targetParam = conn.targetParam?.ToString();

                        // Build connection reference for error reporting
                        var connRef = new
                        {
                            index = connectionIndex,
                            sourceId,
                            sourceParam,
                            targetId,
                            targetParam
                        };

                        if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
                        {
                            results.Add(new { success = false, error = "Missing sourceId or targetId", connection = connRef });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        if (!Guid.TryParse(sourceId, out Guid srcGuid))
                        {
                            results.Add(new { success = false, error = $"Invalid source ID: {sourceId}", connection = connRef });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        if (!Guid.TryParse(targetId, out Guid tgtGuid))
                        {
                            results.Add(new { success = false, error = $"Invalid target ID: {targetId}", connection = connRef });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        // Check if protected - report as "not found" to maintain invisibility
                        if (infraIds.Contains(srcGuid))
                        {
                            results.Add(new { success = false, error = $"Source component not found", connection = connRef });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        if (infraIds.Contains(tgtGuid))
                        {
                            results.Add(new { success = false, error = $"Target component not found", connection = connRef });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        var srcObj = doc.FindObject(srcGuid, true);
                        if (srcObj == null)
                        {
                            results.Add(new { success = false, error = $"Source component not found", connection = connRef });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        var tgtObj = doc.FindObject(tgtGuid, true);
                        if (tgtObj == null)
                        {
                            results.Add(new { success = false, error = $"Target component not found", connection = connRef });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        // Get available params for better error messages
                        var availableOutputs = srcObj is IGH_Component srcComp
                            ? srcComp.Params.Output.Select(p => p.Name).ToList()
                            : new List<string> { "(parameter)" };
                        var availableInputs = tgtObj is IGH_Component tgtComp
                            ? tgtComp.Params.Input.Select(p => p.Name).ToList()
                            : new List<string> { "(parameter)" };

                        IGH_Param sourceOutput = GetOutputParameter(srcObj, sourceParam ?? "0");
                        if (sourceOutput == null)
                        {
                            results.Add(new
                            {
                                success = false,
                                error = $"Source output '{sourceParam}' not found on '{srcObj.NickName ?? srcObj.Name}'",
                                connection = connRef,
                                availableOutputs
                            });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        IGH_Param targetInput = GetInputParameter(tgtObj, targetParam ?? "0");
                        if (targetInput == null)
                        {
                            results.Add(new
                            {
                                success = false,
                                error = $"Target input '{targetParam}' not found on '{tgtObj.NickName ?? tgtObj.Name}'",
                                connection = connRef,
                                availableInputs
                            });
                            failCount++;
                            connectionIndex++;
                            continue;
                        }

                        targetInput.AddSource(sourceOutput);
                        results.Add(new
                        {
                            success = true,
                            index = connectionIndex,
                            source = new { id = sourceId, name = srcObj.NickName ?? srcObj.Name, param = sourceOutput.Name },
                            target = new { id = targetId, name = tgtObj.NickName ?? tgtObj.Name, param = targetInput.Name }
                        });
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new
                        {
                            success = false,
                            index = connectionIndex,
                            error = ex.Message,
                            connection = new
                            {
                                sourceId = conn.sourceId?.ToString(),
                                sourceParam = conn.sourceParam?.ToString(),
                                targetId = conn.targetId?.ToString(),
                                targetParam = conn.targetParam?.ToString()
                            }
                        });
                        failCount++;
                    }
                    connectionIndex++;
                }

                // Trigger one solution after all connections
                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = failCount == 0,
                    total = connectionList.Count,
                    succeeded = successCount,
                    failed = failCount,
                    results = results
                });
            });
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

        [McpServerTool, Description("Validate if a connection between two components is possible before creating it")]
        public string ValidateConnection(
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
