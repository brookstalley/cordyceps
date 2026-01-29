using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.Render;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Rhino render settings operations (background, sun, skylight, ground plane)
    /// </summary>
    [McpServerToolType]
    public class RenderSettingsTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public RenderSettingsTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        #region Background Settings

        [McpServerTool, Description("Get render background settings (style, colors, transparency).")]
        public string RhinoGetRenderSettings()
        {
            _server?.RecordCommand("rhino_get_render_settings");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    backgroundStyle = rs.BackgroundStyle.ToString(),
                    colorTop = ToolHelpers.ColorToHex(rs.BackgroundColorTop),
                    colorBottom = ToolHelpers.ColorToHex(rs.BackgroundColorBottom),
                    transparentBackground = rs.TransparentBackground
                });
            });
        }

        [McpServerTool, Description("Set render background settings.")]
        public string RhinoSetRenderSettings(
            [Description("Background style: 'solid', 'gradient', or 'environment'")] string style = null,
            [Description("Top/solid background color as hex '#RRGGBB' or RGB '255,128,0'")] string colorTop = null,
            [Description("Bottom gradient color")] string colorBottom = null,
            [Description("Transparent background (true/false)")] string transparent = null)
        {
            _server?.RecordCommand("rhino_set_render_settings");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;
                var modified = new List<string>();

                // Set background style
                if (!string.IsNullOrEmpty(style))
                {
                    BackgroundStyle bgStyle;
                    switch (style.ToLowerInvariant())
                    {
                        case "solid":
                        case "solidcolor":
                            bgStyle = BackgroundStyle.SolidColor;
                            break;
                        case "gradient":
                            bgStyle = BackgroundStyle.Gradient;
                            break;
                        case "environment":
                            bgStyle = BackgroundStyle.Environment;
                            break;
                        default:
                            return ToolHelpers.ErrorResponse($"Invalid background style: {style}. Use 'solid', 'gradient', or 'environment'");
                    }
                    rs.BackgroundStyle = bgStyle;
                    modified.Add("backgroundStyle");
                }

                // Set top color
                if (!string.IsNullOrEmpty(colorTop))
                {
                    if (!ToolHelpers.TryParseColor(colorTop, out var color))
                        return ToolHelpers.ErrorResponse($"Invalid colorTop format: {colorTop}. Use hex '#RRGGBB' or RGB '255,128,0'");
                    rs.BackgroundColorTop = color;
                    modified.Add("colorTop");
                }

                // Set bottom color
                if (!string.IsNullOrEmpty(colorBottom))
                {
                    if (!ToolHelpers.TryParseColor(colorBottom, out var color))
                        return ToolHelpers.ErrorResponse($"Invalid colorBottom format: {colorBottom}. Use hex '#RRGGBB' or RGB '255,128,0'");
                    rs.BackgroundColorBottom = color;
                    modified.Add("colorBottom");
                }

                // Set transparent background
                if (!string.IsNullOrEmpty(transparent))
                {
                    if (!bool.TryParse(transparent, out var transparentBool))
                        return ToolHelpers.ErrorResponse($"Invalid transparent value: {transparent}. Use 'true' or 'false'");
                    rs.TransparentBackground = transparentBool;
                    modified.Add("transparentBackground");
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

        #endregion

        #region Ground Plane

        [McpServerTool, Description("Get ground plane settings.")]
        public string RhinoGetGroundPlane()
        {
            _server?.RecordCommand("rhino_get_ground_plane");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var gp = rhinoDoc.RenderSettings.GroundPlane;

                // Try to get material name
                string materialName = null;
                var matId = gp.MaterialInstanceId;
                if (matId != Guid.Empty)
                {
                    var material = rhinoDoc.RenderMaterials.FirstOrDefault(m => m.Id == matId);
                    materialName = material?.Name;
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled = gp.Enabled,
                    altitude = gp.Altitude,
                    autoAltitude = gp.AutoAltitude,
                    shadowOnly = gp.ShadowOnly,
                    showUnderside = gp.ShowUnderside,
                    materialId = matId == Guid.Empty ? null : matId.ToString(),
                    materialName
                });
            });
        }

        [McpServerTool, Description("Set ground plane settings.")]
        public string RhinoSetGroundPlane(
            [Description("Enable ground plane (true/false)")] string enabled = null,
            [Description("Ground plane altitude in model units")] string altitude = null,
            [Description("Auto-altitude mode (true/false)")] string autoAltitude = null,
            [Description("Shadow-only mode (true/false)")] string shadowOnly = null,
            [Description("Show underside (true/false)")] string showUnderside = null,
            [Description("Material name to apply")] string material = null)
        {
            _server?.RecordCommand("rhino_set_ground_plane");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var gp = rhinoDoc.RenderSettings.GroundPlane;
                var modified = new List<string>();

                // Set enabled
                if (!string.IsNullOrEmpty(enabled))
                {
                    if (!bool.TryParse(enabled, out var enabledBool))
                        return ToolHelpers.ErrorResponse($"Invalid enabled value: {enabled}. Use 'true' or 'false'");
                    gp.Enabled = enabledBool;
                    modified.Add("enabled");
                }

                // Set altitude
                if (!string.IsNullOrEmpty(altitude))
                {
                    if (!double.TryParse(altitude, out var altitudeValue))
                        return ToolHelpers.ErrorResponse($"Invalid altitude value: {altitude}. Use a number.");
                    gp.Altitude = altitudeValue;
                    modified.Add("altitude");
                }

                // Set auto-altitude
                if (!string.IsNullOrEmpty(autoAltitude))
                {
                    if (!bool.TryParse(autoAltitude, out var autoAltitudeBool))
                        return ToolHelpers.ErrorResponse($"Invalid autoAltitude value: {autoAltitude}. Use 'true' or 'false'");
                    gp.AutoAltitude = autoAltitudeBool;
                    modified.Add("autoAltitude");
                }

                // Set shadow-only
                if (!string.IsNullOrEmpty(shadowOnly))
                {
                    if (!bool.TryParse(shadowOnly, out var shadowOnlyBool))
                        return ToolHelpers.ErrorResponse($"Invalid shadowOnly value: {shadowOnly}. Use 'true' or 'false'");
                    gp.ShadowOnly = shadowOnlyBool;
                    modified.Add("shadowOnly");
                }

                // Set show underside
                if (!string.IsNullOrEmpty(showUnderside))
                {
                    if (!bool.TryParse(showUnderside, out var showUndersideBool))
                        return ToolHelpers.ErrorResponse($"Invalid showUnderside value: {showUnderside}. Use 'true' or 'false'");
                    gp.ShowUnderside = showUndersideBool;
                    modified.Add("showUnderside");
                }

                // Set material
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
                    showUnderside = gp.ShowUnderside,
                    modified
                });
            });
        }

        #endregion

        #region Sun

        [McpServerTool, Description("Get sun settings.")]
        public string RhinoGetSun()
        {
            _server?.RecordCommand("rhino_get_sun");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var sun = rhinoDoc.Lights.Sun;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled = sun.Enabled,
                    manualControl = sun.ManualControlOn,
                    azimuth = sun.Azimuth,
                    altitude = sun.Altitude,
                    intensity = sun.Intensity,
                    north = sun.North,
                    latitude = sun.Latitude,
                    longitude = sun.Longitude,
                    timeZone = sun.TimeZone,
                    daylightSaving = sun.DaylightSavingOn,
                    dateTime = sun.GetDateTime(DateTimeKind.Local).ToString("o")
                });
            });
        }

        [McpServerTool, Description("Set sun settings. Use azimuth/altitude for manual positioning, or latitude/longitude/dateTime for calculated position.")]
        public string RhinoSetSun(
            [Description("Enable sun (true/false)")] string enabled = null,
            [Description("Manual azimuth in degrees (0-360, north=0, east=90)")] string azimuth = null,
            [Description("Manual altitude in degrees (-90 to 90)")] string altitude = null,
            [Description("Latitude for date-based calculation (-90 to 90)")] string latitude = null,
            [Description("Longitude for date-based calculation (-180 to 180)")] string longitude = null,
            [Description("Date/time as ISO 8601 string for date-based calculation")] string dateTime = null,
            [Description("Time zone offset from UTC in hours")] string timeZone = null,
            [Description("Daylight saving (true/false)")] string daylightSaving = null,
            [Description("Sun intensity multiplier")] string intensity = null,
            [Description("North direction angle in degrees on XY plane")] string north = null)
        {
            _server?.RecordCommand("rhino_set_sun");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var sun = rhinoDoc.Lights.Sun;
                var modified = new List<string>();

                // Set enabled
                if (!string.IsNullOrEmpty(enabled))
                {
                    if (!bool.TryParse(enabled, out var enabledBool))
                        return ToolHelpers.ErrorResponse($"Invalid enabled value: {enabled}. Use 'true' or 'false'");
                    sun.Enabled = enabledBool;
                    modified.Add("enabled");
                }

                // Set intensity
                if (!string.IsNullOrEmpty(intensity))
                {
                    if (!double.TryParse(intensity, out var intensityValue) || intensityValue < 0)
                        return ToolHelpers.ErrorResponse($"Invalid intensity value: {intensity}. Use a positive number.");
                    sun.Intensity = intensityValue;
                    modified.Add("intensity");
                }

                // Set north direction
                if (!string.IsNullOrEmpty(north))
                {
                    if (!double.TryParse(north, out var northValue))
                        return ToolHelpers.ErrorResponse($"Invalid north value: {north}. Use a number in degrees.");
                    sun.North = northValue;
                    modified.Add("north");
                }

                // Manual position mode - set azimuth and/or altitude
                if (!string.IsNullOrEmpty(azimuth) || !string.IsNullOrEmpty(altitude))
                {
                    double az = sun.Azimuth;
                    double alt = sun.Altitude;

                    if (!string.IsNullOrEmpty(azimuth))
                    {
                        if (!double.TryParse(azimuth, out az))
                            return ToolHelpers.ErrorResponse($"Invalid azimuth value: {azimuth}. Use a number in degrees.");
                        modified.Add("azimuth");
                    }

                    if (!string.IsNullOrEmpty(altitude))
                    {
                        if (!double.TryParse(altitude, out alt) || alt < -90 || alt > 90)
                            return ToolHelpers.ErrorResponse($"Invalid altitude value: {altitude}. Use a number between -90 and 90.");
                        modified.Add("altitude");
                    }

                    // Set position using properties (enables manual control)
                    sun.ManualControlOn = true;
                    sun.Azimuth = az;
                    sun.Altitude = alt;
                    if (!modified.Contains("azimuth")) modified.Add("azimuth");
                    if (!modified.Contains("altitude")) modified.Add("altitude");
                    modified.Add("manualControl");
                }

                // Location-based calculation mode
                if (!string.IsNullOrEmpty(latitude))
                {
                    if (!double.TryParse(latitude, out var latValue) || latValue < -90 || latValue > 90)
                        return ToolHelpers.ErrorResponse($"Invalid latitude value: {latitude}. Use a number between -90 and 90.");
                    sun.Latitude = latValue;
                    modified.Add("latitude");
                }

                if (!string.IsNullOrEmpty(longitude))
                {
                    if (!double.TryParse(longitude, out var lonValue) || lonValue < -180 || lonValue > 180)
                        return ToolHelpers.ErrorResponse($"Invalid longitude value: {longitude}. Use a number between -180 and 180.");
                    sun.Longitude = lonValue;
                    modified.Add("longitude");
                }

                if (!string.IsNullOrEmpty(timeZone))
                {
                    if (!double.TryParse(timeZone, out var tzValue) || tzValue < -12 || tzValue > 14)
                        return ToolHelpers.ErrorResponse($"Invalid timeZone value: {timeZone}. Use a number between -12 and 14.");
                    sun.TimeZone = tzValue;
                    modified.Add("timeZone");
                }

                if (!string.IsNullOrEmpty(daylightSaving))
                {
                    if (!bool.TryParse(daylightSaving, out var dstBool))
                        return ToolHelpers.ErrorResponse($"Invalid daylightSaving value: {daylightSaving}. Use 'true' or 'false'");
                    sun.DaylightSavingOn = dstBool;
                    modified.Add("daylightSaving");
                }

                if (!string.IsNullOrEmpty(dateTime))
                {
                    if (!DateTime.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                        return ToolHelpers.ErrorResponse($"Invalid dateTime value: {dateTime}. Use ISO 8601 format (e.g., '2024-06-21T12:00:00').");
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

        #endregion

        #region Skylight

        [McpServerTool, Description("Get skylight settings.")]
        public string RhinoGetSkylight()
        {
            _server?.RecordCommand("rhino_get_skylight");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;
                var skylight = rs.Skylight;

                // Try to get custom environment info using RenderSettings
                string customEnvName = null;
                var customEnvId = rs.RenderEnvironmentId(RenderSettings.EnvironmentUsage.Skylighting, RenderSettings.EnvironmentPurpose.Standard);
                var customEnvironmentOn = rs.RenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting);
                if (customEnvId != Guid.Empty)
                {
                    var env = rhinoDoc.RenderEnvironments.FirstOrDefault(e => e.Id == customEnvId);
                    customEnvName = env?.Name;
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled = skylight.Enabled,
                    shadowIntensity = skylight.ShadowIntensity,
                    customEnvironmentOn,
                    customEnvironmentId = customEnvId == Guid.Empty ? null : customEnvId.ToString(),
                    customEnvironmentName = customEnvName
                });
            });
        }

        [McpServerTool, Description("Set skylight settings.")]
        public string RhinoSetSkylight(
            [Description("Enable skylight (true/false)")] string enabled = null,
            [Description("Shadow intensity (0-2, currently unused by Rhino)")] string shadowIntensity = null,
            [Description("Use custom environment (true/false)")] string customEnvironmentOn = null,
            [Description("Custom environment name")] string customEnvironment = null)
        {
            _server?.RecordCommand("rhino_set_skylight");
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;
                var skylight = rs.Skylight;
                var modified = new List<string>();

                // Set enabled
                if (!string.IsNullOrEmpty(enabled))
                {
                    if (!bool.TryParse(enabled, out var enabledBool))
                        return ToolHelpers.ErrorResponse($"Invalid enabled value: {enabled}. Use 'true' or 'false'");
                    skylight.Enabled = enabledBool;
                    modified.Add("enabled");
                }

                // Set shadow intensity
                if (!string.IsNullOrEmpty(shadowIntensity))
                {
                    if (!double.TryParse(shadowIntensity, out var intensityValue) || intensityValue < 0)
                        return ToolHelpers.ErrorResponse($"Invalid shadowIntensity value: {shadowIntensity}. Use a non-negative number.");
                    skylight.ShadowIntensity = intensityValue;
                    modified.Add("shadowIntensity");
                }

                // Set custom environment on/off using RenderSettings
                if (!string.IsNullOrEmpty(customEnvironmentOn))
                {
                    if (!bool.TryParse(customEnvironmentOn, out var customEnvBool))
                        return ToolHelpers.ErrorResponse($"Invalid customEnvironmentOn value: {customEnvironmentOn}. Use 'true' or 'false'");
                    rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting, customEnvBool);
                    modified.Add("customEnvironmentOn");
                }

                // Set custom environment using RenderSettings
                if (!string.IsNullOrEmpty(customEnvironment))
                {
                    var env = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                        e.Name.Equals(customEnvironment, StringComparison.OrdinalIgnoreCase));
                    if (env == null)
                        return ToolHelpers.ErrorResponse($"Environment '{customEnvironment}' not found");
                    rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Skylighting, env);
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
