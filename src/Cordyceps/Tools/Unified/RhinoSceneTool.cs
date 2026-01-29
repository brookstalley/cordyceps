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
                ["layers"] = new ActionInfo
                {
                    Name = "layers",
                    Description = "List all layers",
                    Example = "action='layers'"
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

        [McpServerTool, Description("Scene operations. Actions: objects|select|layers|hide|show|delete|script|help")]
        public string RhinoScene(
            [Description("Action to perform")] string action,
            [Description("JSON array of object IDs")] string ids = null,
            [Description("Object type filter")] string type = null,
            [Description("Layer name filter")] string layer = null,
            [Description("Filter by selected (true/false)")] string selected = null,
            [Description("Add to selection (true/false)")] string add = null,
            [Description("Show all hidden (true/false)")] string all = null,
            [Description("Rhino command script")] string cmd = null,
            [Description("Max objects to return")] string limit = null)
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
                ("limit", limit)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Parse optional parameters with defaults
            bool selectedBool = ToolHelpers.ParseBool(selected, false);
            bool addBool = ToolHelpers.ParseBool(add, false);
            bool allBool = ToolHelpers.ParseBool(all, false);
            int limitInt = string.IsNullOrEmpty(limit) ? 100 : (int.TryParse(limit, out var l) ? l : 100);

            return action.ToLowerInvariant() switch
            {
                "objects" => ActionObjects(type, layer, selectedBool, limitInt),
                "select" => ActionSelect(ids, type, layer, addBool),
                "layers" => ActionLayers(),
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
