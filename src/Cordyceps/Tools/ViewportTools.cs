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

namespace Cordyceps.Tools
{
    /// <summary>
    /// Rhino viewport operations (display modes, camera control, zoom, render status)
    /// </summary>
    [McpServerToolType]
    public class ViewportTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public ViewportTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("List all available Rhino display modes (Wireframe, Shaded, Rendered, Raytraced, etc.).")]
        public string RhinoGetDisplayModes()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var modes = new List<object>();

                foreach (var mode in DisplayModeDescription.GetDisplayModes())
                {
                    modes.Add(new
                    {
                        id = mode.Id.ToString(),
                        name = mode.EnglishName,
                        localName = mode.LocalName,
                        isBuiltIn = mode.SupportsShadeCommand
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = modes.Count,
                    modes
                });
            });
        }

        [McpServerTool, Description("Set the display mode for a Rhino viewport.")]
        public string RhinoSetDisplayMode(
            [Description("Display mode name (e.g., 'Wireframe', 'Shaded', 'Rendered', 'Ghosted', 'Arctic', 'Raytraced')")] string mode,
            [Description("View name (e.g., 'Perspective', 'Top', 'Front'). Defaults to active view.")] string view = null)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                if (string.IsNullOrEmpty(mode))
                    return ToolHelpers.ErrorResponse("Display mode is required");

                // Find the display mode
                var displayMode = DisplayModeDescription.GetDisplayModes()
                    .FirstOrDefault(m => m.EnglishName.Equals(mode, StringComparison.OrdinalIgnoreCase) ||
                                        m.LocalName.Equals(mode, StringComparison.OrdinalIgnoreCase));

                if (displayMode == null)
                {
                    var availableModes = string.Join(", ", DisplayModeDescription.GetDisplayModes()
                        .Select(m => m.EnglishName));
                    return ToolHelpers.ErrorResponse($"Display mode '{mode}' not found. Available modes: {availableModes}");
                }

                // Find the view
                RhinoView targetView;
                if (string.IsNullOrEmpty(view))
                {
                    targetView = rhinoDoc.Views.ActiveView;
                }
                else
                {
                    targetView = rhinoDoc.Views.Find(view, false);
                    if (targetView == null)
                    {
                        var availableViews = string.Join(", ", rhinoDoc.Views.Select(v => v.ActiveViewport.Name));
                        return ToolHelpers.ErrorResponse($"View '{view}' not found. Available views: {availableViews}");
                    }
                }

                if (targetView == null)
                    return ToolHelpers.ErrorResponse("No active view found");

                // Set the display mode
                targetView.ActiveViewport.DisplayMode = displayMode;
                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = targetView.ActiveViewport.Name,
                    displayMode = displayMode.EnglishName,
                    isRaytraced = displayMode.EnglishName.Equals("Raytraced", StringComparison.OrdinalIgnoreCase)
                });
            });
        }

        [McpServerTool, Description("Get the current camera position, target, lens, and other viewport info. Useful for calculating orbit positions.")]
        public string RhinoGetCamera(
            [Description("View name. Defaults to active view.")] string view = null)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                RhinoView targetView;
                if (string.IsNullOrEmpty(view))
                {
                    targetView = rhinoDoc.Views.ActiveView;
                }
                else
                {
                    targetView = rhinoDoc.Views.Find(view, false);
                }

                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View '{view ?? "active"}' not found");

                var vp = targetView.ActiveViewport;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = vp.Name,
                    location = Point3dToString(vp.CameraLocation),
                    target = Point3dToString(vp.CameraTarget),
                    up = Vector3dToString(vp.CameraUp),
                    direction = Vector3dToString(vp.CameraDirection),
                    lens = vp.Camera35mmLensLength,
                    distance = vp.CameraLocation.DistanceTo(vp.CameraTarget),
                    isPerspective = !vp.IsParallelProjection,
                    displayMode = vp.DisplayMode?.EnglishName ?? "Unknown"
                });
            });
        }

        [McpServerTool, Description("Set the camera position and/or target for a Rhino viewport. Coordinates as 'x,y,z' strings.")]
        public string RhinoSetCamera(
            [Description("Camera location as 'x,y,z' (e.g., '100,50,30')")] string location = null,
            [Description("Camera target as 'x,y,z' (e.g., '0,0,0')")] string target = null,
            [Description("35mm equivalent lens length in mm (e.g., 50). Only for perspective views.")] string lens = null,
            [Description("View name. Defaults to active view.")] string view = null)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                RhinoView targetView;
                if (string.IsNullOrEmpty(view))
                {
                    targetView = rhinoDoc.Views.ActiveView;
                }
                else
                {
                    targetView = rhinoDoc.Views.Find(view, false);
                }

                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View '{view ?? "active"}' not found");

                var vp = targetView.ActiveViewport;

                // Parse and set location
                Point3d? newLocation = null;
                if (!string.IsNullOrEmpty(location))
                {
                    if (!TryParsePoint3d(location, out var loc))
                        return ToolHelpers.ErrorResponse($"Invalid location format: {location}. Use 'x,y,z'");
                    newLocation = loc;
                }

                // Parse and set target
                Point3d? newTarget = null;
                if (!string.IsNullOrEmpty(target))
                {
                    if (!TryParsePoint3d(target, out var tgt))
                        return ToolHelpers.ErrorResponse($"Invalid target format: {target}. Use 'x,y,z'");
                    newTarget = tgt;
                }

                // Set camera
                if (newLocation.HasValue && newTarget.HasValue)
                {
                    vp.SetCameraLocations(newTarget.Value, newLocation.Value);
                }
                else if (newLocation.HasValue)
                {
                    vp.SetCameraLocation(newLocation.Value, true);
                }
                else if (newTarget.HasValue)
                {
                    vp.SetCameraTarget(newTarget.Value, true);
                }

                // Set lens
                if (!string.IsNullOrEmpty(lens) && !vp.IsParallelProjection)
                {
                    if (double.TryParse(lens, out var lensValue) && lensValue > 0)
                    {
                        vp.Camera35mmLensLength = lensValue;
                    }
                }

                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = vp.Name,
                    location = Point3dToString(vp.CameraLocation),
                    target = Point3dToString(vp.CameraTarget),
                    lens = vp.Camera35mmLensLength,
                    distance = vp.CameraLocation.DistanceTo(vp.CameraTarget)
                });
            });
        }

        [McpServerTool, Description("Zoom to fit all visible geometry in the Rhino viewport.")]
        public string RhinoZoomExtents(
            [Description("View name. Defaults to active view.")] string view = null)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                RhinoView targetView;
                if (string.IsNullOrEmpty(view))
                {
                    targetView = rhinoDoc.Views.ActiveView;
                }
                else
                {
                    targetView = rhinoDoc.Views.Find(view, false);
                }

                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View '{view ?? "active"}' not found");

                targetView.ActiveViewport.ZoomExtents();
                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = targetView.ActiveViewport.Name,
                    location = Point3dToString(targetView.ActiveViewport.CameraLocation),
                    target = Point3dToString(targetView.ActiveViewport.CameraTarget)
                });
            });
        }

        [McpServerTool, Description("Zoom to fit specific Rhino objects in the viewport.")]
        public string RhinoZoomObjects(
            [Description("JSON array of object GUIDs to zoom to")] string objectIds,
            [Description("View name. Defaults to active view.")] string view = null)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                List<string> ids;
                try
                {
                    ids = JsonConvert.DeserializeObject<List<string>>(objectIds);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Invalid objectIds format: {ex.Message}");
                }

                if (ids == null || ids.Count == 0)
                    return ToolHelpers.ErrorResponse("objectIds array is empty");

                RhinoView targetView;
                if (string.IsNullOrEmpty(view))
                {
                    targetView = rhinoDoc.Views.ActiveView;
                }
                else
                {
                    targetView = rhinoDoc.Views.Find(view, false);
                }

                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View '{view ?? "active"}' not found");

                // Calculate bounding box of all objects
                var bbox = BoundingBox.Empty;
                int found = 0;

                foreach (var idStr in ids)
                {
                    if (Guid.TryParse(idStr, out var guid))
                    {
                        var obj = rhinoDoc.Objects.FindId(guid);
                        if (obj != null)
                        {
                            var objBbox = obj.Geometry.GetBoundingBox(true);
                            bbox.Union(objBbox);
                            found++;
                        }
                    }
                }

                if (found == 0)
                    return ToolHelpers.ErrorResponse("No valid objects found to zoom to");

                // Add some padding
                bbox.Inflate(bbox.Diagonal.Length * 0.1);

                targetView.ActiveViewport.ZoomBoundingBox(bbox);
                rhinoDoc.Views.Redraw();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = targetView.ActiveViewport.Name,
                    objectsFound = found,
                    location = Point3dToString(targetView.ActiveViewport.CameraLocation),
                    target = Point3dToString(targetView.ActiveViewport.CameraTarget)
                });
            });
        }

        [McpServerTool, Description("Get the current raytraced render status (passes completed, max passes, completion state).")]
        public string RhinoGetRenderStatus(
            [Description("View name. Defaults to active view.")] string view = null)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                RhinoView targetView;
                if (string.IsNullOrEmpty(view))
                {
                    targetView = rhinoDoc.Views.ActiveView;
                }
                else
                {
                    targetView = rhinoDoc.Views.Find(view, false);
                }

                if (targetView == null)
                    return ToolHelpers.ErrorResponse($"View '{view ?? "active"}' not found");

                var vp = targetView.ActiveViewport;
                var displayMode = vp.DisplayMode;
                bool isRaytraced = displayMode?.EnglishName?.Equals("Raytraced", StringComparison.OrdinalIgnoreCase) ?? false;

                if (!isRaytraced)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        view = vp.Name,
                        isRaytraced = false,
                        displayMode = displayMode?.EnglishName ?? "Unknown",
                        note = "View is not in Raytraced mode. Render status only available for Raytraced views."
                    });
                }

                // Try to get render status from RealtimeDisplayMode
                var realtimeMode = targetView.RealtimeDisplayMode;

                if (realtimeMode == null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        view = vp.Name,
                        isRaytraced = true,
                        currentPass = 0,
                        maxPasses = 0,
                        isComplete = false,
                        progress = 0.0,
                        note = "Raytraced mode initializing"
                    });
                }

                int currentPass = realtimeMode.LastRenderedPass();
                int maxPasses = realtimeMode.MaxPasses;
                bool isComplete = realtimeMode.IsCompleted();
                double progress = maxPasses > 0 ? (double)currentPass / maxPasses * 100 : 0;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    view = vp.Name,
                    isRaytraced = true,
                    currentPass,
                    maxPasses,
                    isComplete,
                    progress = Math.Round(progress, 1)
                });
            });
        }

        [McpServerTool, Description("Wait for raytraced render to reach a minimum number of passes or timeout. Returns render status.")]
        public string RhinoWaitForRender(
            [Description("Minimum passes to wait for (default: 100)")] int minPasses = 100,
            [Description("Timeout in seconds (default: 30)")] int timeout = 30,
            [Description("View name. Defaults to active view.")] string view = null)
        {

            var startTime = DateTime.Now;
            var timeoutMs = timeout * 1000;
            var pollIntervalMs = 100;

            // Initial check
            var initialResult = _context.ExecuteOnUiThread<(RhinoView view, string error, bool success)>(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return (null, "No active Rhino document", false);

                RhinoView targetView;
                if (string.IsNullOrEmpty(view))
                {
                    targetView = rhinoDoc.Views.ActiveView;
                }
                else
                {
                    targetView = rhinoDoc.Views.Find(view, false);
                }

                if (targetView == null)
                    return (null, $"View '{view ?? "active"}' not found", false);

                var vp = targetView.ActiveViewport;
                var displayMode = vp.DisplayMode;
                bool isRaytraced = displayMode?.EnglishName?.Equals("Raytraced", StringComparison.OrdinalIgnoreCase) ?? false;

                if (!isRaytraced)
                    return (null, "View is not in Raytraced mode", false);

                return (targetView, null, true);
            });

            if (initialResult.error != null)
                return ToolHelpers.ErrorResponse(initialResult.error);

            // Poll for render completion
            while (true)
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                if (elapsed >= timeoutMs)
                {
                    // Timeout - return current status
                    return _context.ExecuteOnUiThread(() =>
                    {
                        var targetView = initialResult.view;
                        var realtimeMode = targetView.RealtimeDisplayMode;
                        int currentPass = realtimeMode?.LastRenderedPass() ?? 0;
                        int maxPasses = realtimeMode?.MaxPasses ?? 0;
                        bool isComplete = realtimeMode?.IsCompleted() ?? false;

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            view = targetView.ActiveViewport.Name,
                            isRaytraced = true,
                            currentPass,
                            maxPasses,
                            isComplete,
                            progress = maxPasses > 0 ? Math.Round((double)currentPass / maxPasses * 100, 1) : 0,
                            waitedMs = (int)elapsed,
                            timedOut = true,
                            minPassesRequested = minPasses
                        });
                    });
                }

                // Check current status
                var status = _context.ExecuteOnUiThread<(int currentPass, int maxPasses, bool isComplete)>(() =>
                {
                    var targetView = initialResult.view;
                    var realtimeMode = targetView.RealtimeDisplayMode;

                    if (realtimeMode == null)
                        return (0, 0, false);

                    return (realtimeMode.LastRenderedPass(), realtimeMode.MaxPasses, realtimeMode.IsCompleted());
                });

                int currentPass = status.currentPass;
                bool isComplete = status.isComplete;

                // Check if we've reached our goal
                if (currentPass >= minPasses || isComplete)
                {
                    var elapsedFinal = (DateTime.Now - startTime).TotalMilliseconds;
                    return _context.ExecuteOnUiThread(() =>
                    {
                        var targetView = initialResult.view;
                        var realtimeMode = targetView.RealtimeDisplayMode;
                        int cp = realtimeMode?.LastRenderedPass() ?? 0;
                        int mp = realtimeMode?.MaxPasses ?? 0;
                        bool ic = realtimeMode?.IsCompleted() ?? false;

                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            view = targetView.ActiveViewport.Name,
                            isRaytraced = true,
                            currentPass = cp,
                            maxPasses = mp,
                            isComplete = ic,
                            progress = mp > 0 ? Math.Round((double)cp / mp * 100, 1) : 0,
                            waitedMs = (int)elapsedFinal,
                            timedOut = false,
                            minPassesRequested = minPasses
                        });
                    });
                }

                // Wait before next poll
                Thread.Sleep(pollIntervalMs);
            }
        }

        /// <summary>
        /// Convert Point3d to string format
        /// </summary>
        private static string Point3dToString(Point3d pt)
        {
            return $"{pt.X:F3},{pt.Y:F3},{pt.Z:F3}";
        }

        /// <summary>
        /// Convert Vector3d to string format
        /// </summary>
        private static string Vector3dToString(Vector3d vec)
        {
            return $"{vec.X:F3},{vec.Y:F3},{vec.Z:F3}";
        }

        /// <summary>
        /// Parse Point3d from "x,y,z" string
        /// </summary>
        private static bool TryParsePoint3d(string value, out Point3d result)
        {
            result = Point3d.Unset;
            if (string.IsNullOrEmpty(value))
                return false;

            var parts = value.Split(',');
            if (parts.Length != 3)
                return false;

            if (!double.TryParse(parts[0].Trim(), out var x))
                return false;
            if (!double.TryParse(parts[1].Trim(), out var y))
                return false;
            if (!double.TryParse(parts[2].Trim(), out var z))
                return false;

            result = new Point3d(x, y, z);
            return true;
        }
    }
}
