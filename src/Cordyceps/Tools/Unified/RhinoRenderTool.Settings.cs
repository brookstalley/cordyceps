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
            return _context.ExecuteOnUiThread(() => ToolHelpers.WithUndoRecord(RhinoDoc.ActiveDoc, "settings", () =>
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
            }));
        }

        private string ActionGround(string enabled, double altitude, string autoAltitude, string shadowOnly, string material)
        {
            return _context.ExecuteOnUiThread(() => ToolHelpers.WithUndoRecord(RhinoDoc.ActiveDoc, "ground", () =>
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

                if (!double.IsNaN(altitude))
                {
                    gp.Altitude = altitude;
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
            }));
        }

        private string ActionSun(string enabled, double azimuth, double altitude, double intensity, double latitude, double longitude, string dateTime)
        {
            return _context.ExecuteOnUiThread(() => ToolHelpers.WithUndoRecord(RhinoDoc.ActiveDoc, "sun", () =>
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

                if (!double.IsNaN(intensity))
                {
                    if (intensity < 0)
                        return ToolHelpers.ErrorResponse($"Invalid intensity: {intensity} (must be >= 0)");
                    sun.Intensity = intensity;
                    modified.Add("intensity");
                }

                if (!double.IsNaN(azimuth) || !double.IsNaN(altitude))
                {
                    double az = sun.Azimuth, alt = sun.Altitude;

                    if (!double.IsNaN(azimuth))
                    {
                        if (azimuth < 0 || azimuth > 360)
                            return ToolHelpers.ErrorResponse($"Invalid azimuth: {azimuth} (must be 0-360)");
                        az = azimuth;
                        modified.Add("azimuth");
                    }

                    if (!double.IsNaN(altitude))
                    {
                        if (altitude < -90 || altitude > 90)
                            return ToolHelpers.ErrorResponse($"Invalid sunAltitude: {altitude}");
                        alt = altitude;
                        modified.Add("sunAltitude");
                    }

                    sun.ManualControlOn = true;
                    sun.Azimuth = az;
                    sun.Altitude = alt;
                    modified.Add("manualControl");
                }

                // Latitude/longitude/dateTime only affect the sun position while manual control
                // is OFF. When they are provided without azimuth/altitude in the same call, the
                // caller wants the computed position — switch manual control off so the values
                // take effect instead of being silent no-ops.
                bool hasManual = !double.IsNaN(azimuth) || !double.IsNaN(altitude);
                bool hasComputed = !double.IsNaN(latitude) || !double.IsNaN(longitude) || !string.IsNullOrEmpty(dateTime);
                if (hasComputed && !hasManual && sun.ManualControlOn)
                {
                    sun.ManualControlOn = false;
                    modified.Add("manualControl");
                }

                if (!double.IsNaN(latitude))
                {
                    if (latitude < -90 || latitude > 90)
                        return ToolHelpers.ErrorResponse($"Invalid latitude: {latitude}");
                    sun.Latitude = latitude;
                    modified.Add("latitude");
                }

                if (!double.IsNaN(longitude))
                {
                    if (longitude < -180 || longitude > 180)
                        return ToolHelpers.ErrorResponse($"Invalid longitude: {longitude}");
                    sun.Longitude = longitude;
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
                    mode = sun.ManualControlOn ? "manual" : "computed",
                    azimuth = sun.Azimuth,
                    altitude = sun.Altitude,
                    intensity = sun.Intensity,
                    modified
                });
            }));
        }

        private string ActionSkylight(string enabled, double shadowIntensity, string customEnvironment)
        {
            return _context.ExecuteOnUiThread(() => ToolHelpers.WithUndoRecord(RhinoDoc.ActiveDoc, "skylight", () =>
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

                if (!double.IsNaN(shadowIntensity))
                {
                    if (shadowIntensity < 0)
                        return ToolHelpers.ErrorResponse($"Invalid shadowIntensity: {shadowIntensity} (must be >= 0)");
                    skylight.ShadowIntensity = shadowIntensity;
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
            }));
        }

        #endregion
    }
}
