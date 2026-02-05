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

            if (side == GH_ParameterSide.Input)
                ghComponent.Params.RegisterInputParam(newParam);
            else
                ghComponent.Params.RegisterOutputParam(newParam);

            varParamComp.VariableParameterMaintenance();
            ghComponent.ExpireSolution(false);

            return JsonConvert.SerializeObject(new
            {
                success = true,
                id = ghComponent.InstanceGuid.ToString(),
                operation = "add",
                side = side.ToString().ToLower(),
                index = insertIndex,
                parameterName = newParam.Name,
                newCount = paramList.Count
            });
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

            varParamComp.VariableParameterMaintenance();
            ghComponent.ExpireSolution(false);

            return JsonConvert.SerializeObject(new
            {
                success = true,
                id = ghComponent.InstanceGuid.ToString(),
                operation = "remove",
                side = side.ToString().ToLower(),
                index = removeIndex,
                removedParameterName = paramName,
                newCount = paramList.Count
            });
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
                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = ghComponent.InstanceGuid.ToString(),
                    operation = "set_count",
                    side = side.ToString().ToLower(),
                    count = currentCount,
                    message = "Already at target count"
                });
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

                varParamComp.VariableParameterMaintenance();
                ghComponent.ExpireSolution(false);

                bool success = paramList.Count == targetCount;
                return JsonConvert.SerializeObject(new
                {
                    success,
                    id = ghComponent.InstanceGuid.ToString(),
                    operation = "set_count",
                    side = side.ToString().ToLower(),
                    targetCount,
                    actualCount = paramList.Count,
                    added,
                    removed,
                    errors = errors.Count > 0 ? errors : null
                });
            }
            catch (Exception ex)
            {
                return ToolHelpers.ErrorResponse($"Failed during set_count: {ex.Message}");
            }
        }

        #endregion
    }
}
