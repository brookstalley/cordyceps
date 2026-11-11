using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
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
                    // Read without SelectedItems, which selects item 0 itself when a dropdown has
                    // nothing selected: a read must not change the document. A dropdown, sequence
                    // or cycle with nothing selected outputs item 0, so that is what it reports;
                    // a check list with nothing checked outputs nothing.
                    var selected = valueList.ListMode == GH_ValueListMode.CheckList
                        ? valueList.ListItems.FirstOrDefault(i => i.Selected)
                        : valueList.FirstSelectedItem;
                    currentValue = selected?.Name ?? "(none)";
                    valueType = "value_list";
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = component.InstanceGuid.ToString(),
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

                // The change is worked out and validated in full before the edit opens, so a
                // refused value never reaches it. A value equal to the current one changes nothing
                // and records nothing, so the call pushes no undo step.
                Action apply;
                bool unchanged;
                if (component is GH_Panel panel)
                {
                    unchanged = panel.UserText == (value ?? string.Empty);
                    apply = () => panel.UserText = value;
                }
                else if (component is GH_NumberSlider slider)
                {
                    // InvariantCulture: MCP/JSON-sourced numeric strings are always invariant;
                    // a current-culture parse breaks on hosts where ',' is the decimal separator
                    // (same locale bug Core.SliderConfig fixed for 'add'/'config').
                    if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out double numValue)
                        || double.IsNaN(numValue) || double.IsInfinity(numValue))
                        return ToolHelpers.ErrorResponse($"Invalid number: {value}");

                    if (numValue < (double)slider.Slider.Minimum || numValue > (double)slider.Slider.Maximum)
                        return ToolHelpers.ErrorResponse($"Value {numValue} out of range [{slider.Slider.Minimum}, {slider.Slider.Maximum}]");

                    // Within the slider's decimal range, so the conversion cannot overflow.
                    var target = (decimal)numValue;
                    unchanged = slider.Slider.Value == target;
                    apply = () => slider.SetSliderValue(target);
                }
                else if (component is GH_BooleanToggle toggle)
                {
                    // Strict, like 'enabled': an unrecognised word is an error, never read as false.
                    if (!ToolHelpers.TryParseBool(value, out bool state))
                        return ToolHelpers.ErrorResponse($"Invalid toggle value: '{value}'. Use true/false, 1/0, or yes/no.");
                    unchanged = toggle.Value == state;
                    apply = () => toggle.Value = state;
                }
                else if (component is GH_ValueList valueList)
                {
                    var item = valueList.ListItems.FirstOrDefault(i =>
                        i.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                        i.Expression.Equals(value, StringComparison.OrdinalIgnoreCase));

                    if (item == null)
                        return ToolHelpers.ErrorResponse($"Value '{value}' not found in list. Available: {string.Join(", ", valueList.ListItems.Select(i => i.Name))}");

                    int index = valueList.ListItems.IndexOf(item);
                    unchanged = valueList.ListItems.Select((it, i) => it.Selected == (i == index)).All(same => same);
                    apply = () => SelectOnly(valueList, index);
                }
                else
                {
                    return ToolHelpers.ErrorResponse($"Cannot set value on {component.GetType().Name}");
                }

                // Recording a generic change expires the control, so it recomputes at commit.
                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "set"), tx =>
                {
                    if (!unchanged)
                    {
                        tx.RecordGeneric(component);
                        apply();
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        id = component.InstanceGuid.ToString(),
                        type = component.Name,
                        value
                    });
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
                    // Shares Core.SliderConfig with the 'add' action so both configure a slider
                    // identically (GHC-7X4B). Range is applied before value because SetSliderValue
                    // clamps to the current range.
                    SliderConfigPlan plan;
                    try
                    {
                        plan = SliderConfig.Plan(min, max, value, decimals);
                    }
                    catch (OverflowException)
                    {
                        // A slider holds decimals: a min, max or value beyond decimal's range, or
                        // not a finite number, cannot be stored, and is refused before any change.
                        return ToolHelpers.ErrorResponse($"min, max and value must be finite numbers within ±{decimal.MaxValue.ToString("E2", CultureInfo.InvariantCulture)}");
                    }

                    // Parity with action='set': an unparseable value is an explicit error, applied
                    // before any mutation — never a silent success with the value ignored.
                    if (plan.ValueInvalid)
                        return ToolHelpers.ErrorResponse($"Invalid number: {value}");

                    // A setting equal to the current one changes nothing; when none differs, the
                    // call records nothing and pushes no undo step.
                    bool changes = (plan.SetMinimum && plan.Minimum != slider.Slider.Minimum)
                        || (plan.SetMaximum && plan.Maximum != slider.Slider.Maximum)
                        || (plan.SetDecimals && plan.Decimals != slider.Slider.DecimalPlaces)
                        || (plan.SetValue && plan.Value != slider.Slider.Value);

                    return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "config"), tx =>
                    {
                        if (changes)
                        {
                            tx.RecordGeneric(slider);
                            if (plan.SetMinimum) slider.Slider.Minimum = plan.Minimum;
                            if (plan.SetMaximum) slider.Slider.Maximum = plan.Maximum;
                            if (plan.SetDecimals) slider.Slider.DecimalPlaces = plan.Decimals;
                            if (plan.SetValue) slider.SetSliderValue(plan.Value);
                        }

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            id = component.InstanceGuid.ToString(),
                            type = "slider",
                            min = (double)slider.Slider.Minimum,
                            max = (double)slider.Slider.Maximum,
                            value = (double)slider.CurrentValue,
                            decimals = slider.Slider.DecimalPlaces
                        });
                    });
                }
                else if (component is GH_ValueList valueList)
                {
                    if (string.IsNullOrEmpty(items))
                        return ToolHelpers.ErrorResponse("Configuring a value list needs 'items', e.g. items='[{\"name\":\"A\",\"value\":\"1\"}]'");

                    // Every item is checked before the list is touched: replacing the items is
                    // clear-then-add, and a bad item found partway would leave the list empty.
                    if (!ValueListItems.TryParse(items, out var specs, out var itemsError))
                        return ToolHelpers.ErrorResponse(itemsError);

                    // The selection survives the replace by name where its item still exists. The
                    // flags are read straight from the items: SelectedItems selects item 0 itself
                    // when a dropdown has nothing selected, a change outside any record.
                    var selection = new HashSet<int>(ValueListItems.SelectionAfterReplace(
                        valueList.ListItems.Where(i => i.Selected).Select(i => i.Name).ToList(),
                        specs.Select(s => s.Name).ToList(),
                        multiSelect: valueList.ListMode == GH_ValueListMode.CheckList));

                    // The same items with the same selection change nothing and record nothing.
                    bool unchanged = valueList.ListItems.Count == specs.Count
                        && specs.Select((s, i) =>
                            s.Name == valueList.ListItems[i].Name
                            && s.Expression == valueList.ListItems[i].Expression
                            && valueList.ListItems[i].Selected == selection.Contains(i)).All(same => same);

                    return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "config"), tx =>
                    {
                        if (!unchanged)
                        {
                            // The selection is set on the items directly, not through SelectItem,
                            // which pushes an undo record of its own and starts a solve.
                            tx.RecordGeneric(valueList);
                            valueList.ListItems.Clear();
                            for (int i = 0; i < specs.Count; i++)
                            {
                                valueList.ListItems.Add(new GH_ValueListItem(specs[i].Name, specs[i].Expression)
                                {
                                    Selected = selection.Contains(i)
                                });
                            }
                            valueList.Attributes?.ExpireLayout();
                        }

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            id = component.InstanceGuid.ToString(),
                            type = "value_list",
                            itemCount = valueList.ListItems.Count,
                            items = valueList.ListItems.Select(i => new { name = i.Name, value = i.Expression }).ToList()
                        });
                    });
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

                // Per-id results (mirrors ActionDelete): unresolvable ids and components that
                // don't support preview are reported as failures, not silently skipped.
                // Every id resolves against one lookup before the first change, and a refused id
                // never reaches a recorder; the ids that change commit as one undo step.
                var outcomes = ToolHelpers.ResolveIds(new ObjectLookup(doc), idList, acceptGroups: false, Core.Canvas.ResolveIntent.Ordinary, out var resolved);

                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "preview"), tx =>
                {
                    var results = new List<object>();
                    var applied = new HashSet<Guid>();
                    int changed = 0, failed = 0;
                    foreach (var outcome in outcomes)
                    {
                        if (!outcome.IsResolved)
                        {
                            results.Add(new { id = outcome.Text, success = false, error = outcome.Error });
                            failed++;
                            continue;
                        }
                        if (!outcome.ActOn)
                        {
                            AddRepeatResult(results, outcome, applied, ref failed);
                            continue;
                        }

                        var comp = resolved[outcome.Id.Value];
                        // Grasshopper's preview undo action is defined on active objects, so an
                        // object must be both to be shown or hidden here.
                        if (comp is IGH_PreviewObject preview && comp is IGH_ActiveObject active)
                        {
                            // One already in the requested state counts as changed, as it always
                            // has, but records nothing.
                            if (preview.Hidden != !enabled)
                            {
                                tx.RecordPreview(active);
                                preview.Hidden = !enabled;
                            }
                            changed++;
                            applied.Add(comp.InstanceGuid);
                            results.Add(new { id = comp.InstanceGuid.ToString(), success = true });
                        }
                        else
                        {
                            results.Add(new { id = outcome.Text, success = false, error = $"Component '{comp.NickName}' ({comp.GetType().Name}) does not support preview" });
                            failed++;
                        }
                    }

                    Grasshopper.Instances.ActiveCanvas?.Invalidate();

                    return JsonConvert.SerializeObject(new
                    {
                        success = failed == 0,
                        previewEnabled = enabled,
                        changedCount = changed,
                        failedCount = failed,
                        results,
                        error = failed > 0 ? $"{failed} of {idList.Count} id(s) failed — see results" : null
                    });
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

                // Per-id results (mirrors ActionDelete): unresolvable ids and components that
                // can't be enabled/disabled are reported as failures, not silently skipped.
                // Every id resolves against one lookup before the first change, and a refused id
                // never reaches a recorder; the ids that change commit as one undo step.
                var outcomes = ToolHelpers.ResolveIds(new ObjectLookup(doc), idList, acceptGroups: false, Core.Canvas.ResolveIntent.Ordinary, out var resolved);

                return _context.Edit(doc, EditReplies.RecordName("gh_canvas", "enable"), tx =>
                {
                    var results = new List<object>();
                    var applied = new HashSet<Guid>();
                    int changed = 0, failed = 0;
                    foreach (var outcome in outcomes)
                    {
                        if (!outcome.IsResolved)
                        {
                            results.Add(new { id = outcome.Text, success = false, error = outcome.Error });
                            failed++;
                            continue;
                        }
                        if (!outcome.ActOn)
                        {
                            AddRepeatResult(results, outcome, applied, ref failed);
                            continue;
                        }

                        var comp = resolved[outcome.Id.Value];
                        if (comp is IGH_ActiveObject active)
                        {
                            // Recording expires the object, so every toggled one recomputes at
                            // commit. One already in the requested state counts as changed, as it
                            // always has, but records and recomputes nothing.
                            if (active.Locked != !enabled)
                            {
                                tx.RecordEnabled(active);
                                active.Locked = !enabled;
                            }
                            changed++;
                            applied.Add(comp.InstanceGuid);
                            results.Add(new { id = comp.InstanceGuid.ToString(), success = true });
                        }
                        else
                        {
                            results.Add(new { id = outcome.Text, success = false, error = $"Component '{comp.NickName}' ({comp.GetType().Name}) does not support enable/disable" });
                            failed++;
                        }
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = failed == 0,
                        enabled,
                        changedCount = changed,
                        failedCount = failed,
                        results,
                        error = failed > 0 ? $"{failed} of {idList.Count} id(s) failed — see results" : null
                    });
                });
            });
        }

        /// <summary>
        /// Select only the item at <paramref name="index"/>. Done on the items, not through
        /// <c>GH_ValueList.SelectItem</c>, which pushes an undo record of its own and starts a
        /// solve; inside an edit the transaction records the change and schedules the solve.
        /// </summary>
        private static void SelectOnly(GH_ValueList valueList, int index)
        {
            for (int i = 0; i < valueList.ListItems.Count; i++)
                valueList.ListItems[i].Selected = i == index;
        }

        /// <summary>
        /// The result for an entry that names the same object as an earlier one: the object was
        /// acted on once, by that entry, so this one reports that entry's outcome with a note.
        /// </summary>
        private static void AddRepeatResult(List<object> results, IdOutcome outcome, HashSet<Guid> applied, ref int failed)
        {
            if (applied.Contains(outcome.Id.Value))
            {
                results.Add(new { id = outcome.Id.Value.ToString(), success = true, note = outcome.Note });
            }
            else
            {
                results.Add(new { id = outcome.Text, success = false, error = $"{outcome.Note}, which failed", note = outcome.Note });
                failed++;
            }
        }

        private List<string> BuildIdList(string id, string ids, out string error)
        {
            error = null;
            if (!string.IsNullOrEmpty(ids))
            {
                List<string> list;
                try { list = JsonConvert.DeserializeObject<List<string>>(ids); }
                catch (JsonException) { error = "Invalid ids JSON array"; return null; }
                if (list == null) error = "Invalid ids JSON array";
                return list;
            }
            if (!string.IsNullOrEmpty(id))
                return new List<string> { id };

            error = "Either 'id' or 'ids' is required";
            return null;
        }

        #endregion
    }
}
