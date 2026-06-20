using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified script tool - script component operations (get, set, configure, info)
    /// </summary>
    [McpServerToolType]
    public class GhScriptTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_script",
            Description = "Script component operations (C#, Python) - get/set source code, configure parameters",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["get"] = new ActionInfo
                {
                    Name = "get",
                    Description = "Get source code from a script component",
                    Required = new[] { "id" },
                    Example = "action='get', id='abc-123'"
                },
                ["set"] = new ActionInfo
                {
                    Name = "set",
                    Description = "Set source code on a script component",
                    Required = new[] { "id", "code" },
                    Example = "action='set', id='abc', code='print(x)'",
                    Tips = new[] {
                        "Params matching by name keep their wires (LCS diffing)",
                        "Renamed/removed params lose connections — reported in lostConnections",
                        "Use gh_wire(action='connect', connections='<lostConnections>') to restore",
                        "Script language is preserved automatically; start code with '#! python 3' or '// #! csharp' to set it explicitly"
                    }
                },
                ["configure"] = new ActionInfo
                {
                    Name = "configure",
                    Description = "Configure script with inputs, outputs, and full source",
                    Required = new[] { "id" },
                    Optional = new[] { "inputs", "outputs", "code" },
                    Example = "action='configure', id='abc', inputs='[{\"name\":\"x\",\"type\":\"double\"}]', code='...'",
                    Tips = new[] { "inputs: [{name, type, access}]", "outputs: [{name, type}]", "types: int, double, bool, string, Point3d, etc.", "Script language is preserved automatically; start code with '#! python 3' or '// #! csharp' to set it explicitly" }
                },
                ["info"] = new ActionInfo
                {
                    Name = "info",
                    Description = "Get detailed script info (source, params, errors)",
                    Required = new[] { "id" },
                    Example = "action='info', id='abc-123'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "Works with C# Script and Python 3 Script components",
                "access values: 'item' (default), 'list', 'tree'"
            }
        };

        public GhScriptTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Script operations. Actions: get|set|configure|info|help")]
        public string GhScript(
            [Description("Action to perform")] string action,
            [Description("Script component GUID")] string id = null,
            [Description("Source code")] string code = null,
            [Description("JSON array of input definitions")] string inputs = null,
            [Description("JSON array of output definitions")] string outputs = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("id", id),
                ("code", code),
                ("inputs", inputs),
                ("outputs", outputs)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            return action.ToLowerInvariant() switch
            {
                "get" => ActionGet(id),
                "set" => ActionSet(id, code),
                "configure" => ActionConfigure(id, inputs, outputs, code),
                "info" => ActionInfo(id),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionGet(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                string source = TryGetScriptSource(component);
                if (source == null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Could not retrieve source code. Component may not be a script component.",
                        componentType = component.GetType().Name
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    name = component.Name,
                    source,
                    sourceLength = source.Length
                });
            });
        }

        private string ActionSet(string id, string code)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!IsScriptComponent(component))
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"Component '{component.NickName}' is not a script component.",
                        componentType = component.GetType().Name
                    });
                }

                try
                {
                    dynamic scriptComp = component;

                    scriptComp.SetSource(PreserveLanguageDirective(component, code));

                    // Surgically sync params instead of SetParametersFromScript(),
                    // which rebuilds all params and destroys cluster input hooks.
                    List<object> lostConnections = null;
                    try
                    {
                        SyncScriptParams(component, code, out var lost);
                        if (lost.Count > 0) lostConnections = lost;
                    }
                    catch (Exception paramEx)
                    {
                        DebugLog.Warn($"[ActionSet] SyncScriptParams failed: {paramEx.Message}");
                    }

                    // Only expire — don't trigger a full document solution.
                    // Inside clusters, ExpireSolution(true) or NewSolution(true) on the
                    // internal document causes the parent to recreate the cluster,
                    // orphaning the editor and breaking all cluster input hooks.
                    if (component is IGH_ActiveObject activeObj)
                        activeObj.ExpireSolution(false);

                    var inputs = new List<object>();
                    var outputs = new List<object>();

                    if (component is IGH_Component ghComp)
                    {
                        foreach (var param in ghComp.Params.Input)
                            inputs.Add(new { name = param.Name, type = param.TypeName });
                        foreach (var param in ghComp.Params.Output)
                            outputs.Add(new { name = param.Name, type = param.TypeName });
                    }

                    var response = new Dictionary<string, object>
                    {
                        ["success"] = true,
                        ["id"] = component.InstanceGuid.ToString(),
                        ["codeSet"] = true,
                        ["inputs"] = inputs,
                        ["outputs"] = outputs
                    };

                    if (lostConnections != null)
                    {
                        response["lostConnections"] = lostConnections;
                        response["reconnectHint"] = "These connections were lost due to parameter changes. "
                            + "To restore, pass the lostConnections array as 'connections' to gh_wire(action='connect'), "
                            + "updating targetParam/sourceParam if params were renamed.";
                    }

                    return JsonConvert.SerializeObject(response);
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"Failed to set script code: {ex.Message}",
                        componentType = component.GetType().Name
                    });
                }
            });
        }

        private string ActionConfigure(string id, string inputs, string outputs, string code)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(component is IGH_Component ghComponent))
                    return ToolHelpers.ErrorResponse("Not an IGH_Component");

                var inputDefs = ParseParamDefs(inputs);
                var outputDefs = ParseParamDefs(outputs);

                bool configured = false;
                string message = "";

                try
                {
                    dynamic scriptComp = component;

                    if (component is IGH_VariableParameterComponent varParamComp)
                    {
                        configured = ConfigureViaVariableParams(ghComponent, varParamComp, inputDefs, outputDefs);

                        if (configured)
                        {
                            message = $"Parameters configured ({ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs)";

                            if (!string.IsNullOrEmpty(code))
                            {
                                try
                                {
                                    scriptComp.SetSource(PreserveLanguageDirective(component, code));
                                    message += ", source set";
                                }
                                catch (Exception srcEx)
                                {
                                    message += $", source failed: {srcEx.Message}";
                                }
                            }

                            // Re-apply type hints AFTER source is set, because SetSource may reset them
                            ReapplyTypeHints(ghComponent, inputDefs, outputDefs);

                            // Call VariableParameterMaintenance to finalize parameter setup
                            varParamComp.VariableParameterMaintenance();

                            ghComponent.ExpireSolution(false);
                        }
                    }

                    if (!configured && !string.IsNullOrEmpty(code))
                    {
                        try
                        {
                            scriptComp.SetSource(PreserveLanguageDirective(component, code));
                            // Best-effort param sync after SetSource: not all script-component types
                            // expose every hook, so a failure here is non-fatal — log at Debug and continue.
                            try { SyncScriptParams(component, code, out _); }
                            catch (Exception syncEx) { DebugLog.Debug($"SyncScriptParams skipped: {syncEx.Message}"); }
                            try { scriptComp.SyncParameters(); }
                            catch (Exception syncEx) { DebugLog.Debug($"SyncParameters skipped: {syncEx.Message}"); }

                            if (component is IGH_VariableParameterComponent vpComp2)
                            {
                                try { vpComp2.VariableParameterMaintenance(); }
                                catch (Exception vpmEx) { DebugLog.Debug($"VariableParameterMaintenance skipped: {vpmEx.Message}"); }
                            }

                            ghComponent.ExpireSolution(false);
                            configured = true;
                            message = $"Source set ({ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs)";
                        }
                        catch (Exception ex)
                        {
                            message = $"Configuration failed: {ex.Message}";
                        }
                    }
                    else if (!configured)
                    {
                        message = "No configuration applied. Provide 'code' and/or 'inputs'/'outputs' parameters.";
                    }
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Configuration failed: {ex.Message}" });
                }

                var finalInputs = new List<object>();
                var finalOutputs = new List<object>();

                foreach (var param in ghComponent.Params.Input)
                    finalInputs.Add(new { name = param.Name, type = param.TypeName, access = param.Access.ToString() });
                foreach (var param in ghComponent.Params.Output)
                    finalOutputs.Add(new { name = param.Name, type = param.TypeName });

                return JsonConvert.SerializeObject(new
                {
                    success = configured,
                    id = component.InstanceGuid.ToString(),
                    message,
                    inputs = finalInputs,
                    outputs = finalOutputs
                });
            });
        }

        private string ActionInfo(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(component is IGH_Component ghComponent))
                    return ToolHelpers.ErrorResponse("Not a component");

                string source = TryGetScriptSource(component);

                var inputs = new List<object>();
                foreach (var param in ghComponent.Params.Input)
                {
                    inputs.Add(new
                    {
                        name = param.Name,
                        nickname = param.NickName,
                        type = param.TypeName,
                        access = param.Access.ToString(),
                        sourceCount = param.SourceCount
                    });
                }

                var outputs = new List<object>();
                foreach (var param in ghComponent.Params.Output)
                {
                    outputs.Add(new
                    {
                        name = param.Name,
                        nickname = param.NickName,
                        type = param.TypeName,
                        recipientCount = param.Recipients.Count
                    });
                }

                var messages = new List<object>();
                if (ghComponent.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank)
                {
                    foreach (var msg in ghComponent.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                        messages.Add(new { level = "error", message = msg });
                    foreach (var msg in ghComponent.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
                        messages.Add(new { level = "warning", message = msg });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    name = component.Name,
                    nickname = component.NickName,
                    componentType = component.GetType().Name,
                    hasSource = source != null,
                    source,
                    inputs,
                    outputs,
                    runtimeLevel = ghComponent.RuntimeMessageLevel.ToString(),
                    messages
                });
            });
        }

        #region Helper Methods

        /// <summary>
        /// Parse the C# RunScript signature to extract declared input/output parameter names.
        /// Returns null if parsing isn't possible or applicable (Python scripts, unparseable code).
        /// </summary>
        private (List<string> inputs, List<string> outputs)? ParseScriptParams(IGH_DocumentObject component, string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;

            var typeName = component.GetType().Name;

            // Python scripts manage params via ZUI, not from code
            if (typeName.Contains("Python"))
                return null;

            // For C# scripts, parse the RunScript signature
            var match = Regex.Match(code, @"RunScript\s*\(([^)]*)\)");
            if (!match.Success)
                return null;

            var signature = match.Groups[1].Value;
            var codeInputs = new List<string>();
            var codeOutputs = new List<string>();

            var paramStrings = SplitSignatureParams(signature);

            foreach (var paramStr in paramStrings)
            {
                var trimmed = paramStr.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                var tokens = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2)
                    continue;

                if (tokens[0] == "ref" || tokens[0] == "out")
                    codeOutputs.Add(tokens[tokens.Length - 1]);
                else
                    codeInputs.Add(tokens[tokens.Length - 1]);
            }

            return (codeInputs, codeOutputs);
        }

        /// <summary>
        /// Surgically sync component parameters to match what the code declares,
        /// without calling SetParametersFromScript() which destroys all cluster input hooks.
        /// Uses LCS (longest common subsequence) to identify which params are unchanged
        /// and only removes/inserts the ones that actually differ.
        /// </summary>
        private void SyncScriptParams(IGH_DocumentObject component, string code, out List<object> lostConnections)
        {
            lostConnections = new List<object>();

            var parsed = ParseScriptParams(component, code);
            if (parsed == null)
                return; // Can't parse or not applicable — leave params alone

            if (!(component is IGH_Component ghComponent))
                return;

            if (!(component is IGH_VariableParameterComponent varParamComp))
            {
                DebugLog.Warn("[SyncScriptParams] Component is not IGH_VariableParameterComponent, cannot sync params");
                return;
            }

            var (codeInputs, codeOutputs) = parsed.Value;

            var existingInputs = ghComponent.Params.Input.Select(p => p.NickName).ToList();
            // C# scripts have a built-in "out" output at index 0
            var existingOutputs = ghComponent.Params.Output.Skip(1).Select(p => p.NickName).ToList();

            bool inputsMatch = existingInputs.SequenceEqual(codeInputs, StringComparer.Ordinal);
            bool outputsMatch = existingOutputs.SequenceEqual(codeOutputs, StringComparer.Ordinal);

            if (inputsMatch && outputsMatch)
            {
                DebugLog.Info("[SyncScriptParams] Params unchanged, no sync needed (cluster-safe)");
                return;
            }

            DebugLog.Info($"[SyncScriptParams] Syncing params — inputs: [{string.Join(",", existingInputs)}] -> [{string.Join(",", codeInputs)}], outputs: [{string.Join(",", existingOutputs)}] -> [{string.Join(",", codeOutputs)}]");

            if (!inputsMatch)
            {
                SyncParamSide(ghComponent, varParamComp, GH_ParameterSide.Input, codeInputs, 0, out var inputLost);
                lostConnections.AddRange(inputLost);
            }

            if (!outputsMatch)
            {
                SyncParamSide(ghComponent, varParamComp, GH_ParameterSide.Output, codeOutputs, 1, out var outputLost);
                lostConnections.AddRange(outputLost);
            }

            varParamComp.VariableParameterMaintenance();
            DebugLog.Info($"[SyncScriptParams] Done — {ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs");
        }

        /// <summary>
        /// Sync one side (input or output) of a component's params to match target names.
        /// Uses LCS to maximize preserved connections: params whose names appear in both
        /// the existing and target lists keep their connections intact.
        /// </summary>
        /// <param name="skipCount">Number of built-in params to skip (e.g., 1 for C# outputs to skip "out")</param>
        /// <param name="lostConnections">Connections on removed params, directly usable with gh_wire(action='connect')</param>
        private void SyncParamSide(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            GH_ParameterSide side, List<string> targetNames, int skipCount, out List<object> lostConnections)
        {
            lostConnections = new List<object>();
            var paramList = side == GH_ParameterSide.Input ? ghComponent.Params.Input : ghComponent.Params.Output;
            var existingNames = paramList.Skip(skipCount).Select(p => p.NickName).ToList();

            // Find longest common subsequence — these params keep their connections
            var matches = ComputeLCS(existingNames, targetNames);
            var keepIndices = new HashSet<int>(matches.Select(m => m.existIdx));

            // Step 1: Remove params not in LCS (iterate backwards to avoid index shift issues)
            for (int i = existingNames.Count - 1; i >= 0; i--)
            {
                if (keepIndices.Contains(i))
                    continue;

                var param = paramList[i + skipCount];

                // Capture connections before removal
                if (side == GH_ParameterSide.Input)
                {
                    foreach (var source in param.Sources)
                    {
                        var sourceObj = source.Attributes?.GetTopLevel?.DocObject;
                        if (sourceObj == null) continue;
                        lostConnections.Add(new
                        {
                            sourceId = sourceObj.InstanceGuid.ToString(),
                            sourceParam = source.Name,
                            targetId = ghComponent.InstanceGuid.ToString(),
                            targetParam = param.NickName,
                            removedParam = param.NickName,
                            side = "input"
                        });
                    }
                }
                else
                {
                    foreach (var recipient in param.Recipients)
                    {
                        var targetObj = recipient.Attributes?.GetTopLevel?.DocObject;
                        if (targetObj == null) continue;
                        lostConnections.Add(new
                        {
                            sourceId = ghComponent.InstanceGuid.ToString(),
                            sourceParam = param.NickName,
                            targetId = targetObj.InstanceGuid.ToString(),
                            targetParam = recipient.Name,
                            removedParam = param.NickName,
                            side = "output"
                        });
                    }
                }

                DebugLog.Debug($"[SyncParamSide] Removing {side} '{param.NickName}' at index {i + skipCount}");
                if (side == GH_ParameterSide.Input)
                    ghComponent.Params.UnregisterInputParameter(param);
                else
                    ghComponent.Params.UnregisterOutputParameter(param);
            }

            // After removal, remaining custom params (past skipCount) are LCS elements in order.
            // Step 2: Insert new params at the correct positions.
            // Walk through target list left-to-right. For positions covered by LCS, the param
            // is already in place. For other positions, insert a new param.
            // Since we process left-to-right, actualIdx = t + skipCount accounts for
            // all previous insertions automatically.
            var lcsTargetIndices = new HashSet<int>(matches.Select(m => m.targetIdx));

            for (int t = 0; t < targetNames.Count; t++)
            {
                if (lcsTargetIndices.Contains(t))
                    continue; // Already in place from existing params

                int actualIdx = t + skipCount;
                if (!varParamComp.CanInsertParameter(side, actualIdx))
                {
                    DebugLog.Warn($"[SyncParamSide] Cannot insert {side} param at index {actualIdx}");
                    continue;
                }

                var newParam = varParamComp.CreateParameter(side, actualIdx);
                if (newParam == null)
                {
                    DebugLog.Warn($"[SyncParamSide] CreateParameter returned null for {side} at index {actualIdx}");
                    continue;
                }

                newParam.Name = targetNames[t];
                newParam.NickName = targetNames[t];

                DebugLog.Debug($"[SyncParamSide] Inserting {side} '{targetNames[t]}' at index {actualIdx}");
                if (side == GH_ParameterSide.Input)
                    ghComponent.Params.RegisterInputParam(newParam, actualIdx);
                else
                    ghComponent.Params.RegisterOutputParam(newParam, actualIdx);
            }
        }

        /// <summary>
        /// Compute the Longest Common Subsequence of two string lists.
        /// Returns pairs of (index in existing, index in target) for matched elements.
        /// </summary>
        private static List<(int existIdx, int targetIdx)> ComputeLCS(List<string> existing, List<string> target)
        {
            int m = existing.Count, n = target.Count;
            var dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                    dp[i, j] = existing[i - 1] == target[j - 1]
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);

            // Backtrack to find matched pairs
            var matches = new List<(int, int)>();
            int x = m, y = n;
            while (x > 0 && y > 0)
            {
                if (existing[x - 1] == target[y - 1])
                {
                    matches.Add((x - 1, y - 1));
                    x--; y--;
                }
                else if (dp[x - 1, y] >= dp[x, y - 1])
                    x--;
                else
                    y--;
            }

            matches.Reverse();
            return matches;
        }

        /// <summary>
        /// Split a C# method signature into individual parameter strings,
        /// correctly handling generic types like List&lt;Point3d&gt;.
        /// </summary>
        private List<string> SplitSignatureParams(string signature)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < signature.Length; i++)
            {
                char c = signature[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(signature.Substring(start, i - start));
                    start = i + 1;
                }
            }

            if (start < signature.Length)
                result.Add(signature.Substring(start));

            return result;
        }

        private bool IsScriptComponent(IGH_DocumentObject component)
        {
            if (component == null) return false;
            var typeName = component.GetType().Name;
            if (typeName.Contains("Script") || typeName.Contains("Python") || typeName.Contains("CSharp"))
                return true;
            return component.GetType().GetMethod("SetSource") != null;
        }

        private string TryGetScriptSource(IGH_DocumentObject component)
        {
            var tryGetSourceMethod = component.GetType().GetMethod("TryGetSource");
            if (tryGetSourceMethod != null)
            {
                try
                {
                    var parameters = new object[] { null };
                    var result = tryGetSourceMethod.Invoke(component, parameters);
                    if (result is bool success && success)
                        return parameters[0] as string;
                }
                catch (Exception ex) { DebugLog.Debug($"TryGetScriptSource: TryGetSource() invoke failed: {ex.Message}"); }
            }

            // Best-effort probe cascade: try each known source-access member and fall through to the
            // next when it's absent/throws. Debug-logged (off by default) so a probe miss is traceable
            // without normal-run noise.
            try { dynamic sc = component; return sc.Source; }
            catch (Exception ex) { DebugLog.Debug($"TryGetScriptSource: dynamic .Source unavailable: {ex.Message}"); }
            try { dynamic sc = component; return sc.ScriptSource; }
            catch (Exception ex) { DebugLog.Debug($"TryGetScriptSource: dynamic .ScriptSource unavailable: {ex.Message}"); }
            try { dynamic sc = component; return sc.Code; }
            catch (Exception ex) { DebugLog.Debug($"TryGetScriptSource: dynamic .Code unavailable: {ex.Message}"); }

            var sourceProp = component.GetType().GetProperty("Source")
                ?? component.GetType().GetProperty("ScriptSource")
                ?? component.GetType().GetProperty("Code");
            if (sourceProp != null)
            {
                try { return sourceProp.GetValue(component) as string; }
                catch (Exception ex) { DebugLog.Debug($"TryGetScriptSource: {sourceProp.Name} getter failed: {ex.Message}"); }
            }

            return null;
        }

        /// <summary>
        /// Preserve the script component's Rhino 8 language directive (e.g. "#! python 3",
        /// "// #! csharp") when <paramref name="code"/> omits it. SetSource() replaces the whole
        /// body, so a directive-less body would otherwise strip the component's language and
        /// cause "Can not determine input code language" at solve time. A directive already
        /// present in <paramref name="code"/> is respected as-is.
        /// </summary>
        private string PreserveLanguageDirective(IGH_DocumentObject component, string code)
            => ScriptDirective.Preserve(TryGetScriptSource(component), code);

        private List<ParamDef> ParseParamDefs(string json)
        {
            if (string.IsNullOrEmpty(json)) return new List<ParamDef>();
            try
            {
                return JArray.Parse(json)
                    .Select(item => new ParamDef
                    {
                        Name = item["name"]?.ToString() ?? "param",
                        Type = item["type"]?.ToString() ?? "object",
                        Access = item["access"]?.ToString() ?? ""
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                DebugLog.Warn($"ParseParamDefs: invalid param-def JSON ignored ({ex.Message})");
                return new List<ParamDef>();
            }
        }

        /// <summary>
        /// Re-apply type hints to parameters after source has been set
        /// SetSource may reset output type hints, so we need to reapply them
        /// </summary>
        private void ReapplyTypeHints(IGH_Component ghComponent, List<ParamDef> inputDefs, List<ParamDef> outputDefs)
        {
            DebugLog.Debug($"ReapplyTypeHints: {inputDefs.Count} inputs, {outputDefs.Count} outputs");

            // Re-apply input type hints
            for (int i = 0; i < inputDefs.Count && i < ghComponent.Params.Input.Count; i++)
            {
                var def = inputDefs[i];
                var param = ghComponent.Params.Input[i];
                if (!string.IsNullOrEmpty(def.Type))
                {
                    SetParameterTypeHint(param, def.Type);
                }
            }

            // Re-apply output type hints (skip the first 'out' parameter if present)
            // Output parameters start at index 1 (index 0 is 'out')
            for (int i = 0; i < outputDefs.Count; i++)
            {
                var def = outputDefs[i];
                // Output index is i+1 because index 0 is the built-in 'out' parameter
                int paramIndex = i + 1;
                if (paramIndex < ghComponent.Params.Output.Count)
                {
                    var param = ghComponent.Params.Output[paramIndex];
                    if (!string.IsNullOrEmpty(def.Type))
                    {
                        DebugLog.Debug($"ReapplyTypeHints: Output '{param.Name}' -> type '{def.Type}'");
                        SetParameterTypeHint(param, def.Type);
                    }
                }
            }
        }

        private bool ConfigureViaVariableParams(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp, List<ParamDef> inputDefs, List<ParamDef> outputDefs)
        {
            try
            {
                DebugLog.Info($"ConfigureViaVariableParams: {inputDefs.Count} inputs, {outputDefs.Count} outputs");

                // Remove existing inputs
                while (ghComponent.Params.Input.Count > 0)
                {
                    var param = ghComponent.Params.Input[ghComponent.Params.Input.Count - 1];
                    varParamComp.CanRemoveParameter(GH_ParameterSide.Input, ghComponent.Params.Input.Count - 1);
                    ghComponent.Params.UnregisterInputParameter(param);
                }

                // Remove outputs except first ('out')
                while (ghComponent.Params.Output.Count > 1)
                {
                    var param = ghComponent.Params.Output[ghComponent.Params.Output.Count - 1];
                    varParamComp.CanRemoveParameter(GH_ParameterSide.Output, ghComponent.Params.Output.Count - 1);
                    ghComponent.Params.UnregisterOutputParameter(param);
                }

                // Add inputs with type hints
                foreach (var def in inputDefs)
                {
                    if (varParamComp.CanInsertParameter(GH_ParameterSide.Input, ghComponent.Params.Input.Count))
                    {
                        // Use CreateParameter to get the component's native parameter type
                        var newParam = varParamComp.CreateParameter(GH_ParameterSide.Input, ghComponent.Params.Input.Count);
                        if (newParam != null)
                        {
                            newParam.Name = def.Name;
                            newParam.NickName = def.Name;

                            // Set type hint BEFORE registering (for script components)
                            if (!string.IsNullOrEmpty(def.Type))
                            {
                                SetParameterTypeHint(newParam, def.Type);
                            }

                            // Set access mode
                            if (!string.IsNullOrEmpty(def.Access))
                            {
                                switch (def.Access.ToLowerInvariant())
                                {
                                    case "list": newParam.Access = GH_ParamAccess.list; break;
                                    case "tree": newParam.Access = GH_ParamAccess.tree; break;
                                    default: newParam.Access = GH_ParamAccess.item; break;
                                }
                            }

                            ghComponent.Params.RegisterInputParam(newParam);
                            DebugLog.Debug($"Added input '{def.Name}' type='{def.Type}' access='{def.Access}'");
                        }
                    }
                }

                // Add outputs with type hints
                foreach (var def in outputDefs)
                {
                    if (varParamComp.CanInsertParameter(GH_ParameterSide.Output, ghComponent.Params.Output.Count))
                    {
                        var newParam = varParamComp.CreateParameter(GH_ParameterSide.Output, ghComponent.Params.Output.Count);
                        if (newParam != null)
                        {
                            newParam.Name = def.Name;
                            newParam.NickName = def.Name;

                            // Set type hint for output too
                            if (!string.IsNullOrEmpty(def.Type))
                            {
                                SetParameterTypeHint(newParam, def.Type);
                            }

                            ghComponent.Params.RegisterOutputParam(newParam);
                            DebugLog.Debug($"Added output '{def.Name}' type='{def.Type}'");
                        }
                    }
                }

                varParamComp.VariableParameterMaintenance();
                DebugLog.Info($"ConfigureViaVariableParams complete: {ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs");
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Error($"ConfigureViaVariableParams failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Set the TypeHint on a script parameter using Rhino 8's TypeHints.Select(Type) + IScriptParameter.Converter
        /// </summary>
        private void SetParameterTypeHint(IGH_Param param, string typeName)
        {
            if (param == null || string.IsNullOrEmpty(typeName)) return;

            Type targetType = GetRhinoType(typeName);
            if (targetType == null)
            {
                DebugLog.Warn($"Unknown type '{typeName}' for parameter '{param.Name}'");
                return;
            }

            DebugLog.Debug($"SetParameterTypeHint: param='{param.Name}' paramType={param.GetType().Name} targetType={targetType.Name}");

            try
            {
                // Step 1: Get TypeHints collection and select the converter
                var typeHintsProp = param.GetType().GetProperty("TypeHints");
                if (typeHintsProp == null)
                {
                    DebugLog.Warn($"Parameter '{param.Name}' has no TypeHints property (type: {param.GetType().FullName})");
                    return;
                }

                var typeHints = typeHintsProp.GetValue(param);
                if (typeHints == null)
                {
                    DebugLog.Warn($"TypeHints is null for '{param.Name}'");
                    return;
                }

                var selectMethod = typeHints.GetType().GetMethod("Select", new[] { typeof(Type) });
                if (selectMethod == null)
                {
                    DebugLog.Warn($"TypeHints.Select(Type) method not found for '{param.Name}'");
                    return;
                }

                var converter = selectMethod.Invoke(typeHints, new object[] { targetType });
                if (converter == null)
                {
                    DebugLog.Warn($"TypeHints.Select({targetType.Name}) returned null for '{param.Name}'");
                    return;
                }

                // Step 2: Assign converter via IScriptParameter.Converter interface property
                var scriptParamInterface = param.GetType().GetInterfaces()
                    .FirstOrDefault(i => i.Name == "IScriptParameter" || i.FullName?.Contains("IScriptParameter") == true);

                if (scriptParamInterface != null)
                {
                    var converterProp = scriptParamInterface.GetProperty("Converter");
                    if (converterProp != null && converterProp.CanWrite)
                    {
                        converterProp.SetValue(param, converter);
                        DebugLog.Debug($"Set '{param.Name}' type hint to {targetType.Name}");
                        return;
                    }
                    else
                    {
                        DebugLog.Warn($"Converter property not writable for '{param.Name}'");
                    }
                }
                else
                {
                    DebugLog.Warn($"IScriptParameter not found for '{param.Name}'");
                }

                DebugLog.Warn($"Could not assign type hint for '{param.Name}'");
            }
            catch (Exception ex)
            {
                DebugLog.Error($"SetParameterTypeHint failed for '{param.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Get the .NET Type for a given type name
        /// </summary>
        private Type GetRhinoType(string typeName)
        {
            var normalized = typeName.ToLowerInvariant();
            return normalized switch
            {
                "int" or "integer" => typeof(int),
                "double" or "number" or "float" => typeof(double),
                "bool" or "boolean" => typeof(bool),
                "string" or "text" => typeof(string),
                "point" or "point3d" or "pt" => typeof(Rhino.Geometry.Point3d),
                "vector" or "vector3d" or "vec" => typeof(Rhino.Geometry.Vector3d),
                "plane" => typeof(Rhino.Geometry.Plane),
                "mesh" or "msh" => typeof(Rhino.Geometry.Mesh),
                "brep" => typeof(Rhino.Geometry.Brep),
                "curve" or "crv" => typeof(Rhino.Geometry.Curve),
                "surface" or "srf" => typeof(Rhino.Geometry.Surface),
                "line" or "ln" => typeof(Rhino.Geometry.Line),
                "box" => typeof(Rhino.Geometry.Box),
                "circle" or "circ" => typeof(Rhino.Geometry.Circle),
                "arc" => typeof(Rhino.Geometry.Arc),
                "transform" or "xform" => typeof(Rhino.Geometry.Transform),
                "color" or "colour" => typeof(System.Drawing.Color),
                "guid" => typeof(Guid),
                "interval" or "domain" => typeof(Rhino.Geometry.Interval),
                "rectangle" or "rect" => typeof(Rhino.Geometry.Rectangle3d),
                "geometry" or "geom" => typeof(Rhino.Geometry.GeometryBase),
                _ => null
            };
        }

        private class ParamDef
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Access { get; set; }
        }

        /// <summary>
        /// Create a typed parameter instance based on type name
        /// </summary>
        private IGH_Param CreateTypedParameter(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return new Param_GenericObject();

            // Normalize type name (case-insensitive, trim whitespace)
            var normalized = typeName.Trim().ToLowerInvariant();

            // Map common type names to Grasshopper parameter types
            switch (normalized)
            {
                // Numeric types
                case "double":
                case "number":
                case "float":
                    return new Param_Number();

                case "int":
                case "integer":
                    return new Param_Integer();

                case "bool":
                case "boolean":
                    return new Param_Boolean();

                // String types
                case "string":
                case "text":
                    return new Param_String();

                // Geometric types - Points and Vectors
                case "point":
                case "point3d":
                case "pt":
                    return new Param_Point();

                case "vector":
                case "vector3d":
                case "vec":
                    return new Param_Vector();

                case "plane":
                    return new Param_Plane();

                // Curves
                case "curve":
                case "crv":
                    return new Param_Curve();

                case "line":
                case "ln":
                    return new Param_Line();

                case "circle":
                case "circ":
                    return new Param_Circle();

                case "arc":
                    return new Param_Arc();

                case "polyline":
                    return new Param_Curve(); // Polyline uses Param_Curve

                // Surfaces
                case "surface":
                case "srf":
                    return new Param_Surface();

                case "brep":
                    return new Param_Brep();

                // Mesh
                case "mesh":
                case "msh":
                    return new Param_Mesh();

                // Box and other shapes
                case "box":
                    return new Param_Box();

                case "rectangle":
                case "rect":
                    return new Param_Rectangle();

                // Geometry (generic)
                case "geometry":
                case "geom":
                    return new Param_Geometry();

                // Color
                case "color":
                case "colour":
                    return new Param_Colour();

                // Interval and Domain
                case "interval":
                case "domain":
                    return new Param_Interval();

                // Transform
                case "transform":
                case "xform":
                    return new Param_Transform();

                // Generic/Object (fallback)
                case "object":
                case "generic":
                default:
                    return new Param_GenericObject();
            }
        }

        #endregion
    }
}
