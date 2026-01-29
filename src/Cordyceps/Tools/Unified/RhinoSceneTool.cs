using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified Rhino scene tool - object management, layers, visibility
    /// </summary>
    [McpServerToolType]
    public class RhinoSceneTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "rhino_scene",
            Description = "Rhino scene operations - objects, layers, selection, visibility",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["objects"] = new ActionInfo
                {
                    Name = "objects",
                    Description = "List objects in scene with optional filtering",
                    Optional = new[] { "type", "layer", "selected", "limit" },
                    Example = "action='objects' OR action='objects', type='brep', layer='Default'",
                    Tips = new[] { "types: brep, curve, mesh, point, surface, etc.", "limit defaults to 100" }
                },
                ["select"] = new ActionInfo
                {
                    Name = "select",
                    Description = "Select objects by ID or filter",
                    Optional = new[] { "ids", "type", "layer", "add" },
                    Example = "action='select', ids='[\"abc\"]' OR action='select', layer='Layer01'",
                    Tips = new[] { "add=true adds to selection, false replaces" }
                },
                ["deselect"] = new ActionInfo
                {
                    Name = "deselect",
                    Description = "Clear the current selection",
                    Example = "action='deselect'"
                },
                ["set_layer"] = new ActionInfo
                {
                    Name = "set_layer",
                    Description = "Move objects to a layer",
                    Required = new[] { "ids", "layer" },
                    Example = "action='set_layer', ids='[\"abc\"]', layer='NewLayer'",
                    Tips = new[] { "Creates layer if it doesn't exist" }
                },
                ["set_name"] = new ActionInfo
                {
                    Name = "set_name",
                    Description = "Set the name of objects",
                    Required = new[] { "ids", "name" },
                    Example = "action='set_name', ids='[\"abc\"]', name='MyObject'"
                },
                ["layers"] = new ActionInfo
                {
                    Name = "layers",
                    Description = "List all layers",
                    Example = "action='layers'"
                },
                ["layer_create"] = new ActionInfo
                {
                    Name = "layer_create",
                    Description = "Create a new layer",
                    Required = new[] { "name" },
                    Optional = new[] { "color", "visible", "parent" },
                    Example = "action='layer_create', name='MyLayer', color='#FF0000'"
                },
                ["layer_set"] = new ActionInfo
                {
                    Name = "layer_set",
                    Description = "Modify layer properties",
                    Required = new[] { "name" },
                    Optional = new[] { "color", "visible", "locked" },
                    Example = "action='layer_set', name='MyLayer', visible='false'"
                },
                ["layer_delete"] = new ActionInfo
                {
                    Name = "layer_delete",
                    Description = "Delete a layer",
                    Required = new[] { "name" },
                    Optional = new[] { "deleteObjects" },
                    Example = "action='layer_delete', name='MyLayer'",
                    Tips = new[] { "deleteObjects=true deletes objects, false moves to default layer" }
                },
                ["hide"] = new ActionInfo
                {
                    Name = "hide",
                    Description = "Hide objects by ID or selection",
                    Optional = new[] { "ids", "selected" },
                    Example = "action='hide', selected=true OR action='hide', ids='[\"abc\"]'"
                },
                ["show"] = new ActionInfo
                {
                    Name = "show",
                    Description = "Show hidden objects",
                    Optional = new[] { "ids", "all" },
                    Example = "action='show', all=true"
                },
                ["delete"] = new ActionInfo
                {
                    Name = "delete",
                    Description = "Delete objects by ID",
                    Required = new[] { "ids" },
                    Example = "action='delete', ids='[\"abc\",\"def\"]'"
                },
                ["script"] = new ActionInfo
                {
                    Name = "script",
                    Description = "Execute a Rhino command script",
                    Required = new[] { "cmd" },
                    Example = "action='script', cmd='_Circle 0,0,0 10'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "Object IDs are GUIDs that can be used across calls",
                "Use 'layers' to see valid layer names for filtering"
            }
        };

        public RhinoSceneTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Scene operations. Actions: objects|select|deselect|set_layer|set_name|layers|layer_create|layer_set|layer_delete|hide|show|delete|script|help")]
        public string RhinoScene(
            [Description("Action to perform")] string action,
            [Description("JSON array of object IDs")] string ids = null,
            [Description("Object type filter")] string type = null,
            [Description("Layer name filter")] string layer = null,
            [Description("Filter by selected (true/false)")] string selected = null,
            [Description("Add to selection (true/false)")] string add = null,
            [Description("Show all hidden (true/false)")] string all = null,
            [Description("Rhino command script")] string cmd = null,
            [Description("Max objects to return")] string limit = null,
            // Layer parameters
            [Description("Layer name for create/set/delete")] string name = null,
            [Description("Layer color as hex or RGB")] string color = null,
            [Description("Layer visibility (true/false)")] string visible = null,
            [Description("Parent layer name")] string parent = null,
            [Description("Layer locked state (true/false)")] string locked = null,
            [Description("Delete objects on layer (true/false)")] string deleteObjects = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("ids", ids),
                ("type", type),
                ("layer", layer),
                ("selected", selected),
                ("add", add),
                ("all", all),
                ("cmd", cmd),
                ("limit", limit),
                ("name", name),
                ("color", color),
                ("visible", visible),
                ("parent", parent),
                ("locked", locked),
                ("deleteObjects", deleteObjects)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Parse optional parameters with defaults
            bool selectedBool = ToolHelpers.ParseBool(selected, false);
            bool addBool = ToolHelpers.ParseBool(add, false);
            bool allBool = ToolHelpers.ParseBool(all, false);
            int limitInt = string.IsNullOrEmpty(limit) ? 100 : (int.TryParse(limit, out var l) ? l : 100);
            bool visibleBool = ToolHelpers.ParseBool(visible, true);
            bool deleteObjectsBool = ToolHelpers.ParseBool(deleteObjects, false);

            return action.ToLowerInvariant() switch
            {
                "objects" => ActionObjects(type, layer, selectedBool, limitInt),
                "select" => ActionSelect(ids, type, layer, addBool),
                "deselect" => ActionDeselect(),
                "set_layer" => ActionSetLayer(ids, layer),
                "set_name" => ActionSetName(ids, name),
                "layers" => ActionLayers(),
                "layer_create" => ActionLayerCreate(name, color, visibleBool, parent),
                "layer_set" => ActionLayerSet(name, color, visible, locked),
                "layer_delete" => ActionLayerDelete(name, deleteObjectsBool),
                "hide" => ActionHide(ids, selectedBool),
                "show" => ActionShow(ids, allBool),
                "delete" => ActionDelete(ids),
                "script" => ActionScript(cmd),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionObjects(string type, string layer, bool selectedOnly, int limit)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var objects = new List<object>();
                var typeFilter = ParseObjectType(type);

                foreach (var obj in doc.Objects)
                {
                    if (objects.Count >= limit) break;
                    if (obj.IsDeleted) continue;

                    if (typeFilter.HasValue && obj.ObjectType != typeFilter.Value) continue;
                    if (!string.IsNullOrEmpty(layer))
                    {
                        var objLayer = doc.Layers[obj.Attributes.LayerIndex];
                        if (!objLayer.Name.Equals(layer, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (selectedOnly && obj.IsSelected(true) == 0) continue;

                    var bbox = obj.Geometry?.GetBoundingBox(true) ?? BoundingBox.Unset;
                    objects.Add(new
                    {
                        id = obj.Id.ToString(),
                        type = obj.ObjectType.ToString(),
                        layer = doc.Layers[obj.Attributes.LayerIndex].Name,
                        name = obj.Attributes.Name ?? "",
                        visible = !obj.IsHidden,
                        selected = obj.IsSelected(true) > 0,
                        bbox = bbox.IsValid ? new { minX = bbox.Min.X, minY = bbox.Min.Y, minZ = bbox.Min.Z, maxX = bbox.Max.X, maxY = bbox.Max.Y, maxZ = bbox.Max.Z } : null
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = objects.Count,
                    truncated = objects.Count >= limit,
                    objects
                });
            });
        }

        private string ActionSelect(string ids, string type, string layer, bool add)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!add)
                    doc.Objects.UnselectAll();

                int selectedCount = 0;

                if (!string.IsNullOrEmpty(ids))
                {
                    if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                        return ToolHelpers.ErrorResponse(error);

                    foreach (var guid in guids)
                    {
                        var obj = doc.Objects.FindId(guid);
                        if (obj != null)
                        {
                            doc.Objects.Select(guid);
                            selectedCount++;
                        }
                    }
                }
                else
                {
                    var typeFilter = ParseObjectType(type);
                    foreach (var obj in doc.Objects)
                    {
                        if (obj.IsDeleted || obj.IsHidden) continue;
                        if (typeFilter.HasValue && obj.ObjectType != typeFilter.Value) continue;
                        if (!string.IsNullOrEmpty(layer))
                        {
                            var objLayer = doc.Layers[obj.Attributes.LayerIndex];
                            if (!objLayer.Name.Equals(layer, StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        doc.Objects.Select(obj.Id);
                        selectedCount++;
                    }
                }

                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true, selectedCount });
            });
        }

        private string ActionDeselect()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                doc.Objects.UnselectAll();
                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true });
            });
        }

        private string ActionSetLayer(string ids, string layer)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(layer))
                    return ToolHelpers.ErrorResponse("layer is required");

                if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Find or create layer
                var layerIndex = doc.Layers.FindByFullPath(layer, -1);
                if (layerIndex < 0)
                {
                    var newLayer = doc.Layers.FirstOrDefault(l =>
                        l.Name.Equals(layer, StringComparison.OrdinalIgnoreCase));
                    layerIndex = newLayer?.Index ?? -1;
                }
                if (layerIndex < 0)
                {
                    layerIndex = doc.Layers.Add(layer, System.Drawing.Color.Black);
                    if (layerIndex < 0)
                        return ToolHelpers.ErrorResponse($"Failed to create layer '{layer}'");
                }

                int movedCount = 0;
                foreach (var guid in guids)
                {
                    var obj = doc.Objects.FindId(guid);
                    if (obj == null) continue;

                    var attrs = obj.Attributes.Duplicate();
                    attrs.LayerIndex = layerIndex;
                    if (doc.Objects.ModifyAttributes(obj, attrs, true))
                        movedCount++;
                }

                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true, movedCount, layer });
            });
        }

        private string ActionSetName(string ids, string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("name is required");

                if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                    return ToolHelpers.ErrorResponse(error);

                int renamedCount = 0;
                foreach (var guid in guids)
                {
                    var obj = doc.Objects.FindId(guid);
                    if (obj == null) continue;

                    var attrs = obj.Attributes.Duplicate();
                    attrs.Name = name;
                    if (doc.Objects.ModifyAttributes(obj, attrs, true))
                        renamedCount++;
                }

                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true, renamedCount, name });
            });
        }

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

        private string ActionHide(string ids, bool selectedOnly)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(ids) && !selectedOnly)
                    return ToolHelpers.ErrorResponse("Either 'ids' or 'selected=true' is required for hide action");

                int hiddenCount = 0;

                if (!string.IsNullOrEmpty(ids))
                {
                    if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                        return ToolHelpers.ErrorResponse(error);

                    foreach (var guid in guids)
                    {
                        if (doc.Objects.Hide(guid, true))
                            hiddenCount++;
                    }
                }
                else if (selectedOnly)
                {
                    var selected = doc.Objects.GetSelectedObjects(false, false);
                    foreach (var obj in selected)
                    {
                        if (doc.Objects.Hide(obj.Id, true))
                            hiddenCount++;
                    }
                }

                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true, hiddenCount });
            });
        }

        private string ActionShow(string ids, bool showAll)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(ids) && !showAll)
                    return ToolHelpers.ErrorResponse("Either 'ids' or 'all=true' is required for show action");

                int shownCount = 0;

                if (showAll)
                {
                    foreach (var obj in doc.Objects)
                    {
                        if (obj.IsHidden && doc.Objects.Show(obj.Id, true))
                            shownCount++;
                    }
                }
                else if (!string.IsNullOrEmpty(ids))
                {
                    if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                        return ToolHelpers.ErrorResponse(error);

                    foreach (var guid in guids)
                    {
                        if (doc.Objects.Show(guid, true))
                            shownCount++;
                    }
                }

                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true, shownCount });
            });
        }

        private string ActionDelete(string ids)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                    return ToolHelpers.ErrorResponse(error);

                int deletedCount = 0;
                foreach (var guid in guids)
                {
                    if (doc.Objects.Delete(guid, true))
                        deletedCount++;
                }

                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true, deletedCount });
            });
        }

        private string ActionScript(string cmd)
        {
            if (string.IsNullOrEmpty(cmd))
                return ToolHelpers.ErrorResponse("cmd is required");

            return _context.ExecuteOnUiThread(() =>
            {
                bool result = RhinoApp.RunScript(cmd, false);
                return JsonConvert.SerializeObject(new { success = result, cmd });
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
                    isVisible = created.IsVisible
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
                    isVisible = targetLayer.IsVisible,
                    isLocked = targetLayer.IsLocked,
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

        private ObjectType? ParseObjectType(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            return type.ToLowerInvariant() switch
            {
                "brep" => ObjectType.Brep,
                "curve" => ObjectType.Curve,
                "mesh" => ObjectType.Mesh,
                "point" => ObjectType.Point,
                "surface" => ObjectType.Surface,
                "annotation" => ObjectType.Annotation,
                "extrusion" => ObjectType.Extrusion,
                "subd" => ObjectType.SubD,
                "pointcloud" => ObjectType.PointSet,
                "hatch" => ObjectType.Hatch,
                "light" => ObjectType.Light,
                _ => null
            };
        }
    }
}
