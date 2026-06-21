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
    public partial class RhinoSceneTool
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
                    Optional = new[] { "type", "layer", "objectName", "selected", "includeHidden", "limit" },
                    Example = "action='objects' OR action='objects', type='brep', layer='Default'",
                    Tips = new[] { "types: brep, curve, mesh, point, surface, etc.", "limit defaults to 100", "includeHidden defaults to true" }
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
                ["set_color"] = new ActionInfo
                {
                    Name = "set_color",
                    Description = "Set the display color of objects",
                    Required = new[] { "ids", "color" },
                    Example = "action='set_color', ids='[\"abc\"]', color='#FF0000'",
                    Tips = new[] { "Color as hex '#RRGGBB' or RGB 'R,G,B'" }
                },
                ["bbox"] = new ActionInfo
                {
                    Name = "bbox",
                    Description = "Get the combined bounding box of objects",
                    Required = new[] { "ids" },
                    Example = "action='bbox', ids='[\"abc\",\"def\"]'",
                    Tips = new[] { "Returns min, max, center, and size" }
                },
                ["place_image"] = new ActionInfo
                {
                    Name = "place_image",
                    Description = "Place a raster image into the scene as a PictureFrame object",
                    Required = new[] { "path", "x", "y", "z", "width", "height" },
                    Optional = new[] { "rotation", "layer", "name", "replace", "selfIllumination", "embedBitmap", "asMesh" },
                    Example = "action='place_image', path='/tmp/art.png', x=0, y=0, z=0, width=200, height=150",
                    Tips = new[]
                    {
                        "path must be an absolute path to an existing image (png/jpg/...)",
                        "width/height are model units along the plane's X/Y; both must be > 0",
                        "rotation is in-plane about Z in degrees (default 0); placement is flat on world-XY",
                        "layer auto-creates if missing; defaults to the current layer",
                        "replace=true with a 'name' deletes prior same-named objects on the target layer first (idempotent re-placement); returns 'replaced' count",
                        "selfIllumination (default true) keeps the picture visible without scene lights"
                    }
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

        [McpServerTool, Description("Scene operations. Actions: objects|select|deselect|set_layer|set_name|set_color|bbox|layers|layer_create|layer_set|layer_delete|hide|show|delete|place_image|script|help")]
        public string RhinoScene(
            [Description("Action to perform")] string action,
            [Description("JSON array of object IDs")] string ids = null,
            [Description("Object type filter")] string type = null,
            [Description("Layer name filter")] string layer = null,
            [Description("Object name filter (substring match)")] string objectName = null,
            [Description("Filter by selected (true/false)")] string selected = null,
            [Description("Include hidden objects (true/false, default true)")] string includeHidden = null,
            [Description("Add to selection (true/false)")] string add = null,
            [Description("Show all hidden (true/false)")] string all = null,
            [Description("Rhino command script")] string cmd = null,
            [Description("Max objects to return")] int limit = 100,
            // Layer parameters
            [Description("Layer name for create/set/delete")] string name = null,
            [Description("Layer color as hex or RGB")] string color = null,
            [Description("Layer visibility (true/false)")] string visible = null,
            [Description("Parent layer name")] string parent = null,
            [Description("Layer locked state (true/false)")] string locked = null,
            [Description("Delete objects on layer (true/false)")] string deleteObjects = null,
            // place_image parameters
            [Description("Absolute path to the image file (place_image)")] string path = null,
            [Description("Placement origin X in model units (place_image)")] double x = double.NaN,
            [Description("Placement origin Y in model units (place_image)")] double y = double.NaN,
            [Description("Placement origin Z in model units (place_image)")] double z = double.NaN,
            [Description("Picture width along plane X, model units, >0 (place_image)")] double width = double.NaN,
            [Description("Picture height along plane Y, model units, >0 (place_image)")] double height = double.NaN,
            [Description("In-plane rotation about Z in degrees (place_image, default 0)")] double rotation = 0,
            [Description("Replace existing same-named objects on the target layer (place_image, true/false)")] string replace = null,
            [Description("Picture is self-lit, visible without scene lights (place_image, default true)")] string selfIllumination = null,
            [Description("Embed the bitmap in the .3dm vs link to the file (place_image, default false)")] string embedBitmap = null,
            [Description("Mesh-backed vs surface-backed picture (place_image, default false)")] string asMesh = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("ids", ids),
                ("type", type),
                ("layer", layer),
                ("objectName", objectName),
                ("selected", selected),
                ("includeHidden", includeHidden),
                ("add", add),
                ("all", all),
                ("cmd", cmd),
                ("limit", limit != 100 ? (object)limit : null),
                ("name", name),
                ("color", color),  // Used by layer_create, layer_set, and set_color
                ("visible", visible),
                ("parent", parent),
                ("locked", locked),
                ("deleteObjects", deleteObjects),
                // place_image params
                ("path", path),
                ("x", double.IsNaN(x) ? null : (object)x),
                ("y", double.IsNaN(y) ? null : (object)y),
                ("z", double.IsNaN(z) ? null : (object)z),
                ("width", double.IsNaN(width) ? null : (object)width),
                ("height", double.IsNaN(height) ? null : (object)height),
                ("rotation", rotation != 0 ? (object)rotation : null),
                ("replace", replace),
                ("selfIllumination", selfIllumination),
                ("embedBitmap", embedBitmap),
                ("asMesh", asMesh)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Parse optional parameters with defaults
            bool selectedBool = ToolHelpers.ParseBool(selected, false);
            bool includeHiddenBool = ToolHelpers.ParseBool(includeHidden, true);
            bool addBool = ToolHelpers.ParseBool(add, false);
            bool allBool = ToolHelpers.ParseBool(all, false);
            bool visibleBool = ToolHelpers.ParseBool(visible, true);
            bool deleteObjectsBool = ToolHelpers.ParseBool(deleteObjects, false);
            bool replaceBool = ToolHelpers.ParseBool(replace, false);
            bool selfIlluminationBool = ToolHelpers.ParseBool(selfIllumination, true);
            bool embedBitmapBool = ToolHelpers.ParseBool(embedBitmap, false);
            bool asMeshBool = ToolHelpers.ParseBool(asMesh, false);

            return action.ToLowerInvariant() switch
            {
                "objects" => ActionObjects(type, layer, objectName, selectedBool, includeHiddenBool, limit),
                "select" => ActionSelect(ids, type, layer, addBool),
                "deselect" => ActionDeselect(),
                "set_layer" => ActionSetLayer(ids, layer),
                "set_name" => ActionSetName(ids, name),
                "set_color" => ActionSetColor(ids, color),
                "bbox" => ActionBbox(ids),
                "layers" => ActionLayers(),
                "layer_create" => ActionLayerCreate(name, color, visibleBool, parent),
                "layer_set" => ActionLayerSet(name, color, visible, locked),
                "layer_delete" => ActionLayerDelete(name, deleteObjectsBool),
                "hide" => ActionHide(ids, selectedBool),
                "show" => ActionShow(ids, allBool),
                "delete" => ActionDelete(ids),
                "place_image" => ActionPlaceImage(path, x, y, z, width, height, rotation, layer, name, replaceBool, selfIlluminationBool, embedBitmapBool, asMeshBool),
                "script" => ActionScript(cmd),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionObjects(string type, string layer, string objectName, bool selectedOnly, bool includeHidden, int limit)
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

                    if (!includeHidden && obj.IsHidden) continue;
                    if (typeFilter.HasValue && obj.ObjectType != typeFilter.Value) continue;
                    if (!string.IsNullOrEmpty(layer))
                    {
                        var objLayer = doc.Layers[obj.Attributes.LayerIndex];
                        if (!objLayer.Name.Equals(layer, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    if (!string.IsNullOrEmpty(objectName))
                    {
                        var name = obj.Attributes.Name ?? "";
                        if (!name.Contains(objectName, StringComparison.OrdinalIgnoreCase)) continue;
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

                // Find or create layer (shared with place_image)
                var layerIndex = FindOrCreateLayer(doc, layer);
                if (layerIndex < 0)
                    return ToolHelpers.ErrorResponse($"Failed to create layer '{layer}'");

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

        private string ActionSetColor(string ids, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(color))
                    return ToolHelpers.ErrorResponse("color is required");

                if (!ToolHelpers.TryParseColor(color, out var parsedColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}");

                if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                    return ToolHelpers.ErrorResponse(error);

                int coloredCount = 0;
                foreach (var guid in guids)
                {
                    var obj = doc.Objects.FindId(guid);
                    if (obj == null) continue;

                    var attrs = obj.Attributes.Duplicate();
                    attrs.ColorSource = ObjectColorSource.ColorFromObject;
                    attrs.ObjectColor = parsedColor;
                    if (doc.Objects.ModifyAttributes(obj, attrs, true))
                        coloredCount++;
                }

                doc.Views.Redraw();
                return JsonConvert.SerializeObject(new { success = true, coloredCount, color = ToolHelpers.ColorToHex(parsedColor) });
            });
        }

        private string ActionBbox(string ids)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var bbox = BoundingBox.Empty;
                int foundCount = 0;

                foreach (var guid in guids)
                {
                    var obj = doc.Objects.FindId(guid);
                    if (obj?.Geometry == null) continue;

                    var objBbox = obj.Geometry.GetBoundingBox(true);
                    if (objBbox.IsValid)
                    {
                        bbox.Union(objBbox);
                        foundCount++;
                    }
                }

                if (foundCount == 0 || !bbox.IsValid)
                    return ToolHelpers.ErrorResponse("No valid objects found for bounding box calculation");

                var center = bbox.Center;
                var size = bbox.Max - bbox.Min;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    objectCount = foundCount,
                    min = new { x = bbox.Min.X, y = bbox.Min.Y, z = bbox.Min.Z },
                    max = new { x = bbox.Max.X, y = bbox.Max.Y, z = bbox.Max.Z },
                    center = new { x = center.X, y = center.Y, z = center.Z },
                    size = new { x = size.X, y = size.Y, z = size.Z }
                });
            });
        }

        // Layer actions (ActionLayers, ActionLayerCreate, ActionLayerSet, ActionLayerDelete)
        // are in RhinoSceneTool.Layers.cs

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
