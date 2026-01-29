using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Rhino document object operations (query, select, modify, delete)
    /// </summary>
    [McpServerToolType]
    public class ObjectTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public ObjectTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("List Rhino document objects with optional filters. Returns object GUIDs, names, types, and layers.")]
        public string RhinoGetObjects(
            [Description("Filter by layer name")] string layer = null,
            [Description("Filter by object type (Point, Curve, Surface, Brep, Mesh, etc.)")] string type = null,
            [Description("Filter by object name (substring match)")] string name = null,
            [Description("Include hidden objects (default: true)")] bool includeHidden = true)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var objects = new List<object>();
                ObjectType? typeFilter = null;

                // Parse type filter if provided
                if (!string.IsNullOrEmpty(type))
                {
                    if (Enum.TryParse<ObjectType>(type, true, out var parsedType))
                        typeFilter = parsedType;
                    else
                        return ToolHelpers.ErrorResponse($"Unknown object type: {type}. Valid types: Point, Curve, Surface, Brep, Mesh, InstanceReference, etc.");
                }

                // Get layer index if filtering by layer
                int? layerIndex = null;
                if (!string.IsNullOrEmpty(layer))
                {
                    var idx = rhinoDoc.Layers.FindByFullPath(layer, -1);
                    if (idx < 0)
                    {
                        // Try partial match
                        var matchingLayer = rhinoDoc.Layers.FirstOrDefault(l =>
                            l.Name.Equals(layer, StringComparison.OrdinalIgnoreCase) ||
                            l.FullPath.EndsWith(layer, StringComparison.OrdinalIgnoreCase));
                        if (matchingLayer != null)
                            layerIndex = matchingLayer.Index;
                        else
                            return JsonConvert.SerializeObject(new { success = true, count = 0, objects = new List<object>(),
                                filters = new { layer, type, name, includeHidden }, note = $"Layer '{layer}' not found" });
                    }
                    else
                    {
                        layerIndex = idx;
                    }
                }

                // Use GetObjectList to include hidden objects
                var enumeratorSettings = new ObjectEnumeratorSettings
                {
                    IncludeGrips = false,
                    IncludeLights = false,
                    IncludePhantoms = false,
                    NormalObjects = true,
                    LockedObjects = true,
                    HiddenObjects = includeHidden,
                    ActiveObjects = true,
                    ReferenceObjects = true
                };

                foreach (var obj in rhinoDoc.Objects.GetObjectList(enumeratorSettings))
                {
                    // Skip deleted or hidden objects
                    if (obj.IsDeleted) continue;

                    // Apply layer filter
                    if (layerIndex.HasValue && obj.Attributes.LayerIndex != layerIndex.Value)
                        continue;

                    // Apply type filter
                    if (typeFilter.HasValue && obj.ObjectType != typeFilter.Value)
                        continue;

                    // Apply name filter
                    if (!string.IsNullOrEmpty(name))
                    {
                        var objName = obj.Attributes.Name ?? "";
                        if (objName.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                    }

                    var layerObj = rhinoDoc.Layers[obj.Attributes.LayerIndex];
                    objects.Add(new
                    {
                        id = obj.Id.ToString(),
                        name = obj.Attributes.Name ?? "",
                        type = obj.ObjectType.ToString(),
                        layer = layerObj?.Name ?? "",
                        layerFullPath = layerObj?.FullPath ?? "",
                        isHidden = obj.IsHidden,
                        isLocked = obj.IsLocked
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = objects.Count,
                    objects,
                    filters = new { layer, type, name, includeHidden }
                });
            });
        }

        [McpServerTool, Description("Select Rhino objects by their GUIDs. Objects must exist in the active document.")]
        public string RhinoSelectObjects(
            [Description("JSON array of object GUIDs to select")] string objectIds)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                int selected = 0;
                int notFound = 0;
                var results = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!Guid.TryParse(idStr, out var guid))
                    {
                        results.Add(new { id = idStr, success = false, error = "Invalid GUID format" });
                        notFound++;
                        continue;
                    }

                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null)
                    {
                        results.Add(new { id = idStr, success = false, error = "Object not found" });
                        notFound++;
                        continue;
                    }

                    obj.Select(true);
                    selected++;
                    results.Add(new { id = idStr, success = true });
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = notFound == 0,
                    total = ids.Count,
                    selected,
                    notFound,
                    results
                });
            });
        }

        [McpServerTool, Description("Clear the current Rhino selection (deselect all objects).")]
        public string RhinoDeselectAll()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                int deselectedCount = rhinoDoc.Objects.UnselectAll();
                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    deselectedCount
                });
            });
        }

        [McpServerTool, Description("Move Rhino objects to a layer. Creates the layer if it doesn't exist.")]
        public string RhinoSetObjectLayer(
            [Description("JSON array of object GUIDs")] string objectIds,
            [Description("Target layer name (created if doesn't exist)")] string layer)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(layer))
                    return ToolHelpers.ErrorResponse("Layer name is required");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                // Get or create layer
                int layerIndex = rhinoDoc.Layers.FindByFullPath(layer, -1);
                bool layerCreated = false;
                if (layerIndex < 0)
                {
                    var newLayer = new Layer { Name = layer };
                    layerIndex = rhinoDoc.Layers.Add(newLayer);
                    layerCreated = true;
                }

                int succeeded = 0;
                int failed = 0;
                var results = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!Guid.TryParse(idStr, out var guid))
                    {
                        results.Add(new { id = idStr, success = false, error = "Invalid GUID format" });
                        failed++;
                        continue;
                    }

                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null)
                    {
                        results.Add(new { id = idStr, success = false, error = "Object not found" });
                        failed++;
                        continue;
                    }

                    var attrs = obj.Attributes.Duplicate();
                    attrs.LayerIndex = layerIndex;
                    rhinoDoc.Objects.ModifyAttributes(obj, attrs, true);
                    succeeded++;
                    results.Add(new { id = idStr, success = true });
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = ids.Count,
                    succeeded,
                    failed,
                    layer,
                    layerIndex,
                    layerCreated,
                    results
                });
            });
        }

        [McpServerTool, Description("Set the name of Rhino objects.")]
        public string RhinoSetObjectName(
            [Description("JSON array of object GUIDs")] string objectIds,
            [Description("Name to assign to objects")] string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                int succeeded = 0;
                int failed = 0;
                var results = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!Guid.TryParse(idStr, out var guid))
                    {
                        results.Add(new { id = idStr, success = false, error = "Invalid GUID format" });
                        failed++;
                        continue;
                    }

                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null)
                    {
                        results.Add(new { id = idStr, success = false, error = "Object not found" });
                        failed++;
                        continue;
                    }

                    var attrs = obj.Attributes.Duplicate();
                    attrs.Name = name ?? "";
                    rhinoDoc.Objects.ModifyAttributes(obj, attrs, true);
                    succeeded++;
                    results.Add(new { id = idStr, success = true });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = ids.Count,
                    succeeded,
                    failed,
                    name,
                    results
                });
            });
        }

        [McpServerTool, Description("Hide Rhino objects (make them invisible but keep in document).")]
        public string RhinoHideObjects(
            [Description("JSON array of object GUIDs to hide")] string objectIds)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                int succeeded = 0;
                int failed = 0;
                var results = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!Guid.TryParse(idStr, out var guid))
                    {
                        results.Add(new { id = idStr, success = false, error = "Invalid GUID format" });
                        failed++;
                        continue;
                    }

                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null)
                    {
                        results.Add(new { id = idStr, success = false, error = "Object not found" });
                        failed++;
                        continue;
                    }

                    rhinoDoc.Objects.Hide(guid, false);
                    succeeded++;
                    results.Add(new { id = idStr, success = true });
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = ids.Count,
                    hidden = succeeded,
                    failed,
                    results
                });
            });
        }

        [McpServerTool, Description("Show (unhide) Rhino objects.")]
        public string RhinoShowObjects(
            [Description("JSON array of object GUIDs to show")] string objectIds)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                int succeeded = 0;
                int failed = 0;
                var results = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!Guid.TryParse(idStr, out var guid))
                    {
                        results.Add(new { id = idStr, success = false, error = "Invalid GUID format" });
                        failed++;
                        continue;
                    }

                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null)
                    {
                        results.Add(new { id = idStr, success = false, error = "Object not found" });
                        failed++;
                        continue;
                    }

                    rhinoDoc.Objects.Show(guid, false);
                    succeeded++;
                    results.Add(new { id = idStr, success = true });
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = ids.Count,
                    shown = succeeded,
                    failed,
                    results
                });
            });
        }

        [McpServerTool, Description("Delete Rhino objects from the document permanently.")]
        public string RhinoDeleteObjects(
            [Description("JSON array of object GUIDs to delete")] string objectIds)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                int succeeded = 0;
                int failed = 0;
                var results = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!Guid.TryParse(idStr, out var guid))
                    {
                        results.Add(new { id = idStr, success = false, error = "Invalid GUID format" });
                        failed++;
                        continue;
                    }

                    var deleted = rhinoDoc.Objects.Delete(guid, true);
                    if (deleted)
                    {
                        succeeded++;
                        results.Add(new { id = idStr, success = true });
                    }
                    else
                    {
                        failed++;
                        results.Add(new { id = idStr, success = false, error = "Failed to delete (object may not exist)" });
                    }
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = ids.Count,
                    deleted = succeeded,
                    failed,
                    results
                });
            });
        }
    }
}
