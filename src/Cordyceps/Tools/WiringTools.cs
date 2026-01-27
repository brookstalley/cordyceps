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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (!Guid.TryParse(sourceId, out Guid srcGuid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid source component ID" });
                }

                var srcObj = doc.FindObject(srcGuid, true);
                if (srcObj == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Source component not found: {sourceId}" });
                }

                if (!Guid.TryParse(targetId, out Guid tgtGuid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid target component ID" });
                }

                var tgtObj = doc.FindObject(tgtGuid, true);
                if (tgtObj == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Target component not found: {targetId}" });
                }

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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (!Guid.TryParse(sourceId, out Guid srcGuid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid source component ID" });
                }

                var srcObj = doc.FindObject(srcGuid, true);
                if (srcObj == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Source component not found: {sourceId}" });
                }

                if (!Guid.TryParse(targetId, out Guid tgtGuid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid target component ID" });
                }

                var tgtObj = doc.FindObject(tgtGuid, true);
                if (tgtObj == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Target component not found: {targetId}" });
                }

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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (!Guid.TryParse(id, out Guid guid))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Invalid component ID" });
                }

                var component = doc.FindObject(guid, true);
                if (component == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Component not found: {id}" });
                }

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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                var connections = new List<object>();

                foreach (var obj in doc.Objects)
                {
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
                            connections.Add(new
                            {
                                source = new
                                {
                                    componentId = source.Attributes.GetTopLevel.DocObject.InstanceGuid.ToString(),
                                    componentName = source.Attributes.GetTopLevel.DocObject.Name,
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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

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

                foreach (var conn in connectionList)
                {
                    try
                    {
                        string sourceId = conn.sourceId?.ToString();
                        string sourceParam = conn.sourceParam?.ToString();
                        string targetId = conn.targetId?.ToString();
                        string targetParam = conn.targetParam?.ToString();

                        if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(targetId))
                        {
                            results.Add(new { success = false, error = "Missing sourceId or targetId" });
                            failCount++;
                            continue;
                        }

                        if (!Guid.TryParse(sourceId, out Guid srcGuid))
                        {
                            results.Add(new { success = false, error = $"Invalid source ID: {sourceId}" });
                            failCount++;
                            continue;
                        }

                        if (!Guid.TryParse(targetId, out Guid tgtGuid))
                        {
                            results.Add(new { success = false, error = $"Invalid target ID: {targetId}" });
                            failCount++;
                            continue;
                        }

                        var srcObj = doc.FindObject(srcGuid, true);
                        if (srcObj == null)
                        {
                            results.Add(new { success = false, error = $"Source not found: {sourceId}" });
                            failCount++;
                            continue;
                        }

                        var tgtObj = doc.FindObject(tgtGuid, true);
                        if (tgtObj == null)
                        {
                            results.Add(new { success = false, error = $"Target not found: {targetId}" });
                            failCount++;
                            continue;
                        }

                        IGH_Param sourceOutput = GetOutputParameter(srcObj, sourceParam ?? "0");
                        if (sourceOutput == null)
                        {
                            results.Add(new { success = false, error = $"Source output not found: {sourceParam}" });
                            failCount++;
                            continue;
                        }

                        IGH_Param targetInput = GetInputParameter(tgtObj, targetParam ?? "0");
                        if (targetInput == null)
                        {
                            results.Add(new { success = false, error = $"Target input not found: {targetParam}" });
                            failCount++;
                            continue;
                        }

                        targetInput.AddSource(sourceOutput);
                        results.Add(new
                        {
                            success = true,
                            source = new { id = sourceId, param = sourceOutput.Name },
                            target = new { id = targetId, param = targetInput.Name }
                        });
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { success = false, error = ex.Message });
                        failCount++;
                    }
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
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { valid = false, error = "No active Grasshopper document" });
                }

                if (string.IsNullOrEmpty(sourceId))
                {
                    return JsonConvert.SerializeObject(new { valid = false, error = "Missing sourceId" });
                }

                if (string.IsNullOrEmpty(targetId))
                {
                    return JsonConvert.SerializeObject(new { valid = false, error = "Missing targetId" });
                }

                if (!Guid.TryParse(sourceId, out Guid srcGuid))
                {
                    return JsonConvert.SerializeObject(new { valid = false, error = $"Invalid source ID: {sourceId}" });
                }

                var srcObj = doc.FindObject(srcGuid, true);
                if (srcObj == null)
                {
                    return JsonConvert.SerializeObject(new { valid = false, error = $"Source not found: {sourceId}" });
                }

                if (!Guid.TryParse(targetId, out Guid tgtGuid))
                {
                    return JsonConvert.SerializeObject(new { valid = false, error = $"Invalid target ID: {targetId}" });
                }

                var tgtObj = doc.FindObject(tgtGuid, true);
                if (tgtObj == null)
                {
                    return JsonConvert.SerializeObject(new { valid = false, error = $"Target not found: {targetId}" });
                }

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
                    return JsonConvert.SerializeObject(new { valid = false, error = "Could not find source output parameter" });
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
                    return JsonConvert.SerializeObject(new { valid = false, error = "Could not find target input parameter" });
                }

                // Check basic compatibility - Grasshopper is usually flexible about type conversion
                bool isCompatible = true;
                string warning = null;

                // Check if types are obviously incompatible
                var sourceType = sourceOutput.Type;
                var targetType = targetInput.Type;

                if (sourceType != targetType && sourceType != typeof(object) && targetType != typeof(object))
                {
                    // Types differ but Grasshopper often handles conversions, just warn
                    warning = $"Parameter types differ: {sourceOutput.TypeName} → {targetInput.TypeName}. Grasshopper may attempt conversion.";
                }

                return JsonConvert.SerializeObject(new
                {
                    valid = isCompatible,
                    sourceComponent = srcObj.NickName ?? srcObj.Name,
                    sourceParam = sourceOutput.Name,
                    sourceType = sourceOutput.TypeName,
                    targetComponent = tgtObj.NickName ?? tgtObj.Name,
                    targetParam = targetInput.Name,
                    targetType = targetInput.TypeName,
                    warning = warning
                });
            });
        }
    }
}
