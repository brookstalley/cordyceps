using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
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
                    Tips = new[] { "Name a port by its exact name or nickname, or by 0-based index. Case is ignored only when that leaves one match; partial names are never matched, and the error suggests the port you probably meant and lists every port on that side", "On a component, an index that does not exist is refused with the valid range — it never falls back to a name match; a free-floating param object is itself the target and ignores the spec", "For bulk: connections='[{sourceId,sourceParam,targetId,targetParam},...]'" }
                },
                ["disconnect"] = new ActionInfo
                {
                    Name = "disconnect",
                    Description = "Remove a wire between components",
                    Required = new[] { "sourceId", "sourceParam", "targetId", "targetParam" },
                    Example = "action='disconnect', sourceId='a', sourceParam='0', targetId='b', targetParam='R'",
                    Tips = new[] { "Removes a wire, so sourceId and targetId never resolve by a default nickname (one every new object of the type gets, like 'Circle'), even a unique one: pass the GUID or prefix the error prints, or a nickname you set with gh_canvas(action='rename')", "If the wire doesn't exist, the error lists the target input's current sources (currentSources) so you can correct the call", "On a component, an index that does not exist is refused with the valid range — it never falls back to a name match; a free-floating param object is itself the target and ignores the spec" }
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
                    Example = "action='clear', id='abc-123'",
                    Tips = new[] { "Removes wires, so id never resolves by a default nickname (one every new object of the type gets, like 'Circle'), even a unique one: pass the GUID or prefix the error prints, or a nickname you set with gh_canvas(action='rename')" }
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
                "On a component, parameters are specified by exact name or nickname, or by 0-based index; an index outside the port list is refused with the valid range rather than matched as a name. Case is ignored only when that leaves one match; a partial or ambiguous name is refused with the candidates, never guessed. sourceParam is always an output and targetParam always an input. A free-floating param object is itself the target",
                "Disable solver before bulk wiring: gh_document(action='solver', enabled=false)",
                "Ids: an object is named by its GUID, an 8+ hex GUID prefix, or its unique nickname (exact, case-sensitive). A name two objects share is refused with both ids. disconnect and clear remove wires, so a default nickname never selects their objects, even when unique; bulk connect resolves every connection before adding any wire. list with an id that names nothing is an error, not an empty list"
            }
        };

        public GhWireTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Connection operations. Actions: connect|disconnect|list|clear|validate|help")]
        public string GhWire(
            [Description("Action to perform")] string action,
            [Description("Source object: GUID, 8+ hex GUID prefix, or unique nickname (for disconnect, not a default nickname)")] string sourceId = null,
            [Description("Source output port: exact name or nickname, or 0-based index")] string sourceParam = null,
            [Description("Target object: GUID, 8+ hex GUID prefix, or unique nickname (for disconnect, not a default nickname)")] string targetId = null,
            [Description("Target input port: exact name or nickname, or 0-based index")] string targetParam = null,
            [Description("JSON array of connections for bulk connect: [{sourceId,sourceParam,targetId,targetParam},...]; each id is an object: GUID, 8+ hex GUID prefix, or unique nickname")] string connections = null,
            [Description("Object: GUID, 8+ hex GUID prefix, or unique nickname, for list filter or clear (for clear, not a default nickname)")] string id = null,
            [Description("Alias for id (for list filter). Object: GUID, 8+ hex GUID prefix, or unique nickname")] string componentId = null)
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

                List<ConnectionEntry> connectionList;
                if (!string.IsNullOrEmpty(connections))
                {
                    // Names stay text (a nickname can look like a date); see Core/BulkEntries.
                    // The JSON literal null, or anything but an array, is a malformed call.
                    if (!BulkEntries.TryParseConnections(connections, out connectionList, out var parseError))
                        return ToolHelpers.ErrorResponse(parseError);
                }
                else if (!string.IsNullOrEmpty(sourceId) && !string.IsNullOrEmpty(targetId))
                {
                    connectionList = new List<ConnectionEntry> { new ConnectionEntry(sourceId, sourceParam ?? "0", targetId, targetParam ?? "0", null) };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Provide sourceId+targetId for single, or connections array for bulk");
                }

                // Pass 1: resolve every connection's source, target and ports against one lookup,
                // before the first wire is added. Pass 2 wires only the connections that passed.
                var lookup = new ObjectLookup(doc);
                var results = new object[connectionList.Count];
                var pending = new List<(int Index, IGH_DocumentObject Src, IGH_Param Output, IGH_DocumentObject Tgt, IGH_Param Input)>();
                int successCount = 0, failCount = 0;

                for (int i = 0; i < connectionList.Count; i++)
                {
                    var conn = connectionList[i];
                    string srcId = conn.SourceId;
                    string srcParam = conn.SourceParam;
                    string tgtId = conn.TargetId;
                    string tgtParam = conn.TargetParam;

                    if (conn.Error != null)
                    {
                        results[i] = new { success = false, error = conn.Error };
                        failCount++;
                        continue;
                    }

                    if (!ToolHelpers.TryGetUnprotectedComponent(lookup, srcId, out var srcObj, out var srcError))
                    {
                        results[i] = new { success = false, error = $"Source: {srcError}" };
                        failCount++;
                        continue;
                    }
                    if (!ToolHelpers.TryGetUnprotectedComponent(lookup, tgtId, out var tgtObj, out var tgtError))
                    {
                        results[i] = new { success = false, error = $"Target: {tgtError}" };
                        failCount++;
                        continue;
                    }

                    var sourceOutput = GetOutputParameter(srcObj, srcParam, "sourceParam", out var srcParamError);
                    if (sourceOutput == null)
                    {
                        var available = srcObj is IGH_Component c ? c.Params.Output.Select(p => p.Name).ToList() : new List<string>();
                        results[i] = new { success = false, error = $"Source: {srcParamError}", availableOutputs = available };
                        failCount++;
                        continue;
                    }

                    var targetInput = GetInputParameter(tgtObj, tgtParam, "targetParam", out var tgtParamError);
                    if (targetInput == null)
                    {
                        var available = tgtObj is IGH_Component c ? c.Params.Input.Select(p => p.Name).ToList() : new List<string>();
                        results[i] = new { success = false, error = $"Target: {tgtParamError}", availableInputs = available };
                        failCount++;
                        continue;
                    }

                    pending.Add((i, srcObj, sourceOutput, tgtObj, targetInput));
                }

                if (pending.Count == 0)
                    return ConnectReply(connectionList.Count, results, successCount, failCount);

                // Pass 2, one undo step: each target input's sources are recorded before its first
                // new wire, which also has every distinct target recomputed once at commit.
                return _context.Edit(doc, EditReplies.RecordName("gh_wire", "connect"), tx =>
                {
                    foreach (var (index, srcObj, sourceOutput, tgtObj, targetInput) in pending)
                    {
                        // AddSource ignores a wire that already exists and a param wired to
                        // itself; such a connection changes nothing, so it records nothing and an
                        // all-existing call pushes no undo step.
                        if (!ReferenceEquals(targetInput, sourceOutput) && !targetInput.Sources.Contains(sourceOutput))
                        {
                            tx.RecordWires(targetInput);
                            targetInput.AddSource(sourceOutput);
                        }
                        results[index] = new
                        {
                            success = true,
                            source = new { id = srcObj.InstanceGuid.ToString(), name = srcObj.NickName ?? srcObj.Name, param = sourceOutput.Name },
                            target = new { id = tgtObj.InstanceGuid.ToString(), name = tgtObj.NickName ?? tgtObj.Name, param = targetInput.Name }
                        };
                        successCount++;
                    }
                    return ConnectReply(connectionList.Count, results, successCount, failCount);
                });
            });
        }

        private static string ConnectReply(int total, object[] results, int successCount, int failCount)
        {
            if (total == 1)
                return JsonConvert.SerializeObject(results[0]);

            return JsonConvert.SerializeObject(new { success = failCount == 0, total, succeeded = successCount, failed = failCount, results });
        }

        private string ActionDisconnect(string sourceId, string sourceParam, string targetId, string targetParam)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var lookup = new ObjectLookup(doc);
                if (!ToolHelpers.TryGetUnprotectedComponent(lookup, sourceId, Core.Canvas.ResolveIntent.Destructive, out var srcObj, out error))
                    return ToolHelpers.ErrorResponse($"Source: {error}");

                if (!ToolHelpers.TryGetUnprotectedComponent(lookup, targetId, Core.Canvas.ResolveIntent.Destructive, out var tgtObj, out error))
                    return ToolHelpers.ErrorResponse($"Target: {error}");

                var sourceOutput = GetOutputParameter(srcObj, sourceParam, "sourceParam", out var sourceParamError);
                if (sourceOutput == null)
                    return ToolHelpers.ErrorResponse($"Source: {sourceParamError}");

                var targetInput = GetInputParameter(tgtObj, targetParam, "targetParam", out var targetParamError);
                if (targetInput == null)
                    return ToolHelpers.ErrorResponse($"Target: {targetParamError}");

                // Verify the wire actually exists before RemoveSource — otherwise report the
                // input's current sources so the caller can correct itself instead of getting
                // a silent no-op success.
                if (!targetInput.Sources.Contains(sourceOutput))
                {
                    var currentSources = targetInput.Sources.Select(s =>
                    {
                        var owner = s.Attributes?.GetTopLevel?.DocObject;
                        return new
                        {
                            componentId = owner?.InstanceGuid.ToString(),
                            componentName = owner == null ? null : (owner.NickName ?? owner.Name),
                            param = s.Name
                        };
                    }).ToList();

                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"No wire from '{sourceOutput.Name}' on {sourceId} to input '{targetInput.Name}' on {targetId}",
                        currentSources
                    });
                }

                return _context.Edit(doc, EditReplies.RecordName("gh_wire", "disconnect"), tx =>
                {
                    tx.RecordWires(targetInput);
                    targetInput.RemoveSource(sourceOutput);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        disconnected = true,
                        source = new { id = srcObj.InstanceGuid.ToString(), param = sourceOutput.Name },
                        target = new { id = tgtObj.InstanceGuid.ToString(), param = targetInput.Name }
                    });
                });
            });
        }

        private string ActionList(string componentId)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // The filter resolves like any object id: an id that names nothing is an error,
                // never an empty list that reads as "no wires".
                Guid? filterGuid = null;
                if (!string.IsNullOrEmpty(componentId))
                {
                    if (!ToolHelpers.TryGetUnprotectedComponent(new ObjectLookup(doc), componentId, out var filterObj, out var filterError))
                        return ToolHelpers.ErrorResponse(filterError);
                    filterGuid = filterObj.InstanceGuid;
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
                            // Null-safe like ToolHelpers.GetActiveObjects: phantom params (left
                            // over from undo/teardown) can have no attributes/owner — skip them.
                            var sourceObj = source.Attributes?.GetTopLevel?.DocObject;
                            if (sourceObj == null) continue;
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
                if (filterGuid.HasValue) result["filteredBy"] = filterGuid.Value.ToString();

                return JsonConvert.SerializeObject(result);
            });
        }

        private string ActionClear(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, Core.Canvas.ResolveIntent.Destructive, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                IEnumerable<IGH_Param> inputs = component is IGH_Component comp
                    ? comp.Params.Input
                    : component is IGH_Param param ? new[] { param } : null;

                if (inputs == null)
                    return ToolHelpers.ErrorResponse("Component has no inputs");

                // Only inputs that have sources are recorded and cleared, so a component with nothing
                // wired yields no undo record and no solve.
                return _context.Edit(doc, EditReplies.RecordName("gh_wire", "clear"), tx =>
                {
                    int clearedCount = 0;
                    var clearedInputs = new List<string>();

                    foreach (var input in inputs.ToList())
                    {
                        int sourceCount = input.SourceCount;
                        if (sourceCount > 0)
                        {
                            tx.RecordWires(input);
                            input.RemoveAllSources();
                            clearedCount += sourceCount;
                            clearedInputs.Add($"{input.Name} ({sourceCount})");
                        }
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        componentId = component.InstanceGuid.ToString(),
                        componentName = component.Name,
                        clearedCount,
                        clearedInputs
                    });
                });
            });
        }

        private string ActionValidate(string sourceId, string sourceParam, string targetId, string targetParam)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var lookup = new ObjectLookup(doc);
                if (!ToolHelpers.TryGetUnprotectedComponent(lookup, sourceId, out var srcObj, out error))
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
                        sourceId = srcObj.InstanceGuid.ToString(),
                        sourceName = srcObj.NickName ?? srcObj.Name,
                        outputs,
                        hint = "Specify targetId to validate a specific connection"
                    });
                }

                if (!ToolHelpers.TryGetUnprotectedComponent(lookup, targetId, out var tgtObj, out error))
                    return ToolHelpers.ErrorResponse($"Target: {error}");

                var sourceOutput = GetOutputParameter(srcObj, sourceParam ?? "0", "sourceParam", out var sourceParamError);
                var targetInput = GetInputParameter(tgtObj, targetParam ?? "0", "targetParam", out var targetParamError);

                if (sourceOutput == null)
                    return ToolHelpers.ErrorResponse($"Source: {sourceParamError}");
                if (targetInput == null)
                    return ToolHelpers.ErrorResponse($"Target: {targetParamError}");

                // Basic type compatibility check
                var sourceType = sourceOutput.TypeName?.ToLowerInvariant() ?? "";
                var targetType = targetInput.TypeName?.ToLowerInvariant() ?? "";
                var compatibility = AnalyzeTypeCompatibility(sourceType, targetType);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    valid = compatibility.Level != "incompatible",
                    source = new { id = srcObj.InstanceGuid.ToString(), name = srcObj.NickName ?? srcObj.Name, param = sourceOutput.Name, type = sourceOutput.TypeName },
                    target = new { id = tgtObj.InstanceGuid.ToString(), name = tgtObj.NickName ?? tgtObj.Name, param = targetInput.Name, type = targetInput.TypeName },
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

        private IGH_Param GetOutputParameter(IGH_DocumentObject obj, string paramSpec,
            string specArgName, out string error)
            => GetParameter(obj, paramSpec, isInput: false, specArgName, out error);

        private IGH_Param GetInputParameter(IGH_DocumentObject obj, string paramSpec,
            string specArgName, out string error)
            => GetParameter(obj, paramSpec, isInput: true, specArgName, out error);

        /// <summary>
        /// Resolve the port a wiring call names on one side of an object, reporting why when it
        /// cannot. The spec is read by <see cref="ParamSpecLookup"/>, shared with
        /// <c>gh_canvas(action='modifier')</c>: a numeric spec is an index and nothing else, so an
        /// index past the end of the list is refused with the valid range rather than falling
        /// through to a name search that can land on an unrelated digit-bearing port.
        /// </summary>
        private IGH_Param GetParameter(IGH_DocumentObject obj, string paramSpec, bool isInput,
            string specArgName, out string error)
        {
            error = null;

            if (obj is IGH_Param param) return param;

            if (!(obj is IGH_Component comp))
            {
                error = $"Object '{obj.NickName}' has no parameters to wire";
                return null;
            }

            var list = isInput ? comp.Params.Input : comp.Params.Output;
            var lookup = ParamSpecLookup.Resolve(
                paramSpec,
                list.Select(p => new ParamIdentity(p.Name, p.NickName)).ToList(),
                isInput,
                comp.NickName,
                specArgName);

            if (!lookup.IsResolved)
            {
                error = lookup.Error;
                return null;
            }

            return list[lookup.Index];
        }
    }
}
