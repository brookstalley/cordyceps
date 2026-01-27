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
        public string SetScriptCode(
            [Description("Script component GUID")] string id,
            [Description("Source code to set")] string code)
        {
            _server?.RecordCommand("set_script_code");
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

                bool codeSet = false;

                try
                {
                    dynamic scriptComp = component;

                    // Try SetSource method (Rhino 8)
                    try
                    {
                        scriptComp.SetSource(code);
                        codeSet = true;
                    }
                    catch
                    {
                        // Try Source property
                        try
                        {
                            scriptComp.Source = code;
                            codeSet = true;
                        }
                        catch
                        {
                            // Try reflection
                            var setSourceMethod = component.GetType().GetMethod("SetSource", new[] { typeof(string) });
                            if (setSourceMethod != null)
                            {
                                setSourceMethod.Invoke(component, new object[] { code });
                                codeSet = true;
                            }
                        }
                    }

                    if (codeSet)
                    {
                        // Try to sync parameters
                        try
                        {
                            scriptComp.SetParametersFromScript();
                        }
                        catch { }

                        // Expire solution
                        if (component is IGH_ActiveObject activeObj)
                        {
                            activeObj.ExpireSolution(true);
                        }
                        doc.NewSolution(false);
                    }
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Failed to set code: {ex.Message}" });
                }

                if (!codeSet)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Could not set code for this component type" });
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
        public string ConfigureScriptComponent(
            [Description("Script component GUID")] string id,
            [Description("JSON array of input definitions [{name, type, access}]")] string inputs,
            [Description("JSON array of output definitions [{name, type}]")] string outputs,
            [Description("Full source code including RunScript method")] string fullSource)
        {
            _server?.RecordCommand("configure_script_component");
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

                if (!(component is IGH_Component ghComponent))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Not an IGH_Component" });
                }

                // Parse input/output definitions
                var inputDefs = ParseParamDefs(inputs);
                var outputDefs = ParseParamDefs(outputs);

                bool configured = false;
                string message = "";

                try
                {
                    dynamic scriptComp = component;

                    // Source-first approach: set source, then sync parameters
                    if (!string.IsNullOrEmpty(fullSource))
                    {
                        try
                        {
                            scriptComp.SetSource(fullSource);
                            DebugLog.Info($"SetSource completed for {ghComponent.NickName}");

                            // Try multiple methods to sync parameters
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

                            if (component is IGH_VariableParameterComponent vpComp)
                            {
                                try
                                {
                                    vpComp.VariableParameterMaintenance();
                                    DebugLog.Debug("VariableParameterMaintenance completed");
                                }
                                catch (Exception ex3)
                                {
                                    DebugLog.Warn($"VariableParameterMaintenance failed: {ex3.Message}");
                                }
                            }

                            // Apply access modes
                            ApplyAccessModes(ghComponent, inputDefs);

                            ghComponent.ExpireSolution(true);
                            doc.NewSolution(false);

                            // Verify parameters were created
                            int inputCount = ghComponent.Params.Input.Count;
                            int outputCount = ghComponent.Params.Output.Count;
                            DebugLog.Info($"After sync - inputs: {inputCount}, outputs: {outputCount}");

                            // Consider configured if we have the expected parameters
                            if (inputCount >= inputDefs.Count && outputCount >= outputDefs.Count)
                            {
                                configured = true;
                                message = $"Configured via source-first ({inputCount} inputs, {outputCount} outputs)";
                            }
                            else
                            {
                                message = $"Source set but parameters not synced (got {inputCount}/{inputDefs.Count} inputs, {outputCount}/{outputDefs.Count} outputs)";
                                DebugLog.Warn(message);
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Error($"Source-first failed: {ex.Message}");
                        }
                    }

                    // Fallback: VariableParameterComponent approach
                    if (!configured && component is IGH_VariableParameterComponent varParamComp)
                    {
                        configured = ConfigureViaVariableParams(ghComponent, varParamComp, inputDefs, outputDefs);
                        if (configured)
                        {
                            message = "Configured via VariableParameterComponent";

                            if (!string.IsNullOrEmpty(fullSource))
                            {
                                try
                                {
                                    scriptComp.SetSource(fullSource);
                                    message += ", source set";
                                }
                                catch { }
                            }
                        }
                    }

                    if (configured)
                    {
                        ghComponent.ExpireSolution(true);
                        doc.NewSolution(false);
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

        #region Helper Methods

        private List<ParamDef> ParseParamDefs(string json)
        {
            var defs = new List<ParamDef>();
            if (string.IsNullOrEmpty(json)) return defs;

            try
            {
                var array = JArray.Parse(json);
                foreach (var item in array)
                {
                    defs.Add(new ParamDef
                    {
                        Name = item["name"]?.ToString() ?? "param",
                        Type = item["type"]?.ToString() ?? "object",
                        Access = item["access"]?.ToString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLog.Error($"Error parsing param defs: {ex.Message}");
            }

            return defs;
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
