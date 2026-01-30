using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Render;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified Rhino render tool - viewport, camera, display modes
    /// </summary>
    [McpServerToolType]
    public class RhinoRenderTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "rhino_render",
            Description = "Rhino viewport and render operations - display modes, camera, zoom",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["display"] = new ActionInfo
                {
                    Name = "display",
                    Description = "Get/set display mode for a viewport",
                    Optional = new[] { "mode", "view" },
                    Example = "action='display' OR action='display', mode='Shaded', view='Perspective'",
                    Tips = new[] { "modes: Wireframe, Shaded, Rendered, Ghosted, Arctic, Raytraced" }
                },
                ["camera"] = new ActionInfo
                {
                    Name = "camera",
                    Description = "Get/set camera position and target",
                    Optional = new[] { "location", "target", "lens", "view" },
                    Example = "action='camera' OR action='camera', location='100,50,30', target='0,0,0'",
                    Tips = new[] { "Coordinates as 'x,y,z'", "lens is 35mm equivalent focal length" }
                },
                ["zoom"] = new ActionInfo
                {
                    Name = "zoom",
                    Description = "Zoom to fit geometry",
                    Optional = new[] { "ids", "view" },
                    Example = "action='zoom' OR action='zoom', ids='[\"abc\"]'",
                    Tips = new[] { "Without ids, zooms to all geometry" }
                },
                ["modes"] = new ActionInfo
                {
                    Name = "modes",
                    Description = "List all available display modes",
                    Example = "action='modes'"
                },
                ["render"] = new ActionInfo
                {
                    Name = "render",
                    Description = "Get raytraced render status or wait for passes",
                    Optional = new[] { "view", "wait", "timeout" },
                    Example = "action='render' OR action='render', wait=100, timeout=30",
                    Tips = new[] { "wait: minimum passes to wait for", "Only for Raytraced mode" }
                },
                ["settings"] = new ActionInfo
                {
                    Name = "settings",
                    Description = "Get/set background render settings",
                    Optional = new[] { "style", "colorTop", "colorBottom", "transparent" },
                    Example = "action='settings' OR action='settings', style='gradient', colorTop='#87CEEB'",
                    Tips = new[] { "style: 'solid', 'gradient', or 'environment'" }
                },
                ["ground"] = new ActionInfo
                {
                    Name = "ground",
                    Description = "Get/set ground plane settings",
                    Optional = new[] { "groundEnabled", "groundAltitude", "autoAltitude", "shadowOnly", "material" },
                    Example = "action='ground', groundEnabled='true', shadowOnly='true'",
                    Tips = new[] { "groundAltitude is in model units" }
                },
                ["sun"] = new ActionInfo
                {
                    Name = "sun",
                    Description = "Get/set sun settings",
                    Optional = new[] { "sunEnabled", "azimuth", "sunAltitude", "intensity", "latitude", "longitude", "dateTime" },
                    Example = "action='sun', sunEnabled='true', azimuth='180', sunAltitude='45'",
                    Tips = new[] { "sunAltitude is in degrees (-90 to 90)" }
                },
                ["skylight"] = new ActionInfo
                {
                    Name = "skylight",
                    Description = "Get/set skylight settings",
                    Optional = new[] { "skylightEnabled", "shadowIntensity", "customEnvironment" },
                    Example = "action='skylight', skylightEnabled='true'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "view defaults to active view if not specified",
                "Use gh_capture to capture viewport images after setting up view"
            }
        };

        public RhinoRenderTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Render operations. Actions: display|camera|zoom|modes|render|settings|ground|sun|skylight|help")]
        public string RhinoRender(
            [Description("Action to perform")] string action,
            [Description("Display mode name")] string mode = null,
            [Description("View name")] string view = null,
            [Description("Camera location 'x,y,z'")] string location = null,
            [Description("Camera target 'x,y,z'")] string target = null,
            [Description("35mm lens length")] string lens = null,
            [Description("JSON array of object IDs for zoom")] string ids = null,
            [Description("Min render passes to wait for")] string wait = null,
            [Description("Timeout in seconds")] string timeout = null,
            // Settings parameters
            [Description("Background style: 'solid', 'gradient', 'environment'")] string style = null,
            [Description("Top/solid background color")] string colorTop = null,
            [Description("Bottom gradient color")] string colorBottom = null,
            [Description("Transparent background (true/false)")] string transparent = null,
            // Ground plane parameters
            [Description("Enable ground plane (true/false)")] string groundEnabled = null,
            [Description("Ground plane altitude in model units")] string groundAltitude = null,
            [Description("Auto-altitude (true/false)")] string autoAltitude = null,
            [Description("Shadow-only mode (true/false)")] string shadowOnly = null,
            [Description("Material name for ground")] string material = null,
            // Sun parameters
            [Description("Enable sun (true/false)")] string sunEnabled = null,
            [Description("Sun azimuth in degrees (0-360)")] string azimuth = null,
            [Description("Sun altitude in degrees (-90 to 90)")] string sunAltitude = null,
            [Description("Sun intensity multiplier")] string intensity = null,
            [Description("Latitude for sun calculation")] string latitude = null,
            [Description("Longitude for sun calculation")] string longitude = null,
            [Description("DateTime for sun calculation")] string dateTime = null,
            // Skylight parameters
            [Description("Enable skylight (true/false)")] string skylightEnabled = null,
            [Description("Shadow intensity")] string shadowIntensity = null,
            [Description("Custom environment name")] string customEnvironment = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("mode", mode),
                ("view", view),
                ("location", location),
                ("target", target),
                ("lens", lens),
                ("ids", ids),
                ("wait", wait),
                ("timeout", timeout),
                ("style", style),
                ("colorTop", colorTop),
                ("colorBottom", colorBottom),
                ("transparent", transparent),
                ("groundEnabled", groundEnabled),
                ("groundAltitude", groundAltitude),
                ("autoAltitude", autoAltitude),
                ("shadowOnly", shadowOnly),
                ("material", material),
                ("sunEnabled", sunEnabled),
                ("azimuth", azimuth),
                ("sunAltitude", sunAltitude),
                ("intensity", intensity),
                ("latitude", latitude),
                ("longitude", longitude),
                ("dateTime", dateTime),
                ("skylightEnabled", skylightEnabled),
                ("shadowIntensity", shadowIntensity),
                ("customEnvironment", customEnvironment)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Parse numeric parameters with defaults
            double lensDbl = string.IsNullOrEmpty(lens) ? 0 : (double.TryParse(lens, out var l) ? l : 0);
            int waitInt = string.IsNullOrEmpty(wait) ? 0 : (int.TryParse(wait, out var w) ? w : 0);
            int timeoutInt = string.IsNullOrEmpty(timeout) ? 30 : (int.TryParse(timeout, out var t) ? t : 30);

            return action.ToLowerInvariant() switch
            {
                "display" => ActionDisplay(mode, view),
                "camera" => ActionCamera(location, target, lensDbl, view),
                "zoom" => ActionZoom(ids, view),
                "modes" => ActionModes(),
                "render" => ActionRender(view, waitInt, timeoutInt),
                "settings" => ActionSettings(style, colorTop, colorBottom, transparent),
                "ground" => ActionGround(groundEnabled, groundAltitude, autoAltitude, shadowOnly, material),
                "sun" => ActionSun(sunEnabled, azimuth, sunAltitude, intensity, latitude, longitude, dateTime),
                "skylight" => ActionSkylight(skylightEnabled, shadowIntensity, customEnvironment),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionDisplay(string mode, string view)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var targetView = GetView(doc, view);
                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View not found: {view ?? "active"}");

                if (!string.IsNullOrEmpty(mode))
                {
                    var displayMode = DisplayModeDescription.GetDisplayModes()
                        .FirstOrDefault(m => m.EnglishName.Equals(mode, StringComparison.OrdinalIgnoreCase));
                    if (displayMode == null)
                    {
                        var avail = string.Join(", ", DisplayModeDescription.GetDisplayModes().Select(m => m.EnglishName));
                        return ToolHelpers.ErrorResponse($"Mode '{mode}' not found. Available: {avail}");
                    }
                    targetView.ActiveViewport.DisplayMode = displayMode;
                    doc.Views.Redraw();
                }

                var vp = targetView.ActiveViewport;
                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = vp.Name,
                    displayMode = vp.DisplayMode?.EnglishName ?? "Unknown",
                    isRaytraced = vp.DisplayMode?.EnglishName?.Equals("Raytraced", StringComparison.OrdinalIgnoreCase) ?? false
                });
            });
        }

        private string ActionCamera(string location, string target, double lens, string view)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var targetView = GetView(doc, view);
                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View not found: {view ?? "active"}");

                var vp = targetView.ActiveViewport;

                if (!string.IsNullOrEmpty(location) || !string.IsNullOrEmpty(target))
                {
                    Point3d? newLoc = null, newTgt = null;
                    if (!string.IsNullOrEmpty(location))
                    {
                        if (!ToolHelpers.TryParsePoint3d(location, out var loc))
                            return ToolHelpers.ErrorResponse($"Invalid location: {location}");
                        newLoc = loc;
                    }
                    if (!string.IsNullOrEmpty(target))
                    {
                        if (!ToolHelpers.TryParsePoint3d(target, out var tgt))
                            return ToolHelpers.ErrorResponse($"Invalid target: {target}");
                        newTgt = tgt;
                    }

                    if (newLoc.HasValue && newTgt.HasValue)
                        vp.SetCameraLocations(newTgt.Value, newLoc.Value);
                    else if (newLoc.HasValue)
                        vp.SetCameraLocation(newLoc.Value, true);
                    else if (newTgt.HasValue)
                        vp.SetCameraTarget(newTgt.Value, true);
                }

                if (lens > 0 && !vp.IsParallelProjection)
                    vp.Camera35mmLensLength = lens;

                doc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = vp.Name,
                    location = $"{vp.CameraLocation.X:F3},{vp.CameraLocation.Y:F3},{vp.CameraLocation.Z:F3}",
                    target = $"{vp.CameraTarget.X:F3},{vp.CameraTarget.Y:F3},{vp.CameraTarget.Z:F3}",
                    lens = vp.Camera35mmLensLength,
                    distance = vp.CameraLocation.DistanceTo(vp.CameraTarget),
                    isPerspective = !vp.IsParallelProjection
                });
            });
        }

        private string ActionZoom(string ids, string view)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var targetView = GetView(doc, view);
                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View not found: {view ?? "active"}");

                if (!string.IsNullOrEmpty(ids))
                {
                    if (!ToolHelpers.TryParseGuidArray(ids, out var guids, out var error))
                        return ToolHelpers.ErrorResponse(error);

                    var bbox = BoundingBox.Empty;
                    int found = 0;
                    foreach (var guid in guids)
                    {
                        var obj = doc.Objects.FindId(guid);
                        if (obj != null)
                        {
                            bbox.Union(obj.Geometry.GetBoundingBox(true));
                            found++;
                        }
                    }

                    if (found == 0)
                        return ToolHelpers.ErrorResponse("No valid objects found");

                    bbox.Inflate(bbox.Diagonal.Length * 0.1);
                    targetView.ActiveViewport.ZoomBoundingBox(bbox);
                }
                else
                {
                    targetView.ActiveViewport.ZoomExtents();
                }

                doc.Views.Redraw();
                var vp = targetView.ActiveViewport;
                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = vp.Name,
                    location = $"{vp.CameraLocation.X:F3},{vp.CameraLocation.Y:F3},{vp.CameraLocation.Z:F3}",
                    target = $"{vp.CameraTarget.X:F3},{vp.CameraTarget.Y:F3},{vp.CameraTarget.Z:F3}"
                });
            });
        }

        private string ActionModes()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var modes = DisplayModeDescription.GetDisplayModes().Select(m => new
                {
                    name = m.EnglishName,
                    localName = m.LocalName,
                    id = m.Id.ToString()
                }).ToList();

                return JsonConvert.SerializeObject(new { success = true, count = modes.Count, modes });
            });
        }

        private string ActionRender(string view, int wait, int timeout)
        {
            if (wait > 0)
                return WaitForRender(view, wait, timeout);

            return _context.ExecuteOnUiThread(() =>
            {
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var targetView = GetView(doc, view);
                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View not found: {view ?? "active"}");

                var vp = targetView.ActiveViewport;
                bool isRaytraced = vp.DisplayMode?.EnglishName?.Equals("Raytraced", StringComparison.OrdinalIgnoreCase) ?? false;

                if (!isRaytraced)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        view = vp.Name,
                        isRaytraced = false,
                        displayMode = vp.DisplayMode?.EnglishName ?? "Unknown"
                    });
                }

                var rtMode = targetView.RealtimeDisplayMode;
                int currentPass = rtMode?.LastRenderedPass() ?? 0;
                int maxPasses = rtMode?.MaxPasses ?? 0;
                bool isComplete = rtMode?.IsCompleted() ?? false;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = vp.Name,
                    isRaytraced = true,
                    currentPass,
                    maxPasses,
                    isComplete,
                    progress = maxPasses > 0 ? Math.Round((double)currentPass / maxPasses * 100, 1) : 0
                });
            });
        }

        private string WaitForRender(string view, int minPasses, int timeoutSec)
        {
            var startTime = DateTime.Now;
            var timeoutMs = timeoutSec * 1000;

            while (true)
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                if (elapsed >= timeoutMs)
                {
                    return _context.ExecuteOnUiThread(() =>
                    {
                        var doc = RhinoDoc.ActiveDoc;
                        var targetView = GetView(doc, view);
                        var rtMode = targetView?.RealtimeDisplayMode;
                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            timedOut = true,
                            currentPass = rtMode?.LastRenderedPass() ?? 0,
                            waitedMs = (int)elapsed
                        });
                    });
                }

                var status = _context.ExecuteOnUiThread<(int pass, bool complete)>(() =>
                {
                    var doc = RhinoDoc.ActiveDoc;
                    var targetView = GetView(doc, view);
                    var rtMode = targetView?.RealtimeDisplayMode;
                    if (rtMode == null) return (0, false);
                    return (rtMode.LastRenderedPass(), rtMode.IsCompleted());
                });

                if (status.pass >= minPasses || status.complete)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        timedOut = false,
                        currentPass = status.pass,
                        isComplete = status.complete,
                        waitedMs = (int)(DateTime.Now - startTime).TotalMilliseconds
                    });
                }

                Thread.Sleep(100);
            }
        }

        private RhinoView GetView(RhinoDoc doc, string viewName)
        {
            if (string.IsNullOrEmpty(viewName))
                return doc?.Views?.ActiveView;
            return doc?.Views?.Find(viewName, false) ??
                   doc?.Views?.FirstOrDefault(v => v.MainViewport.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));
        }

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
                        if (!double.TryParse(azimuth, out az))
                            return ToolHelpers.ErrorResponse($"Invalid azimuth: {azimuth}");
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
    }
}
