using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Newtonsoft.Json;

namespace Cordyceps.Tools.Unified
{
    public partial class GhCanvasTool
    {
        #region Value Adjustment Actions

        private string ActionGet(string id)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponent(_context, id, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                object currentValue = null;
                string valueType = "unknown";

                if (component is GH_NumberSlider slider)
                {
                    currentValue = slider.CurrentValue;
                    valueType = "slider";
                }
                else if (component is GH_Panel panel)
                {
                    currentValue = panel.UserText;
                    valueType = "panel";
                }
                else if (component is GH_BooleanToggle toggle)
                {
                    currentValue = toggle.Value;
                    valueType = "toggle";
                }
                else if (component is GH_ValueList valueList)
                {
                    var selected = valueList.SelectedItems.FirstOrDefault();
                    currentValue = selected?.Name ?? "(none)";
                    valueType = "value_list";
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id,
                    name = component.Name,
                    nickname = component.NickName,
                    valueType,
                    value = currentValue
                });
            });
        }

        private string ActionSet(string id, string value)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (component is GH_Panel panel)
                {
                    panel.UserText = value;
                }
                else if (component is GH_NumberSlider slider)
                {
                    if (!double.TryParse(value, out double numValue))
                        return ToolHelpers.ErrorResponse($"Invalid number: {value}");

                    if (numValue < (double)slider.Slider.Minimum || numValue > (double)slider.Slider.Maximum)
                        return ToolHelpers.ErrorResponse($"Value {numValue} out of range [{slider.Slider.Minimum}, {slider.Slider.Maximum}]");

                    slider.SetSliderValue((decimal)numValue);
                }
                else if (component is GH_BooleanToggle toggle)
                {
                    toggle.Value = value.ToLower() == "true" || value == "1";
                }
                else if (component is GH_ValueList valueList)
                {
                    var item = valueList.ListItems.FirstOrDefault(i =>
                        i.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                        i.Expression.Equals(value, StringComparison.OrdinalIgnoreCase));

                    if (item == null)
                        return ToolHelpers.ErrorResponse($"Value '{value}' not found in list. Available: {string.Join(", ", valueList.ListItems.Select(i => i.Name))}");

                    valueList.SelectItem(valueList.ListItems.IndexOf(item));
                }
                else
                {
                    return ToolHelpers.ErrorResponse($"Cannot set value on {component.GetType().Name}");
                }

                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id,
                    type = component.Name,
                    value
                });
            });
        }

        private string ActionConfig(string id, double min, double max, string value, int decimals, string items)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out var doc, out var component, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (component is GH_NumberSlider slider)
                {
                    if (!double.IsNaN(min)) slider.Slider.Minimum = (decimal)min;
                    if (!double.IsNaN(max)) slider.Slider.Maximum = (decimal)max;
                    if (decimals >= 0) slider.Slider.DecimalPlaces = decimals;
                    if (!string.IsNullOrEmpty(value) && double.TryParse(value, out double val))
                        slider.SetSliderValue((decimal)val);

                    doc.NewSolution(true);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id,
                        type = "slider",
                        min = (double)slider.Slider.Minimum,
                        max = (double)slider.Slider.Maximum,
                        value = (double)slider.CurrentValue,
                        decimals = slider.Slider.DecimalPlaces
                    });
                }
                else if (component is GH_ValueList valueList && !string.IsNullOrEmpty(items))
                {
                    try
                    {
                        var itemList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(items);
                        valueList.ListItems.Clear();

                        foreach (var item in itemList)
                        {
                            var itemName = item.ContainsKey("name") ? item["name"] : "Item";
                            var expr = item.ContainsKey("value") ? item["value"] : "0";
                            valueList.ListItems.Add(new GH_ValueListItem(itemName, expr));
                        }

                        if (valueList.ListItems.Count > 0)
                            valueList.SelectItem(0);

                        doc.NewSolution(true);

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            id,
                            type = "value_list",
                            itemCount = valueList.ListItems.Count,
                            items = valueList.ListItems.Select(i => new { name = i.Name, value = i.Expression }).ToList()
                        });
                    }
                    catch (Exception ex)
                    {
                        return ToolHelpers.ErrorResponse($"Failed to parse items: {ex.Message}");
                    }
                }
                else
                {
                    return ToolHelpers.ErrorResponse($"Cannot configure {component.GetType().Name}. Use config for sliders and value lists.");
                }
            });
        }

        private string ActionPreview(string id, string ids, bool enabled)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                List<string> idList = BuildIdList(id, ids, out error);
                if (idList == null) return ToolHelpers.ErrorResponse(error);

                int changed = 0;
                foreach (var compId in idList)
                {
                    if (ToolHelpers.TryGetUnprotectedComponent(_context, compId, out var comp, out _))
                    {
                        if (comp is IGH_PreviewObject preview)
                        {
                            preview.Hidden = !enabled;
                            changed++;
                        }
                    }
                }

                Grasshopper.Instances.ActiveCanvas?.Invalidate();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    previewEnabled = enabled,
                    changedCount = changed
                });
            });
        }

        private string ActionEnable(string id, string ids, bool enabled)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                List<string> idList = BuildIdList(id, ids, out error);
                if (idList == null) return ToolHelpers.ErrorResponse(error);

                int changed = 0;
                foreach (var compId in idList)
                {
                    if (ToolHelpers.TryGetUnprotectedComponent(_context, compId, out var comp, out _))
                    {
                        if (comp is IGH_ActiveObject active)
                        {
                            active.Locked = !enabled;
                            changed++;
                        }
                    }
                }

                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled,
                    changedCount = changed
                });
            });
        }

        private List<string> BuildIdList(string id, string ids, out string error)
        {
            error = null;
            if (!string.IsNullOrEmpty(ids))
            {
                try { return JsonConvert.DeserializeObject<List<string>>(ids); }
                catch (Exception) { error = "Invalid ids JSON array"; return null; }
            }
            if (!string.IsNullOrEmpty(id))
                return new List<string> { id };

            error = "Either 'id' or 'ids' is required";
            return null;
        }

        #endregion
    }
}
