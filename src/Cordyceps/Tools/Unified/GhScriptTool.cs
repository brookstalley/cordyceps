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
                    Example = "action='get', id='abc-123'",
                    Tips = new[] {
                        "'source' is the text stored on the component. Whenever the running program can be read the response carries sourceDiverged (true or false), plus runningSource and divergenceNote when they differ",
                        "If the running program cannot be read — usually because the component has never built — sourceDiverged is absent and runningSourceUnavailable says why. An absent sourceDiverged never means 'they agree'",
                        "Divergence is normal for C# script components: Rhino rewrites the RunScript signature of SDK-mode scripts as it builds them. runningSource is what executes"
                    }
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
                        "Script language is preserved automatically; start code with '#! python 3' or '// #! csharp' to set it explicitly",
                        "A bare unified 'Script' component (no language picked) returns a languageWarning until code starts with a directive — add '#! python 3' or '// #! csharp'",
                        "Components that expose their code as a visible input parameter can't be written directly — the error says so and nothing is changed; remove that parameter, or feed the code through it with gh_wire",
                        "The component is rebuilt from the new source before the call returns: rebuilt=true means it will run the new program on its next solve",
                        "rebuilt=false always carries the reason beside it: rebuildSkipped (nothing needed rebuilding, or the component has no rebuild hooks and recompiles on its own — the string says which) or rebuildFailed (a hook threw; the component is probably still running the old program). Read the reason, not the count",
                        "verified=true means the running program was read back and matches what was written. verified=false adds sourceDiverged and runningSource — expected for C# SDK-mode scripts, whose RunScript signature Rhino rewrites as it builds",
                        "If the program could not be read there is no verified field at all, and verificationSkipped says why — never read an absent verified as a match",
                        "Code that fails to compile still reports success here — the write and the rebuild both succeeded. Build errors surface on the component; after setting code, check gh_inspect(action='status') (it takes no id and lists every component's status and messages) or gh_inspect(action='tree', id=...) (each tree carries its owner's error or warning)",
                        "The compile happens inside this call rather than at the next solve, so the first script write of a session can take noticeably longer"
                    }
                },
                ["configure"] = new ActionInfo
                {
                    Name = "configure",
                    Description = "Configure script with inputs, outputs, and full source",
                    Required = new[] { "id" },
                    Optional = new[] { "inputs", "outputs", "code" },
                    Example = "action='configure', id='abc', inputs='[{\"name\":\"x\",\"type\":\"double\"}]', code='...'",
                    Tips = new[] {
                        "inputs: [{name, type, access}]", "outputs: [{name, type}]", "types: int, double, bool, string, Point3d, etc.",
                        "Partial update: omit a side (don't pass inputs/outputs) to leave it untouched; pass [] to clear that side. Passing inputs without outputs no longer wipes outputs.",
                        "Malformed inputs/outputs JSON is rejected with an error before anything changes — it is never treated as an empty array",
                        "Params matching by name keep their wires (like 'set'); renamed/removed params lose connections, reported in lostConnections — pass it to gh_wire(action='connect') to restore",
                        "Script language is preserved automatically; start code with '#! python 3' or '// #! csharp' to set it explicitly",
                        "A bare unified 'Script' component (no language picked) returns a languageWarning until code starts with a directive — add '#! python 3' or '// #! csharp'",
                        "When 'code' is provided the response includes codeSet (bool); if setting the source fails after params were applied, success is false, codeSet=false, and sourceError holds the reason — the params DID change",
                        "The parameter half reports the same way: parametersApplied (bool) with parametersError when a component cannot vary its parameters and the requested inputs/outputs were refused. success is false whenever either half failed, so never read a written code as the whole request landing",
                        "The inputs/outputs echoed back are the component's actual surface, not what you asked for — with parametersApplied=false they are unchanged, so check that field before wiring to a port you assume exists",
                        "Components that expose their code as a visible input parameter can't be written directly — sourceError says so and the code is left unchanged",
                        "Any call that changes something — new code, new parameters, or both — rebuilds the component's program and reads it back: rebuilt/rebuildSkipped and verified/runningSource mean exactly what they do for action='set'",
                        "Changing inputs/outputs alone rebuilds too: the build reads the parameters to shape the program's ports, so a program compiled against the old ones is stale. Its verified/sourceDiverged compares the running program against the source already stored on the component",
                        "A call that changed nothing (no code, and no parameter change this component could apply) returns rebuilt=false with rebuildSkipped and verificationSkipped saying so — every response answers the rebuild question, so an absent rebuilt field is never the answer",
                        "With codeSet=false the params still changed, so the rebuild still runs — but there is no verified field, and verificationSkipped says the component was rebuilt from the text it already held. Nothing here describes the code you sent; read codeSet and sourceError",
                        "runningSource can arrive WITHOUT a verified field: whenever nothing was verified, the program the component will run is still read and handed back unjudged. Present with verified=false it is the divergence; present with verificationSkipped it is simply what runs"
                    }
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
                "Source is written through the component's SetSource method when it has one, otherwise its writable Code property; a component with neither fails with an error naming the type and what was probed — never a silent no-op",
                "Neither writing source nor reshaping parameters recompiles on its own — set/configure ask the component to rebuild whenever they changed something, then read the running program back, so 'success' means the new program is the one that will run",
                "access values: 'item' (default), 'list', 'tree'",
                "Ids: 'id' names the script component by its GUID, an 8+ hex GUID prefix, or its unique nickname (exact, case-sensitive); a name two objects share is refused with both ids"
            }
        };

        public GhScriptTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Script operations. Actions: get|set|configure|info|help")]
        public string GhScript(
            [Description("Action to perform")] string action,
            [Description("Script component. Object: GUID, 8+ hex GUID prefix, or unique nickname")] string id = null,
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

                string source = ScriptComponents.ReadSource(component);
                if (source == null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Could not retrieve source code. Component may not be a script component.",
                        componentType = component.GetType().Name
                    });
                }

                var response = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["id"] = component.InstanceGuid.ToString(),
                    ["name"] = component.Name,
                    ["source"] = source,
                    ["sourceLength"] = source.Length
                };

                // 'source' is the text stored on the component. The program it actually runs is the
                // built code, which Rhino keeps separately — so a clean round-trip of 'source' is
                // not on its own evidence that a write took effect (issue #33).
                var comparison = ScriptProgram.CompareRunning(component, source);
                ScriptComponents.LogProbes(comparison.Probes);
                if (!comparison.Readable)
                    DebugLog.Info($"[ActionGet] running program unreadable on {component.GetType().Name} — {comparison.Reason}");

                foreach (var field in ScriptProgram.DescribeRead(comparison))
                    response[field.Key] = field.Value;

                return JsonConvert.SerializeObject(response);
            });
        }

        private string ActionSet(string id, string code)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!ScriptComponents.IsScriptComponent(component))
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
                    var finalSource = PreserveLanguageDirective(component, code);
                    WriteScriptSource(component, finalSource);

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

                    // Storing source does not recompile: Rhino builds a Code from the script and
                    // keeps running that until something invalidates it, so a write alone leaves the
                    // component executing its previous program (issue #33). Ask for the rebuild
                    // after the params are settled — the rebuild reads them — and verify the write
                    // landed by reading the running program back.
                    var rebuildFields = ScriptComponents.RebuildAndVerify(component, finalSource);

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

                    foreach (var field in rebuildFields)
                        response[field.Key] = field.Value;

                    // The source is stored, but a unified Script component with no language directive
                    // compiles to nothing ("Can not determine input code language") — and SetSource
                    // reports no error. The failure only surfaces at solve time, which we can't observe
                    // here without forcing a (cluster-unsafe) recompute, so flag it up front (issue #15).
                    var languageWarning = ScriptDirective.LanguageWarning(component.GetType().Name, finalSource);
                    if (languageWarning != null)
                        response["languageWarning"] = languageWarning;

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

                // Partial-update contract: a side is "provided" only when its JSON arg is non-empty.
                // Omitted (null/"") => leave that side (and its wires) untouched; "[]" => clear it.
                // Malformed JSON is a hard error BEFORE any mutation — it must never be read as
                // "empty array", which would wipe that side's params and wires under success:true.
                if (!ScriptParamDefs.TryParse(inputs, out var inputDefs, out var inputsError))
                    return ToolHelpers.ErrorResponse($"Invalid inputs JSON: {inputsError}");
                if (!ScriptParamDefs.TryParse(outputs, out var outputDefs, out var outputsError))
                    return ToolHelpers.ErrorResponse($"Invalid outputs JSON: {outputsError}");

                bool configured = false;
                string message = "";
                string languageWarning = null;
                List<object> lostConnections = null;
                // Partial-apply detectability: when 'code' is provided, codeSet records whether
                // SetSource succeeded and sourceError carries the failure, so a params-applied /
                // code-failed outcome is machine-readable (and overall success is false).
                bool? codeSet = null;
                string sourceError = null;
                // The mirror of codeSet/sourceError for the parameter half: null when no side was
                // asked for, true when it was applied, false when the component refused it. A
                // request half-dropped under success:true is the failure mode this pair exists to
                // make machine-readable — see the codeSet note above.
                bool? parametersApplied = null;
                string parametersError = null;
                // Set once the source lands, so the rebuild that follows the parameter work can
                // check itself against the text that was actually written.
                string writtenSource = null;
                // Set once the parameter surface is actually reshaped — the other half of what
                // decides the rebuild. Distinct from 'configured', which a source-only call also
                // sets, and from "inputs/outputs were supplied", which a component that cannot
                // vary its parameters leaves undone.
                bool parametersChanged = false;
                Dictionary<string, object> rebuildFields = null;

                try
                {
                    dynamic scriptComp = component;

                    bool anyParamSide = inputDefs != null || outputDefs != null;
                    if (component is IGH_VariableParameterComponent varParamComp && anyParamSide)
                    {
                        // Sync only the provided sides via the same LCS helper 'set' uses, so
                        // name-matched params keep their wires and removed-param wires surface in
                        // lostConnections (GHS-3W9N). Replaces the old "unregister everything" rebuild.
                        SyncParamsForConfigure(ghComponent, varParamComp, inputDefs, outputDefs, out var lost);
                        configured = true;
                        parametersChanged = true;
                        parametersApplied = true;
                        message = $"Parameters configured ({ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs)";

                        if (!string.IsNullOrEmpty(code))
                        {
                            try
                            {
                                var finalSource = PreserveLanguageDirective(component, code);
                                WriteScriptSource(component, finalSource);
                                languageWarning = ScriptDirective.LanguageWarning(component.GetType().Name, finalSource);
                                codeSet = true;
                                writtenSource = finalSource;
                                message += ", source set";
                            }
                            catch (Exception srcEx)
                            {
                                codeSet = false;
                                sourceError = srcEx.Message;
                                message += $", source failed: {srcEx.Message} (parameter changes WERE applied — the component's params reflect the new configuration but its code is unchanged)";
                            }
                        }

                        // Re-apply type hints + access AFTER source is set, because SetSource may reset them.
                        ApplyParamHints(ghComponent, inputDefs, outputDefs);

                        // Call VariableParameterMaintenance to finalize parameter setup
                        varParamComp.VariableParameterMaintenance();

                        // Rebuild last, once the params are final: storing source doesn't recompile,
                        // and the rebuild reads the params to shape the code's inputs and outputs —
                        // which is why reshaping those params needs the rebuild every bit as much
                        // as writing new code does, even when this call wrote none.
                        rebuildFields = ScriptComponents.RebuildForChange(component, parametersChanged, writtenSource, codeSet == false);

                        ghComponent.ExpireSolution(false);

                        if (lost.Count > 0) lostConnections = lost;
                    }

                    // Reaching here with a parameter side requested means the branch above declined
                    // it — the component cannot vary its parameters. Record the refusal before the
                    // code write, so it is reported whether or not that write succeeds: writing the
                    // source is not permission to drop the other half of the request silently.
                    if (!configured && anyParamSide)
                    {
                        parametersApplied = false;
                        parametersError = $"'{component.GetType().Name}' cannot vary its parameters, "
                            + "so the requested inputs/outputs were not applied. The inputs/outputs "
                            + "in this response are the component's unchanged surface.";
                    }

                    if (!configured && !string.IsNullOrEmpty(code))
                    {
                        try
                        {
                            var finalSource = PreserveLanguageDirective(component, code);
                            WriteScriptSource(component, finalSource);
                            languageWarning = ScriptDirective.LanguageWarning(component.GetType().Name, finalSource);
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

                            // See the parameter branch above: the write alone leaves the previous
                            // program running, so ask for the rebuild once the params are final.
                            rebuildFields = ScriptComponents.RebuildForChange(component, parametersChanged, finalSource, sourceWriteFailed: false);

                            ghComponent.ExpireSolution(false);
                            configured = true;
                            codeSet = true;
                            writtenSource = finalSource;
                            // The port counts here are the component's own, not the requested ones —
                            // say so when a parameter request was refused, or the numbers read as
                            // confirmation that it was applied.
                            message = $"Source set ({ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs)";
                            if (parametersError != null)
                                message += $" — but {parametersError}";
                        }
                        catch (Exception ex)
                        {
                            codeSet = false;
                            sourceError = ex.Message;
                            // Both halves can fail at once; this message replaces the other's, so
                            // carry the parameter refusal forward rather than letting the source
                            // error be the only thing the prose reports.
                            message = $"Configuration failed: {ex.Message}";
                            if (parametersError != null)
                                message += $" Also: {parametersError}";
                        }
                    }
                    else if (!configured)
                    {
                        // Tell the two no-op cases apart. One message for both told a caller who
                        // HAD passed inputs to pass inputs, which reads as though the request never
                        // arrived — and it sits beside a rebuildSkipped that correctly reports
                        // nothing changed, so the pair has to agree on why.
                        message = parametersError != null
                            ? $"No configuration applied: {parametersError} Pass 'code' to set its source instead."
                            : "No configuration applied. Provide 'code' and/or 'inputs'/'outputs' parameters.";
                    }
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Configuration failed: {ex.Message}" });
                }

                // The paths above rebuild whenever they changed something; this covers the ones
                // that changed nothing — a component that cannot vary its parameters, a source
                // write that threw, a call that supplied neither side. They report why instead of
                // returning nothing, so a caller never has to read an absent rebuild field as an
                // answer.
                if (rebuildFields == null)
                    rebuildFields = ScriptComponents.RebuildForChange(component, parametersChanged, writtenSource, codeSet == false);

                var finalInputs = new List<object>();
                var finalOutputs = new List<object>();

                foreach (var param in ghComponent.Params.Input)
                    finalInputs.Add(new { name = param.Name, type = param.TypeName, access = param.Access.ToString() });
                foreach (var param in ghComponent.Params.Output)
                    finalOutputs.Add(new { name = param.Name, type = param.TypeName });

                // Either half of the request failing makes the whole call a failure: a failed
                // SetSource when the params were applied, and a refused parameter change when the
                // source landed. A partial apply must be detectable, not reported as success:true.
                var configureResponse = new Dictionary<string, object>
                {
                    ["success"] = configured && codeSet != false && parametersApplied != false,
                    ["id"] = component.InstanceGuid.ToString(),
                    ["message"] = message,
                    ["inputs"] = finalInputs,
                    ["outputs"] = finalOutputs
                };
                // Unconditional: every route to here has filled these, so the response always
                // states whether the component's program was rebuilt.
                foreach (var field in rebuildFields)
                    configureResponse[field.Key] = field.Value;
                if (codeSet.HasValue)
                    configureResponse["codeSet"] = codeSet.Value;
                if (sourceError != null)
                    configureResponse["sourceError"] = sourceError;
                if (parametersApplied.HasValue)
                    configureResponse["parametersApplied"] = parametersApplied.Value;
                if (parametersError != null)
                    configureResponse["parametersError"] = parametersError;
                if (languageWarning != null)
                    configureResponse["languageWarning"] = languageWarning;

                // Same lostConnections contract as 'set': removed-param wires are returned so the
                // caller can restore them with gh_wire(action='connect') (GHS-3W9N).
                if (lostConnections != null)
                {
                    configureResponse["lostConnections"] = lostConnections;
                    configureResponse["reconnectHint"] = "These connections were lost due to parameter changes. "
                        + "To restore, pass the lostConnections array as 'connections' to gh_wire(action='connect'), "
                        + "updating targetParam/sourceParam if params were renamed.";
                }

                return JsonConvert.SerializeObject(configureResponse);
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

                string source = ScriptComponents.ReadSource(component);

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

            // Pure LCS diff: params whose names appear in both lists are kept (wires preserved);
            // the rest are removed and missing target names inserted. Shared with the 'configure' path.
            var plan = Core.ParamSyncPlan.Compute(existingNames, targetNames);

            // Step 1: Remove params not kept (descending indices — shift-safe). Capture wires first.
            foreach (int i in plan.RemoveExistingIndicesDescending)
            {
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

            // After removal, the kept custom params (past skipCount) are the LCS elements in order.
            // Step 2: Insert new params at their target positions. Insertions are ascending, so
            // applying them left-to-right makes actualIdx = TargetIndex + skipCount land each
            // correctly (earlier insertions account for the later ones automatically).
            foreach (var insertion in plan.Insertions)
            {
                int actualIdx = insertion.TargetIndex + skipCount;
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

                newParam.Name = insertion.Name;
                newParam.NickName = insertion.Name;

                DebugLog.Debug($"[SyncParamSide] Inserting {side} '{insertion.Name}' at index {actualIdx}");
                if (side == GH_ParameterSide.Input)
                    ghComponent.Params.RegisterInputParam(newParam, actualIdx);
                else
                    ghComponent.Params.RegisterOutputParam(newParam, actualIdx);
            }
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

        /// <summary>
        /// Write <paramref name="finalSource"/> to a script component through
        /// <see cref="ScriptSourceWriter"/>'s probe cascade, logging the probe trace and throwing
        /// on failure so each call site's existing error reporting carries the actionable message.
        /// </summary>
        /// <remarks>
        /// <paramref name="finalSource"/> must already have been through
        /// <see cref="PreserveLanguageDirective"/>: the write replaces the whole body, so a source
        /// missing its language directive strips the component's language and makes it fail at
        /// solve time with "Can not determine input code language".
        /// </remarks>
        private static void WriteScriptSource(IGH_DocumentObject component, string finalSource)
        {
            var result = ScriptSourceWriter.Write(component, finalSource);

            foreach (var probe in result.Probes)
                DebugLog.Debug($"WriteScriptSource: {probe}");

            if (!result.Success)
                throw new InvalidOperationException(result.Error);

            DebugLog.Info($"WriteScriptSource: source set on {component.GetType().Name} via {result.Method}");
        }

        /// <summary>
        /// Preserve the script component's Rhino 8 language directive (e.g. "#! python 3",
        /// "// #! csharp") when <paramref name="code"/> omits it. SetSource() replaces the whole
        /// body, so a directive-less body would otherwise strip the component's language and
        /// cause "Can not determine input code language" at solve time. A directive already
        /// present in <paramref name="code"/> is respected as-is.
        /// </summary>
        private string PreserveLanguageDirective(IGH_DocumentObject component, string code)
            => ScriptDirective.Preserve(ScriptComponents.ReadSource(component), code);

        /// <summary>
        /// Configure-path parameter sync. Reshapes only the <em>provided</em> sides (a null def list
        /// means "side omitted — leave it and its wires untouched"; an empty list means "clear that
        /// side") to match the given defs <em>by name</em>, routing through the same LCS-based
        /// <see cref="SyncParamSide"/> that <c>set</c> uses. Params whose names are unchanged keep
        /// their wires; wires on removed params are captured into <paramref name="lostConnections"/>
        /// in the gh_wire(action='connect')-ready shape. This replaces the previous
        /// unregister-everything rebuild that destroyed all wires (GHS-3W9N).
        ///
        /// <para>Type hints and access are NOT set here — <see cref="ApplyParamHints"/> applies them
        /// after <c>SetSource</c>, which can reset type hints.</para>
        /// </summary>
        private void SyncParamsForConfigure(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp,
            List<ScriptParamDef> inputDefs, List<ScriptParamDef> outputDefs, out List<object> lostConnections)
        {
            lostConnections = new List<object>();

            if (inputDefs != null)
            {
                var targetNames = inputDefs.Select(d => d.Name).ToList();
                SyncParamSide(ghComponent, varParamComp, GH_ParameterSide.Input, targetNames, 0, out var inputLost);
                lostConnections.AddRange(inputLost);
            }

            if (outputDefs != null)
            {
                // skipCount=1: preserve the built-in index-0 'out' output (mirrors the 'set' path).
                var targetNames = outputDefs.Select(d => d.Name).ToList();
                SyncParamSide(ghComponent, varParamComp, GH_ParameterSide.Output, targetNames, 1, out var outputLost);
                lostConnections.AddRange(outputLost);
            }

            varParamComp.VariableParameterMaintenance();
            DebugLog.Info($"SyncParamsForConfigure complete: {ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs, {lostConnections.Count} wires lost");
        }

        /// <summary>
        /// Apply type hints (both sides) and access modes (inputs only) from the provided defs to the
        /// component's params by position. A null def list means the side was omitted — leave it
        /// alone (partial-update contract). After <see cref="SyncParamsForConfigure"/> the params are
        /// in def order, so def[i] maps to input[i] / output[i+1] (index 0 is the built-in 'out').
        /// Called after <c>SetSource</c>, which can reset type hints.
        /// </summary>
        private void ApplyParamHints(IGH_Component ghComponent, List<ScriptParamDef> inputDefs, List<ScriptParamDef> outputDefs)
        {
            if (inputDefs != null)
            {
                for (int i = 0; i < inputDefs.Count && i < ghComponent.Params.Input.Count; i++)
                {
                    var def = inputDefs[i];
                    var param = ghComponent.Params.Input[i];
                    if (!string.IsNullOrEmpty(def.Type))
                        SetParameterTypeHint(param, def.Type);
                    if (!string.IsNullOrEmpty(def.Access))
                    {
                        switch (def.Access.ToLowerInvariant())
                        {
                            case "list": param.Access = GH_ParamAccess.list; break;
                            case "tree": param.Access = GH_ParamAccess.tree; break;
                            default: param.Access = GH_ParamAccess.item; break;
                        }
                    }
                }
            }

            if (outputDefs != null)
            {
                // Output params start at index 1 (index 0 is the built-in 'out').
                for (int i = 0; i < outputDefs.Count; i++)
                {
                    int paramIndex = i + 1;
                    if (paramIndex < ghComponent.Params.Output.Count)
                    {
                        var def = outputDefs[i];
                        var param = ghComponent.Params.Output[paramIndex];
                        if (!string.IsNullOrEmpty(def.Type))
                        {
                            DebugLog.Debug($"ApplyParamHints: Output '{param.Name}' -> type '{def.Type}'");
                            SetParameterTypeHint(param, def.Type);
                        }
                    }
                }
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
