using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Microsoft.CSharp.RuntimeBinder;
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
                        "A C# RunScript signature that drops or renames an existing port removes it and cuts its wires, so that call never selects the component by its default nickname, even a unique one: pass the id the error prints. Code that keeps or only adds ports accepts it",
                        "Use gh_wire(action='connect', connections='<lostConnections>') to restore",
                        "Script language is preserved automatically; start code with '#! python 3' or '// #! csharp' to set it explicitly",
                        "A bare unified 'Script' component (no language picked) returns a languageWarning until code starts with a directive — add '#! python 3' or '// #! csharp'",
                        "Components that expose their code as a visible input parameter can't be written directly — the error says so and nothing is changed; remove that parameter, or feed the code through it with gh_wire",
                        "The component is rebuilt from the new source before the call returns: rebuilt=true means it will run the new program on its next solve",
                        "rebuilt=false always carries the reason beside it: rebuildSkipped (nothing needed rebuilding, or the component has no rebuild hooks and recompiles on its own — the string says which) or rebuildFailed (a hook threw; the component is probably still running the old program). Read the reason, not the count",
                        "verified=true means the running program was read back and matches what was written. verified=false adds sourceDiverged and runningSource — expected for C# SDK-mode scripts, whose RunScript signature Rhino rewrites as it builds",
                        "If the program could not be read there is no verified field at all, and verificationSkipped says why — never read an absent verified as a match",
                        "Code that fails to compile still reports success here — the write and the rebuild both succeeded. Build errors surface on the component; after setting code, check gh_inspect(action='status') (it takes no id and lists every component's status and messages) or gh_inspect(action='tree', id=...) (each tree carries its owner's error or warning)",
                        "The compile happens inside this call rather than at the next solve, so the first script write of a session can take noticeably longer",
                        "One undo step: Ctrl+Z restores the previous source and ports, with their wires. A source write or port sync that fails rolls the whole call back (success=false, rolledBack=true): nothing is changed"
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
                        "Script components only: any other component is refused before anything changes. To add or remove ports on Merge, Entwine and other variable-parameter components, use gh_canvas(action='zoomable')",
                        "Partial update: omit a side (don't pass inputs/outputs) to leave it untouched; pass [] to clear that side. Passing inputs without outputs no longer wipes outputs.",
                        "Malformed inputs/outputs JSON is rejected with an error before anything changes — it is never treated as an empty array",
                        "Params matching by name keep their wires (like 'set'); renamed/removed params lose connections, reported in lostConnections — pass it to gh_wire(action='connect') to restore",
                        "A call that drops or renames an existing port (including inputs='[]' on a side that has ports) cuts its wires, so it never selects the component by its default nickname, even a unique one: pass the id the error prints. Nothing is changed when it is refused. A call that keeps or only adds ports accepts it",
                        "Script language is preserved automatically; start code with '#! python 3' or '// #! csharp' to set it explicitly",
                        "A bare unified 'Script' component (no language picked) returns a languageWarning until code starts with a directive — add '#! python 3' or '// #! csharp'",
                        "A failed source write or port sync rolls the whole call back, ports included (success=false, rolledBack=true): a configure never leaves new ports around old code. codeSet=true means the code was written",
                        "inputs/outputs on a component that cannot vary its parameters refuse the whole call before anything changes, code included: success=false, an error starting 'No configuration applied', parametersApplied=false with parametersError, codeSet=false when code was passed, and no undo. To set only the source, call again without inputs/outputs",
                        "The inputs/outputs echoed back are the component's actual surface, not what you asked for — with parametersApplied=false they are unchanged, so check that field before wiring to a port you assume exists",
                        "Components that expose their code as a visible input parameter can't be written directly — the error says so and nothing is changed",
                        "Any call that changes something — new code, new parameters, or both — rebuilds the component's program and reads it back: rebuilt/rebuildSkipped and verified/runningSource mean exactly what they do for action='set'",
                        "Changing inputs/outputs alone rebuilds too: the build reads the parameters to shape the program's ports, so a program compiled against the old ones is stale. Its verified/sourceDiverged compares the running program against the source already stored on the component",
                        "A call that changed nothing (no code and no inputs/outputs, or one refused up front) returns rebuilt=false with rebuildSkipped and verificationSkipped saying so — every response answers the rebuild question, so an absent rebuilt field is never the answer",
                        "runningSource can arrive WITHOUT a verified field: whenever nothing was verified, the program the component will run is still read and handed back unjudged. Present with verified=false it is the divergence; present with verificationSkipped it is simply what runs",
                        "One undo step: Ctrl+Z restores the source and ports, with their wires. A call that changes nothing records nothing and returns no undo"
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
                "Ids: 'id' names the script component by its GUID, an 8+ hex GUID prefix, or its unique nickname (exact, case-sensitive); a name two objects share is refused with both ids. A set or configure that removes an existing port never takes a default nickname",
                "gh_canvas(action='zoomable') also reshapes a script component's ports, and rebuilds and reports the same way configure does",
                "Undo: set and configure are one Grasshopper undo step each, so a human's Ctrl+Z reverts the whole call; the reply's undo field names the step. Agents restore state with gh_document snapshot/revert"
            }
        };

        public GhScriptTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Script operations. Actions: get|set|configure|info|help")]
        public string GhScript(
            [Description("Action to perform")] string action,
            [Description("Script component. Object: GUID, 8+ hex GUID prefix, or unique nickname (not a default nickname when set/configure removes a port)")] string id = null,
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
                // not on its own evidence that a write took effect.
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

                // The ports a C# RunScript signature declares replace the current ones by name, so
                // a signature that drops or renames a port cuts its wires. Decided before the write,
                // so a refused call changes nothing. Both read the source as it will be stored, with
                // the component's language line kept, so Python is never read as a C# signature.
                var finalSource = PreserveLanguageDirective(component, code);
                if (!ToolHelpers.TryConfirmPortRemoval(doc, id, component, SourceSyncRemoval(component, finalSource), out var refusal))
                    return ToolHelpers.ErrorResponse(refusal);

                return _context.Edit(doc, EditReplies.RecordName("gh_script", "set"), tx =>
                {
                    // One generic record, before the write, covers the source and every port the
                    // sync below removes or adds, with the wires of each: undo puts the old source
                    // and ports back, and the component rebuilds when it next solves.
                    tx.RecordGeneric(component);

                    WriteScriptSource(component, finalSource);

                    // Surgically sync params instead of SetParametersFromScript(), which rebuilds
                    // all params and destroys cluster input hooks. A sync that throws partway rolls
                    // the whole call back, source included, rather than leaving ports half synced
                    // to a source the reply calls set.
                    SyncScriptParams(component, finalSource, out var lost);
                    List<object> lostConnections = lost.Count > 0 ? lost : null;

                    // Storing source does not recompile: Rhino builds a Code from the script and
                    // keeps running that until something invalidates it, so a write alone leaves the
                    // component executing its previous program. Ask for the rebuild
                    // after the params are settled — the rebuild reads them — and verify the write
                    // landed by reading the running program back. The rebuild is not recorded:
                    // it neither expires nor solves the document, and undo restores the source it
                    // was built from.
                    var rebuildFields = ScriptComponents.RebuildAndVerify(component, finalSource);

                    // No expire or solve here: the generic record has the edit recompute the
                    // component once at commit, as ExpireSolution(false) — never
                    // ExpireSolution(true) or NewSolution, which inside a cluster make the parent
                    // recreate it, orphaning the editor and breaking its input hooks.

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
                    // here without forcing a (cluster-unsafe) recompute, so flag it up front.
                    var languageWarning = ScriptDirective.LanguageWarning(component.GetType().Name, finalSource);
                    if (languageWarning != null)
                        response["languageWarning"] = languageWarning;

                    if (lostConnections != null)
                    {
                        response["lostConnections"] = lostConnections;
                        response["reconnectHint"] = ReconnectHint;
                    }

                    return JsonConvert.SerializeObject(response);
                });
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

                // configure treats output 0 as a script's built-in 'out' and rebuilds a program, so
                // on any other component it would misplace ports; zoomable is the path for those.
                if (!ScriptComponents.IsScriptComponent(component))
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"Component '{component.NickName}' is not a script component. To add or remove ports on other variable-parameter components, use gh_canvas(action='zoomable').",
                        componentType = component.GetType().Name
                    });
                }

                // Partial-update contract: a side is "provided" only when its JSON arg is non-empty.
                // Omitted (null/"") => leave that side (and its wires) untouched; "[]" => clear it.
                // Malformed JSON is a hard error BEFORE any mutation — it must never be read as
                // "empty array", which would wipe that side's params and wires under success:true.
                if (!ScriptParamDefs.TryParse(inputs, out var inputDefs, out var inputsError))
                    return ToolHelpers.ErrorResponse($"Invalid inputs JSON: {inputsError}");
                if (!ScriptParamDefs.TryParse(outputs, out var outputDefs, out var outputsError))
                    return ToolHelpers.ErrorResponse($"Invalid outputs JSON: {outputsError}");

                // A configure that drops or renames an existing port cuts its wires, so it is decided
                // here, before the first mutation, from the same plan each route below executes.
                if (!ToolHelpers.TryConfirmPortRemoval(doc, id, component,
                        ConfigureRemoval(ghComponent, inputDefs, outputDefs,
                            string.IsNullOrEmpty(code) ? code : PreserveLanguageDirective(component, code)), out var refusal))
                    return ToolHelpers.ErrorResponse(refusal);

                return _context.Edit(doc, EditReplies.RecordName("gh_script", "configure"), tx =>
                    ApplyConfigure(tx, ghComponent, inputDefs, outputDefs, code));
            });
        }

        /// <summary>
        /// The changing half of <c>configure</c>, run inside its edit once every input has been
        /// checked. Each route records the component before its first change, so the ports, their
        /// wires and the source come back together on undo; a route that changes nothing records
        /// nothing and the call pushes no undo step. A step that throws, the source write among
        /// them, rolls the whole call back: a configure never leaves new ports around old code.
        /// </summary>
        private string ApplyConfigure(EditTransaction tx, IGH_Component ghComponent,
            List<ScriptParamDef> inputDefs, List<ScriptParamDef> outputDefs, string code)
        {
            IGH_DocumentObject component = ghComponent;
            bool configured = false;
            string message = "";
            string languageWarning = null;
            List<object> lostConnections = null;
            // Whether this call wrote source: null when no 'code' was passed, true once written. A
            // write that fails rolls the call back, so it is never false.
            bool? codeSet = null;
            // The parameter half: null when no side was asked for, true when it was applied, false
            // when the component cannot vary its parameters. A request half-dropped under
            // success:true is the failure mode this pair exists to make machine-readable.
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

            dynamic scriptComp = component;

            bool anyParamSide = inputDefs != null || outputDefs != null;
            if (component is IGH_VariableParameterComponent varParamComp && anyParamSide)
            {
                tx.RecordGeneric(component);

                // Sync only the provided sides via the same LCS helper 'set' uses, so
                // name-matched params keep their wires and removed-param wires surface in
                // lostConnections.
                SyncParamsForConfigure(ghComponent, varParamComp, inputDefs, outputDefs, out var lost);
                configured = true;
                parametersChanged = true;
                parametersApplied = true;
                message = $"Parameters configured ({ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs)";

                if (!string.IsNullOrEmpty(code))
                {
                    var finalSource = PreserveLanguageDirective(component, code);
                    WriteScriptSource(component, finalSource);
                    languageWarning = ScriptDirective.LanguageWarning(component.GetType().Name, finalSource);
                    codeSet = true;
                    writtenSource = finalSource;
                    message += ", source set";
                }

                // Re-apply type hints + access AFTER source is set, because SetSource may reset them.
                ApplyParamHints(ghComponent, inputDefs, outputDefs);

                // Call VariableParameterMaintenance to finalize parameter setup
                varParamComp.VariableParameterMaintenance();

                // Rebuild last, once the params are final: storing source doesn't recompile,
                // and the rebuild reads the params to shape the code's inputs and outputs —
                // which is why reshaping those params needs the rebuild every bit as much
                // as writing new code does, even when this call wrote none.
                rebuildFields = ScriptComponents.RebuildForChange(component, parametersChanged, writtenSource);

                if (lost.Count > 0) lostConnections = lost;
            }

            // Reaching here with a parameter side requested means the branch above declined
            // it — the component cannot vary its parameters. Record the refusal before the
            // code write, so it is reported alongside it: writing the source is not permission
            // to drop the other half of the request silently.
            if (!configured && anyParamSide)
            {
                parametersApplied = false;
                parametersError = $"'{component.GetType().Name}' cannot vary its parameters, "
                    + "so the requested inputs/outputs were not applied. The inputs/outputs "
                    + "in this response are the component's unchanged surface.";
            }

            if (!configured && !string.IsNullOrEmpty(code))
            {
                tx.RecordGeneric(component);

                var finalSource = PreserveLanguageDirective(component, code);
                try
                {
                    WriteScriptSource(component, finalSource);
                }
                catch (EditRollbackException ex) when (parametersError != null)
                {
                    // Both halves failed: the rollback's message carries the parameter refusal
                    // too, so the source error is not the only thing reported.
                    throw new EditRollbackException($"{ex.Message} Also: {parametersError}");
                }
                languageWarning = ScriptDirective.LanguageWarning(component.GetType().Name, finalSource);

                SyncScriptParams(component, finalSource, out var lost);
                if (lost.Count > 0) lostConnections = lost;

                // Not every script-component type has SyncParameters; one that lacks it fails
                // to bind before anything runs, which is no failure of this call.
                try { scriptComp.SyncParameters(); }
                catch (RuntimeBinderException syncEx) { DebugLog.Debug($"SyncParameters skipped: {syncEx.Message}"); }

                if (component is IGH_VariableParameterComponent vpComp2)
                    vpComp2.VariableParameterMaintenance();

                // See the parameter branch above: the write alone leaves the previous
                // program running, so ask for the rebuild once the params are final.
                rebuildFields = ScriptComponents.RebuildForChange(component, parametersChanged, finalSource);

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

            // The paths above rebuild whenever they changed something; this covers the ones
            // that changed nothing — a component that cannot vary its parameters, a call that
            // supplied neither side. They report why instead of returning nothing, so a caller
            // never has to read an absent rebuild field as an answer.
            if (rebuildFields == null)
                rebuildFields = ScriptComponents.RebuildForChange(component, parametersChanged, writtenSource);

            var finalInputs = new List<object>();
            var finalOutputs = new List<object>();

            foreach (var param in ghComponent.Params.Input)
                finalInputs.Add(new { name = param.Name, type = param.TypeName, access = param.Access.ToString() });
            foreach (var param in ghComponent.Params.Output)
                finalOutputs.Add(new { name = param.Name, type = param.TypeName });

            // A refused parameter change makes the whole call a failure even when the source
            // landed: a partial apply must be detectable, not reported as success:true.
            var configureResponse = new Dictionary<string, object>
            {
                ["success"] = configured && parametersApplied != false,
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
            if (parametersApplied.HasValue)
                configureResponse["parametersApplied"] = parametersApplied.Value;
            if (parametersError != null)
                configureResponse["parametersError"] = parametersError;
            if (languageWarning != null)
                configureResponse["languageWarning"] = languageWarning;

            // Same lostConnections contract as 'set': removed-param wires are returned so the
            // caller can restore them with gh_wire(action='connect').
            if (lostConnections != null)
            {
                configureResponse["lostConnections"] = lostConnections;
                configureResponse["reconnectHint"] = ReconnectHint;
            }

            return JsonConvert.SerializeObject(configureResponse);
        }

        private const string ReconnectHint = "These connections were lost due to parameter changes. "
            + "To restore, pass the lostConnections array as 'connections' to gh_wire(action='connect'), "
            + "updating targetParam/sourceParam if params were renamed.";

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
        /// The existing ports <see cref="SyncScriptParams"/> would remove when syncing to
        /// <paramref name="code"/>, computed without changing anything. Mirrors its guards: only a
        /// variable-parameter component whose ports the code declares is synced, and the built-in
        /// leading output is never touched.
        /// </summary>
        private static PortRemoval SourceSyncRemoval(IGH_DocumentObject component, string code)
        {
            if (!(component is IGH_Component ghComponent) || !(component is IGH_VariableParameterComponent))
                return PortRemoval.None;

            return PortRemoval.ForSourceSync(component.GetType().Name, code,
                ghComponent.Params.Input.Select(p => p.NickName).ToList(),
                ghComponent.Params.Output.Skip(1).Select(p => p.NickName).ToList());
        }

        /// <summary>
        /// The existing ports a <c>configure</c> call would remove, computed without changing
        /// anything, for whichever route the call takes: the parameter route syncs the provided
        /// sides to the defs' names (<see cref="SyncParamsForConfigure"/>); the code route, taken
        /// when no side applies, syncs to the code's signature (<see cref="SyncScriptParams"/>).
        /// </summary>
        private static PortRemoval ConfigureRemoval(IGH_Component ghComponent,
            List<ScriptParamDef> inputDefs, List<ScriptParamDef> outputDefs, string code)
        {
            bool anyParamSide = inputDefs != null || outputDefs != null;
            if (ghComponent is IGH_VariableParameterComponent && anyParamSide)
            {
                return PortRemoval.ForSync(
                    ghComponent.Params.Input.Select(p => p.NickName).ToList(),
                    inputDefs?.Select(d => d.Name).ToList(),
                    ghComponent.Params.Output.Skip(1).Select(p => p.NickName).ToList(),
                    outputDefs?.Select(d => d.Name).ToList());
            }

            return string.IsNullOrEmpty(code) ? PortRemoval.None : SourceSyncRemoval(ghComponent, code);
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

            var parsed = ScriptSignature.Parse(component.GetType().Name, code);
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
        /// the existing and target lists keep their connections intact. Runs inside an edit, after
        /// the component's generic record, which brings back the ports it removes and their wires.
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
            // correctly (earlier insertions account for the later ones automatically). A port the
            // component will not take stops the sync and rolls the call back: skipping it would
            // shift every later port off its target index and leave a surface the source does
            // not declare, under a reply that calls it set.
            var sideName = side.ToString().ToLowerInvariant();
            foreach (var insertion in plan.Insertions)
            {
                int actualIdx = insertion.TargetIndex + skipCount;
                if (!varParamComp.CanInsertParameter(side, actualIdx))
                    throw new EditRollbackException($"The component refused a new {sideName} parameter '{insertion.Name}' at index {actualIdx}");

                var newParam = varParamComp.CreateParameter(side, actualIdx);
                if (newParam == null)
                    throw new EditRollbackException($"The component could not create the {sideName} parameter '{insertion.Name}' at index {actualIdx}");

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
        /// Write <paramref name="finalSource"/> to a script component through
        /// <see cref="ScriptSourceWriter"/>'s probe cascade, logging the probe trace. Called inside
        /// an edit, after the component is recorded: a failed write throws
        /// <see cref="EditRollbackException"/> with the writer's actionable message, so the call is
        /// rolled back and answered with it. The writer reports a failed host call as a failure
        /// rather than throwing, and that call may have changed the component before it failed.
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
                throw new EditRollbackException($"Failed to set script code: {result.Error}");

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
        /// in the gh_wire(action='connect')-ready shape, instead of unregistering every
        /// param and losing every wire.
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
            // The reflection failures a TypeHints API of another shape produces. A missing hint
            // is logged and the call goes on, as for an unknown type name; anything else
            // propagates and rolls the edit back.
            catch (Exception ex) when (ex is TargetInvocationException || ex is TargetException
                || ex is ArgumentException || ex is AmbiguousMatchException
                || ex is MethodAccessException || ex is TargetParameterCountException)
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
