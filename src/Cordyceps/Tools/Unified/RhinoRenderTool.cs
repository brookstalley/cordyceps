using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Cordyceps.Core;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

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

        [McpServerTool, Description("Render operations. Actions: display|camera|zoom|modes|render|help")]
        public string RhinoRender(
            [Description("Action to perform")] string action,
            [Description("Display mode name")] string mode = null,
            [Description("View name")] string view = null,
            [Description("Camera location 'x,y,z'")] string location = null,
            [Description("Camera target 'x,y,z'")] string target = null,
            [Description("35mm lens length")] string lens = null,
            [Description("JSON array of object IDs for zoom")] string ids = null,
            [Description("Min render passes to wait for")] string wait = null,
            [Description("Timeout in seconds")] string timeout = null)
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
                ("timeout", timeout)
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
    }
}
