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

        [McpServerTool, Description("Set a component's value (slider, panel, or parameter). For sliders, use 'min<default<max' format to set range, or just a number to set current value.")]
        public string SetComponentValue(
            [Description("Component GUID")] string id,
            [Description("Value to set. For sliders: number or 'min<default<max' format")] string value)
        {
            _server?.RecordCommand("set_component_value");
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

                // Handle different component types
                if (component is GH_Panel panel)
                {
                    panel.UserText = value;
                }
                else if (component is GH_NumberSlider slider)
                {
                    // Support both single value and range format
                    // Range format: "min<default<max" e.g., "0<2<4" or "0.0<0.5<1.0"
                    if (value.Contains("<"))
                    {
                        // Parse range format: min<default<max
                        var parts = value.Split('<').Select(s => s.Trim()).ToArray();
                        if (parts.Length == 3 &&
                            double.TryParse(parts[0], out double minVal) &&
                            double.TryParse(parts[1], out double defaultVal) &&
                            double.TryParse(parts[2], out double maxVal))
                        {
                            // Determine if this is an integer slider based on the values
                            bool isInteger = (minVal == Math.Floor(minVal)) &&
                                           (defaultVal == Math.Floor(defaultVal)) &&
                                           (maxVal == Math.Floor(maxVal)) &&
                                           !parts[0].Contains(".") &&
                                           !parts[1].Contains(".") &&
                                           !parts[2].Contains(".");

                            // Set slider properties directly
                            slider.Slider.Minimum = (decimal)minVal;
                            slider.Slider.Maximum = (decimal)maxVal;
                            slider.Slider.Value = (decimal)defaultVal;

                            // Set slider type
                            if (isInteger)
                            {
                                slider.Slider.Type = Grasshopper.GUI.Base.GH_SliderAccuracy.Integer;
                                slider.Slider.DecimalPlaces = 0;
                            }
                            else
                            {
                                slider.Slider.Type = Grasshopper.GUI.Base.GH_SliderAccuracy.Float;
                                // Determine decimal places from the format
                                int maxDecimals = 0;
                                foreach (var part in parts)
                                {
                                    int dotIndex = part.IndexOf('.');
                                    if (dotIndex >= 0)
                                    {
                                        int decimals = part.Length - dotIndex - 1;
                                        maxDecimals = Math.Max(maxDecimals, decimals);
                                    }
                                }
                                slider.Slider.DecimalPlaces = maxDecimals > 0 ? maxDecimals : 1;
                            }

                            // Update slider display
                            if (slider.Attributes != null)
                                slider.Attributes.ExpireLayout();
                        }
                        else
                        {
                            return JsonConvert.SerializeObject(new { success = false, error = $"Invalid slider range format: '{value}'. Expected 'min<default<max'" });
                        }
                    }
                    else
                    {
                        // Try to parse as number
                        if (double.TryParse(value, out double numValue))
                        {
                            // Clamp to slider range
                            decimal min = slider.Slider.Minimum;
                            decimal max = slider.Slider.Maximum;
                            decimal clampedValue = Math.Max(min, Math.Min(max, (decimal)numValue));
                            slider.SetSliderValue(clampedValue);
                        }
                        else
                        {
                            return JsonConvert.SerializeObject(new { success = false, error = $"Cannot parse '{value}' as a number for slider" });
                        }
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

        [McpServerTool, Description("Get parameter information for a component type (inputs, outputs, descriptions)")]
        public string GetComponentParameters(
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

        [McpServerTool, Description("Toggle component preview visibility on/off")]
        public string TogglePreview(
            [Description("Component GUID")] string id,
            [Description("Optional: explicitly set preview state (true=visible, false=hidden). If not provided, toggles current state.")] bool? enabled = null)
        {
            _server?.RecordCommand("toggle_preview");
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

                if (component is IGH_PreviewObject previewObj)
                {
                    bool newState = enabled ?? !previewObj.Hidden;
                    previewObj.Hidden = !newState; // Hidden is opposite of preview enabled

                    doc.NewSolution(false);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id = component.InstanceGuid.ToString(),
                        previewEnabled = !previewObj.Hidden
                    });
                }
                else
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Component does not support preview toggle" });
                }
            });
        }

        [McpServerTool, Description("Toggle component enabled/disabled state (disabled components don't compute)")]
        public string ToggleEnabled(
            [Description("Component GUID")] string id,
            [Description("Optional: explicitly set enabled state. If not provided, toggles current state.")] bool? enabled = null)
        {
            _server?.RecordCommand("toggle_enabled");
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

                if (component is IGH_ActiveObject activeObj)
                {
                    bool newState = enabled ?? activeObj.Locked;
                    activeObj.Locked = !newState; // Locked is opposite of enabled

                    activeObj.ExpireSolution(true);
                    doc.NewSolution(false);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id = component.InstanceGuid.ToString(),
                        enabled = !activeObj.Locked
                    });
                }
                else
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Component does not support enable/disable" });
                }
            });
        }

        [McpServerTool, Description("Bake geometry from a component's output to the Rhino document")]
        public string BakeGeometry(
            [Description("Component GUID")] string id,
            [Description("Optional layer name to bake to (creates if doesn't exist)")] string layer = null)
        {
            _server?.RecordCommand("bake_geometry");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Rhino document" });
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

                // Get or create the target layer
                int layerIndex = -1;
                if (!string.IsNullOrEmpty(layer))
                {
                    layerIndex = rhinoDoc.Layers.FindByFullPath(layer, -1);
                    if (layerIndex < 0)
                    {
                        // Create the layer
                        var newLayer = new Layer { Name = layer };
                        layerIndex = rhinoDoc.Layers.Add(newLayer);
                    }
                }

                var bakedIds = new List<string>();
                var attributes = new ObjectAttributes();
                if (layerIndex >= 0)
                    attributes.LayerIndex = layerIndex;

                // Helper function to bake geometry from a param
                Action<IGH_Param> bakeFromParam = (param) =>
                {
                    if (param.VolatileDataCount == 0) return;

                    foreach (var data in param.VolatileData.AllData(true))
                    {
                        if (data is IGH_GeometricGoo goo)
                        {
                            // Try to get the geometry and add it directly
                            var geom = goo.ScriptVariable() as GeometryBase;
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
                });
            });
        }

        [McpServerTool, Description("Configure a Value List component with named items")]
        public string ConfigureValueList(
            [Description("Value List component GUID")] string id,
            [Description("JSON array of items [{name, value}] where value is the expression (usually same as index)")] string items,
            [Description("Index of initially selected item (0-based)")] int selectedIndex = 0)
        {
            _server?.RecordCommand("configure_value_list");
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
    }
}
