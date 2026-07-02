using System;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Cordyceps.Tools.Unified
{
    public partial class RhinoSceneTool
    {
        #region Place Image (PictureFrame)

        /// <summary>
        /// Place a raster image into the scene as a Rhino PictureFrame object. The image is laid
        /// flat on a world-XY-parallel plane at (x,y,z), sized width × height in model units, with
        /// an optional in-plane rotation about Z (degrees). When <paramref name="replace"/> is true
        /// and a <paramref name="name"/> is given, prior objects with that exact name on the target
        /// layer are deleted (only after the new add succeeds), so the call is idempotent for
        /// parametric re-placement without risking loss of the old picture on a failed add.
        /// </summary>
        private string ActionPlaceImage(
            string path, double x, double y, double z, double width, double height, double rotation,
            string layer, string name, bool replace, bool selfIllumination, bool embedBitmap, bool asMesh)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var validationError = PlaceImageValidation.Validate(path, width, height);
                if (validationError != null)
                    return ToolHelpers.ErrorResponse(validationError);

                // Resolve the destination layer (current layer when none specified).
                int layerIndex = doc.Layers.CurrentLayerIndex;
                if (!string.IsNullOrEmpty(layer))
                {
                    layerIndex = FindOrCreateLayer(doc, layer, out var layerError);
                    if (layerIndex < 0)
                        return ToolHelpers.ErrorResponse(layerError ?? $"Failed to create layer '{layer}'");
                }

                // Build the placement plane: world-XY at the origin, optional Z rotation (degrees).
                var plane = Plane.WorldXY;
                plane.Origin = new Point3d(x, y, z);
                if (rotation != 0)
                    plane.Rotate(RhinoMath.ToRadians(rotation), plane.ZAxis);

                var objectId = doc.Objects.AddPictureFrame(
                    plane, path, asMesh, width, height, selfIllumination, embedBitmap);
                if (objectId == Guid.Empty)
                    return ToolHelpers.ErrorResponse("AddPictureFrame failed to create the object");

                // Idempotent replace: delete prior objects with the same name on the target layer.
                // Runs AFTER the new add succeeded so a failed add never destroys the old picture;
                // the freshly added object is excluded from the delete scan.
                int replaced = 0;
                string note = null;
                if (replace)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        note = "replace=true ignored: no 'name' provided to match existing objects";
                    }
                    else
                    {
                        var targetLayer = doc.Layers[layerIndex];
                        // FindByLayer is documented to return null when no objects are found.
                        foreach (var existing in doc.Objects.FindByLayer(targetLayer) ?? Array.Empty<RhinoObject>())
                        {
                            if (existing.IsDeleted || existing.Id == objectId) continue;
                            if (string.Equals(existing.Attributes.Name, name, StringComparison.Ordinal)
                                && doc.Objects.Delete(existing, true))
                                replaced++;
                        }
                    }
                }

                // AddPictureFrame has no ObjectAttributes overload — set layer/name post-add.
                // If attribute application fails, the picture exists but on the wrong layer or
                // unnamed: surface that as a warning instead of silently claiming it.
                string warning = null;
                var newObj = doc.Objects.FindId(objectId);
                if (newObj == null)
                {
                    warning = "Picture added, but the object could not be re-found to apply layer/name attributes";
                }
                else
                {
                    var attrs = newObj.Attributes.Duplicate();
                    attrs.LayerIndex = layerIndex;
                    if (!string.IsNullOrEmpty(name))
                        attrs.Name = name;
                    if (!doc.Objects.ModifyAttributes(newObj, attrs, true))
                        warning = "Picture added, but layer/name attributes could not be applied";
                }

                if (warning != null)
                    DebugLog.Warn($"[place_image] {warning}");

                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    objectId = objectId.ToString(),
                    layer = doc.Layers[layerIndex].Name,
                    replaced,
                    note,
                    warning
                });
            });
        }

        /// <summary>
        /// Find a layer by exact full path ("Parent::Child"), then by case-insensitive short
        /// name, creating it (default color) if absent. A multi-segment path that doesn't exist
        /// is created segment by segment ("::" is illegal inside a layer NAME, so passing the
        /// whole path to Layers.Add would fail); each missing segment is parented to the one
        /// before it via <see cref="Layer.ParentLayerId"/>. A bare short name matching multiple
        /// existing layers is ambiguous and returns -1 with <paramref name="error"/> listing the
        /// candidate full paths. Shared by set_layer and place_image.
        /// </summary>
        private static int FindOrCreateLayer(RhinoDoc doc, string layer, out string error)
        {
            error = null;

            var layerIndex = doc.Layers.FindByFullPath(layer, -1);
            if (layerIndex >= 0)
                return layerIndex;

            var segments = layer.Split(new[] { "::" }, StringSplitOptions.None);

            if (segments.Length == 1)
            {
                // Short name: match an existing layer anywhere in the hierarchy before creating.
                var matches = doc.Layers
                    .Where(l => !l.IsDeleted && l.Name.Equals(layer, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count == 1)
                    return matches[0].Index;
                if (matches.Count > 1)
                {
                    error = $"Layer name '{layer}' is ambiguous — multiple layers share it. Use a full path: {string.Join(", ", matches.Select(l => l.FullPath))}";
                    return -1;
                }
            }

            // Create the (remaining) hierarchy: find or create each segment under its parent.
            // The per-segment lookup is scoped to the parent and case-insensitive, so an
            // existing 'a::b' satisfies a request for 'A::B' instead of Layers.Add rejecting
            // the duplicate name.
            int index = -1;
            var parentId = Guid.Empty;
            string pathSoFar = null;
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                {
                    error = $"Invalid layer path '{layer}': empty segment";
                    return -1;
                }

                pathSoFar = pathSoFar == null ? segment : pathSoFar + "::" + segment;
                var segParentId = parentId;
                var existing = doc.Layers.FirstOrDefault(l =>
                    !l.IsDeleted && l.ParentLayerId == segParentId &&
                    l.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    index = existing.Index;
                    parentId = existing.Id;
                    continue;
                }

                var newLayer = new Layer { Name = segment, Color = System.Drawing.Color.Black };
                if (parentId != Guid.Empty)
                    newLayer.ParentLayerId = parentId;

                index = doc.Layers.Add(newLayer);
                if (index < 0)
                {
                    error = $"Failed to create layer '{pathSoFar}'";
                    return -1;
                }
                parentId = doc.Layers[index].Id;
            }

            return index;
        }

        #endregion
    }
}
