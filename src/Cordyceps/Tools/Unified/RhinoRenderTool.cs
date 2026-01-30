using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
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

        // Built-in material types mapped to ContentUuids
        private static readonly Dictionary<string, Guid> BuiltInMaterialTypes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            ["Metal"] = ContentUuids.MetalMaterialType,
            ["Glass"] = ContentUuids.GlassMaterialType,
            ["Plastic"] = ContentUuids.PlasticMaterialType,
            ["Paint"] = ContentUuids.PaintMaterialType,
            ["Gem"] = ContentUuids.GemMaterialType,
            ["Plaster"] = ContentUuids.PlasterMaterialType,
            ["Picture"] = ContentUuids.PictureMaterialType,
            ["PhysicallyBased"] = ContentUuids.PhysicallyBasedMaterialType,
            ["Blend"] = ContentUuids.BlendMaterialType,
            ["DoubleSided"] = ContentUuids.DoubleSidedMaterialType,
            ["Emission"] = ContentUuids.EmissionMaterialType
        };

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
                },
                // Material actions (from rhino_material)
                ["material_list"] = new ActionInfo
                {
                    Name = "material_list",
                    Description = "List all materials in the document",
                    Example = "action='material_list'"
                },
                ["material_library"] = new ActionInfo
                {
                    Name = "material_library",
                    Description = "List available built-in material types",
                    Example = "action='material_library'"
                },
                ["material_instantiate"] = new ActionInfo
                {
                    Name = "material_instantiate",
                    Description = "Create a material from a built-in type",
                    Required = new[] { "type" },
                    Optional = new[] { "name", "color" },
                    Example = "action='material_instantiate', type='Metal', name='Copper', color='#B87333'"
                },
                ["material_create"] = new ActionInfo
                {
                    Name = "material_create",
                    Description = "Create a PBR material from scratch",
                    Required = new[] { "name", "color" },
                    Optional = new[] { "roughness", "metallic", "transparency", "emission", "ior" },
                    Example = "action='material_create', name='Red Metal', color='#FF0000', metallic=1"
                },
                ["material_apply"] = new ActionInfo
                {
                    Name = "material_apply",
                    Description = "Apply material to objects",
                    Required = new[] { "ids", "material" },
                    Example = "action='material_apply', ids='[\"abc\"]', material='Red Metal'"
                },
                ["material_delete"] = new ActionInfo
                {
                    Name = "material_delete",
                    Description = "Delete a material",
                    Required = new[] { "name" },
                    Example = "action='material_delete', name='Red Metal'"
                },
                // Environment actions (from rhino_environment)
                ["env_list"] = new ActionInfo
                {
                    Name = "env_list",
                    Description = "List all render environments",
                    Example = "action='env_list'"
                },
                ["env_current"] = new ActionInfo
                {
                    Name = "env_current",
                    Description = "Get current environment for each usage type",
                    Example = "action='env_current'"
                },
                ["env_set"] = new ActionInfo
                {
                    Name = "env_set",
                    Description = "Set the current render environment",
                    Required = new[] { "environment" },
                    Optional = new[] { "usage" },
                    Example = "action='env_set', environment='Studio', usage='all'"
                },
                ["env_create"] = new ActionInfo
                {
                    Name = "env_create",
                    Description = "Create a solid-color environment",
                    Required = new[] { "name", "color" },
                    Example = "action='env_create', name='Blue Sky', color='#87CEEB'"
                },
                ["env_delete"] = new ActionInfo
                {
                    Name = "env_delete",
                    Description = "Delete an environment",
                    Required = new[] { "name" },
                    Example = "action='env_delete', name='Blue Sky'"
                }
            },
            Notes = new[]
            {
                "view defaults to active view if not specified",
                "Use gh_document(action='capture_viewport') to capture images"
            }
        };

        public RhinoRenderTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Render operations. Actions: display|camera|zoom|modes|render|settings|ground|sun|skylight|material_list|material_library|material_instantiate|material_create|material_apply|material_delete|env_list|env_current|env_set|env_create|env_delete|help")]
        public string RhinoRender(
            [Description("Action to perform")] string action,
            [Description("Display mode name")] string mode = null,
            [Description("View name")] string view = null,
            [Description("Camera location 'x,y,z'")] string location = null,
            [Description("Camera target 'x,y,z'")] string target = null,
            [Description("35mm lens length")] string lens = null,
            [Description("JSON array of object IDs")] string ids = null,
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
            // Sun parameters
            [Description("Enable sun (true/false)")] string sunEnabled = null,
            [Description("Sun azimuth 0-360 (135/225=classic 3/4 lighting)")] string azimuth = null,
            [Description("Sun altitude degrees (30-45=daylight, 10-20=dramatic)")] string sunAltitude = null,
            [Description("Sun intensity multiplier")] string intensity = null,
            [Description("Latitude for sun calculation")] string latitude = null,
            [Description("Longitude for sun calculation")] string longitude = null,
            [Description("DateTime for sun calculation")] string dateTime = null,
            // Skylight parameters
            [Description("Enable skylight (use WITH sun for realistic shadows)")] string skylightEnabled = null,
            [Description("Shadow intensity")] string shadowIntensity = null,
            [Description("Custom environment name")] string customEnvironment = null,
            // Material parameters
            [Description("Material or object name")] string name = null,
            [Description("Built-in material type")] string type = null,
            [Description("Base color as hex '#RRGGBB'")] string color = null,
            [Description("Roughness 0-1")] double roughness = 0.5,
            [Description("Metalness 0-1")] double metallic = 0,
            [Description("Transparency 0-1")] double transparency = 0,
            [Description("Emission color")] string emission = null,
            [Description("IOR (glass=1.5)")] double ior = 1.0,
            [Description("Material name to apply")] string material = null,
            // Environment parameters
            [Description("Environment name or GUID")] string environment = null,
            [Description("Usage: 'background', 'lighting', 'reflection', 'all'")] string usage = "all")
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
                ("customEnvironment", customEnvironment),
                // Material params
                ("name", name),
                ("type", type),
                ("color", color),
                ("roughness", roughness),
                ("metallic", metallic),
                ("transparency", transparency),
                ("emission", emission),
                ("ior", ior),
                // Environment params
                ("environment", environment),
                ("usage", usage)
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
                // Material actions
                "material_list" => ActionMaterialList(),
                "material_library" => ActionMaterialLibrary(),
                "material_instantiate" => ActionMaterialInstantiate(type, name, color),
                "material_create" => ActionMaterialCreate(name, color, roughness, metallic, transparency, emission, ior),
                "material_apply" => ActionMaterialApply(ids, material),
                "material_delete" => ActionMaterialDelete(name),
                // Environment actions
                "env_list" => ActionEnvList(),
                "env_current" => ActionEnvCurrent(),
                "env_set" => ActionEnvSet(environment, usage),
                "env_create" => ActionEnvCreate(name, color),
                "env_delete" => ActionEnvDelete(name),
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

        #region Material Actions (from rhino_material)

        private string ActionMaterialList()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var materials = new List<object>();

                foreach (var renderMaterial in rhinoDoc.RenderMaterials)
                {
                    var pbr = renderMaterial.ConvertToPhysicallyBased(RenderTexture.TextureGeneration.Allow);

                    var materialInfo = new Dictionary<string, object>
                    {
                        ["id"] = renderMaterial.Id.ToString(),
                        ["name"] = renderMaterial.Name,
                        ["type"] = "RenderMaterial"
                    };

                    if (pbr != null)
                    {
                        materialInfo["baseColor"] = ToolHelpers.ColorToHex(pbr.BaseColor);
                        materialInfo["roughness"] = pbr.Roughness;
                        materialInfo["metallic"] = pbr.Metallic;
                        materialInfo["opacity"] = pbr.Opacity;
                        materialInfo["ior"] = pbr.OpacityIOR;
                    }

                    materials.Add(materialInfo);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = materials.Count,
                    materials
                });
            });
        }

        private string ActionMaterialLibrary()
        {
            var types = BuiltInMaterialTypes.Select(kvp => new
            {
                name = kvp.Key,
                guid = kvp.Value.ToString(),
                description = GetMaterialTypeDescription(kvp.Key)
            }).ToList();

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = types.Count,
                types,
                usage = "Use action='material_instantiate', type='<type>' to create"
            });
        }

        private static string GetMaterialTypeDescription(string typeName)
        {
            return typeName switch
            {
                "Metal" => "Metallic materials (gold, copper, silver, aluminum)",
                "Glass" => "Transparent with refraction (ior=1.5)",
                "Plastic" => "Non-metallic with varying glossiness",
                "Paint" => "Painted surface with color and sheen",
                "Gem" => "Gemstone materials with dispersion",
                "Plaster" => "Matte diffuse (concrete, stone)",
                "Picture" => "Image-based materials",
                "PhysicallyBased" => "Full PBR material",
                "Blend" => "Blend between two materials",
                "DoubleSided" => "Different materials front/back",
                "Emission" => "Light-emitting materials",
                _ => "Material type"
            };
        }

        private string ActionMaterialInstantiate(string type, string name, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(type))
                    return ToolHelpers.ErrorResponse("type is required. Use action='material_library' to see types.");

                if (!BuiltInMaterialTypes.TryGetValue(type, out var typeGuid))
                {
                    var availableTypes = string.Join(", ", BuiltInMaterialTypes.Keys);
                    return ToolHelpers.ErrorResponse($"Unknown type: '{type}'. Available: {availableTypes}");
                }

                var materialName = !string.IsNullOrEmpty(name) ? name : $"{type} Material";

                var existing = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = false,
                        alreadyExists = true,
                        id = existing.Id.ToString(),
                        name = existing.Name,
                        type
                    });
                }

                try
                {
                    var renderMaterial = RenderContentType.NewContentFromTypeId(typeGuid) as RenderMaterial;
                    if (renderMaterial == null)
                        return ToolHelpers.ErrorResponse($"Failed to create material of type '{type}'");

                    renderMaterial.BeginChange(RenderContent.ChangeContexts.Program);
                    renderMaterial.Name = materialName;

                    if (!string.IsNullOrEmpty(color) && ToolHelpers.TryParseColor(color, out var baseColor))
                    {
                        var color4f = Color4f.FromArgb(1, baseColor.R / 255f, baseColor.G / 255f, baseColor.B / 255f);
                        var colorParamNames = new[] { "color", "diffuse", "diffuse-color", "base-color", "Color" };
                        bool colorSet = false;

                        foreach (var paramName in colorParamNames)
                        {
                            try
                            {
                                if (renderMaterial.SetParameter(paramName, color4f))
                                {
                                    colorSet = true;
                                    break;
                                }
                            }
                            catch { }
                        }

                        if (!colorSet)
                        {
                            try
                            {
                                var sim = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
                                if (sim != null)
                                    sim.DiffuseColor = baseColor;
                            }
                            catch { }
                        }
                    }

                    renderMaterial.EndChange();
                    rhinoDoc.RenderMaterials.Add(renderMaterial);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = true,
                        id = renderMaterial.Id.ToString(),
                        name = renderMaterial.Name,
                        type,
                        typeGuid = typeGuid.ToString()
                    });
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Failed to instantiate material: {ex.Message}");
                }
            });
        }

        private string ActionMaterialCreate(string name, string color, double roughness, double metallic, double transparency, string emission, double ior)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseColor(color, out var baseColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}");

                var existing = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = false,
                        alreadyExists = true,
                        id = existing.Id.ToString(),
                        name = existing.Name
                    });
                }

                roughness = Math.Max(0, Math.Min(1, roughness));
                metallic = Math.Max(0, Math.Min(1, metallic));
                transparency = Math.Max(0, Math.Min(1, transparency));
                ior = Math.Max(1, Math.Min(3, ior));

                var basicMaterial = new Material
                {
                    Name = name,
                    DiffuseColor = baseColor,
                    Transparency = transparency,
                    Reflectivity = metallic * 0.5,
                    IndexOfRefraction = ior
                };

                var renderMaterial = RenderMaterial.CreateBasicMaterial(basicMaterial, rhinoDoc);

                try
                {
                    var pbr = renderMaterial.ConvertToPhysicallyBased(RenderTexture.TextureGeneration.Allow);
                    if (pbr != null)
                    {
                        pbr.BaseColor = Color4f.FromArgb(1, baseColor.R / 255f, baseColor.G / 255f, baseColor.B / 255f);
                        pbr.Roughness = roughness;
                        pbr.Metallic = metallic;
                        pbr.Opacity = 1.0 - transparency;
                        pbr.OpacityIOR = ior;

                        if (!string.IsNullOrEmpty(emission) && ToolHelpers.TryParseColor(emission, out var emissionColor))
                            pbr.Emission = Color4f.FromArgb(1, emissionColor.R / 255f, emissionColor.G / 255f, emissionColor.B / 255f);
                    }
                }
                catch { }

                rhinoDoc.RenderMaterials.Add(renderMaterial);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    created = true,
                    id = renderMaterial.Id.ToString(),
                    name = renderMaterial.Name,
                    color = ToolHelpers.ColorToHex(baseColor),
                    roughness,
                    metallic,
                    transparency,
                    ior
                });
            });
        }

        private string ActionMaterialApply(string objectIds, string material)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseGuidArray(objectIds, out var guids, out var parseError))
                    return ToolHelpers.ErrorResponse(parseError);

                RenderMaterial renderMaterial = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(material, StringComparison.OrdinalIgnoreCase));

                int materialIndex = -1;
                if (renderMaterial == null)
                {
                    if (int.TryParse(material, out var idx) && idx >= 0 && idx < rhinoDoc.Materials.Count)
                        materialIndex = idx;
                    else
                        return ToolHelpers.ErrorResponse($"Material '{material}' not found");
                }

                int succeeded = 0, failed = 0;

                foreach (var guid in guids)
                {
                    var obj = rhinoDoc.Objects.FindId(guid);
                    if (obj == null) { failed++; continue; }

                    var attrs = obj.Attributes.Duplicate();
                    attrs.MaterialSource = ObjectMaterialSource.MaterialFromObject;

                    if (renderMaterial != null)
                        attrs.RenderMaterial = renderMaterial;
                    else
                        attrs.MaterialIndex = materialIndex;

                    if (rhinoDoc.Objects.ModifyAttributes(obj, attrs, true))
                        succeeded++;
                    else
                        failed++;
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = failed == 0,
                    total = guids.Count,
                    succeeded,
                    failed,
                    material = renderMaterial?.Name ?? material
                });
            });
        }

        private string ActionMaterialDelete(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var renderMaterial = rhinoDoc.RenderMaterials.FirstOrDefault(m =>
                    m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (renderMaterial != null)
                {
                    var deleted = rhinoDoc.RenderMaterials.Remove(renderMaterial);
                    return JsonConvert.SerializeObject(new { success = deleted, name, deleted });
                }

                var materialIndex = rhinoDoc.Materials.Find(name, true);
                if (materialIndex >= 0)
                {
                    var deleted = rhinoDoc.Materials.DeleteAt(materialIndex);
                    return JsonConvert.SerializeObject(new { success = deleted, name, deleted });
                }

                return ToolHelpers.ErrorResponse($"Material '{name}' not found");
            });
        }

        #endregion

        #region Environment Actions (from rhino_environment)

        private string ActionEnvList()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var environments = new List<object>();

                foreach (var env in rhinoDoc.RenderEnvironments)
                {
                    var simEnv = env.SimulateEnvironment(true);
                    var envInfo = new Dictionary<string, object>
                    {
                        ["id"] = env.Id.ToString(),
                        ["name"] = env.Name,
                        ["typeName"] = env.TypeName
                    };

                    if (simEnv != null)
                        envInfo["backgroundColor"] = ToolHelpers.ColorToHex(simEnv.BackgroundColor);

                    environments.Add(envInfo);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = environments.Count,
                    environments
                });
            });
        }

        private string ActionEnvCurrent()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var rs = rhinoDoc.RenderSettings;

                object GetEnvInfo(RenderSettings.EnvironmentUsage usage)
                {
                    var env = rs.RenderEnvironment(usage, RenderSettings.EnvironmentPurpose.Standard);
                    if (env == null) return null;
                    return new { id = env.Id.ToString(), name = env.Name };
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    background = GetEnvInfo(RenderSettings.EnvironmentUsage.Background),
                    lighting = GetEnvInfo(RenderSettings.EnvironmentUsage.Skylighting),
                    reflection = GetEnvInfo(RenderSettings.EnvironmentUsage.Reflection)
                });
            });
        }

        private string ActionEnvSet(string environment, string usage)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                RenderEnvironment targetEnv = null;

                if (Guid.TryParse(environment, out var guid))
                    targetEnv = rhinoDoc.RenderEnvironments.FirstOrDefault(e => e.Id == guid);

                if (targetEnv == null)
                    targetEnv = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                        e.Name.Equals(environment, StringComparison.OrdinalIgnoreCase));

                if (targetEnv == null)
                {
                    var available = string.Join(", ", rhinoDoc.RenderEnvironments.Select(e => e.Name));
                    return ToolHelpers.ErrorResponse($"Environment '{environment}' not found. Available: {available}");
                }

                var rs = rhinoDoc.RenderSettings;
                var modified = new List<string>();

                usage = usage?.ToLowerInvariant() ?? "all";

                switch (usage)
                {
                    case "background":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Background, targetEnv);
                        modified.Add("background");
                        break;
                    case "lighting":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Skylighting, targetEnv);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting, true);
                        modified.Add("lighting");
                        break;
                    case "reflection":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Reflection, targetEnv);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Reflection, true);
                        modified.Add("reflection");
                        break;
                    case "all":
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Background, targetEnv);
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Skylighting, targetEnv);
                        rs.SetRenderEnvironment(RenderSettings.EnvironmentUsage.Reflection, targetEnv);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Skylighting, true);
                        rs.SetRenderEnvironmentOverride(RenderSettings.EnvironmentUsage.Reflection, true);
                        modified.AddRange(new[] { "background", "lighting", "reflection" });
                        break;
                    default:
                        return ToolHelpers.ErrorResponse($"Invalid usage: {usage}. Use 'background', 'lighting', 'reflection', or 'all'");
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    environment = targetEnv.Name,
                    environmentId = targetEnv.Id.ToString(),
                    modified
                });
            });
        }

        private string ActionEnvCreate(string name, string color)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (!ToolHelpers.TryParseColor(color, out var bgColor))
                    return ToolHelpers.ErrorResponse($"Invalid color format: {color}");

                var existing = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                    e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = false,
                        alreadyExists = true,
                        id = existing.Id.ToString(),
                        name = existing.Name
                    });
                }

                var simEnv = new SimulatedEnvironment();
                simEnv.BackgroundColor = bgColor;

                var renderEnv = RenderEnvironment.NewBasicEnvironment(simEnv, rhinoDoc);
                if (renderEnv == null)
                    return ToolHelpers.ErrorResponse("Failed to create environment");

                renderEnv.Name = name;
                rhinoDoc.RenderEnvironments.Add(renderEnv);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    created = true,
                    id = renderEnv.Id.ToString(),
                    name = renderEnv.Name,
                    color = ToolHelpers.ColorToHex(bgColor)
                });
            });
        }

        private string ActionEnvDelete(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var env = rhinoDoc.RenderEnvironments.FirstOrDefault(e =>
                    e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

                if (env == null)
                    return ToolHelpers.ErrorResponse($"Environment '{name}' not found");

                var envId = env.Id.ToString();
                var deleted = rhinoDoc.RenderEnvironments.Remove(env);

                return JsonConvert.SerializeObject(new { success = deleted, name, id = envId, deleted });
            });
        }

        #endregion
    }
}
