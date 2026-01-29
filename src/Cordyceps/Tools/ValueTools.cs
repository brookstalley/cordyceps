using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Component value operations (set values, get parameters, toggle states, bake geometry)
    /// </summary>
    [McpServerToolType]
    public class ValueTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public ValueTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Set a component's value (slider, panel, or parameter). For sliders, sets the current value only. Use gh_set_slider_properties to configure slider range and type. Example: gh_set_value(id='abc', value='42')")]
        public string GhSetValue(
            [Description("Component GUID")] string id,
            [Description("Value to set (number for sliders, text for panels)")] string value)
        {
            _server?.RecordCommand("set_component_value");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Handle different component types
                if (component is GH_Panel panel)
                {
                    panel.UserText = value;
                }
                else if (component is GH_NumberSlider slider)
                {
                    // Try to parse value as number
                    if (double.TryParse(value, out double numValue))
                    {
                        // Clamp to slider range
                        decimal sliderMin = slider.Slider.Minimum;
                        decimal sliderMax = slider.Slider.Maximum;
                        decimal clampedValue = Math.Max(sliderMin, Math.Min(sliderMax, (decimal)numValue));
                        slider.SetSliderValue(clampedValue);
                    }
                    else
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = $"Cannot parse '{value}' as a number for slider. Use set_slider_properties to configure slider range and type." });
                    }
                }
                else if (component is IGH_Param param)
                {
                    // For generic params, try to set persistent data
                    param.ClearData();

                    // Try to parse as different types
                    if (double.TryParse(value, out double numVal))
                    {
                        param.AddVolatileData(new GH_Path(0), 0, new GH_Number(numVal));
                    }
                    else if (bool.TryParse(value, out bool boolVal))
                    {
                        param.AddVolatileData(new GH_Path(0), 0, new GH_Boolean(boolVal));
                    }
                    else
                    {
                        param.AddVolatileData(new GH_Path(0), 0, new GH_String(value));
                    }
                }
                else
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Cannot set value for component type {component.GetType().Name}" });
                }

                // Expire and recompute
                if (component is IGH_ActiveObject activeObj)
                {
                    activeObj.ExpireSolution(true);
                }
                doc.NewSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    type = component.GetType().Name,
                    valueSet = true,
                    value
                });
            });
        }

        [McpServerTool, Description("Get parameter information for a component type (inputs, outputs, descriptions). Example: gh_get_parameters(componentType='Circle')")]
        public string GhGetParameters(
            [Description("Component type name (e.g., 'Circle', 'Addition')")] string componentType)
        {
            _server?.RecordCommand("get_component_parameters");
            return _context.ExecuteOnUiThread(() =>
            {
                // Find component proxy by name
                var proxy = Instances.ComponentServer.ObjectProxies
                    .FirstOrDefault(p => p.Desc.Name.Equals(componentType, StringComparison.OrdinalIgnoreCase));

                if (proxy == null)
                {
                    // Try partial match
                    proxy = Instances.ComponentServer.ObjectProxies
                        .FirstOrDefault(p => p.Desc.Name.IndexOf(componentType, StringComparison.OrdinalIgnoreCase) >= 0);
                }

                if (proxy == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Component type '{componentType}' not found" });
                }

                // Create temporary instance to get parameter info
                var instance = proxy.CreateInstance();

                var parameterInfo = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["name"] = proxy.Desc.Name,
                    ["category"] = proxy.Desc.Category,
                    ["subCategory"] = proxy.Desc.SubCategory,
                    ["description"] = proxy.Desc.Description,
                    ["guid"] = proxy.Guid.ToString()
                };

                if (instance is IGH_Component ghComponent)
                {
                    var inputs = new List<Dictionary<string, object>>();
                    foreach (var param in ghComponent.Params.Input)
                    {
                        inputs.Add(new Dictionary<string, object>
                        {
                            ["name"] = param.Name,
                            ["nickname"] = param.NickName,
                            ["description"] = param.Description,
                            ["type"] = param.TypeName,
                            ["access"] = param.Access.ToString(),
                            ["optional"] = param.Optional
                        });
                    }
                    parameterInfo["inputs"] = inputs;

                    var outputs = new List<Dictionary<string, object>>();
                    foreach (var param in ghComponent.Params.Output)
                    {
                        outputs.Add(new Dictionary<string, object>
                        {
                            ["name"] = param.Name,
                            ["nickname"] = param.NickName,
                            ["description"] = param.Description,
                            ["type"] = param.TypeName
                        });
                    }
                    parameterInfo["outputs"] = outputs;
                }
                else if (instance is IGH_Param ghParam)
                {
                    parameterInfo["parameterType"] = ghParam.TypeName;
                    parameterInfo["kind"] = ghParam.Kind.ToString();
                }

                return JsonConvert.SerializeObject(parameterInfo);
            });
        }

        [McpServerTool, Description("Configure a Number Slider's range, value, and type (integer vs floating-point). Example: gh_set_slider(id='abc', min=0, max=100, value=50)")]
        public string GhSetSlider(
            [Description("Slider component GUID")] string id,
            [Description("Minimum value for the slider range")] double min,
            [Description("Maximum value for the slider range")] double max,
            [Description("Current/default value (must be between min and max)")] double value,
            [Description("Optional: force integer type (true) or floating-point (false). If not specified, auto-detects from values.")] string integer = null)
        {
            _server?.RecordCommand("set_slider_properties");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (!(component is GH_NumberSlider slider))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Component is not a Number Slider: {component.GetType().Name}" });
                }

                // Validate slider range: min <= value <= max
                if (min > max)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Invalid slider range: min ({min}) must be <= max ({max})" });
                }
                if (value < min || value > max)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Invalid slider value: value ({value}) must be between min ({min}) and max ({max})" });
                }

                // Determine if this is an integer slider
                bool? integerParsed = ParseOptionalBool(integer);
                bool isInteger = integerParsed ?? ((min == Math.Floor(min)) &&
                                                   (max == Math.Floor(max)) &&
                                                   (value == Math.Floor(value)));

                // Set slider properties
                slider.Slider.Minimum = (decimal)min;
                slider.Slider.Maximum = (decimal)max;
                slider.Slider.Value = (decimal)value;

                // Set slider type
                if (isInteger)
                {
                    slider.Slider.Type = Grasshopper.GUI.Base.GH_SliderAccuracy.Integer;
                    slider.Slider.DecimalPlaces = 0;
                }
                else
                {
                    slider.Slider.Type = Grasshopper.GUI.Base.GH_SliderAccuracy.Float;
                    // Determine decimal places from the values
                    int maxDecimals = new[] { min, max, value }
                        .Select(v => v.ToString())
                        .Where(s => s.Contains('.'))
                        .Select(s => s.TrimEnd('0').Length - s.IndexOf('.') - 1)
                        .DefaultIfEmpty(0)
                        .Max();
                    slider.Slider.DecimalPlaces = Math.Max(maxDecimals, 1);
                }

                // Update slider display
                slider.Attributes?.ExpireLayout();

                // Expire and recompute
                slider.ExpireSolution(true);
                doc.NewSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    min,
                    max,
                    value,
                    integer = isInteger,
                    decimalPlaces = slider.Slider.DecimalPlaces
                });
            });
        }

        [McpServerTool, Description("Set component preview visibility (whether geometry is shown in Rhino viewport). Accepts single id or array. Example: gh_set_preview(id='abc', enabled=true) or gh_set_preview(ids='[\"a\",\"b\"]', enabled=false)")]
        public string GhSetPreview(
            [Description("Single component GUID")] string id = null,
            [Description("JSON array of component GUIDs")] string ids = null,
            [Description("Preview state: true=visible, false=hidden")] bool enabled = true)
        {
            _server?.RecordCommand("gh_set_preview");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Build ID list from either parameter
                List<string> idList;
                if (!string.IsNullOrEmpty(ids))
                {
                    try { idList = JsonConvert.DeserializeObject<List<string>>(ids); }
                    catch (Exception ex) { return ToolHelpers.ErrorResponse($"Invalid ids format: {ex.Message}"); }
                }
                else if (!string.IsNullOrEmpty(id))
                {
                    idList = new List<string> { id };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Either 'id' or 'ids' is required");
                }

                if (idList == null || idList.Count == 0)
                    return ToolHelpers.ErrorResponse("No component IDs provided");

                var results = new List<object>();
                int succeeded = 0, failed = 0;

                foreach (var compId in idList)
                {
                    if (!ToolHelpers.TryGetUnprotectedComponent(_context, compId, out var component, out var compError))
                    {
                        results.Add(new { id = compId, success = false, error = compError });
                        failed++;
                        continue;
                    }

                    if (component is IGH_PreviewObject previewObj)
                    {
                        previewObj.Hidden = !enabled;
                        results.Add(new { id = compId, success = true, previewEnabled = enabled });
                        succeeded++;
                    }
                    else
                    {
                        results.Add(new { id = compId, success = false, error = "Component does not support preview" });
                        failed++;
                    }
                }

                doc.NewSolution(false);

                // Simplified response for single item
                if (idList.Count == 1)
                    return JsonConvert.SerializeObject(results[0]);

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = idList.Count,
                    succeeded,
                    failed,
                    previewEnabled = enabled,
                    results
                });
            });
        }

        [McpServerTool, Description("Set component enabled/disabled state (disabled components don't compute). Accepts single id or array. Example: gh_set_enabled(id='abc', enabled=true)")]
        public string GhSetEnabled(
            [Description("Single component GUID")] string id = null,
            [Description("JSON array of component GUIDs")] string ids = null,
            [Description("Enabled state: true=enabled (computes), false=disabled (locked)")] bool enabled = true)
        {
            _server?.RecordCommand("gh_set_enabled");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Build ID list from either parameter
                List<string> idList;
                if (!string.IsNullOrEmpty(ids))
                {
                    try { idList = JsonConvert.DeserializeObject<List<string>>(ids); }
                    catch (Exception ex) { return ToolHelpers.ErrorResponse($"Invalid ids format: {ex.Message}"); }
                }
                else if (!string.IsNullOrEmpty(id))
                {
                    idList = new List<string> { id };
                }
                else
                {
                    return ToolHelpers.ErrorResponse("Either 'id' or 'ids' is required");
                }

                if (idList == null || idList.Count == 0)
                    return ToolHelpers.ErrorResponse("No component IDs provided");

                var results = new List<object>();
                int succeeded = 0, failed = 0;

                foreach (var compId in idList)
                {
                    if (!ToolHelpers.TryGetUnprotectedComponent(_context, compId, out var component, out var compError))
                    {
                        results.Add(new { id = compId, success = false, error = compError });
                        failed++;
                        continue;
                    }

                    if (component is IGH_ActiveObject activeObj)
                    {
                        activeObj.Locked = !enabled;
                        activeObj.ExpireSolution(true);
                        results.Add(new { id = compId, success = true, enabled });
                        succeeded++;
                    }
                    else
                    {
                        results.Add(new { id = compId, success = false, error = "Component does not support enable/disable" });
                        failed++;
                    }
                }

                doc.NewSolution(false);

                // Simplified response for single item
                if (idList.Count == 1)
                    return JsonConvert.SerializeObject(results[0]);

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = idList.Count,
                    succeeded,
                    failed,
                    enabled,
                    results
                });
            });
        }

        [McpServerTool, Description("Bake geometry from a component's output to the Rhino document. Example: gh_bake(id='abc', layer='Baked')")]
        public string GhBake(
            [Description("Component GUID")] string id,
            [Description("Optional layer name to bake to (creates if doesn't exist)")] string layer = null,
            [Description("Optional name to assign to baked objects")] string name = null)
        {
            _server?.RecordCommand("bake_geometry");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                // Get or create the target layer
                int layerIndex = -1;
                bool layerCreated = false;
                if (!string.IsNullOrEmpty(layer))
                {
                    layerIndex = rhinoDoc.Layers.FindByFullPath(layer, -1);
                    if (layerIndex < 0)
                    {
                        // Create the layer
                        var newLayer = new Layer { Name = layer };
                        layerIndex = rhinoDoc.Layers.Add(newLayer);
                        layerCreated = true;
                    }
                }

                var bakedIds = new List<string>();
                var attributes = new ObjectAttributes();
                if (layerIndex >= 0)
                    attributes.LayerIndex = layerIndex;
                if (!string.IsNullOrEmpty(name))
                    attributes.Name = name;

                // Helper function to bake geometry from a param
                Action<IGH_Param> bakeFromParam = (param) =>
                {
                    if (param.VolatileDataCount == 0) return;

                    foreach (var data in param.VolatileData.AllData(true))
                    {
                        if (data is IGH_GeometricGoo goo)
                        {
                            GeometryBase geom = null;
                            var scriptVar = goo.ScriptVariable();

                            // Handle different geometry types
                            if (scriptVar is GeometryBase gb)
                            {
                                geom = gb;
                            }
                            else if (scriptVar is Box box)
                            {
                                // Convert Box to Brep for baking
                                geom = box.ToBrep();
                            }
                            else if (scriptVar is Rectangle3d rect)
                            {
                                // Convert Rectangle to PolylineCurve
                                geom = rect.ToPolyline().ToPolylineCurve();
                            }
                            else if (scriptVar is Circle circle)
                            {
                                // Convert Circle to ArcCurve
                                geom = new ArcCurve(circle);
                            }
                            else if (scriptVar is Arc arc)
                            {
                                geom = new ArcCurve(arc);
                            }
                            else if (scriptVar is Line line)
                            {
                                geom = new LineCurve(line);
                            }
                            else if (scriptVar is Polyline polyline)
                            {
                                geom = polyline.ToPolylineCurve();
                            }

                            if (geom != null)
                            {
                                var bakedGuid = rhinoDoc.Objects.Add(geom, attributes);
                                if (bakedGuid != Guid.Empty)
                                    bakedIds.Add(bakedGuid.ToString());
                            }
                        }
                    }
                };

                // Bake from component outputs
                if (component is IGH_Component ghComponent)
                {
                    foreach (var output in ghComponent.Params.Output)
                    {
                        bakeFromParam(output);
                    }
                }
                else if (component is IGH_Param paramObj)
                {
                    bakeFromParam(paramObj);
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    bakedCount = bakedIds.Count,
                    bakedIds,
                    layer,
                    layerIndex,
                    layerCreated,
                    name
                });
            });
        }

        [McpServerTool, Description("Configure a Value List component with named items. Example: gh_configure_value_list(id='abc', items='[{\"name\":\"Option1\",\"value\":\"0\"}]')")]
        public string GhConfigureValueList(
            [Description("Value List component GUID")] string id,
            [Description("JSON array of items [{name, value}] where value is the expression (usually same as index)")] string items,
            [Description("Index of initially selected item (0-based)")] int selectedIndex = 0)
        {
            _server?.RecordCommand("configure_value_list");
            return _context.ExecuteOnUiThread(() =>
            {
                // Use protected method - infrastructure components appear as "not found"
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Check if it's a Value List
                if (!(component is GH_ValueList valueList))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Component is not a Value List: {component.GetType().Name}" });
                }

                // Parse items
                List<ValueListItem> itemDefs;
                try
                {
                    itemDefs = JsonConvert.DeserializeObject<List<ValueListItem>>(items);
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Invalid items format: {ex.Message}" });
                }

                if (itemDefs == null || itemDefs.Count == 0)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Items array is empty" });
                }

                // Clear existing items and add new ones
                valueList.ListItems.Clear();

                foreach (var item in itemDefs)
                {
                    var listItem = new GH_ValueListItem(item.Name, item.Value ?? item.Name);
                    valueList.ListItems.Add(listItem);
                }

                // Select the specified item
                if (selectedIndex >= 0 && selectedIndex < valueList.ListItems.Count)
                {
                    valueList.SelectItem(selectedIndex);
                }

                // Update the component
                valueList.ExpireSolution(true);
                doc.NewSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
                    itemCount = valueList.ListItems.Count,
                    selectedIndex = valueList.ListItems.IndexOf(valueList.FirstSelectedItem),
                    outputName = valueList.NickName  // Value List output uses its nickname
                });
            });
        }

        private class ValueListItem
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }

        /// <summary>
        /// Parse a string parameter to nullable bool.
        /// Accepts "true"/"false" (case-insensitive) or null/empty for null.
        /// </summary>
        private static bool? ParseOptionalBool(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            if (bool.TryParse(value, out bool result))
                return result;

            // Also accept common variations
            if (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;

            if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;

            return null;
        }
    }
}
