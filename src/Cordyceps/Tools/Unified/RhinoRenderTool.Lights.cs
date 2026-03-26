using System;
using System.Collections.Generic;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Cordyceps.Tools.Unified
{
    public partial class RhinoRenderTool
    {
        #region Light Actions

        private string ActionLightAdd(string lightType, string location, string target, string color, double intensity, double spotAngle, string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(lightType))
                    return ToolHelpers.ErrorResponse("lightType is required (point, spot, directional)");

                if (string.IsNullOrEmpty(location))
                    return ToolHelpers.ErrorResponse("lightLocation is required");

                if (!ToolHelpers.TryParsePoint3d(location, out var loc))
                    return ToolHelpers.ErrorResponse($"Invalid lightLocation: {location}");

                Point3d tgt = Point3d.Origin;
                if (!string.IsNullOrEmpty(target))
                {
                    if (!ToolHelpers.TryParsePoint3d(target, out tgt))
                        return ToolHelpers.ErrorResponse($"Invalid lightTarget: {target}");
                }

                var light = new Light();
                var lightTypeLower = lightType.ToLowerInvariant();

                switch (lightTypeLower)
                {
                    case "point":
                        light.LightStyle = LightStyle.WorldPoint;
                        light.Location = loc;
                        break;
                    case "spot":
                        light.LightStyle = LightStyle.WorldSpot;
                        light.Location = loc;
                        if (string.IsNullOrEmpty(target))
                            tgt = new Point3d(loc.X, loc.Y, 0); // Default to looking down
                        light.Direction = tgt - loc;

                        // Set spot angle if provided
                        if (!double.IsNaN(spotAngle))
                        {
                            var radians = spotAngle * Math.PI / 180.0;
                            light.SpotAngleRadians = radians;
                        }
                        break;
                    case "directional":
                        light.LightStyle = LightStyle.WorldDirectional;
                        light.Location = loc;
                        if (string.IsNullOrEmpty(target))
                            tgt = Point3d.Origin;
                        light.Direction = tgt - loc;
                        break;
                    default:
                        return ToolHelpers.ErrorResponse($"Invalid lightType: '{lightType}'. Use: point, spot, directional");
                }

                // Set color if provided
                if (!string.IsNullOrEmpty(color))
                {
                    if (!ToolHelpers.TryParseColor(color, out var lightColor))
                        return ToolHelpers.ErrorResponse($"Invalid lightColor: {color}");
                    light.Diffuse = lightColor;
                }
                else
                {
                    light.Diffuse = System.Drawing.Color.White;
                }

                // Set intensity if provided
                if (!double.IsNaN(intensity))
                    light.Intensity = intensity;

                // Set name if provided
                if (!string.IsNullOrEmpty(name))
                    light.Name = name;

                light.IsEnabled = true;

                var index = doc.Lights.Add(light);
                if (index < 0)
                    return ToolHelpers.ErrorResponse("Failed to add light");

                var lightObj = doc.Lights[index];
                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    id = lightObj.Id.ToString(),
                    name = light.Name ?? "",
                    lightType = lightTypeLower,
                    location = $"{light.Location.X:F3},{light.Location.Y:F3},{light.Location.Z:F3}",
                    color = ToolHelpers.ColorToHex(light.Diffuse),
                    intensity = light.Intensity,
                    enabled = light.IsEnabled
                });
            });
        }

        private string ActionLightList()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var lights = new List<object>();
                foreach (var lightObj in doc.Lights)
                {
                    if (lightObj.IsDeleted) continue;

                    var light = lightObj.LightGeometry;
                    var lightInfo = new Dictionary<string, object>
                    {
                        ["id"] = lightObj.Id.ToString(),
                        ["name"] = light.Name ?? "",
                        ["lightType"] = GetLightTypeName(light.LightStyle),
                        ["location"] = $"{light.Location.X:F3},{light.Location.Y:F3},{light.Location.Z:F3}",
                        ["color"] = ToolHelpers.ColorToHex(light.Diffuse),
                        ["intensity"] = light.Intensity,
                        ["enabled"] = light.IsEnabled
                    };

                    if (light.LightStyle == LightStyle.WorldSpot)
                    {
                        lightInfo["spotAngle"] = light.SpotAngleRadians * 180.0 / Math.PI;
                        var target = light.Location + light.Direction;
                        lightInfo["target"] = $"{target.X:F3},{target.Y:F3},{target.Z:F3}";
                    }
                    else if (light.LightStyle == LightStyle.WorldDirectional)
                    {
                        lightInfo["direction"] = $"{light.Direction.X:F3},{light.Direction.Y:F3},{light.Direction.Z:F3}";
                    }

                    lights.Add(lightInfo);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = lights.Count,
                    lights
                });
            });
        }

        private string ActionLightSet(string ids, string location, string target, string color, double intensity, double spotAngle, string enabled)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                    return ToolHelpers.ErrorResponse(error);

                int modifiedCount = 0;
                var modified = new List<string>();

                foreach (var guid in guids)
                {
                    var lightObj = doc.Lights.FindId(guid);
                    if (lightObj == null) continue;

                    var light = lightObj.LightGeometry;
                    if (light == null) continue;
                    bool changed = false;

                    if (!string.IsNullOrEmpty(location))
                    {
                        if (ToolHelpers.TryParsePoint3d(location, out var loc))
                        {
                            light.Location = loc;
                            changed = true;
                            if (!modified.Contains("location")) modified.Add("location");
                        }
                    }

                    if (!string.IsNullOrEmpty(target))
                    {
                        if (ToolHelpers.TryParsePoint3d(target, out var tgt))
                        {
                            light.Direction = tgt - light.Location;
                            changed = true;
                            if (!modified.Contains("target")) modified.Add("target");
                        }
                    }

                    if (!string.IsNullOrEmpty(color))
                    {
                        if (ToolHelpers.TryParseColor(color, out var lightColor))
                        {
                            light.Diffuse = lightColor;
                            changed = true;
                            if (!modified.Contains("color")) modified.Add("color");
                        }
                    }

                    if (!double.IsNaN(intensity))
                    {
                        light.Intensity = intensity;
                        changed = true;
                        if (!modified.Contains("intensity")) modified.Add("intensity");
                    }

                    if (!double.IsNaN(spotAngle) && light.LightStyle == LightStyle.WorldSpot)
                    {
                        light.SpotAngleRadians = spotAngle * Math.PI / 180.0;
                        changed = true;
                        if (!modified.Contains("spotAngle")) modified.Add("spotAngle");
                    }

                    if (!string.IsNullOrEmpty(enabled))
                    {
                        if (bool.TryParse(enabled, out var val))
                        {
                            light.IsEnabled = val;
                            changed = true;
                            if (!modified.Contains("enabled")) modified.Add("enabled");
                        }
                    }

                    if (changed)
                    {
                        doc.Lights.Modify(lightObj.Id, light);
                        modifiedCount++;
                    }
                }

                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = modifiedCount > 0,
                    modifiedCount,
                    modified
                });
            });
        }

        private string ActionLightDelete(string ids)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Collect indices first, then delete in reverse order to avoid index shifting
                var indicesToDelete = new List<int>();
                foreach (var guid in guids)
                {
                    for (int i = 0; i < doc.Lights.Count; i++)
                    {
                        if (doc.Lights[i].Id == guid && !doc.Lights[i].IsDeleted)
                        {
                            indicesToDelete.Add(i);
                            break;
                        }
                    }
                }

                // Sort descending and delete from highest index first
                indicesToDelete.Sort((a, b) => b.CompareTo(a));

                int deletedCount = 0;
                foreach (var idx in indicesToDelete)
                {
                    if (doc.Lights.Delete(idx, true))
                        deletedCount++;
                }

                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    deletedCount
                });
            });
        }

        private static string GetLightTypeName(LightStyle style)
        {
            return style switch
            {
                LightStyle.WorldPoint => "point",
                LightStyle.WorldSpot => "spot",
                LightStyle.WorldDirectional => "directional",
                LightStyle.WorldLinear => "linear",
                LightStyle.WorldRectangular => "rectangular",
                LightStyle.Ambient => "ambient",
                _ => "unknown"
            };
        }

        #endregion
    }
}
