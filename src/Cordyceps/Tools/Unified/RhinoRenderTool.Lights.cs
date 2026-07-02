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

                // 'type' and 'location' presence is enforced upstream by ValidateAction
                // (they are Required params for light_add).

                if (!ToolHelpers.TryParsePoint3d(location, out var loc))
                    return ToolHelpers.ErrorResponse($"Invalid location: {location} (expected 'x,y,z')");

                Point3d tgt = Point3d.Origin;
                if (!string.IsNullOrEmpty(target))
                {
                    if (!ToolHelpers.TryParsePoint3d(target, out tgt))
                        return ToolHelpers.ErrorResponse($"Invalid target: {target} (expected 'x,y,z')");
                }

                // SpotAngleRadians contract is 0 to pi/2 (0 to 90 degrees); validate in degrees
                // before converting.
                if (!double.IsNaN(spotAngle) && (spotAngle <= 0 || spotAngle > 90))
                    return ToolHelpers.ErrorResponse($"Invalid spotAngle: {spotAngle} (must be > 0 and <= 90 degrees)");

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
                        var spotDir = tgt - loc;
                        if (spotDir.IsZero)
                            return ToolHelpers.ErrorResponse(
                                "Spot light direction is zero-length: target equals location. " +
                                "Note: with no target, a spot light aims straight down at z=0, so a spot placed AT z=0 needs an explicit target — pass target='x,y,z' distinct from location.");
                        light.Direction = spotDir;

                        // Set spot angle if provided (validated above)
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
                        var dir = tgt - loc;
                        if (dir.IsZero)
                            return ToolHelpers.ErrorResponse(
                                "Directional light direction is zero-length: target equals location. " +
                                "Note: with no target, a directional light aims at the world origin, so a directional light placed AT the origin needs an explicit target — pass target='x,y,z' distinct from location.");
                        light.Direction = dir;
                        break;
                    default:
                        return ToolHelpers.ErrorResponse($"Invalid type: '{lightType}'. Use: point, spot, directional");
                }

                // Set color if provided
                if (!string.IsNullOrEmpty(color))
                {
                    if (!ToolHelpers.TryParseColor(color, out var lightColor))
                        return ToolHelpers.ErrorResponse($"Invalid color: {color}");
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

                // Validate every property up front so a bad value is a hard error instead of a
                // silent per-light skip (consistent with layer_set).
                var loc = Point3d.Unset;
                if (!string.IsNullOrEmpty(location) && !ToolHelpers.TryParsePoint3d(location, out loc))
                    return ToolHelpers.ErrorResponse($"Invalid location: {location} (expected 'x,y,z')");

                var tgt = Point3d.Unset;
                if (!string.IsNullOrEmpty(target) && !ToolHelpers.TryParsePoint3d(target, out tgt))
                    return ToolHelpers.ErrorResponse($"Invalid target: {target} (expected 'x,y,z')");

                var lightColor = System.Drawing.Color.Empty;
                if (!string.IsNullOrEmpty(color) && !ToolHelpers.TryParseColor(color, out lightColor))
                    return ToolHelpers.ErrorResponse($"Invalid color: {color}");

                // SpotAngleRadians contract is 0 to pi/2 (0 to 90 degrees).
                if (!double.IsNaN(spotAngle) && (spotAngle <= 0 || spotAngle > 90))
                    return ToolHelpers.ErrorResponse($"Invalid spotAngle: {spotAngle} (must be > 0 and <= 90 degrees)");

                bool enabledVal = false;
                if (!string.IsNullOrEmpty(enabled) && !ToolHelpers.TryParseBool(enabled, out enabledVal))
                    return ToolHelpers.ErrorResponse($"Invalid enabled: '{enabled}' (expected true/false, 1/0, or yes/no)");

                bool anyProperty = !string.IsNullOrEmpty(location) || !string.IsNullOrEmpty(target) ||
                                   !string.IsNullOrEmpty(color) || !double.IsNaN(intensity) ||
                                   !double.IsNaN(spotAngle) || !string.IsNullOrEmpty(enabled);
                if (!anyProperty)
                    return ToolHelpers.ErrorResponse("light_set requires at least one property to change: location, target, color, intensity, spotAngle, enabled");

                int modifiedCount = 0;
                var modified = new List<string>();
                var notFound = new List<string>();
                var failed = new List<string>();

                foreach (var guid in guids)
                {
                    var lightObj = doc.Lights.FindId(guid);
                    var light = lightObj?.LightGeometry;
                    if (light == null)
                    {
                        notFound.Add(guid.ToString());
                        continue;
                    }

                    bool anyApplied = false;

                    if (!string.IsNullOrEmpty(location))
                    {
                        light.Location = loc;
                        if (!modified.Contains("location")) modified.Add("location");
                        anyApplied = true;
                    }

                    if (!string.IsNullOrEmpty(target))
                    {
                        light.Direction = tgt - light.Location;
                        if (!modified.Contains("target")) modified.Add("target");
                        anyApplied = true;
                    }

                    if (!string.IsNullOrEmpty(color))
                    {
                        light.Diffuse = lightColor;
                        if (!modified.Contains("color")) modified.Add("color");
                        anyApplied = true;
                    }

                    if (!double.IsNaN(intensity))
                    {
                        light.Intensity = intensity;
                        if (!modified.Contains("intensity")) modified.Add("intensity");
                        anyApplied = true;
                    }

                    if (!double.IsNaN(spotAngle) && light.LightStyle == LightStyle.WorldSpot)
                    {
                        light.SpotAngleRadians = spotAngle * Math.PI / 180.0;
                        if (!modified.Contains("spotAngle")) modified.Add("spotAngle");
                        anyApplied = true;
                    }

                    if (!string.IsNullOrEmpty(enabled))
                    {
                        light.IsEnabled = enabledVal;
                        if (!modified.Contains("enabled")) modified.Add("enabled");
                        anyApplied = true;
                    }

                    // A light none of the requested properties apply to (e.g. spotAngle on a
                    // non-spot light) is a per-light failure, not a no-op Modify "success".
                    if (!anyApplied)
                    {
                        failed.Add($"{guid}: none of the requested properties apply to this light type");
                        continue;
                    }

                    // Modify can reject the change — only count lights it actually accepted.
                    if (doc.Lights.Modify(lightObj.Id, light))
                        modifiedCount++;
                    else
                        failed.Add(guid.ToString());
                }

                doc.Views.Redraw();

                if (modifiedCount == 0 && guids.Count > 0)
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = notFound.Count == guids.Count
                            ? "No lights were modified: none of the given ids match lights in the document"
                            : "No lights were modified: the light table rejected every change",
                        modifiedCount,
                        notFound = notFound.Count > 0 ? notFound : null,
                        failed = failed.Count > 0 ? failed : null
                    });

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    modifiedCount,
                    modified,
                    notFound = notFound.Count > 0 ? notFound : null,
                    failed = failed.Count > 0 ? failed : null
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
                var notFound = new List<string>();
                foreach (var guid in guids)
                {
                    bool found = false;
                    for (int i = 0; i < doc.Lights.Count; i++)
                    {
                        if (doc.Lights[i].Id == guid && !doc.Lights[i].IsDeleted)
                        {
                            indicesToDelete.Add(i);
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                        notFound.Add(guid.ToString());
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

                if (deletedCount == 0 && guids.Count > 0)
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = notFound.Count == guids.Count
                            ? "No lights were deleted: none of the given ids match lights in the document"
                            : "No lights were deleted: the light table rejected every delete",
                        deletedCount,
                        notFound = notFound.Count > 0 ? notFound : null
                    });

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    deletedCount,
                    notFound = notFound.Count > 0 ? notFound : null
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
