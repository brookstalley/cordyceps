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
    public partial class RhinoRenderTool
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
            Description = "Rhino rendering: viewport, camera, display modes, named views, lights, materials, environments, ground plane, sun/skylight",
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
                    Description = "Get/set camera position and target, or apply a standard view preset",
                    Optional = new[] { "location", "target", "lens", "view", "preset" },
                    Example = "action='camera' OR action='camera', preset='front' OR action='camera', location='100,50,30', target='0,0,0'",
                    Tips = new[] { "presets: top, bottom, front, back, left, right, perspective, iso_nw, iso_ne, iso_sw, iso_se", "Coordinates as 'x,y,z'", "lens is 35mm equivalent focal length" }
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
                // Named view actions
                ["view_save"] = new ActionInfo
                {
                    Name = "view_save",
                    Description = "Save the current camera position as a named view",
                    Required = new[] { "name" },
                    Optional = new[] { "view" },
                    Example = "action='view_save', name='MyView'"
                },
                ["view_load"] = new ActionInfo
                {
                    Name = "view_load",
                    Description = "Restore a named view",
                    Required = new[] { "name" },
                    Optional = new[] { "view" },
                    Example = "action='view_load', name='MyView'"
                },
                ["view_list"] = new ActionInfo
                {
                    Name = "view_list",
                    Description = "List all named views",
                    Example = "action='view_list'"
                },
                ["view_delete"] = new ActionInfo
                {
                    Name = "view_delete",
                    Description = "Delete a named view",
                    Required = new[] { "name" },
                    Example = "action='view_delete', name='MyView'"
                },
                // Light actions
                ["light_add"] = new ActionInfo
                {
                    Name = "light_add",
                    Description = "Create a light in the scene",
                    Required = new[] { "type", "location" },
                    Optional = new[] { "target", "color", "intensity", "spotAngle", "name" },
                    Example = "action='light_add', type='point', location='10,10,20', color='#FFFFFF'",
                    Tips = new[] { "type: point, spot, directional", "spot requires target", "location/target as 'x,y,z'" }
                },
                ["light_list"] = new ActionInfo
                {
                    Name = "light_list",
                    Description = "List all lights in the scene",
                    Example = "action='light_list'"
                },
                ["light_set"] = new ActionInfo
                {
                    Name = "light_set",
                    Description = "Modify a light's properties",
                    Required = new[] { "ids" },
                    Optional = new[] { "location", "target", "color", "intensity", "spotAngle", "enabled" },
                    Example = "action='light_set', ids='[\"abc\"]', intensity=2.0"
                },
                ["light_delete"] = new ActionInfo
                {
                    Name = "light_delete",
                    Description = "Delete lights from the scene",
                    Required = new[] { "ids" },
                    Example = "action='light_delete', ids='[\"abc\"]'"
                },
                // Material actions (from rhino_material)
                ["material_list"] = new ActionInfo
                {
                    Name = "material_list",
                    Description = "List all materials in the document",
                    Example = "action='material_list'",
                    Tips = new[] { "Shows which PBR slots have textures assigned per material" }
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
                ["material_texture"] = new ActionInfo
                {
                    Name = "material_texture",
                    Description = "Set or remove a texture on a PBR material slot",
                    Required = new[] { "name", "slot" },
                    Optional = new[] { "path", "repeat", "amount" },
                    Example = "action='material_texture', name='Grass', slot='base-color', path='C:/textures/grass.jpg', repeat='4,4'",
                    Tips = new[] {
                        "slots: base-color, roughness, metallic, bump, opacity, emission, displacement, ambient-occlusion, clearcoat, clearcoat-roughness",
                        "Omit path to remove texture from slot",
                        "repeat as 'u,v' (default '1,1'). amount 0-100 (default 100)"
                    }
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

        [McpServerTool, Description("Render operations. Actions: display|camera|zoom|modes|render|settings|ground|sun|skylight|view_save|view_load|view_list|view_delete|light_add|light_list|light_set|light_delete|material_list|material_library|material_instantiate|material_create|material_texture|material_apply|material_delete|env_list|env_current|env_set|env_create|env_delete|help")]
        public string RhinoRender(
            [Description("Action to perform")] string action,
            [Description("Display mode name")] string mode = null,
            [Description("View name")] string view = null,
            [Description("Camera location 'x,y,z'")] string location = null,
            [Description("Camera target 'x,y,z'")] string target = null,
            [Description("35mm lens length")] double lens = double.NaN,
            [Description("Standard view preset (top, bottom, front, back, left, right, perspective, iso_nw, iso_ne, iso_sw, iso_se)")] string preset = null,
            [Description("JSON array of object IDs")] string ids = null,
            [Description("Min render passes to wait for")] int wait = 0,
            [Description("Timeout in seconds")] int timeout = 30,
            // Settings parameters
            [Description("Background style: 'solid', 'gradient', 'environment'")] string style = null,
            [Description("Top/solid background color")] string colorTop = null,
            [Description("Bottom gradient color")] string colorBottom = null,
            [Description("Transparent background (true/false)")] string transparent = null,
            // Ground plane parameters
            [Description("Enable ground plane (true/false)")] string groundEnabled = null,
            [Description("Ground plane altitude in model units")] double groundAltitude = double.NaN,
            [Description("Auto-altitude (true/false)")] string autoAltitude = null,
            [Description("Shadow-only mode (true/false)")] string shadowOnly = null,
            // Sun parameters
            [Description("Enable sun (true/false)")] string sunEnabled = null,
            [Description("Sun azimuth 0-360 (135/225=classic 3/4 lighting)")] double azimuth = double.NaN,
            [Description("Sun altitude degrees (30-45=daylight, 10-20=dramatic)")] double sunAltitude = double.NaN,
            [Description("Sun intensity multiplier")] double intensity = double.NaN,
            [Description("Latitude for sun calculation")] double latitude = double.NaN,
            [Description("Longitude for sun calculation")] double longitude = double.NaN,
            [Description("DateTime for sun calculation")] string dateTime = null,
            // Skylight parameters
            [Description("Enable skylight (use WITH sun for realistic shadows)")] string skylightEnabled = null,
            [Description("Shadow intensity")] double shadowIntensity = double.NaN,
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
            // Texture parameters
            [Description("File path for texture image")] string path = null,
            [Description("UV repeat as 'u,v' (e.g., '4,4')")] string repeat = null,
            [Description("Slot influence 0-100")] double amount = 100,
            [Description("PBR slot name")] string slot = null,
            // Environment parameters
            [Description("Environment name or GUID")] string environment = null,
            [Description("Usage: 'background', 'lighting', 'reflection', 'all'")] string usage = "all",
            // Light parameters (reuses: type, location, target, color, intensity from other actions)
            [Description("Spot light cone angle in degrees")] double spotAngle = double.NaN,
            [Description("Enable/disable (true/false)")] string enabled = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("mode", mode),
                ("view", view),
                ("location", location),
                ("target", target),
                ("lens", double.IsNaN(lens) ? null : (object)lens),
                ("preset", preset),
                ("ids", ids),
                ("wait", wait != 0 ? (object)wait : null),
                ("timeout", timeout != 30 ? (object)timeout : null),
                ("style", style),
                ("colorTop", colorTop),
                ("colorBottom", colorBottom),
                ("transparent", transparent),
                ("groundEnabled", groundEnabled),
                ("groundAltitude", double.IsNaN(groundAltitude) ? null : (object)groundAltitude),
                ("autoAltitude", autoAltitude),
                ("shadowOnly", shadowOnly),
                ("material", material),
                ("sunEnabled", sunEnabled),
                ("azimuth", double.IsNaN(azimuth) ? null : (object)azimuth),
                ("sunAltitude", double.IsNaN(sunAltitude) ? null : (object)sunAltitude),
                ("intensity", double.IsNaN(intensity) ? null : (object)intensity),
                ("latitude", double.IsNaN(latitude) ? null : (object)latitude),
                ("longitude", double.IsNaN(longitude) ? null : (object)longitude),
                ("dateTime", dateTime),
                ("skylightEnabled", skylightEnabled),
                ("shadowIntensity", double.IsNaN(shadowIntensity) ? null : (object)shadowIntensity),
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
                // Texture params
                ("path", path),
                ("repeat", repeat),
                ("amount", amount),
                ("slot", slot),
                // Environment params
                ("environment", environment),
                ("usage", usage),
                // Light params (type, location, target, color, intensity already included above)
                ("spotAngle", double.IsNaN(spotAngle) ? null : (object)spotAngle),
                ("enabled", enabled)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            return action.ToLowerInvariant() switch
            {
                "display" => ActionDisplay(mode, view),
                "camera" => ActionCamera(location, target, lens, view, preset),
                "zoom" => ActionZoom(ids, view),
                "modes" => ActionModes(),
                "render" => ActionRender(view, wait, timeout),
                "settings" => ActionSettings(style, colorTop, colorBottom, transparent),
                "ground" => ActionGround(groundEnabled, groundAltitude, autoAltitude, shadowOnly, material),
                "sun" => ActionSun(sunEnabled, azimuth, sunAltitude, intensity, latitude, longitude, dateTime),
                "skylight" => ActionSkylight(skylightEnabled, shadowIntensity, customEnvironment),
                // Named view actions
                "view_save" => ActionViewSave(name, view),
                "view_load" => ActionViewLoad(name, view),
                "view_list" => ActionViewList(),
                "view_delete" => ActionViewDelete(name),
                // Light actions (reuse shared params: type, location, target, color, intensity)
                "light_add" => ActionLightAdd(type, location, target, color, intensity, spotAngle, name),
                "light_list" => ActionLightList(),
                "light_set" => ActionLightSet(ids, location, target, color, intensity, spotAngle, enabled),
                "light_delete" => ActionLightDelete(ids),
                // Material actions
                "material_list" => ActionMaterialList(),
                "material_library" => ActionMaterialLibrary(),
                "material_instantiate" => ActionMaterialInstantiate(type, name, color),
                "material_create" => ActionMaterialCreate(name, color, roughness, metallic, transparency, emission, ior),
                "material_texture" => ActionMaterialTexture(name, slot, path, repeat, amount),
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

        private string ActionCamera(string location, string target, double lens, string view, string preset)
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
                string appliedPreset = null;

                // Handle preset views first (overrides location/target)
                if (!string.IsNullOrEmpty(preset))
                {
                    var presetResult = ApplyViewPreset(vp, preset);
                    if (presetResult != null)
                        return presetResult; // Error response
                    appliedPreset = preset.ToLowerInvariant();
                }
                else if (!string.IsNullOrEmpty(location) || !string.IsNullOrEmpty(target))
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

                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["view"] = vp.Name,
                    ["location"] = $"{vp.CameraLocation.X:F3},{vp.CameraLocation.Y:F3},{vp.CameraLocation.Z:F3}",
                    ["target"] = $"{vp.CameraTarget.X:F3},{vp.CameraTarget.Y:F3},{vp.CameraTarget.Z:F3}",
                    ["lens"] = vp.Camera35mmLensLength,
                    ["distance"] = vp.CameraLocation.DistanceTo(vp.CameraTarget),
                    ["isPerspective"] = !vp.IsParallelProjection
                };

                if (appliedPreset != null)
                    result["preset"] = appliedPreset;

                return JsonConvert.SerializeObject(result);
            });
        }

        private string ApplyViewPreset(RhinoViewport vp, string preset)
        {
            var presetLower = preset.ToLowerInvariant();

            // Handle standard orthographic and perspective views
            DefinedViewportProjection? projection = presetLower switch
            {
                "top" => DefinedViewportProjection.Top,
                "bottom" => DefinedViewportProjection.Bottom,
                "front" => DefinedViewportProjection.Front,
                "back" => DefinedViewportProjection.Back,
                "left" => DefinedViewportProjection.Left,
                "right" => DefinedViewportProjection.Right,
                "perspective" => DefinedViewportProjection.Perspective,
                _ => null
            };

            if (projection.HasValue)
            {
                vp.SetProjection(projection.Value, null, false);
                return null; // Success - no error
            }

            // Handle isometric presets manually by setting camera position
            // Isometric views are positioned at 45-degree angles around the model
            Vector3d direction;
            switch (presetLower)
            {
                case "iso_nw" or "iso-nw" or "isonw":
                    direction = new Vector3d(-1, 1, 1);
                    break;
                case "iso_ne" or "iso-ne" or "isone":
                    direction = new Vector3d(1, 1, 1);
                    break;
                case "iso_sw" or "iso-sw" or "isosw":
                    direction = new Vector3d(-1, -1, 1);
                    break;
                case "iso_se" or "iso-se" or "isose":
                    direction = new Vector3d(1, -1, 1);
                    break;
                default:
                    return ToolHelpers.ErrorResponse($"Invalid preset: '{preset}'. Valid: top, bottom, front, back, left, right, perspective, iso_nw, iso_ne, iso_sw, iso_se");
            }

            // Set perspective and position camera
            vp.SetProjection(DefinedViewportProjection.Perspective, null, false);
            direction.Unitize();
            var target = vp.CameraTarget;
            var distance = vp.CameraLocation.DistanceTo(target);
            if (distance < 1) distance = 100; // Default distance if camera is at target
            var newLocation = target + direction * distance;
            vp.SetCameraLocations(target, newLocation);

            return null; // Success - no error
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
                        if (obj?.Geometry != null)
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
                        if (doc == null)
                            return JsonConvert.SerializeObject(new { success = false, error = "No active document" });
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
                    if (doc == null) return (0, false);
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

        // Settings Actions (settings, ground, sun, skylight) - see RhinoRenderTool.Settings.cs
        // Named View Actions (view_save, view_load, view_list, view_delete) - see RhinoRenderTool.Views.cs
        // Light Actions (light_add, light_list, light_set, light_delete) - see RhinoRenderTool.Lights.cs
        // Material Actions (material_*) - see RhinoRenderTool.Materials.cs
        // Environment Actions (env_*) - see RhinoRenderTool.Materials.cs
    }
}
