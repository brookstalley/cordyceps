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

        [McpServerTool, Description("Add a component to the Grasshopper canvas by name or GUID. Use GUID or 'Category/Name' format to avoid ambiguity.")]
        public string AddComponent(
            [Description("Component type name (e.g., 'Circle', 'Addition'), GUID, or category-qualified name ('Curve/Circle')")] string type,
            [Description("X position on canvas")] double x,
            [Description("Y position on canvas")] double y,
            [Description("Optional nickname/display name for the component")] string nickname = null)
        {
            _server?.RecordCommand("add_component");
            Core.DebugLog.Info($"AddComponent called: type='{type}', x={x}, y={y}, nickname='{nickname}'");

            return _context.ExecuteOnUiThread(() =>
            {
                try
                {
                    if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                        return ToolHelpers.ErrorResponse(error);

                    Core.DebugLog.Debug($"Creating component: {type}");

                    // Try to create the component, handling disambiguation
                    var (component, matches) = ComponentRegistry.TryCreateComponent(type);

                    // Check if disambiguation is needed
                    if (matches != null && matches.Count > 1)
                    {
                        Core.DebugLog.Info($"Ambiguous component name '{type}' - {matches.Count} matches found");
                        return JsonConvert.SerializeObject(new
                        {
                            success = false,
                            error = "ambiguous_name",
                            message = $"Multiple components match '{type}'. Specify using GUID or 'Category/Name' format.",
                            matchCount = matches.Count,
                            matches = matches.Select(m => new
                            {
                                name = m.Name,
                                guid = m.Guid,
                                category = m.Category,
                                subcategory = m.SubCategory,
                                role = m.Role,
                                description = m.Description,
                                inputs = m.Inputs?.Select(i => $"{i.Name} ({i.Type})").ToList(),
                                outputs = m.Outputs?.Select(o => $"{o.Name} ({o.Type})").ToList()
                            })
                        });
                    }

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

                    // Set nickname if provided
                    if (!string.IsNullOrEmpty(nickname))
                    {
                        component.NickName = nickname;
                    }

                    // Set position
                    component.Attributes.Pivot = new PointF((float)x, (float)y);

                    // Add to document
                    doc.AddObject(component, false);
                    doc.NewSolution(true);

                    Core.DebugLog.Info($"Component added: {component.NickName ?? component.Name} ({component.InstanceGuid})");

                    // Get component bounds and details for response
                    var bounds = component.Attributes.Bounds;
                    int inputCount = 0;
                    int outputCount = 0;
                    string category = null;
                    string subcategory = null;

                    // Try to get category info from proxy - use ComponentGuid for accurate lookup
                    IGH_ObjectProxy proxy = null;
                    if (component is IGH_ActiveObject activeObj)
                    {
                        proxy = Instances.ComponentServer.ObjectProxies
                            .FirstOrDefault(p => p.Guid == activeObj.ComponentGuid);
                    }
                    // Fallback to name-based lookup if ComponentGuid didn't match
                    if (proxy == null)
                    {
                        proxy = Instances.ComponentServer.ObjectProxies
                            .FirstOrDefault(p => p.Desc.Name == component.Name);
                    }
                    if (proxy != null)
                    {
                        category = proxy.Desc.Category;
                        subcategory = proxy.Desc.SubCategory;
                    }

                    if (component is IGH_Component ghComp)
                    {
                        inputCount = ghComp.Params.Input.Count;
                        outputCount = ghComp.Params.Output.Count;
                    }
                    else if (component is IGH_Param ghParam)
                    {
                        inputCount = ghParam.SourceCount > 0 ? 1 : 0;
                        outputCount = 1;
                    }

                    var role = ComponentRegistry.GetRole(category, subcategory);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id = component.InstanceGuid.ToString(),
                        type = component.Name,
                        nickname = component.NickName,
                        category = category,
                        subcategory = subcategory,
                        role = role,
                        x = component.Attributes.Pivot.X,
                        y = component.Attributes.Pivot.Y,
                        width = bounds.Width,
                        height = bounds.Height,
                        inputCount = inputCount,
                        outputCount = outputCount
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
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

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
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                component.Attributes.Pivot = new PointF((float)x, (float)y);
                component.Attributes.ExpireLayout();
                Instances.ActiveCanvas?.Invalidate();

                // Get bounds after move for layout planning
                var bounds = component.Attributes.Bounds;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = id,
                    pivot = new { x = x, y = y },
                    bounds = new
                    {
                        x = bounds.X,
                        y = bounds.Y,
                        width = bounds.Width,
                        height = bounds.Height,
                        right = bounds.Right,
                        bottom = bounds.Bottom
                    }
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
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

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
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get infrastructure component IDs to filter
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                var components = new List<object>();
                foreach (var obj in doc.Objects)
                {
                    // Skip Cordyceps infrastructure
                    if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds))
                        continue;

                    var compInfo = new Dictionary<string, object>
                    {
                        ["id"] = obj.InstanceGuid.ToString(),
                        ["name"] = obj.Name,
                        ["nickname"] = obj.NickName,
                        ["type"] = obj.GetType().Name,
                        ["x"] = obj.Attributes.Pivot.X,
                        ["y"] = obj.Attributes.Pivot.Y
                    };

                    // Look up category info from proxy using ComponentGuid for accuracy
                    string category = null;
                    string subcategory = null;
                    if (obj is IGH_ActiveObject activeObj)
                    {
                        var proxy = Instances.ComponentServer.ObjectProxies
                            .FirstOrDefault(p => p.Guid == activeObj.ComponentGuid);
                        if (proxy != null)
                        {
                            category = proxy.Desc.Category;
                            subcategory = proxy.Desc.SubCategory;
                        }
                    }

                    if (obj is IGH_Component comp)
                    {
                        // Fallback to component's own category if proxy not found
                        category = category ?? comp.Category;
                        subcategory = subcategory ?? comp.SubCategory;
                        compInfo["category"] = category;
                        compInfo["subcategory"] = subcategory;
                        compInfo["role"] = ComponentRegistry.GetRole(category, subcategory);
                        compInfo["inputCount"] = comp.Params.Input.Count;
                        compInfo["outputCount"] = comp.Params.Output.Count;
                        compInfo["runtimeMessageLevel"] = comp.RuntimeMessageLevel.ToString();
                    }
                    else if (obj is IGH_Param param)
                    {
                        // Fallback for parameters not found in server
                        category = category ?? "Params";
                        subcategory = subcategory ?? "Unknown";
                        compInfo["category"] = category;
                        compInfo["subcategory"] = subcategory;
                        compInfo["role"] = ComponentRegistry.GetRole(category, subcategory);
                        compInfo["sourceCount"] = param.SourceCount;
                        compInfo["recipientCount"] = param.Recipients.Count;
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
                var basicResults = ComponentRegistry.SearchComponents(query);
                var enhancedResults = new List<object>();

                foreach (var result in basicResults)
                {
                    // Try to get plugin info based on category
                    var pluginInfo = PluginRegistry.Instance.GetPluginForCategory(result.Category);

                    // Try to create instance for parameter info and deprecation checking
                    var inputs = new List<object>();
                    var outputs = new List<object>();
                    var isDeprecated = false;
                    UpgradeInfo upgradeInfo = null;
                    var proxyObsolete = false;
                    var typeNameObsolete = false;

                    try
                    {
                        if (Guid.TryParse(result.Guid, out Guid guid))
                        {
                            // Check deprecation by GUID (correct approach)
                            isDeprecated = DeprecationRegistry.Instance.IsDeprecated(guid);
                            upgradeInfo = DeprecationRegistry.Instance.GetUpgradeInfo(guid);

                            var proxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Guid == guid);
                            if (proxy != null)
                            {
                                // Check proxy.Obsolete property
                                proxyObsolete = proxy.Obsolete;

                                var instance = proxy.CreateInstance();

                                // Check if type name contains OBSOLETE (another deprecation indicator)
                                if (instance != null)
                                {
                                    typeNameObsolete = instance.GetType().Name.Contains("OBSOLETE", StringComparison.OrdinalIgnoreCase);
                                }

                                if (instance is IGH_Component comp)
                                {
                                    foreach (var input in comp.Params.Input)
                                    {
                                        inputs.Add(new
                                        {
                                            name = input.Name,
                                            nickname = input.NickName,
                                            type = input.TypeName,
                                            access = input.Access.ToString(),
                                            optional = input.Optional
                                        });
                                    }

                                    foreach (var output in comp.Params.Output)
                                    {
                                        outputs.Add(new
                                        {
                                            name = output.Name,
                                            nickname = output.NickName,
                                            type = output.TypeName
                                        });
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors creating instances
                    }

                    // Component is deprecated if ANY deprecation indicator is true
                    var finalDeprecated = isDeprecated || proxyObsolete || typeNameObsolete;

                    var role = ComponentRegistry.GetRole(result.Category, result.SubCategory);

                    enhancedResults.Add(new
                    {
                        name = result.Name,
                        description = result.Description,
                        category = result.Category,
                        subCategory = result.SubCategory,
                        role = role,
                        guid = result.Guid,
                        deprecated = finalDeprecated,
                        upgrade = upgradeInfo != null ? new
                        {
                            toName = upgradeInfo.ToName,
                            toGuid = upgradeInfo.ToGuid.ToString()
                        } : null,
                        plugin = pluginInfo != null ? new
                        {
                            name = pluginInfo.Name,
                            documentationUrl = pluginInfo.DocumentationUrl
                        } : null,
                        inputs = inputs.Count > 0 ? inputs : null,
                        outputs = outputs.Count > 0 ? outputs : null
                    });
                }

                // Sort results: non-deprecated first, then by name length (shorter = more specific match)
                var sortedResults = enhancedResults
                    .OrderBy(r => ((dynamic)r).deprecated ? 1 : 0)
                    .ThenBy(r => ((dynamic)r).name.Length)
                    .ToList();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = sortedResults.Count,
                    components = sortedResults
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
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

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
                if (!ToolHelpers.TryGetComponentWithDoc(_context, id, out var doc, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var component = obj as IGH_Component;
                if (component == null)
                    return ToolHelpers.ErrorResponse("Object is not a component");

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

        [McpServerTool, Description("Get the bounding box and dimensions of a component on the canvas")]
        public string GetComponentBounds(
            [Description("Component GUID")] string id)
        {
            _server?.RecordCommand("get_component_bounds");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var bounds = component.Attributes.Bounds;
                var pivot = component.Attributes.Pivot;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = id,
                    name = component.Name,
                    nickname = component.NickName,
                    bounds = new
                    {
                        x = bounds.X,
                        y = bounds.Y,
                        width = bounds.Width,
                        height = bounds.Height,
                        right = bounds.Right,
                        bottom = bounds.Bottom
                    },
                    pivot = new
                    {
                        x = pivot.X,
                        y = pivot.Y
                    }
                });
            });
        }

        [McpServerTool, Description("Validate canvas layout for overlaps and spacing issues")]
        public string ValidateLayout()
        {
            _server?.RecordCommand("validate_layout");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var overlaps = new List<object>();
                var suggestions = new List<string>();
                var components = doc.Objects.ToList();

                // Check for overlaps between all pairs of components
                for (int i = 0; i < components.Count; i++)
                {
                    for (int j = i + 1; j < components.Count; j++)
                    {
                        var comp1 = components[i];
                        var comp2 = components[j];

                        // Skip groups - they're meant to contain other components
                        if (comp1 is GH_Group || comp2 is GH_Group) continue;

                        var bounds1 = comp1.Attributes.Bounds;
                        var bounds2 = comp2.Attributes.Bounds;

                        if (bounds1.IntersectsWith(bounds2))
                        {
                            var intersection = RectangleF.Intersect(bounds1, bounds2);
                            var overlapArea = intersection.Width * intersection.Height;

                            overlaps.Add(new
                            {
                                component1 = new { id = comp1.InstanceGuid.ToString(), name = comp1.Name, nickname = comp1.NickName },
                                component2 = new { id = comp2.InstanceGuid.ToString(), name = comp2.Name, nickname = comp2.NickName },
                                overlapArea = overlapArea
                            });

                            suggestions.Add($"'{ToolHelpers.GetDisplayName(comp1)}' overlaps with '{ToolHelpers.GetDisplayName(comp2)}' - move one component");
                        }
                    }
                }

                // Check for tight spacing (components too close together horizontally)
                var sortedByX = components.Where(c => !(c is GH_Group))
                    .OrderBy(c => c.Attributes.Bounds.X).ToList();

                for (int i = 0; i < sortedByX.Count - 1; i++)
                {
                    var comp1 = sortedByX[i];
                    var comp2 = sortedByX[i + 1];

                    var gap = comp2.Attributes.Bounds.X - comp1.Attributes.Bounds.Right;
                    if (gap > 0 && gap < 40) // Less than 40px is too tight
                    {
                        suggestions.Add($"Tight horizontal spacing ({gap:F0}px) between '{ToolHelpers.GetDisplayName(comp1)}' and '{ToolHelpers.GetDisplayName(comp2)}' - consider 60-80px gaps");
                    }
                }

                // Check for tight spacing vertically
                // Skip compact components (sliders, value lists) which are intentionally close together
                var sortedByY = components.Where(c => !(c is GH_Group))
                    .OrderBy(c => c.Attributes.Bounds.Y).ToList();

                for (int i = 0; i < sortedByY.Count - 1; i++)
                {
                    var comp1 = sortedByY[i];
                    var comp2 = sortedByY[i + 1];

                    // Skip if either component is a compact type (sliders, value lists)
                    if (ToolHelpers.IsCompactComponent(comp1) || ToolHelpers.IsCompactComponent(comp2))
                        continue;

                    // Only check if they're in similar X positions (same column)
                    if (Math.Abs(comp1.Attributes.Pivot.X - comp2.Attributes.Pivot.X) < 100)
                    {
                        var gap = comp2.Attributes.Bounds.Y - comp1.Attributes.Bounds.Bottom;
                        if (gap > 0 && gap < 30) // Less than 30 units is tight
                        {
                            suggestions.Add($"Tight vertical spacing ({gap:F0}px) between '{ToolHelpers.GetDisplayName(comp1)}' and '{ToolHelpers.GetDisplayName(comp2)}' - consider 70px gaps");
                        }
                    }
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    componentCount = components.Count,
                    overlapCount = overlaps.Count,
                    overlaps = overlaps,
                    suggestions = suggestions,
                    isClean = overlaps.Count == 0 && suggestions.Count == 0
                });
            });
        }

        // TODO: V1.1 - Re-enable when auto-spacing is improved to preserve intentional layout
        // [McpServerTool, Description("Automatically space components to eliminate overlaps")]
        public string AutoSpaceComponents(
            [Description("Spacing mode: 'flow' (default, respects data flow direction, stacks components at same depth vertically), 'vertical' (stack all vertically), 'horizontal' (WARNING: destroys vertical stacking), 'grid'")] string mode = "flow",
            [Description("JSON array of component IDs to arrange (optional, defaults to all)")] string componentIds = null,
            [Description("Spacing between components in pixels. Default 60 is about 1x component width.")] int spacing = 60,
            [Description("Component ID to keep fixed as anchor (optional)")] string anchor = null)
        {
            _server?.RecordCommand("auto_space_components");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Parse component IDs if provided
                List<Guid> targetIds = null;
                if (!string.IsNullOrEmpty(componentIds))
                {
                    try
                    {
                        var idList = JsonConvert.DeserializeObject<List<string>>(componentIds);
                        targetIds = idList.Select(id => Guid.Parse(id)).ToList();
                    }
                    catch
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "Invalid componentIds JSON array" });
                    }
                }

                // Get components to arrange
                var components = doc.Objects
                    .Where(c => !(c is GH_Group))
                    .Where(c => targetIds == null || targetIds.Contains(c.InstanceGuid))
                    .ToList();

                if (components.Count == 0)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No components to arrange" });
                }

                // Parse anchor ID
                Guid? anchorId = null;
                if (!string.IsNullOrEmpty(anchor) && Guid.TryParse(anchor, out Guid anchorGuid))
                {
                    anchorId = anchorGuid;
                }

                // Find starting position
                float startX, startY;
                if (anchorId.HasValue)
                {
                    var anchorComp = components.FirstOrDefault(c => c.InstanceGuid == anchorId.Value);
                    if (anchorComp != null)
                    {
                        startX = anchorComp.Attributes.Pivot.X;
                        startY = anchorComp.Attributes.Pivot.Y;
                        // Remove anchor from list so it doesn't move
                        components.Remove(anchorComp);
                    }
                    else
                    {
                        startX = components.Min(c => c.Attributes.Pivot.X);
                        startY = components.Min(c => c.Attributes.Pivot.Y);
                    }
                }
                else
                {
                    startX = components.Min(c => c.Attributes.Pivot.X);
                    startY = components.Min(c => c.Attributes.Pivot.Y);
                }

                var movedCount = 0;

                switch (mode.ToLowerInvariant())
                {
                    case "horizontal":
                        // Sort by current X position and space horizontally
                        var sortedH = components.OrderBy(c => c.Attributes.Pivot.X).ToList();
                        float currentX = startX;
                        foreach (var comp in sortedH)
                        {
                            comp.Attributes.Pivot = new PointF(currentX, comp.Attributes.Pivot.Y);
                            comp.Attributes.ExpireLayout();
                            currentX += comp.Attributes.Bounds.Width + spacing;
                            movedCount++;
                        }
                        break;

                    case "vertical":
                        // Sort by current Y position and space vertically
                        var sortedV = components.OrderBy(c => c.Attributes.Pivot.Y).ToList();
                        float currentY = startY;
                        foreach (var comp in sortedV)
                        {
                            comp.Attributes.Pivot = new PointF(comp.Attributes.Pivot.X, currentY);
                            comp.Attributes.ExpireLayout();
                            currentY += comp.Attributes.Bounds.Height + spacing;
                            movedCount++;
                        }
                        break;

                    case "grid":
                        // Arrange in a grid pattern
                        int cols = (int)Math.Ceiling(Math.Sqrt(components.Count));
                        var sortedG = components.OrderBy(c => c.Attributes.Pivot.Y)
                            .ThenBy(c => c.Attributes.Pivot.X).ToList();
                        float gridX = startX;
                        float gridY = startY;
                        int col = 0;
                        float maxHeightInRow = 0;

                        foreach (var comp in sortedG)
                        {
                            comp.Attributes.Pivot = new PointF(gridX, gridY);
                            comp.Attributes.ExpireLayout();
                            maxHeightInRow = Math.Max(maxHeightInRow, comp.Attributes.Bounds.Height);

                            col++;
                            if (col >= cols)
                            {
                                col = 0;
                                gridX = startX;
                                gridY += maxHeightInRow + spacing;
                                maxHeightInRow = 0;
                            }
                            else
                            {
                                gridX += comp.Attributes.Bounds.Width + spacing;
                            }
                            movedCount++;
                        }
                        break;

                    case "flow":
                        // Arrange respecting data flow: components at same depth stack vertically
                        // Depth = longest path from any source (component with no inputs connected)
                        var componentSet = new HashSet<Guid>(components.Select(c => c.InstanceGuid));
                        var depths = new Dictionary<Guid, int>();
                        var downstream = new Dictionary<Guid, List<Guid>>();

                        // Initialize
                        foreach (var comp in components)
                        {
                            depths[comp.InstanceGuid] = 0;
                            downstream[comp.InstanceGuid] = new List<Guid>();
                        }

                        // Build downstream adjacency from actual wire connections
                        foreach (var comp in components)
                        {
                            if (comp is IGH_Component ghComp)
                            {
                                foreach (var input in ghComp.Params.Input)
                                {
                                    foreach (var source in input.Sources)
                                    {
                                        var sourceCompId = source.Attributes?.GetTopLevel?.DocObject?.InstanceGuid;
                                        if (sourceCompId.HasValue && componentSet.Contains(sourceCompId.Value))
                                        {
                                            if (!downstream[sourceCompId.Value].Contains(comp.InstanceGuid))
                                            {
                                                downstream[sourceCompId.Value].Add(comp.InstanceGuid);
                                            }
                                        }
                                    }
                                }
                            }
                            else if (comp is IGH_Param param)
                            {
                                foreach (var source in param.Sources)
                                {
                                    var sourceCompId = source.Attributes?.GetTopLevel?.DocObject?.InstanceGuid;
                                    if (sourceCompId.HasValue && componentSet.Contains(sourceCompId.Value))
                                    {
                                        if (!downstream[sourceCompId.Value].Contains(comp.InstanceGuid))
                                        {
                                            downstream[sourceCompId.Value].Add(comp.InstanceGuid);
                                        }
                                    }
                                }
                            }
                        }

                        // Compute depths using BFS from sources (components with no upstream in set)
                        var hasUpstream = new HashSet<Guid>();
                        foreach (var kvp in downstream)
                        {
                            foreach (var target in kvp.Value)
                            {
                                hasUpstream.Add(target);
                            }
                        }

                        var sources = components.Where(c => !hasUpstream.Contains(c.InstanceGuid)).ToList();
                        var queue = new Queue<Guid>();
                        foreach (var src in sources)
                        {
                            queue.Enqueue(src.InstanceGuid);
                            depths[src.InstanceGuid] = 0;
                        }

                        // BFS to find longest path (max depth)
                        while (queue.Count > 0)
                        {
                            var current = queue.Dequeue();
                            var currentDepth = depths[current];
                            foreach (var next in downstream[current])
                            {
                                if (depths[next] < currentDepth + 1)
                                {
                                    depths[next] = currentDepth + 1;
                                    queue.Enqueue(next);
                                }
                            }
                        }

                        // Group by depth
                        var byDepth = components.GroupBy(c => depths[c.InstanceGuid])
                            .OrderBy(g => g.Key)
                            .ToList();

                        // Position each column
                        float flowX = startX;
                        foreach (var depthGroup in byDepth)
                        {
                            // Sort within column by original Y position for stability
                            var columnComps = depthGroup.OrderBy(c => c.Attributes.Pivot.Y).ToList();
                            float flowY = startY;
                            float maxWidthInColumn = 0;

                            foreach (var comp in columnComps)
                            {
                                comp.Attributes.Pivot = new PointF(flowX, flowY);
                                comp.Attributes.ExpireLayout();
                                flowY += comp.Attributes.Bounds.Height + spacing;
                                maxWidthInColumn = Math.Max(maxWidthInColumn, comp.Attributes.Bounds.Width);
                                movedCount++;
                            }

                            flowX += maxWidthInColumn + spacing;
                        }
                        break;

                    default:
                        return JsonConvert.SerializeObject(new { success = false, error = $"Unknown mode: {mode}" });
                }

                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    mode = mode,
                    movedCount = movedCount,
                    spacing = spacing
                });
            });
        }

        [McpServerTool, Description("Add a constant value panel to the canvas (convenience for creating pre-configured panels)")]
        public string AddConstant(
            [Description("Constant value (e.g., '0', '1', '2', 'Pi')")] string value,
            [Description("X position on canvas")] double x,
            [Description("Y position on canvas")] double y,
            [Description("Optional nickname for the panel")] string nickname = null)
        {
            _server?.RecordCommand("add_constant");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Create a panel
                var panel = new GH_Panel();
                panel.CreateAttributes();
                panel.Attributes.Pivot = new PointF((float)x, (float)y);

                // Set the value
                panel.SetUserText(value);

                // Set nickname if provided, otherwise use the value
                panel.NickName = nickname ?? value;

                // Add to document
                doc.AddObject(panel, false);
                doc.NewSolution(true);

                var bounds = panel.Attributes.Bounds;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = panel.InstanceGuid.ToString(),
                    type = "Panel",
                    value = value,
                    nickname = panel.NickName,
                    x = panel.Attributes.Pivot.X,
                    y = panel.Attributes.Pivot.Y,
                    width = bounds.Width,
                    height = bounds.Height
                });
            });
        }

        [McpServerTool, Description("Suggest a pattern for a given task description")]
        public string SuggestPattern(
            [Description("Description of what you want to create (e.g., 'arrange objects in a circle')")] string description)
        {
            _server?.RecordCommand("suggest_pattern");
            // Pattern matching is done locally without UI thread access
            var desc = description.ToLowerInvariant();

            var suggestions = new List<object>();

            // Radial/circular patterns
            if (desc.Contains("circle") || desc.Contains("radial") || desc.Contains("rotate") ||
                desc.Contains("around") || desc.Contains("polar") || desc.Contains("spoke"))
            {
                suggestions.Add(new
                {
                    pattern = "radial-array",
                    resource = "gh://patterns/radial-array",
                    description = "Create N copies arranged in a circle around a center point",
                    components = new[] { "Number Slider", "Pi", "Division", "Series", "Construct Point", "Rotate 3D", "Unit Z" },
                    estimatedComponentCount = 10
                });
            }

            // Linear patterns
            if (desc.Contains("line") || desc.Contains("linear") || desc.Contains("row") ||
                desc.Contains("repeat") || desc.Contains("copy") || desc.Contains("array") ||
                desc.Contains("space") || desc.Contains("distribute"))
            {
                suggestions.Add(new
                {
                    pattern = "linear-array",
                    resource = "gh://patterns/linear-array",
                    description = "Create N copies arranged in a straight line",
                    components = new[] { "Number Slider", "Series", "Unit X", "Move" },
                    estimatedComponentCount = 6
                });
            }

            // Grid patterns
            if (desc.Contains("grid") || desc.Contains("matrix") || desc.Contains("2d array") ||
                desc.Contains("rows and columns") || desc.Contains("panel") || desc.Contains("facade"))
            {
                suggestions.Add(new
                {
                    pattern = "grid-array",
                    resource = "gh://patterns/grid-array",
                    description = "Create a 2D grid of copies with X and Y spacing",
                    components = new[] { "Number Slider", "Series", "Cross Reference", "Construct Point", "Rectangular Grid" },
                    estimatedComponentCount = 8
                });
            }

            if (suggestions.Count == 0)
            {
                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    found = false,
                    message = "No specific pattern recognized. Try describing your goal using keywords like: circle, radial, line, array, grid, copy, repeat, distribute"
                });
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                found = true,
                count = suggestions.Count,
                suggestions = suggestions
            });
        }

        [McpServerTool, Description("Find a component by its nickname (display name)")]
        public string GetComponentByNickname(
            [Description("Nickname to search for (exact or partial match)")] string nickname,
            [Description("If true, require exact match; if false (default), allow partial/case-insensitive match")] bool exact = false)
        {
            _server?.RecordCommand("get_component_by_nickname");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (string.IsNullOrEmpty(nickname))
                    return ToolHelpers.ErrorResponse("Nickname is required");

                // Get infrastructure IDs to filter
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                var matches = new List<object>();

                foreach (var obj in doc.Objects)
                {
                    // Skip infrastructure components
                    if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds))
                        continue;
                    bool isMatch = false;

                    if (exact)
                    {
                        isMatch = obj.NickName == nickname;
                    }
                    else
                    {
                        isMatch = obj.NickName != null &&
                            obj.NickName.IndexOf(nickname, StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    if (isMatch)
                    {
                        var info = new Dictionary<string, object>
                        {
                            ["id"] = obj.InstanceGuid.ToString(),
                            ["name"] = obj.Name,
                            ["nickname"] = obj.NickName,
                            ["type"] = obj.GetType().Name,
                            ["x"] = obj.Attributes.Pivot.X,
                            ["y"] = obj.Attributes.Pivot.Y
                        };

                        // Look up category from proxy using ComponentGuid for accuracy
                        string category = null;
                        string subcategory = null;
                        if (obj is IGH_ActiveObject activeObj)
                        {
                            var proxy = Instances.ComponentServer.ObjectProxies
                                .FirstOrDefault(p => p.Guid == activeObj.ComponentGuid);
                            if (proxy != null)
                            {
                                category = proxy.Desc.Category;
                                subcategory = proxy.Desc.SubCategory;
                            }
                        }

                        if (obj is IGH_Component comp)
                        {
                            category = category ?? comp.Category;
                            subcategory = subcategory ?? comp.SubCategory;
                            info["category"] = category;
                            info["subcategory"] = subcategory;
                            info["role"] = ComponentRegistry.GetRole(category, subcategory);
                            info["inputCount"] = comp.Params.Input.Count;
                            info["outputCount"] = comp.Params.Output.Count;
                            info["runtimeMessageLevel"] = comp.RuntimeMessageLevel.ToString();
                        }
                        else if (obj is IGH_Param param)
                        {
                            category = category ?? "Params";
                            subcategory = subcategory ?? "Unknown";
                            info["category"] = category;
                            info["subcategory"] = subcategory;
                            info["role"] = ComponentRegistry.GetRole(category, subcategory);
                            info["sourceCount"] = param.SourceCount;
                            info["recipientCount"] = param.Recipients.Count;
                        }

                        matches.Add(info);
                    }
                }

                if (matches.Count == 0)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        found = false,
                        message = $"No component found with nickname '{nickname}'"
                    });
                }

                // If single match, return it directly; otherwise return list
                if (matches.Count == 1)
                {
                    var result = (Dictionary<string, object>)matches[0];
                    result["success"] = true;
                    result["found"] = true;
                    return JsonConvert.SerializeObject(result);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    found = true,
                    count = matches.Count,
                    components = matches
                });
            });
        }

        [McpServerTool, Description("Move multiple components at once efficiently")]
        public string BulkMoveComponents(
            [Description("JSON array of move operations: [{id, x, y}, ...]")] string moves)
        {
            _server?.RecordCommand("bulk_move_components");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!ToolHelpers.TryDeserializeList<dynamic>(moves, out var moveList, out error))
                    return ToolHelpers.ErrorResponse(error);

                if (moveList == null || moveList.Count == 0)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No moves provided" });
                }

                // Get infrastructure IDs to filter
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                var results = new List<object>();
                int successCount = 0;
                int failCount = 0;

                foreach (var move in moveList)
                {
                    try
                    {
                        string id = move.id?.ToString();
                        if (string.IsNullOrEmpty(id))
                        {
                            results.Add(new { success = false, error = "Missing id in move" });
                            failCount++;
                            continue;
                        }

                        if (!Guid.TryParse(id, out Guid guid))
                        {
                            results.Add(new { success = false, id, error = "Invalid component ID" });
                            failCount++;
                            continue;
                        }

                        // Check if protected - report as "not found" to maintain invisibility
                        if (infraIds.Contains(guid))
                        {
                            results.Add(new { success = false, id, error = "Component not found" });
                            failCount++;
                            continue;
                        }

                        var component = doc.FindObject(guid, true);
                        if (component == null)
                        {
                            results.Add(new { success = false, id, error = "Component not found" });
                            failCount++;
                            continue;
                        }

                        double x = (double)move.x;
                        double y = (double)move.y;

                        component.Attributes.Pivot = new PointF((float)x, (float)y);
                        component.Attributes.ExpireLayout();

                        var bounds = component.Attributes.Bounds;
                        results.Add(new
                        {
                            success = true,
                            id,
                            nickname = component.NickName,
                            x,
                            y,
                            bounds = new
                            {
                                width = bounds.Width,
                                height = bounds.Height,
                                right = bounds.Right,
                                bottom = bounds.Bottom
                            }
                        });
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        string id = move.id?.ToString() ?? "unknown";
                        results.Add(new { success = false, id, error = ex.Message });
                        failCount++;
                    }
                }

                Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = failCount == 0,
                    total = moveList.Count,
                    succeeded = successCount,
                    failed = failCount,
                    results
                });
            });
        }
    }
}
