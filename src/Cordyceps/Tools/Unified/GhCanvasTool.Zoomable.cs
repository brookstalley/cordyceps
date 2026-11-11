using System;
using System.Collections.Generic;
using Cordyceps.Core;
using Grasshopper.Kernel;
using Newtonsoft.Json;

namespace Cordyceps.Tools.Unified
{
    public partial class GhCanvasTool
    {
        #region Zoomable Actions

        private string ActionZoomable(string id, string side, string operation, int count, int index)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(component is IGH_Component ghComponent))
                    return ToolHelpers.ErrorResponse("Component does not support parameters");

                if (!(component is IGH_VariableParameterComponent varParamComp))
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"Component '{ghComponent.NickName}' does not support variable parameters",
                        componentType = component.GetType().Name,
                        suggestion = "This action only works with components like Stream Filter, Merge, Entwine, Gate, etc."
                    });
                }

                // Default side to input
                var paramSide = GH_ParameterSide.Input;
                if (!string.IsNullOrEmpty(side))
                {
                    if (side.Equals("output", StringComparison.OrdinalIgnoreCase))
                        paramSide = GH_ParameterSide.Output;
                    else if (!side.Equals("input", StringComparison.OrdinalIgnoreCase))
                        return ToolHelpers.ErrorResponse($"Invalid side: {side}. Use 'input' or 'output'");
                }

                // Default operation to add
                var op = operation?.ToLowerInvariant() ?? "add";

                try
                {
                    int initialCount = paramSide == GH_ParameterSide.Input
                        ? ghComponent.Params.Input.Count
                        : ghComponent.Params.Output.Count;

                    switch (op)
                    {
                        case "add":
                            return AddParameter(ghComponent, varParamComp, paramSide, index, doc);

                        case "remove":
                            return RemoveParameter(ghComponent, varParamComp, paramSide, index, doc);

                        case "set_count":
                            if (count < 0)
                                return ToolHelpers.ErrorResponse("'count' parameter required for set_count operation");
                            return SetParameterCount(ghComponent, varParamComp, paramSide, count, doc);

                        default:
                            return ToolHelpers.ErrorResponse($"Invalid operation: {operation}. Use 'add', 'remove', or 'set_count'");
                    }
                }
                catch (Exception ex)
                {
                    Core.DebugLog.Error($"gh_canvas zoomable failed: {ex.Message}");
                    return ToolHelpers.ErrorResponse($"Failed to manage zoomable parameters: {ex.Message}");
                }
            });
        }

        private string AddParameter(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            GH_ParameterSide side, int index, GH_Document doc)
        {
            var paramList = side == GH_ParameterSide.Input ? ghComponent.Params.Input : ghComponent.Params.Output;

            // If index not specified, add at the end
            int insertIndex = index >= 0 ? index : paramList.Count;

            if (!varParamComp.CanInsertParameter(side, insertIndex))
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = $"Cannot insert parameter at index {insertIndex}",
                    currentCount = paramList.Count,
                    suggestion = "Component may have reached maximum parameter count or index is invalid"
                });
            }

            var newParam = varParamComp.CreateParameter(side, insertIndex);
            if (newParam == null)
            {
                return ToolHelpers.ErrorResponse("Failed to create parameter");
            }

            // Indexed overloads (like GhScriptTool's SyncParamSide) so an explicit index inserts
            // at that position — the non-indexed overloads append, making the reported index wrong.
            if (side == GH_ParameterSide.Input)
                ghComponent.Params.RegisterInputParam(newParam, insertIndex);
            else
                ghComponent.Params.RegisterOutputParam(newParam, insertIndex);

            var rebuildFields = FinishReshape(ghComponent, varParamComp, parametersChanged: true);

            return ZoomableReply(new Dictionary<string, object>
            {
                ["success"] = true,
                ["id"] = ghComponent.InstanceGuid.ToString(),
                ["operation"] = "add",
                ["side"] = side.ToString().ToLower(),
                ["index"] = insertIndex,
                ["parameterName"] = newParam.Name,
                ["newCount"] = paramList.Count
            }, rebuildFields);
        }

        private string RemoveParameter(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            GH_ParameterSide side, int index, GH_Document doc)
        {
            var paramList = side == GH_ParameterSide.Input ? ghComponent.Params.Input : ghComponent.Params.Output;

            // If index not specified, remove from the end
            int removeIndex = index >= 0 ? index : paramList.Count - 1;

            if (removeIndex < 0 || removeIndex >= paramList.Count)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = $"Invalid index {removeIndex}",
                    currentCount = paramList.Count,
                    suggestion = $"Index must be between 0 and {paramList.Count - 1}"
                });
            }

            if (!varParamComp.CanRemoveParameter(side, removeIndex))
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = $"Cannot remove parameter at index {removeIndex}",
                    currentCount = paramList.Count,
                    suggestion = "Component may require a minimum number of parameters"
                });
            }

            var param = paramList[removeIndex];
            string paramName = param.Name;

            if (side == GH_ParameterSide.Input)
                ghComponent.Params.UnregisterInputParameter(param);
            else
                ghComponent.Params.UnregisterOutputParameter(param);

            var rebuildFields = FinishReshape(ghComponent, varParamComp, parametersChanged: true);

            return ZoomableReply(new Dictionary<string, object>
            {
                ["success"] = true,
                ["id"] = ghComponent.InstanceGuid.ToString(),
                ["operation"] = "remove",
                ["side"] = side.ToString().ToLower(),
                ["index"] = removeIndex,
                ["removedParameterName"] = paramName,
                ["newCount"] = paramList.Count
            }, rebuildFields);
        }

        private string SetParameterCount(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            GH_ParameterSide side, int targetCount, GH_Document doc)
        {
            var paramList = side == GH_ParameterSide.Input ? ghComponent.Params.Input : ghComponent.Params.Output;
            int currentCount = paramList.Count;

            if (targetCount < 0)
            {
                return ToolHelpers.ErrorResponse("Target count must be non-negative");
            }

            if (targetCount == currentCount)
            {
                // Nothing moved, so nothing is rebuilt; a script component still gets the answer.
                var unchanged = ScriptComponents.IsScriptComponent(ghComponent)
                    ? ScriptComponents.RebuildForChange(ghComponent, parametersChanged: false, writtenSource: null)
                    : null;
                return ZoomableReply(new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["id"] = ghComponent.InstanceGuid.ToString(),
                    ["operation"] = "set_count",
                    ["side"] = side.ToString().ToLower(),
                    ["count"] = currentCount,
                    ["message"] = "Already at target count"
                }, unchanged);
            }

            int added = 0, removed = 0;
            var errors = new List<string>();

            try
            {
                // Remove excess parameters
                while (paramList.Count > targetCount)
                {
                    int removeIdx = paramList.Count - 1;
                    if (!varParamComp.CanRemoveParameter(side, removeIdx))
                    {
                        errors.Add($"Cannot remove parameter at index {removeIdx}");
                        break;
                    }

                    var param = paramList[removeIdx];
                    if (side == GH_ParameterSide.Input)
                        ghComponent.Params.UnregisterInputParameter(param);
                    else
                        ghComponent.Params.UnregisterOutputParameter(param);
                    removed++;
                }

                // Add missing parameters
                while (paramList.Count < targetCount)
                {
                    int insertIdx = paramList.Count;
                    if (!varParamComp.CanInsertParameter(side, insertIdx))
                    {
                        errors.Add($"Cannot add parameter at index {insertIdx}");
                        break;
                    }

                    var newParam = varParamComp.CreateParameter(side, insertIdx);
                    if (newParam == null)
                    {
                        errors.Add($"Failed to create parameter at index {insertIdx}");
                        break;
                    }

                    if (side == GH_ParameterSide.Input)
                        ghComponent.Params.RegisterInputParam(newParam);
                    else
                        ghComponent.Params.RegisterOutputParam(newParam);
                    added++;
                }

                // A set_count that stopped partway still moved the ports it got to, and those
                // need the rebuild as much as a complete one does.
                var rebuildFields = FinishReshape(ghComponent, varParamComp, parametersChanged: added + removed > 0);

                bool success = paramList.Count == targetCount;
                return ZoomableReply(new Dictionary<string, object>
                {
                    ["success"] = success,
                    ["id"] = ghComponent.InstanceGuid.ToString(),
                    ["operation"] = "set_count",
                    ["side"] = side.ToString().ToLower(),
                    ["targetCount"] = targetCount,
                    ["actualCount"] = paramList.Count,
                    ["added"] = added,
                    ["removed"] = removed,
                    ["errors"] = errors.Count > 0 ? errors : null
                }, rebuildFields);
            }
            catch (Exception ex)
            {
                return ToolHelpers.ErrorResponse($"Failed during set_count: {ex.Message}");
            }
        }

        /// <summary>
        /// Settle a component whose ports this call reshaped, and on a script component rebuild its
        /// program before the solution is expired. Returns the rebuild fields for a script
        /// component, <c>null</c> for any other.
        /// </summary>
        /// <remarks>
        /// A script component's program is compiled against its ports, so a reshape leaves it
        /// running a program built for ports it no longer has until it is rebuilt. This goes
        /// through the same gate as <c>gh_script(action='configure')</c>, so both tools answer the
        /// rebuild question in the same words. Other variable-parameter components (Merge, Entwine,
        /// Gate) have no program and get no rebuild fields.
        /// </remarks>
        private static Dictionary<string, object> FinishReshape(IGH_Component ghComponent,
            IGH_VariableParameterComponent varParamComp, bool parametersChanged)
        {
            varParamComp.VariableParameterMaintenance();

            Dictionary<string, object> rebuildFields = null;
            if (ScriptComponents.IsScriptComponent(ghComponent))
                rebuildFields = ScriptComponents.RebuildForChange(ghComponent, parametersChanged, writtenSource: null);

            ghComponent.ExpireSolution(false);
            return rebuildFields;
        }

        /// <summary>Serialize a zoomable reply, with the rebuild fields merged in when there are any.</summary>
        private static string ZoomableReply(Dictionary<string, object> reply, Dictionary<string, object> rebuildFields)
        {
            if (rebuildFields != null)
            {
                foreach (var field in rebuildFields)
                    reply[field.Key] = field.Value;
            }
            return JsonConvert.SerializeObject(reply);
        }

        #endregion
    }
}
