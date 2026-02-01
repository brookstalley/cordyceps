using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;

namespace Cordyceps.Tools.Unified
{
    public partial class RhinoSceneTool
    {
        #region Layer Actions

        private string ActionLayers()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var layers = doc.Layers
                    .Where(l => !l.IsDeleted)
                    .Select(l => new
                    {
                        name = l.Name,
                        fullPath = l.FullPath,
                        index = l.Index,
                        visible = l.IsVisible,
                        locked = l.IsLocked,
                        color = $"#{l.Color.R:X2}{l.Color.G:X2}{l.Color.B:X2}",
                        objectCount = doc.Objects.FindByLayer(l).Length
                    }).ToList();

                return JsonConvert.SerializeObject(new { success = true, count = layers.Count, layers });
            });
        }

        private string ActionLayerCreate(string name, string color, bool visible, string parent)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Layer name is required");

                var existingIndex = doc.Layers.FindByFullPath(name, -1);
                if (existingIndex >= 0)
                {
                    var existing = doc.Layers[existingIndex];
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = false,
                        alreadyExists = true,
                        index = existingIndex,
                        name = existing.Name,
                        fullPath = existing.FullPath
                    });
                }

                var layer = new Layer { Name = name, IsVisible = visible };

                if (!string.IsNullOrEmpty(color))
                {
                    if (ToolHelpers.TryParseColor(color, out var parsedColor))
                        layer.Color = parsedColor;
                    else
                        return ToolHelpers.ErrorResponse($"Invalid color: {color}");
                }

                if (!string.IsNullOrEmpty(parent))
                {
                    var parentIndex = doc.Layers.FindByFullPath(parent, -1);
                    if (parentIndex < 0)
                    {
                        var parentLayer = doc.Layers.FirstOrDefault(l =>
                            l.Name.Equals(parent, StringComparison.OrdinalIgnoreCase));
                        parentIndex = parentLayer?.Index ?? -1;
                    }
                    if (parentIndex < 0)
                        return ToolHelpers.ErrorResponse($"Parent layer '{parent}' not found");
                    layer.ParentLayerId = doc.Layers[parentIndex].Id;
                }

                var index = doc.Layers.Add(layer);
                if (index < 0)
                    return ToolHelpers.ErrorResponse("Failed to create layer");

                var created = doc.Layers[index];
                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    created = true,
                    index,
                    name = created.Name,
                    fullPath = created.FullPath,
                    color = ToolHelpers.ColorToHex(created.Color),
                    visible = created.IsVisible
                });
            });
        }

        private string ActionLayerSet(string name, string color, string visible, string locked)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Layer name is required");

                var layerIndex = doc.Layers.FindByFullPath(name, -1);
                if (layerIndex < 0)
                {
                    var layer = doc.Layers.FirstOrDefault(l =>
                        l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    layerIndex = layer?.Index ?? -1;
                }

                if (layerIndex < 0)
                    return ToolHelpers.ErrorResponse($"Layer '{name}' not found");

                var targetLayer = doc.Layers[layerIndex];
                var modified = new List<string>();

                if (!string.IsNullOrEmpty(color))
                {
                    if (ToolHelpers.TryParseColor(color, out var parsedColor))
                    {
                        targetLayer.Color = parsedColor;
                        modified.Add("color");
                    }
                    else
                        return ToolHelpers.ErrorResponse($"Invalid color: {color}");
                }

                if (!string.IsNullOrEmpty(visible))
                {
                    if (bool.TryParse(visible, out var val))
                    {
                        targetLayer.IsVisible = val;
                        modified.Add("visible");
                    }
                    else
                        return ToolHelpers.ErrorResponse($"Invalid visible: {visible}");
                }

                if (!string.IsNullOrEmpty(locked))
                {
                    if (bool.TryParse(locked, out var val))
                    {
                        targetLayer.IsLocked = val;
                        modified.Add("locked");
                    }
                    else
                        return ToolHelpers.ErrorResponse($"Invalid locked: {locked}");
                }

                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    index = layerIndex,
                    name = targetLayer.Name,
                    fullPath = targetLayer.FullPath,
                    color = ToolHelpers.ColorToHex(targetLayer.Color),
                    visible = targetLayer.IsVisible,
                    locked = targetLayer.IsLocked,
                    modified
                });
            });
        }

        private string ActionLayerDelete(string name, bool deleteObjects)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Layer name is required");

                var layerIndex = doc.Layers.FindByFullPath(name, -1);
                if (layerIndex < 0)
                {
                    var layer = doc.Layers.FirstOrDefault(l =>
                        l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    layerIndex = layer?.Index ?? -1;
                }

                if (layerIndex < 0)
                    return ToolHelpers.ErrorResponse($"Layer '{name}' not found");

                var targetLayer = doc.Layers[layerIndex];
                var layerName = targetLayer.Name;
                var objectsOnLayer = doc.Objects.FindByLayer(targetLayer);

                int objectsDeleted = 0, objectsMoved = 0;

                if (deleteObjects)
                {
                    foreach (var obj in objectsOnLayer)
                    {
                        if (doc.Objects.Delete(obj, true))
                            objectsDeleted++;
                    }
                }
                else
                {
                    var defaultLayerIndex = doc.Layers.CurrentLayerIndex;
                    if (defaultLayerIndex == layerIndex)
                        defaultLayerIndex = doc.Layers.FirstOrDefault(l => l.Index != layerIndex && !l.IsDeleted)?.Index ?? 0;

                    foreach (var obj in objectsOnLayer)
                    {
                        var attrs = obj.Attributes.Duplicate();
                        attrs.LayerIndex = defaultLayerIndex;
                        if (doc.Objects.ModifyAttributes(obj, attrs, true))
                            objectsMoved++;
                    }
                }

                var deleted = doc.Layers.Delete(layerIndex, true);
                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = deleted,
                    layerDeleted = deleted,
                    layerName,
                    objectsDeleted,
                    objectsMoved,
                    error = deleted ? null : "Failed to delete layer (may have child layers)"
                });
            });
        }

        #endregion
    }
}
