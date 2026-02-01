using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
                    Example = "action='set', id='abc', code='print(x)'"
                },
                ["configure"] = new ActionInfo
                {
                    Name = "configure",
                    Description = "Configure script with inputs, outputs, and full source",
                    Required = new[] { "id" },
                    Optional = new[] { "inputs", "outputs", "code" },
                    Example = "action='configure', id='abc', inputs='[{\"name\":\"x\",\"type\":\"double\"}]', code='...'",
                    Tips = new[] { "inputs: [{name, type, access}]", "outputs: [{name, type}]", "types: int, double, bool, string, Point3d, etc." }
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
                    scriptComp.SetSource(code);

                    try { scriptComp.SetParametersFromScript(); }
                    catch { }

                    if (component is IGH_ActiveObject activeObj)
                        activeObj.ExpireSolution(true);

                    doc.NewSolution(false);

                    var inputs = new List<object>();
                    var outputs = new List<object>();

                    if (component is IGH_Component ghComp)
                    {
                        foreach (var param in ghComp.Params.Input)
                            inputs.Add(new { name = param.Name, type = param.TypeName });
                        foreach (var param in ghComp.Params.Output)
                            outputs.Add(new { name = param.Name, type = param.TypeName });
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id = component.InstanceGuid.ToString(),
                        codeSet = true,
                        inputs,
                        outputs
                    });
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
                                    scriptComp.SetSource(code);
                                    message += ", source set";
                                }
                                catch (Exception srcEx)
                                {
                                    message += $", source failed: {srcEx.Message}";
                                }
                            }

                            ghComponent.ExpireSolution(true);
                            doc.NewSolution(false);
                        }
                    }

                    if (!configured && !string.IsNullOrEmpty(code))
                    {
                        try
                        {
                            scriptComp.SetSource(code);
                            try { scriptComp.SetParametersFromScript(); } catch { }
                            try { scriptComp.SyncParameters(); } catch { }

                            if (component is IGH_VariableParameterComponent vpComp2)
                            {
                                try { vpComp2.VariableParameterMaintenance(); } catch { }
                            }

                            ghComponent.ExpireSolution(true);
                            doc.NewSolution(false);
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
                catch { }
            }

            try { dynamic sc = component; return sc.Source; } catch { }
            try { dynamic sc = component; return sc.ScriptSource; } catch { }
            try { dynamic sc = component; return sc.Code; } catch { }

            var sourceProp = component.GetType().GetProperty("Source")
                ?? component.GetType().GetProperty("ScriptSource")
                ?? component.GetType().GetProperty("Code");
            if (sourceProp != null)
            {
                try { return sourceProp.GetValue(component) as string; } catch { }
            }

            return null;
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
            catch
            {
                return new List<ParamDef>();
            }
        }

        private bool ConfigureViaVariableParams(IGH_Component ghComponent, IGH_VariableParameterComponent varParamComp, List<ParamDef> inputDefs, List<ParamDef> outputDefs)
        {
            try
            {
                while (ghComponent.Params.Input.Count > 0)
                {
                    var param = ghComponent.Params.Input[ghComponent.Params.Input.Count - 1];
                    varParamComp.CanRemoveParameter(GH_ParameterSide.Input, ghComponent.Params.Input.Count - 1);
                    ghComponent.Params.UnregisterInputParameter(param);
                }

                while (ghComponent.Params.Output.Count > 1)
                {
                    var param = ghComponent.Params.Output[ghComponent.Params.Output.Count - 1];
                    varParamComp.CanRemoveParameter(GH_ParameterSide.Output, ghComponent.Params.Output.Count - 1);
                    ghComponent.Params.UnregisterOutputParameter(param);
                }

                foreach (var def in inputDefs)
                {
                    if (varParamComp.CanInsertParameter(GH_ParameterSide.Input, ghComponent.Params.Input.Count))
                    {
                        // Create typed parameter based on the type definition
                        var newParam = CreateTypedParameter(def.Type);
                        if (newParam != null)
                        {
                            newParam.Name = def.Name;
                            newParam.NickName = def.Name;
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
                        }
                    }
                }

                foreach (var def in outputDefs)
                {
                    if (varParamComp.CanInsertParameter(GH_ParameterSide.Output, ghComponent.Params.Output.Count))
                    {
                        // Create typed parameter based on the type definition
                        var newParam = CreateTypedParameter(def.Type);
                        if (newParam != null)
                        {
                            newParam.Name = def.Name;
                            newParam.NickName = def.Name;
                            ghComponent.Params.RegisterOutputParam(newParam);
                        }
                    }
                }

                varParamComp.VariableParameterMaintenance();
                return true;
            }
            catch
            {
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
