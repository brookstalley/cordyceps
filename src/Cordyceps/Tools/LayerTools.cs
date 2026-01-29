using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Rhino document layer operations (create, modify, query, delete)
    /// </summary>
    [McpServerToolType]
    public class LayerTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public LayerTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("List all layers in the Rhino document with their properties.")]
        public string RhinoGetLayers()
        {
            _server?.RecordCommand("rhino_get_layers");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var layers = new List<object>();

                foreach (var layer in rhinoDoc.Layers)
                {
                    if (layer.IsDeleted) continue;

                    // Count objects on this layer
                    var objectCount = rhinoDoc.Objects.FindByLayer(layer).Length;

                    layers.Add(new
                    {
                        index = layer.Index,
                        name = layer.Name,
                        fullPath = layer.FullPath,
                        color = ToolHelpers.ColorToHex(layer.Color),
                        isVisible = layer.IsVisible,
                        isLocked = layer.IsLocked,
                        parentIndex = layer.ParentLayerId == Guid.Empty ? -1 : rhinoDoc.Layers.FindId(layer.ParentLayerId)?.Index ?? -1,
                        objectCount
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = layers.Count,
                    layers
                });
            });
        }

        [McpServerTool, Description("Create a new layer in the Rhino document.")]
        public string RhinoCreateLayer(
            [Description("Layer name")] string name,
            [Description("Layer color as hex (e.g., '#FF0000') or RGB (e.g., '255,0,0')")] string color = null,
            [Description("Layer visibility (default: true)")] bool visible = true,
            [Description("Parent layer name for nesting (optional)")] string parent = null)
        {
            _server?.RecordCommand("rhino_create_layer");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Layer name is required");

                // Check if layer already exists
                var existingIndex = rhinoDoc.Layers.FindByFullPath(name, -1);
                if (existingIndex >= 0)
                {
                    var existing = rhinoDoc.Layers[existingIndex];
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

                // Create new layer
                var layer = new Layer { Name = name };

                // Set color if provided
                if (!string.IsNullOrEmpty(color))
                {
                    if (ToolHelpers.TryParseColor(color, out var parsedColor))
                        layer.Color = parsedColor;
                    else
                        return ToolHelpers.ErrorResponse($"Invalid color format: {color}. Use hex '#RRGGBB' or RGB '255,128,0'");
                }

                // Set visibility
                layer.IsVisible = visible;

                // Set parent if provided
                if (!string.IsNullOrEmpty(parent))
                {
                    var parentIndex = rhinoDoc.Layers.FindByFullPath(parent, -1);
                    if (parentIndex < 0)
                    {
                        // Try to find by name
                        var parentLayer = rhinoDoc.Layers.FirstOrDefault(l =>
                            l.Name.Equals(parent, StringComparison.OrdinalIgnoreCase));
                        if (parentLayer != null)
                            parentIndex = parentLayer.Index;
                    }

                    if (parentIndex >= 0)
                    {
                        layer.ParentLayerId = rhinoDoc.Layers[parentIndex].Id;
                    }
                    else
                    {
                        return ToolHelpers.ErrorResponse($"Parent layer '{parent}' not found");
                    }
                }

                var index = rhinoDoc.Layers.Add(layer);
                if (index < 0)
                    return ToolHelpers.ErrorResponse("Failed to create layer");

                var createdLayer = rhinoDoc.Layers[index];

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    created = true,
                    index,
                    name = createdLayer.Name,
                    fullPath = createdLayer.FullPath,
                    color = ToolHelpers.ColorToHex(createdLayer.Color),
                    isVisible = createdLayer.IsVisible
                });
            });
        }

        [McpServerTool, Description("Modify properties of an existing Rhino layer.")]
        public string RhinoSetLayerProperties(
            [Description("Layer name or full path")] string name,
            [Description("New color as hex or RGB (optional)")] string color = null,
            [Description("Visibility state (optional)")] string visible = null,
            [Description("Locked state (optional)")] string locked = null)
        {
            _server?.RecordCommand("rhino_set_layer_properties");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Layer name is required");

                // Find layer
                var layerIndex = rhinoDoc.Layers.FindByFullPath(name, -1);
                if (layerIndex < 0)
                {
                    // Try partial match
                    var layer = rhinoDoc.Layers.FirstOrDefault(l =>
                        l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (layer != null)
                        layerIndex = layer.Index;
                }

                if (layerIndex < 0)
                    return ToolHelpers.ErrorResponse($"Layer '{name}' not found");

                var targetLayer = rhinoDoc.Layers[layerIndex];
                var modified = new List<string>();

                // Update color
                if (!string.IsNullOrEmpty(color))
                {
                    if (ToolHelpers.TryParseColor(color, out var parsedColor))
                    {
                        targetLayer.Color = parsedColor;
                        modified.Add("color");
                    }
                    else
                    {
                        return ToolHelpers.ErrorResponse($"Invalid color format: {color}");
                    }
                }

                // Update visibility
                if (!string.IsNullOrEmpty(visible))
                {
                    if (bool.TryParse(visible, out var visibleBool))
                    {
                        targetLayer.IsVisible = visibleBool;
                        modified.Add("visible");
                    }
                    else
                    {
                        return ToolHelpers.ErrorResponse($"Invalid visible value: {visible}. Use 'true' or 'false'");
                    }
                }

                // Update locked
                if (!string.IsNullOrEmpty(locked))
                {
                    if (bool.TryParse(locked, out var lockedBool))
                    {
                        targetLayer.IsLocked = lockedBool;
                        modified.Add("locked");
                    }
                    else
                    {
                        return ToolHelpers.ErrorResponse($"Invalid locked value: {locked}. Use 'true' or 'false'");
                    }
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    index = layerIndex,
                    name = targetLayer.Name,
                    fullPath = targetLayer.FullPath,
                    color = ToolHelpers.ColorToHex(targetLayer.Color),
                    isVisible = targetLayer.IsVisible,
                    isLocked = targetLayer.IsLocked,
                    modified
                });
            });
        }

        [McpServerTool, Description("Delete a layer from the Rhino document.")]
        public string RhinoDeleteLayer(
            [Description("Layer name or full path")] string name,
            [Description("Delete objects on the layer (default: false, moves to default layer)")] bool deleteObjects = false)
        {
            _server?.RecordCommand("rhino_delete_layer");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Layer name is required");

                // Find layer
                var layerIndex = rhinoDoc.Layers.FindByFullPath(name, -1);
                if (layerIndex < 0)
                {
                    var layer = rhinoDoc.Layers.FirstOrDefault(l =>
                        l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (layer != null)
                        layerIndex = layer.Index;
                }

                if (layerIndex < 0)
                    return ToolHelpers.ErrorResponse($"Layer '{name}' not found");

                var targetLayer = rhinoDoc.Layers[layerIndex];
                var layerName = targetLayer.Name;  // Capture name before deletion
                var objectsOnLayer = rhinoDoc.Objects.FindByLayer(targetLayer);

                // Handle objects on layer
                int objectsDeleted = 0;
                int objectsMoved = 0;

                if (deleteObjects)
                {
                    foreach (var obj in objectsOnLayer)
                    {
                        if (rhinoDoc.Objects.Delete(obj, true))
                            objectsDeleted++;
                    }
                }
                else
                {
                    // Move objects to default layer
                    var defaultLayerIndex = rhinoDoc.Layers.CurrentLayerIndex;
                    if (defaultLayerIndex == layerIndex)
                    {
                        // Find another layer to move to
                        defaultLayerIndex = rhinoDoc.Layers.FirstOrDefault(l => l.Index != layerIndex && !l.IsDeleted)?.Index ?? 0;
                    }

                    foreach (var obj in objectsOnLayer)
                    {
                        var attrs = obj.Attributes.Duplicate();
                        attrs.LayerIndex = defaultLayerIndex;
                        if (rhinoDoc.Objects.ModifyAttributes(obj, attrs, true))
                            objectsMoved++;
                    }
                }

                // Delete the layer
                var deleted = rhinoDoc.Layers.Delete(layerIndex, true);

                rhinoDoc.Views.Redraw();

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

    }
}
