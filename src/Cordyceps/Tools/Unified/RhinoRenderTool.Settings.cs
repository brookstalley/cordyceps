using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.Render;

namespace Cordyceps.Tools.Unified
{
    public partial class RhinoRenderTool
    {
        #region Settings Actions

        private string ActionSettings(string style, string colorTop, string colorBottom, string transparent)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;
                var modified = new List<string>();

                if (!string.IsNullOrEmpty(style))
                {
                    BackgroundStyle bgStyle;
                    switch (style.ToLowerInvariant())
                    {
                        case "solid": case "solidcolor": bgStyle = BackgroundStyle.SolidColor; break;
                        case "gradient": bgStyle = BackgroundStyle.Gradient; break;
                        case "environment": bgStyle = BackgroundStyle.Environment; break;
                        default: return ToolHelpers.ErrorResponse($"Invalid style: {style}. Use 'solid', 'gradient', or 'environment'");
                    }
                    rs.BackgroundStyle = bgStyle;
                    modified.Add("style");
                }

                if (!string.IsNullOrEmpty(colorTop))
                {
                    if (!ToolHelpers.TryParseColor(colorTop, out var color))
                        return ToolHelpers.ErrorResponse($"Invalid colorTop: {colorTop}");
                    rs.BackgroundColorTop = color;
                    modified.Add("colorTop");
                }

                if (!string.IsNullOrEmpty(colorBottom))
                {
                    if (!ToolHelpers.TryParseColor(colorBottom, out var color))
                        return ToolHelpers.ErrorResponse($"Invalid colorBottom: {colorBottom}");
                    rs.BackgroundColorBottom = color;
                    modified.Add("colorBottom");
                }

                if (!string.IsNullOrEmpty(transparent))
                {
                    if (!bool.TryParse(transparent, out var val))
                        return ToolHelpers.ErrorResponse($"Invalid transparent: {transparent}");
                    rs.TransparentBackground = val;
                    modified.Add("transparent");
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    backgroundStyle = rs.BackgroundStyle.ToString(),
                    colorTop = ToolHelpers.ColorToHex(rs.BackgroundColorTop),
                    colorBottom = ToolHelpers.ColorToHex(rs.BackgroundColorBottom),
                    transparentBackground = rs.TransparentBackground,
                    modified
                });
            });
        }

        private string ActionGround(string enabled, string altitude, string autoAltitude, string shadowOnly, string material)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var gp = rhinoDoc.RenderSettings.GroundPlane;
                var modified = new List<string>();

                if (!string.IsNullOrEmpty(enabled))
                {
                    if (!bool.TryParse(enabled, out var val))
                        return ToolHelpers.ErrorResponse($"Invalid groundEnabled: {enabled}");
                    gp.Enabled = val;
                    modified.Add("groundEnabled");
                }

                if (!string.IsNullOrEmpty(altitude))
                {
                    if (!double.TryParse(altitude, out var val))
                        return ToolHelpers.ErrorResponse($"Invalid groundAltitude: {altitude}");
                    gp.Altitude = val;
                    modified.Add("groundAltitude");
                }

                if (!string.IsNullOrEmpty(autoAltitude))
                {
                    if (!bool.TryParse(autoAltitude, out var val))
                        return ToolHelpers.ErrorResponse($"Invalid autoAltitude: {autoAltitude}");
                    gp.AutoAltitude = val;
                    modified.Add("autoAltitude");
                }

                if (!string.IsNullOrEmpty(shadowOnly))
                {
                    if (!bool.TryParse(shadowOnly, out var val))
                        return ToolHelpers.ErrorResponse($"Invalid shadowOnly: {shadowOnly}");
                    gp.ShadowOnly = val;
                    modified.Add("shadowOnly");
                }

                if (!string.IsNullOrEmpty(material))
                {
                    var mat = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                        m.Name.Equals(material, StringComparison.OrdinalIgnoreCase));
                    if (mat == null)
                        return ToolHelpers.ErrorResponse($"Material '{material}' not found");
                    gp.MaterialInstanceId = mat.Id;
                    modified.Add("material");
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled = gp.Enabled,
                    altitude = gp.Altitude,
                    autoAltitude = gp.AutoAltitude,
                    shadowOnly = gp.ShadowOnly,
                    modified
                });
            });
        }

        private string ActionSun(string enabled, string azimuth, string altitude, string intensity, string latitude, string longitude, string dateTime)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var sun = rhinoDoc.Lights.Sun;
                var modified = new List<string>();

                if (!string.IsNullOrEmpty(enabled))
                {
                    if (!bool.TryParse(enabled, out var val))
                        return ToolHelpers.ErrorResponse($"Invalid sunEnabled: {enabled}");
                    sun.Enabled = val;
                    modified.Add("sunEnabled");
                }

                if (!string.IsNullOrEmpty(intensity))
                {
                    if (!double.TryParse(intensity, out var val) || val < 0)
                        return ToolHelpers.ErrorResponse($"Invalid intensity: {intensity}");
                    sun.Intensity = val;
                    modified.Add("intensity");
                }

                if (!string.IsNullOrEmpty(azimuth) || !string.IsNullOrEmpty(altitude))
                {
                    double az = sun.Azimuth, alt = sun.Altitude;

                    if (!string.IsNullOrEmpty(azimuth))
                    {
                        if (!double.TryParse(azimuth, out az) || az < 0 || az > 360)
                            return ToolHelpers.ErrorResponse($"Invalid azimuth: {azimuth} (must be 0-360)");
                        modified.Add("azimuth");
                    }

                    if (!string.IsNullOrEmpty(altitude))
                    {
                        if (!double.TryParse(altitude, out alt) || alt < -90 || alt > 90)
                            return ToolHelpers.ErrorResponse($"Invalid sunAltitude: {altitude}");
                        modified.Add("sunAltitude");
                    }

                    sun.ManualControlOn = true;
                    sun.Azimuth = az;
                    sun.Altitude = alt;
                    modified.Add("manualControl");
                }

                if (!string.IsNullOrEmpty(latitude))
                {
                    if (!double.TryParse(latitude, out var val) || val < -90 || val > 90)
                        return ToolHelpers.ErrorResponse($"Invalid latitude: {latitude}");
                    sun.Latitude = val;
                    modified.Add("latitude");
                }

                if (!string.IsNullOrEmpty(longitude))
                {
                    if (!double.TryParse(longitude, out var val) || val < -180 || val > 180)
                        return ToolHelpers.ErrorResponse($"Invalid longitude: {longitude}");
                    sun.Longitude = val;
                    modified.Add("longitude");
                }

                if (!string.IsNullOrEmpty(dateTime))
                {
                    if (!DateTime.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                        return ToolHelpers.ErrorResponse($"Invalid dateTime: {dateTime}");
                    sun.SetDateTime(dt, DateTimeKind.Local);
                    modified.Add("dateTime");
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled = sun.Enabled,
                    manualControl = sun.ManualControlOn,
                    azimuth = sun.Azimuth,
                    altitude = sun.Altitude,
                    intensity = sun.Intensity,
                    modified
                });
            });
        }

        private string ActionSkylight(string enabled, string shadowIntensity, string customEnvironment)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;
                var skylight = rs.Skylight;
                var modified = new List<string>();

                if (!string.IsNullOrEmpty(enabled))
                {
                    if (!bool.TryParse(enabled, out var val))
                        return ToolHelpers.ErrorResponse($"Invalid skylightEnabled: {enabled}");
                    skylight.Enabled = val;
                    modified.Add("skylightEnabled");
                }

                if (!string.IsNullOrEmpty(shadowIntensity))
                {
                    if (!double.TryParse(shadowIntensity, out var val) || val < 0)
                        return ToolHelpers.ErrorResponse($"Invalid shadowIntensity: {shadowIntensity}");
                    skylight.ShadowIntensity = val;
                    modified.Add("shadowIntensity");
                }

                if (!string.IsNullOrEmpty(customEnvironment))
                {
                    var env = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                        e.Name.Equals(customEnvironment, StringComparison.OrdinalIgnoreCase));
                    if (env == null)
                        return ToolHelpers.ErrorResponse($"Environment '{customEnvironment}' not found");
                    rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Skylighting, env);
                    rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting, true);
                    modified.Add("customEnvironment");
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled = skylight.Enabled,
                    shadowIntensity = skylight.ShadowIntensity,
                    customEnvironmentOn = rs.RenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting),
                    modified
                });
            });
        }

        #endregion
    }
}
