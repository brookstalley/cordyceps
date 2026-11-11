using System;
using System.Collections.Generic;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
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

                // Every check runs here, before anything changes: each one asks the component
                // (CanInsertParameter, CanRemoveParameter), and a third-party component may throw
                // from those. Nothing has been touched yet, so such a throw is answered as a plain
                // error; the reshape itself runs below, inside the edit, where a throw rolls back.
                Func<EditTransaction, string> apply;
                string reply;
                try
                {
                    switch (op)
                    {
                        case "add":
                            reply = PrepareAdd(ghComponent, varParamComp, paramSide, index, out apply);
                            break;

                        case "remove":
                            reply = PrepareRemove(ghComponent, varParamComp, paramSide, index, doc, id, out apply);
                            break;

                        case "set_count":
                            if (count < 0)
                                return ToolHelpers.ErrorResponse("'count' parameter required for set_count operation");
                            reply = PrepareSetCount(ghComponent, varParamComp, paramSide, count, doc, id, out apply);
                            break;

                        default:
                            return ToolHelpers.ErrorResponse($"Invalid operation: {operation}. Use 'add', 'remove', or 'set_count'");
                    }
                }
                catch (Exception ex) // prawduct:allow prawduct/broad-except -- validation only: a third-party component's Can*Parameter may throw anything, and nothing has changed yet
                {
                    Core.DebugLog.Error($"gh_canvas zoomable failed: {ex.Message}");
                    return ToolHelpers.ErrorResponse($"Failed to manage zoomable parameters: {ex.Message}");
                }

                if (apply == null)
                    return reply;

                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "zoomable"), apply);
            });
        }

        /// <summary>
        /// Check an <c>add</c>. Returns the refusal, or null with <paramref name="apply"/> set to
        /// the edit that adds the port.
        /// </summary>
        private static string PrepareAdd(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            GH_ParameterSide side, int index, out Func<EditTransaction, string> apply)
        {
            apply = null;
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

            apply = tx =>
            {
                // Recorded before CreateParameter, which belongs to the component and may set
                // the component up for the port it makes.
                tx.RecordGeneric(ghComponent);

                var newParam = varParamComp.CreateParameter(side, insertIndex);
                if (newParam == null)
                    throw new EditRollbackException("Failed to create parameter");

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
            };
            return null;
        }

        /// <summary>
        /// Check a <c>remove</c>, including the destructive id resolution a port removal needs.
        /// Returns the refusal, or null with <paramref name="apply"/> set to the edit that removes
        /// the port.
        /// </summary>
        private static string PrepareRemove(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            GH_ParameterSide side, int index, GH_Document doc, string id, out Func<EditTransaction, string> apply)
        {
            apply = null;
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

            // Removing a port cuts its wires, so the id must pass destructive resolution before
            // anything changes: a default nickname never selects the component that loses them.
            var removal = PortRemoval.ForRemove(PortNames(paramList), side == GH_ParameterSide.Input, removeIndex,
                i => varParamComp.CanRemoveParameter(side, i));
            if (!ToolHelpers.TryConfirmPortRemoval(doc, id, ghComponent, removal, out var refusal))
                return ToolHelpers.ErrorResponse(refusal);

            apply = tx =>
            {
                // The generic record takes the port's wires first, so undo rebuilds the port and
                // then reconnects it.
                tx.RecordGeneric(ghComponent);

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
            };
            return null;
        }

        /// <summary>
        /// Check a <c>set_count</c>. Returns the reply when there is nothing to do or the call is
        /// refused, or null with <paramref name="apply"/> set to the edit that reshapes the side.
        /// </summary>
        private static string PrepareSetCount(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            GH_ParameterSide side, int targetCount, GH_Document doc, string id, out Func<EditTransaction, string> apply)
        {
            apply = null;
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

            // Shrinking removes ports from the end and cuts their wires; growing removes nothing
            // and still accepts any id that resolves.
            var removal = PortRemoval.ForSetCount(PortNames(paramList), side == GH_ParameterSide.Input, targetCount,
                i => varParamComp.CanRemoveParameter(side, i));
            if (!ToolHelpers.TryConfirmPortRemoval(doc, id, ghComponent, removal, out var refusal))
                return ToolHelpers.ErrorResponse(refusal);

            apply = tx =>
            {
                tx.RecordGeneric(ghComponent);

                int added = 0, removed = 0;

                // Whether the component accepts each step is known only once the steps before it
                // have run, so a refusal can come partway. It rolls the whole call back: a
                // component left with some of the requested ports is the half-edit a call must
                // never leave.
                while (paramList.Count > targetCount)
                {
                    int removeIdx = paramList.Count - 1;
                    if (!varParamComp.CanRemoveParameter(side, removeIdx))
                        throw SetCountStopped(side, targetCount, paramList.Count, $"Cannot remove parameter at index {removeIdx}");

                    var param = paramList[removeIdx];
                    if (side == GH_ParameterSide.Input)
                        ghComponent.Params.UnregisterInputParameter(param);
                    else
                        ghComponent.Params.UnregisterOutputParameter(param);
                    removed++;
                }

                while (paramList.Count < targetCount)
                {
                    int insertIdx = paramList.Count;
                    if (!varParamComp.CanInsertParameter(side, insertIdx))
                        throw SetCountStopped(side, targetCount, paramList.Count, $"Cannot add parameter at index {insertIdx}");

                    var newParam = varParamComp.CreateParameter(side, insertIdx);
                    if (newParam == null)
                        throw SetCountStopped(side, targetCount, paramList.Count, $"Failed to create parameter at index {insertIdx}");

                    if (side == GH_ParameterSide.Input)
                        ghComponent.Params.RegisterInputParam(newParam);
                    else
                        ghComponent.Params.RegisterOutputParam(newParam);
                    added++;
                }

                var rebuildFields = FinishReshape(ghComponent, varParamComp, parametersChanged: true);

                return ZoomableReply(new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["id"] = ghComponent.InstanceGuid.ToString(),
                    ["operation"] = "set_count",
                    ["side"] = side.ToString().ToLower(),
                    ["targetCount"] = targetCount,
                    ["actualCount"] = paramList.Count,
                    ["added"] = added,
                    ["removed"] = removed,
                    ["errors"] = null
                }, rebuildFields);
            };
            return null;
        }

        /// <summary>The rollback for a <c>set_count</c> the component stopped partway.</summary>
        private static EditRollbackException SetCountStopped(GH_ParameterSide side, int targetCount, int reached, string reason)
            => new EditRollbackException(
                $"set_count could not reach {targetCount} {side.ToString().ToLower()} parameters: {reason} (it had reached {reached})");

        /// <summary>
        /// Settle a component whose ports this call reshaped, and on a script component rebuild its
        /// program. Returns the rebuild fields for a script component, <c>null</c> for any other.
        /// Runs inside the edit, after the component's generic record: the rebuild itself is not
        /// recorded, because undo restores the ports and the source and the component rebuilds
        /// when it is next solved.
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

            // No expire here: the component's generic record has the edit recompute it, and
            // everything downstream, once at commit.
            return rebuildFields;
        }

        /// <summary>The nicknames of a side's ports, in order: the names the removal rule reports.</summary>
        private static List<string> PortNames(List<IGH_Param> ports)
        {
            var names = new List<string>(ports.Count);
            foreach (var p in ports)
                names.Add(p.NickName);
            return names;
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
