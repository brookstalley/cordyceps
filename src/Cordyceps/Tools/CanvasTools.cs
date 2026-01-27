using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Newtonsoft.Json;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Canvas/component operations
    /// </summary>
    [McpServerToolType]
    public class CanvasTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public CanvasTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Add a component to the Grasshopper canvas by name or GUID")]
        public string AddComponent(
            [Description("Component type name (e.g., 'Circle', 'Addition') or GUID")] string type,
            [Description("X position on canvas")] double x,
            [Description("Y position on canvas")] double y)
        {
            _server?.RecordCommand("add_component");
            Core.DebugLog.Info($"AddComponent called: type='{type}', x={x}, y={y}");

            return _context.ExecuteOnUiThread(() =>
            {
                try
                {
                    var doc = _context.GetActiveDocument();
                    if (doc == null)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                    }

                    Core.DebugLog.Debug($"Creating component: {type}");

                    // Create the component
                    var component = ComponentRegistry.CreateComponent(type);
                    if (component == null)
                    {
                        Core.DebugLog.Warn($"Unknown component type: {type}");
                        return JsonConvert.SerializeObject(new { success = false, error = $"Unknown component type: {type}" });
                    }

                    Core.DebugLog.Debug($"Component created: {component.GetType().Name}");

                    // Ensure attributes exist (some components like GH_NumberSlider don't auto-create them)
                    if (component.Attributes == null)
                    {
                        Core.DebugLog.Debug("Creating attributes for component");
                        component.CreateAttributes();
                    }

                    // Set position
                    component.Attributes.Pivot = new PointF((float)x, (float)y);

                    // Add to document
                    doc.AddObject(component, false);
                    doc.NewSolution(true);

                    Core.DebugLog.Info($"Component added: {component.Name} ({component.InstanceGuid})");

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id = component.InstanceGuid.ToString(),
                        type = component.Name,
                        x = component.Attributes.Pivot.X,
                        y = component.Attributes.Pivot.Y
                    });
                }
                catch (Exception ex)
                {
                    Core.DebugLog.Error($"AddComponent failed: {ex.Message}");
                    Core.DebugLog.Error($"Stack: {ex.StackTrace}");
                    throw;
                }
            });
        }

        [McpServerTool, Description("Delete a component from the canvas by its ID")]
        public string DeleteComponent(
            [Description("Component GUID")] string id)
        {
            _server?.RecordCommand("delete_component");
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

                doc.RemoveObject(component, true);
                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new { success = true, deleted = id });
            });
        }

        [McpServerTool, Description("Move a component to a new position on the canvas")]
        public string MoveComponent(
            [Description("Component GUID")] string id,
            [Description("New X position")] double x,
            [Description("New Y position")] double y)
        {
            _server?.RecordCommand("move_component");
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

                component.Attributes.Pivot = new PointF((float)x, (float)y);
                component.Attributes.ExpireLayout();
                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = id,
                    x = x,
                    y = y
                });
            });
        }

        [McpServerTool, Description("Get detailed information about a component including its inputs and outputs")]
        public string GetComponentInfo(
            [Description("Component GUID")] string id)
        {
            _server?.RecordCommand("get_component_info");
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

                var obj = doc.FindObject(guid, true);
                if (obj == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Component not found: {id}" });
                }

                var info = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["id"] = obj.InstanceGuid.ToString(),
                    ["name"] = obj.Name,
                    ["nickname"] = obj.NickName,
                    ["type"] = obj.GetType().Name,
                    ["x"] = obj.Attributes.Pivot.X,
                    ["y"] = obj.Attributes.Pivot.Y
                };

                // If it's a component with parameters, include input/output info
                if (obj is IGH_Component comp)
                {
                    info["category"] = comp.Category;
                    info["subcategory"] = comp.SubCategory;

                    var inputs = new List<object>();
                    foreach (var param in comp.Params.Input)
                    {
                        inputs.Add(new
                        {
                            name = param.Name,
                            nickname = param.NickName,
                            type = param.TypeName,
                            sourceCount = param.SourceCount,
                            optional = param.Optional
                        });
                    }
                    info["inputs"] = inputs;

                    var outputs = new List<object>();
                    foreach (var param in comp.Params.Output)
                    {
                        outputs.Add(new
                        {
                            name = param.Name,
                            nickname = param.NickName,
                            type = param.TypeName,
                            recipientCount = param.Recipients.Count
                        });
                    }
                    info["outputs"] = outputs;

                    // Runtime status
                    info["runtimeMessageLevel"] = comp.RuntimeMessageLevel.ToString();
                }

                // If it's a parameter, include value info
                if (obj is IGH_Param param2)
                {
                    info["dataCount"] = param2.VolatileDataCount;
                    info["sourceCount"] = param2.SourceCount;
                    info["recipientCount"] = param2.Recipients.Count;
                }

                return JsonConvert.SerializeObject(info);
            });
        }

        [McpServerTool, Description("Get all components currently on the Grasshopper canvas")]
        public string GetAllComponents()
        {
            _server?.RecordCommand("get_all_components");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                var components = new List<object>();
                foreach (var obj in doc.Objects)
                {
                    var compInfo = new Dictionary<string, object>
                    {
                        ["id"] = obj.InstanceGuid.ToString(),
                        ["name"] = obj.Name,
                        ["nickname"] = obj.NickName,
                        ["type"] = obj.GetType().Name,
                        ["x"] = obj.Attributes.Pivot.X,
                        ["y"] = obj.Attributes.Pivot.Y
                    };

                    if (obj is IGH_Component comp)
                    {
                        compInfo["category"] = comp.Category;
                        compInfo["inputCount"] = comp.Params.Input.Count;
                        compInfo["outputCount"] = comp.Params.Output.Count;
                        compInfo["runtimeMessageLevel"] = comp.RuntimeMessageLevel.ToString();
                    }

                    components.Add(compInfo);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = components.Count,
                    components = components
                });
            });
        }

        [McpServerTool, Description("Search for available Grasshopper component types by name")]
        public string SearchComponents(
            [Description("Search query (e.g., 'circle', 'script')")] string query)
        {
            _server?.RecordCommand("search_components");
            return _context.ExecuteOnUiThread(() =>
            {
                var results = ComponentRegistry.SearchComponents(query);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = results.Count,
                    components = results
                });
            });
        }

        [McpServerTool, Description("Rename a component (change its nickname/display name)")]
        public string RenameComponent(
            [Description("Component GUID")] string id,
            [Description("New nickname to display")] string nickname = null,
            [Description("Alias for nickname")] string name = null)
        {
            _server?.RecordCommand("rename_component");
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

                // Use nickname if provided, otherwise use name
                string newName = nickname ?? name;
                if (string.IsNullOrEmpty(newName))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Either 'nickname' or 'name' is required" });
                }

                component.NickName = newName;
                component.Attributes?.ExpireLayout();
                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = id,
                    nickname = newName
                });
            });
        }

        [McpServerTool, Description("Manage zoomable/variable inputs on components like Merge or Stream Filter")]
        public string ManageZoomableInputs(
            [Description("Component GUID")] string id,
            [Description("Action: 'add', 'remove', or 'set_count'")] string action,
            [Description("Parameter side: 'input' or 'output'")] string side,
            [Description("Number of parameters to add/remove or target count")] int count)
        {
            _server?.RecordCommand("manage_zoomable_inputs");
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

                var component = doc.FindObject(guid, true) as IGH_Component;
                if (component == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Component not found or not a component: {id}" });
                }

                // Check if component supports variable parameters
                var varParamComp = component as IGH_VariableParameterComponent;
                if (varParamComp == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Component does not support variable parameters" });
                }

                // Determine parameter side
                var paramSide = side.ToLowerInvariant() == "output"
                    ? GH_ParameterSide.Output
                    : GH_ParameterSide.Input;

                var currentParams = paramSide == GH_ParameterSide.Input
                    ? component.Params.Input
                    : component.Params.Output;

                int currentCount = currentParams.Count;
                int addedCount = 0;
                int removedCount = 0;

                switch (action.ToLowerInvariant())
                {
                    case "add":
                        for (int i = 0; i < count; i++)
                        {
                            int insertIndex = currentParams.Count;
                            if (varParamComp.CanInsertParameter(paramSide, insertIndex))
                            {
                                IGH_Param newParam = varParamComp.CreateParameter(paramSide, insertIndex);
                                if (newParam != null)
                                {
                                    if (paramSide == GH_ParameterSide.Input)
                                        component.Params.RegisterInputParam(newParam);
                                    else
                                        component.Params.RegisterOutputParam(newParam);
                                    addedCount++;
                                }
                            }
                        }
                        break;

                    case "remove":
                        for (int i = 0; i < count; i++)
                        {
                            int removeIndex = currentParams.Count - 1;
                            if (removeIndex >= 0 && varParamComp.CanRemoveParameter(paramSide, removeIndex))
                            {
                                varParamComp.DestroyParameter(paramSide, removeIndex);
                                removedCount++;
                            }
                        }
                        break;

                    case "set_count":
                        int targetCount = count;
                        while (currentParams.Count < targetCount)
                        {
                            int insertIndex = currentParams.Count;
                            if (varParamComp.CanInsertParameter(paramSide, insertIndex))
                            {
                                IGH_Param newParam = varParamComp.CreateParameter(paramSide, insertIndex);
                                if (newParam != null)
                                {
                                    if (paramSide == GH_ParameterSide.Input)
                                        component.Params.RegisterInputParam(newParam);
                                    else
                                        component.Params.RegisterOutputParam(newParam);
                                    addedCount++;
                                }
                                else break;
                            }
                            else break;
                        }
                        while (currentParams.Count > targetCount)
                        {
                            int removeIndex = currentParams.Count - 1;
                            if (removeIndex >= 0 && varParamComp.CanRemoveParameter(paramSide, removeIndex))
                            {
                                varParamComp.DestroyParameter(paramSide, removeIndex);
                                removedCount++;
                            }
                            else break;
                        }
                        break;

                    default:
                        return JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" });
                }

                // Apply variable parameter maintenance
                varParamComp.VariableParameterMaintenance();
                component.Params.OnParametersChanged();
                component.Attributes.ExpireLayout();
                doc.NewSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = id,
                    action = action,
                    side = side,
                    previousCount = currentCount,
                    currentCount = currentParams.Count,
                    added = addedCount,
                    removed = removedCount
                });
            });
        }
    }
}
