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
        /// layer are deleted first, so the call is idempotent for parametric re-placement.
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
                    layerIndex = FindOrCreateLayer(doc, layer);
                    if (layerIndex < 0)
                        return ToolHelpers.ErrorResponse($"Failed to create layer '{layer}'");
                }

                // Idempotent replace: delete prior objects with the same name on the target layer.
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
                        foreach (var existing in doc.Objects.FindByLayer(targetLayer))
                        {
                            if (existing.IsDeleted) continue;
                            if (string.Equals(existing.Attributes.Name, name, StringComparison.Ordinal)
                                && doc.Objects.Delete(existing, true))
                                replaced++;
                        }
                    }
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

                // AddPictureFrame has no ObjectAttributes overload — set layer/name post-add.
                var newObj = doc.Objects.FindId(objectId);
                if (newObj != null)
                {
                    var attrs = newObj.Attributes.Duplicate();
                    attrs.LayerIndex = layerIndex;
                    if (!string.IsNullOrEmpty(name))
                        attrs.Name = name;
                    doc.Objects.ModifyAttributes(newObj, attrs, true);
                }

                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    objectId = objectId.ToString(),
                    layer = doc.Layers[layerIndex].Name,
                    replaced,
                    note
                });
            });
        }

        /// <summary>
        /// Find a layer by full path or name, creating it (default color) if absent.
        /// Returns the layer index, or -1 if creation failed. Shared by set_layer and place_image.
        /// </summary>
        private static int FindOrCreateLayer(RhinoDoc doc, string layer)
        {
            var layerIndex = doc.Layers.FindByFullPath(layer, -1);
            if (layerIndex < 0)
            {
                var existing = doc.Layers.FirstOrDefault(l =>
                    l.Name.Equals(layer, StringComparison.OrdinalIgnoreCase));
                layerIndex = existing?.Index ?? -1;
            }
            if (layerIndex < 0)
                layerIndex = doc.Layers.Add(layer, System.Drawing.Color.Black);
            return layerIndex;
        }

        #endregion
    }
}
