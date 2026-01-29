using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Script component operations (C# Script, Python Script)
    /// </summary>
    [McpServerToolType]
    public class ScriptTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public ScriptTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Set the source code for a C# or Python script component")]
        public string GhSetScriptCode(
            [Description("Script component GUID")] string id,
            [Description("Source code to set")] string code)
        {
            _server?.RecordCommand("set_script_code");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Check if this is a script component
                if (!IsScriptComponent(component))
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"Component '{component.NickName}' is not a script component. Expected C# Script, Python 3 Script, or IronPython 2 Script.",
                        componentType = component.GetType().Name
                    });
                }

                try
                {
                    dynamic scriptComp = component;
                    scriptComp.SetSource(code);

                    // Try to sync parameters from script
                    try
                    {
                        scriptComp.SetParametersFromScript();
                    }
                    catch (Exception paramEx)
                    {
                        DebugLog.Debug($"SetParametersFromScript not available or failed: {paramEx.Message}");
                    }

                    // Expire solution
                    if (component is IGH_ActiveObject activeObj)
                    {
                        activeObj.ExpireSolution(true);
                    }
                    doc.NewSolution(false);
                }
                catch (Exception ex)
                {
                    DebugLog.Error($"SetScriptCode failed: {ex.Message}");
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Failed to set script code. The component may not support code modification.",
                        componentType = component.GetType().Name,
                        hint = "Use this tool only with C# Script, Python 3 Script, or IronPython 2 Script components."
                    });
                }

                // Get parameter info
                var inputs = new List<object>();
                var outputs = new List<object>();

                if (component is IGH_Component ghComp)
                {
                    foreach (var param in ghComp.Params.Input)
                    {
                        inputs.Add(new { name = param.Name, nickname = param.NickName, type = param.TypeName });
                    }
                    foreach (var param in ghComp.Params.Output)
                    {
                        outputs.Add(new { name = param.Name, nickname = param.NickName, type = param.TypeName });
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    codeSet = true,
                    inputs,
                    outputs
                });
            });
        }

        [McpServerTool, Description("Configure a script component with inputs, outputs, and source code")]
        public string GhConfigureScript(
            [Description("Script component GUID")] string id,
            [Description("JSON array of input definitions [{name, type, access}]")] string inputs,
            [Description("JSON array of output definitions [{name, type}]")] string outputs,
            [Description("Full source code including RunScript method")] string fullSource)
        {
            _server?.RecordCommand("configure_script_component");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(component is IGH_Component ghComponent))
                    return ToolHelpers.ErrorResponse("Not an IGH_Component");

                // Parse input/output definitions
                var inputDefs = ParseParamDefs(inputs);
                var outputDefs = ParseParamDefs(outputs);

                bool configured = false;
                string message = "";

                try
                {
                    dynamic scriptComp = component;

                    // PARAMETERS-FIRST approach: Configure parameters using VariableParameterComponent,
                    // THEN set the source code. This ensures parameter names match the source.
                    if (component is IGH_VariableParameterComponent varParamComp)
                    {
                        DebugLog.Info($"Configuring parameters first for {ghComponent.NickName}");
                        configured = ConfigureViaVariableParams(ghComponent, varParamComp, inputDefs, outputDefs);

                        if (configured)
                        {
                            message = $"Parameters configured ({ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs)";
                            DebugLog.Info(message);

                            // Now set the source code after parameters are configured
                            if (!string.IsNullOrEmpty(fullSource))
                            {
                                try
                                {
                                    scriptComp.SetSource(fullSource);
                                    message += ", source set";
                                    DebugLog.Info("Source code set successfully");
                                }
                                catch (Exception srcEx)
                                {
                                    DebugLog.Warn($"Failed to set source: {srcEx.Message}");
                                    message += $", source failed: {srcEx.Message}";
                                }
                            }

                            ghComponent.ExpireSolution(true);
                            doc.NewSolution(false);
                        }
                        else
                        {
                            DebugLog.Warn("VariableParameterComponent configuration failed");
                        }
                    }

                    // Fallback: Try source-first approach if VariableParameterComponent didn't work
                    if (!configured && !string.IsNullOrEmpty(fullSource))
                    {
                        DebugLog.Info("Trying source-first approach as fallback");
                        try
                        {
                            scriptComp.SetSource(fullSource);
                            DebugLog.Info($"SetSource completed for {ghComponent.NickName}");

                            // Try to sync parameters from the source
                            try
                            {
                                scriptComp.SetParametersFromScript();
                                DebugLog.Info("SetParametersFromScript completed");
                            }
                            catch (Exception ex1)
                            {
                                DebugLog.Warn($"SetParametersFromScript failed: {ex1.Message}");
                                try
                                {
                                    scriptComp.SyncParameters();
                                    DebugLog.Info("SyncParameters completed");
                                }
                                catch (Exception ex2)
                                {
                                    DebugLog.Warn($"SyncParameters failed: {ex2.Message}");
                                }
                            }

                            if (component is IGH_VariableParameterComponent vpComp2)
                            {
                                try
                                {
                                    vpComp2.VariableParameterMaintenance();
                                    DebugLog.Debug("VariableParameterMaintenance completed");
                                }
                                catch (Exception ex3)
                                {
                                    DebugLog.Warn($"VariableParameterMaintenance failed: {ex3.Message}");
                                }
                            }

                            ghComponent.ExpireSolution(true);
                            doc.NewSolution(false);

                            // Verify parameters match requested names
                            bool paramsMatch = VerifyParameterNames(ghComponent, inputDefs, outputDefs);
                            if (paramsMatch)
                            {
                                configured = true;
                                message = $"Configured via source-first ({ghComponent.Params.Input.Count} inputs, {ghComponent.Params.Output.Count} outputs)";
                            }
                            else
                            {
                                message = "Source set but parameter names don't match - check that RunScript signature matches inputs/outputs definitions";
                                DebugLog.Warn(message);
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Error($"Source-first failed: {ex.Message}");
                            message = $"Source-first failed: {ex.Message}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Configuration failed: {ex.Message}" });
                }

                // Get final parameter state
                var finalInputs = new List<object>();
                var finalOutputs = new List<object>();

                foreach (var param in ghComponent.Params.Input)
                {
                    finalInputs.Add(new
                    {
                        name = param.Name,
                        nickname = param.NickName,
                        type = param.TypeName,
                        access = param.Access.ToString()
                    });
                }

                foreach (var param in ghComponent.Params.Output)
                {
                    finalOutputs.Add(new
                    {
                        name = param.Name,
                        nickname = param.NickName,
                        type = param.TypeName
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = configured,
                    id = component.InstanceGuid.ToString(),
                    configured,
                    message = configured ? message : "Configuration failed - parameters may need manual setup",
                    inputs = finalInputs,
                    outputs = finalOutputs
                });
            });
        }

        /// <summary>
        /// Verify that the component's parameters match the requested definitions by name
        /// </summary>
        private bool VerifyParameterNames(IGH_Component comp, List<ParamDef> inputDefs, List<ParamDef> outputDefs)
        {
            // Check inputs match (skip 'out' which is always first output)
            for (int i = 0; i < inputDefs.Count; i++)
            {
                if (i >= comp.Params.Input.Count)
                    return false;

                var param = comp.Params.Input[i];
                var def = inputDefs[i];

                // Check if name matches (case-insensitive)
                if (!string.Equals(param.Name, def.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(param.NickName, def.Name, StringComparison.OrdinalIgnoreCase))
                {
                    DebugLog.Debug($"Input mismatch at {i}: expected '{def.Name}', got '{param.Name}' / '{param.NickName}'");
                    return false;
                }
            }

            // Check outputs match (skip 'out' which is index 0)
            for (int i = 0; i < outputDefs.Count; i++)
            {
                // Output index 0 is usually 'out', so user-defined outputs start at index 1
                int paramIndex = i + 1;
                if (paramIndex >= comp.Params.Output.Count)
                    return false;

                var param = comp.Params.Output[paramIndex];
                var def = outputDefs[i];

                if (!string.Equals(param.Name, def.Name, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(param.NickName, def.Name, StringComparison.OrdinalIgnoreCase))
                {
                    DebugLog.Debug($"Output mismatch at {i}: expected '{def.Name}', got '{param.Name}' / '{param.NickName}'");
                    return false;
                }
            }

            return true;
        }

        [McpServerTool, Description("Get the source code from a C# or Python script component")]
        public string GhGetScriptCode(
            [Description("Script component GUID")] string id)
        {
            _server?.RecordCommand("get_script_code");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                try
                {
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
                        nickname = component.NickName,
                        source = source,
                        sourceLength = source.Length
                    });
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = $"Failed to get script code: {ex.Message}",
                        componentType = component.GetType().Name
                    });
                }
            });
        }

        /// <summary>
        /// Try to get source code from a script component using various methods
        /// </summary>
        private string TryGetScriptSource(IGH_DocumentObject component)
        {
            // Method 1: Try TryGetSource method (Rhino 8 script components)
            var tryGetSourceMethod = component.GetType().GetMethod("TryGetSource");
            if (tryGetSourceMethod != null)
            {
                try
                {
                    var parameters = new object[] { null };
                    var result = tryGetSourceMethod.Invoke(component, parameters);
                    if (result is bool success && success)
                    {
                        return parameters[0] as string;
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Debug($"TryGetSource failed: {ex.Message}");
                }
            }

            // Method 2: Try Source property via dynamic
            try
            {
                dynamic scriptComp = component;
                return scriptComp.Source;
            }
            catch { }

            // Method 3: Try ScriptSource property
            try
            {
                dynamic scriptComp = component;
                return scriptComp.ScriptSource;
            }
            catch { }

            // Method 4: Try Code property
            try
            {
                dynamic scriptComp = component;
                return scriptComp.Code;
            }
            catch { }

            // Method 5: Try via reflection
            var sourceProperty = component.GetType().GetProperty("Source")
                ?? component.GetType().GetProperty("ScriptSource")
                ?? component.GetType().GetProperty("Code");

            if (sourceProperty != null)
            {
                try
                {
                    return sourceProperty.GetValue(component) as string;
                }
                catch { }
            }

            return null;
        }

        [McpServerTool, Description("Get detailed information about a script component including source code, parameters, and type hints")]
        public string GhGetScriptInfo(
            [Description("Script component GUID")] string id)
        {
            _server?.RecordCommand("get_script_info");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(component is IGH_Component ghComponent))
                    return ToolHelpers.ErrorResponse("Not a component");

                // Get source code
                string source = TryGetScriptSource(component);

                // Get input parameters with type hints
                var inputs = new List<object>();
                foreach (var param in ghComponent.Params.Input)
                {
                    var inputInfo = new Dictionary<string, object>
                    {
                        ["name"] = param.Name,
                        ["nickname"] = param.NickName,
                        ["description"] = param.Description,
                        ["type"] = param.TypeName,
                        ["access"] = param.Access.ToString(),
                        ["optional"] = param.Optional,
                        ["sourceCount"] = param.SourceCount
                    };

                    // Try to get type hint
                    var typeHint = GetParameterTypeHint(param);
                    if (typeHint != null)
                    {
                        inputInfo["typeHint"] = typeHint;
                    }

                    inputs.Add(inputInfo);
                }

                // Get output parameters with type hints
                var outputs = new List<object>();
                foreach (var param in ghComponent.Params.Output)
                {
                    var outputInfo = new Dictionary<string, object>
                    {
                        ["name"] = param.Name,
                        ["nickname"] = param.NickName,
                        ["description"] = param.Description,
                        ["type"] = param.TypeName,
                        ["recipientCount"] = param.Recipients.Count
                    };

                    // Try to get type hint
                    var typeHint = GetParameterTypeHint(param);
                    if (typeHint != null)
                    {
                        outputInfo["typeHint"] = typeHint;
                    }

                    outputs.Add(outputInfo);
                }

                // Get runtime messages
                var messages = new List<object>();
                if (ghComponent.RuntimeMessageLevel != GH_RuntimeMessageLevel.Blank)
                {
                    foreach (var msg in ghComponent.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                        messages.Add(new { level = "error", message = msg });
                    foreach (var msg in ghComponent.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
                        messages.Add(new { level = "warning", message = msg });
                    foreach (var msg in ghComponent.RuntimeMessages(GH_RuntimeMessageLevel.Remark))
                        messages.Add(new { level = "remark", message = msg });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    name = component.Name,
                    nickname = component.NickName,
                    componentType = component.GetType().Name,
                    hasSource = source != null,
                    source = source,
                    sourceLength = source?.Length ?? 0,
                    inputs,
                    outputs,
                    runtimeMessageLevel = ghComponent.RuntimeMessageLevel.ToString(),
                    messages
                });
            });
        }

        /// <summary>
        /// Get the type hint from a script parameter (if available)
        /// </summary>
        private string GetParameterTypeHint(IGH_Param param)
        {
            if (param == null) return null;

            try
            {
                // Try to get IScriptParameter.Converter
                var scriptParamInterface = param.GetType().GetInterfaces()
                    .FirstOrDefault(i => i.Name == "IScriptParameter" || i.FullName?.Contains("IScriptParameter") == true);

                if (scriptParamInterface != null)
                {
                    var converterProp = scriptParamInterface.GetProperty("Converter");
                    if (converterProp != null)
                    {
                        var converter = converterProp.GetValue(param);
                        if (converter != null)
                        {
                            // Get the target type from the converter
                            var targetTypeProp = converter.GetType().GetProperty("TargetType")
                                ?? converter.GetType().GetProperty("Type");
                            if (targetTypeProp != null)
                            {
                                var targetType = targetTypeProp.GetValue(converter) as Type;
                                if (targetType != null)
                                {
                                    return targetType.Name;
                                }
                            }

                            // Fallback: use converter's ToString or type name
                            return converter.GetType().Name;
                        }
                    }
                }

                // Try TypeHint property directly
                var typeHintProp = param.GetType().GetProperty("TypeHint");
                if (typeHintProp != null)
                {
                    var typeHint = typeHintProp.GetValue(param);
                    if (typeHint != null)
                    {
                        return typeHint.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Debug($"GetParameterTypeHint failed for '{param.Name}': {ex.Message}");
            }

            return null;
        }

        #region Helper Methods

        /// <summary>
        /// Check if a component is a script component (C#, Python 3, IronPython 2)
        /// </summary>
        private bool IsScriptComponent(IGH_DocumentObject component)
        {
            if (component == null) return false;

            var typeName = component.GetType().Name;

            // Check for common script component type names
            if (typeName.Contains("Script") ||
                typeName.Contains("Python") ||
                typeName.Contains("CSharp"))
            {
                return true;
            }

            // Check if component has a SetSource method
            var setSourceMethod = component.GetType().GetMethod("SetSource");
            return setSourceMethod != null;
        }

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
                DebugLog.Error($"Error parsing param defs: {ex.Message}");
                return new List<ParamDef>();
            }
        }

        private void ApplyAccessModes(IGH_Component ghComponent, List<ParamDef> inputDefs)
        {
            for (int i = 0; i < inputDefs.Count && i < ghComponent.Params.Input.Count; i++)
            {
                var def = inputDefs[i];
                var param = ghComponent.Params.Input[i];

                // Apply type hint
                if (!string.IsNullOrEmpty(def.Type))
                {
                    SetParameterTypeHint(param, def.Type);
                }

                // Apply access mode
                if (!string.IsNullOrEmpty(def.Access))
                {
                    var oldAccess = param.Access;
                    switch (def.Access.ToLowerInvariant())
                    {
                        case "list":
                            param.Access = GH_ParamAccess.list;
                            break;
                        case "tree":
                            param.Access = GH_ParamAccess.tree;
                            break;
                        case "item":
                            param.Access = GH_ParamAccess.item;
                            break;
                    }
                    if (param.Access != oldAccess)
                    {
                        DebugLog.Debug($"Changed access for '{param.Name}' from {oldAccess} to {param.Access}");
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
                        var newParam = varParamComp.CreateParameter(GH_ParameterSide.Input, ghComponent.Params.Input.Count);
                        if (newParam != null)
                        {
                            newParam.Name = def.Name;
                            newParam.NickName = def.Name;

                            // Set type hint BEFORE registering
                            if (!string.IsNullOrEmpty(def.Type))
                            {
                                SetParameterTypeHint(newParam, def.Type);
                            }

                            // Set access mode
                            if (!string.IsNullOrEmpty(def.Access))
                            {
                                switch (def.Access.ToLowerInvariant())
                                {
                                    case "list":
                                        newParam.Access = GH_ParamAccess.list;
                                        break;
                                    case "tree":
                                        newParam.Access = GH_ParamAccess.tree;
                                        break;
                                    default:
                                        newParam.Access = GH_ParamAccess.item;
                                        break;
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

        private class ParamDef
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Access { get; set; }
        }

        /// <summary>
        /// Set the TypeHint on a script parameter (Rhino 8 only)
        /// Rhino 8 uses TypeHints.Select(Type) to get a converter, then assigns it via IScriptParameter.Converter
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

            try
            {
                // Step 1: Get TypeHints collection and select the converter
                var typeHintsProp = param.GetType().GetProperty("TypeHints");
                if (typeHintsProp == null)
                {
                    DebugLog.Warn($"Parameter '{param.Name}' has no TypeHints property");
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
                        DebugLog.Debug($"Set '{param.Name}' type to {targetType.Name} via IScriptParameter.Converter");
                        return;
                    }
                }

                DebugLog.Warn($"Could not assign converter for '{param.Name}' - IScriptParameter.Converter not found");
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
            switch (normalized)
            {
                case "int":
                case "integer":
                    return typeof(int);
                case "double":
                case "number":
                case "float":
                    return typeof(double);
                case "bool":
                case "boolean":
                    return typeof(bool);
                case "string":
                case "text":
                    return typeof(string);
                case "point":
                case "point3d":
                    return typeof(Rhino.Geometry.Point3d);
                case "vector":
                case "vector3d":
                    return typeof(Rhino.Geometry.Vector3d);
                case "plane":
                    return typeof(Rhino.Geometry.Plane);
                case "mesh":
                    return typeof(Rhino.Geometry.Mesh);
                case "brep":
                    return typeof(Rhino.Geometry.Brep);
                case "curve":
                    return typeof(Rhino.Geometry.Curve);
                case "surface":
                    return typeof(Rhino.Geometry.Surface);
                case "line":
                    return typeof(Rhino.Geometry.Line);
                case "box":
                    return typeof(Rhino.Geometry.Box);
                case "circle":
                    return typeof(Rhino.Geometry.Circle);
                case "arc":
                    return typeof(Rhino.Geometry.Arc);
                case "transform":
                    return typeof(Rhino.Geometry.Transform);
                case "color":
                    return typeof(System.Drawing.Color);
                case "guid":
                    return typeof(Guid);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Map type name to C# type string for source code generation
        /// </summary>
        public static string MapTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "object";

            switch (typeName.ToLowerInvariant())
            {
                case "int":
                case "integer":
                    return "int";
                case "double":
                case "number":
                case "float":
                    return "double";
                case "bool":
                case "boolean":
                    return "bool";
                case "string":
                case "text":
                    return "string";
                case "point":
                case "point3d":
                    return "Point3d";
                case "vector":
                case "vector3d":
                    return "Vector3d";
                case "plane":
                    return "Plane";
                case "curve":
                    return "Curve";
                case "line":
                    return "Line";
                case "mesh":
                    return "Mesh";
                case "brep":
                    return "Brep";
                case "surface":
                    return "Surface";
                case "box":
                    return "Box";
                case "list":
                    return "List<object>";
                default:
                    return typeName;
            }
        }

        #endregion
    }
}
