using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;

#pragma warning disable CA1416

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// Unified capture tool - screenshot operations for canvas and viewport
    /// </summary>
    [McpServerToolType]
    public class GhCaptureTool
    {
        private readonly GrasshopperContext _context;

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_capture",
            Description = "Screenshot operations for Grasshopper canvas and Rhino viewport",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["canvas"] = new ActionInfo
                {
                    Name = "canvas",
                    Description = "Capture the Grasshopper canvas to an image",
                    Optional = new[] { "path", "fit", "padding" },
                    Example = "action='canvas' OR action='canvas', path='/tmp/canvas.png'",
                    Tips = new[] { "fit=true (default) auto-zooms to content", "Use Read tool to view captured image" }
                },
                ["viewport"] = new ActionInfo
                {
                    Name = "viewport",
                    Description = "Capture Rhino viewport (3D geometry preview)",
                    Optional = new[] { "path", "view", "width", "height", "transparent" },
                    Example = "action='viewport' OR action='viewport', view='Perspective'",
                    Tips = new[] { "view: 'Perspective', 'Top', 'Front', 'Right'", "transparent only works with PNG" }
                },
                ["region"] = new ActionInfo
                {
                    Name = "region",
                    Description = "Capture a specific region of the canvas",
                    Required = new[] { "xMin", "yMin", "xMax", "yMax" },
                    Optional = new[] { "path" },
                    Example = "action='region', xMin=0, yMin=0, xMax=500, yMax=300"
                },
                ["views"] = new ActionInfo
                {
                    Name = "views",
                    Description = "List available Rhino views",
                    Example = "action='views'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                }
            },
            Notes = new[]
            {
                "Supports .png, .jpg, .bmp formats",
                "If path omitted, saves to temp file and returns path",
                "Use Read tool to view the captured image"
            }
        };

        public GhCaptureTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Capture operations. Actions: canvas|viewport|region|views|help")]
        public string GhCapture(
            [Description("Action to perform")] string action,
            [Description("Output file path (.png, .jpg, .bmp)")] string path = null,
            [Description("View name for viewport capture")] string view = null,
            [Description("Auto-fit content (true/false)")] string fit = null,
            [Description("Padding around content")] string padding = null,
            [Description("Output width")] string width = null,
            [Description("Output height")] string height = null,
            [Description("Transparent background (true/false)")] string transparent = null,
            [Description("Region xMin")] string xMin = null,
            [Description("Region yMin")] string yMin = null,
            [Description("Region xMax")] string xMax = null,
            [Description("Region yMax")] string yMax = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            // Parse numeric/boolean parameters
            bool fitBool = ToolHelpers.ParseBool(fit, true);
            int paddingInt = string.IsNullOrEmpty(padding) ? 50 : (int.TryParse(padding, out var p) ? p : 50);
            int widthInt = string.IsNullOrEmpty(width) ? 0 : (int.TryParse(width, out var w) ? w : 0);
            int heightInt = string.IsNullOrEmpty(height) ? 0 : (int.TryParse(height, out var h) ? h : 0);
            bool transparentBool = ToolHelpers.ParseBool(transparent, false);
            float xMinF = string.IsNullOrEmpty(xMin) ? 0 : (float.TryParse(xMin, out var x1) ? x1 : 0);
            float yMinF = string.IsNullOrEmpty(yMin) ? 0 : (float.TryParse(yMin, out var y1) ? y1 : 0);
            float xMaxF = string.IsNullOrEmpty(xMax) ? 0 : (float.TryParse(xMax, out var x2) ? x2 : 0);
            float yMaxF = string.IsNullOrEmpty(yMax) ? 0 : (float.TryParse(yMax, out var y2) ? y2 : 0);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("path", path),
                ("view", view),
                ("fit", fit),
                ("padding", padding),
                ("width", width),
                ("height", height),
                ("transparent", transparent),
                ("xMin", xMin),
                ("yMin", yMin),
                ("xMax", xMax),
                ("yMax", yMax)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            return action.ToLowerInvariant() switch
            {
                "canvas" => ActionCanvas(path, fitBool, paddingInt),
                "viewport" => ActionViewport(path, view, widthInt, heightInt, transparentBool),
                "region" => ActionRegion(path, xMinF, yMinF, xMaxF, yMaxF),
                "views" => ActionViews(),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionCanvas(string path, bool fit, int padding)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var canvas = Instances.ActiveCanvas;
                if (canvas == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper canvas");

                var doc = canvas.Document;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper document");

                var actualPath = path ?? GetTempImagePath("canvas", ".png");

                try
                {
                    EnsureDirectory(actualPath);
                    var format = GetImageFormat(actualPath);
                    if (format == null)
                        return ToolHelpers.ErrorResponse("Unsupported format. Use .png, .jpg, .bmp");

                    Bitmap bitmap;
                    if (fit && doc.ObjectCount > 0)
                    {
                        var bounds = GetContentBounds(doc, padding);
                        bitmap = bounds.HasValue ? CaptureCanvasRegion(canvas, bounds.Value) : canvas.GetCanvasScreenBuffer(GH_CanvasMode.Export);
                    }
                    else
                    {
                        bitmap = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Export);
                        // Check if non-fit capture is black
                        if (bitmap != null && IsBlackImage(bitmap))
                        {
                            bitmap.Dispose();
                            bitmap = null;
                        }
                    }

                    if (bitmap == null)
                        return ToolHelpers.ErrorResponse("Canvas capture returned black image. Ensure the Grasshopper window is visible and not minimized.");

                    bitmap.Save(actualPath, format);
                    var result = new { success = true, filePath = actualPath, width = bitmap.Width, height = bitmap.Height, hint = "Use Read tool to view image" };
                    bitmap.Dispose();
                    return JsonConvert.SerializeObject(result);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Capture failed: {ex.Message}");
                }
            });
        }

        private string ActionViewport(string path, string view, int width, int height, bool transparent)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var actualPath = path ?? GetTempImagePath("viewport", ".png");

                try
                {
                    EnsureDirectory(actualPath);
                    var format = GetImageFormat(actualPath);
                    if (format == null)
                        return ToolHelpers.ErrorResponse("Unsupported format. Use .png, .jpg, .bmp");

                    RhinoView targetView = null;
                    if (!string.IsNullOrEmpty(view))
                    {
                        targetView = rhinoDoc.Views.Find(view, false) ??
                            rhinoDoc.Views.FirstOrDefault(v => v.MainViewport.Name.Equals(view, StringComparison.OrdinalIgnoreCase));
                        if (targetView == null)
                        {
                            var avail = string.Join(", ", rhinoDoc.Views.Select(v => v.MainViewport.Name));
                            return ToolHelpers.ErrorResponse($"View '{view}' not found. Available: {avail}");
                        }
                    }
                    else
                    {
                        targetView = rhinoDoc.Views.ActiveView;
                    }

                    if (targetView == null)
                        return ToolHelpers.ErrorResponse("No active view");

                    Bitmap bitmap;
                    var displayMode = targetView.ActiveViewport.DisplayMode;
                    bool isRaytraced = displayMode?.EnglishName?.Equals("Raytraced", StringComparison.OrdinalIgnoreCase) ?? false;

                    if (width > 0 || height > 0 || transparent || isRaytraced)
                    {
                        var vc = new ViewCapture
                        {
                            Width = width > 0 ? width : targetView.ActiveViewport.Size.Width,
                            Height = height > 0 ? height : targetView.ActiveViewport.Size.Height,
                            TransparentBackground = transparent
                        };
                        bitmap = vc.CaptureToBitmap(targetView);
                    }
                    else
                    {
                        bitmap = targetView.CaptureToBitmap();
                    }

                    if (bitmap == null)
                        return ToolHelpers.ErrorResponse("Failed to capture viewport");

                    bitmap.Save(actualPath, format);
                    var result = new
                    {
                        success = true,
                        filePath = actualPath,
                        viewName = targetView.MainViewport.Name,
                        width = bitmap.Width,
                        height = bitmap.Height,
                        hint = "Use Read tool to view image"
                    };
                    bitmap.Dispose();
                    return JsonConvert.SerializeObject(result);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Capture failed: {ex.Message}");
                }
            });
        }

        private string ActionRegion(string path, float xMin, float yMin, float xMax, float yMax)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var canvas = Instances.ActiveCanvas;
                if (canvas == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper canvas");

                if (xMax <= xMin || yMax <= yMin)
                    return ToolHelpers.ErrorResponse("Invalid region: xMax > xMin and yMax > yMin required");

                var actualPath = path ?? GetTempImagePath("region", ".png");

                try
                {
                    EnsureDirectory(actualPath);
                    var format = GetImageFormat(actualPath);
                    if (format == null)
                        return ToolHelpers.ErrorResponse("Unsupported format. Use .png, .jpg, .bmp");

                    var bounds = new RectangleF(xMin, yMin, xMax - xMin, yMax - yMin);
                    var bitmap = CaptureCanvasRegion(canvas, bounds);

                    if (bitmap == null)
                        return ToolHelpers.ErrorResponse("Region capture returned black image. Ensure the Grasshopper window is visible and not minimized.");

                    bitmap.Save(actualPath, format);
                    var result = new
                    {
                        success = true,
                        filePath = actualPath,
                        region = new { xMin, yMin, xMax, yMax },
                        width = bitmap.Width,
                        height = bitmap.Height,
                        hint = "Use Read tool to view image"
                    };
                    bitmap.Dispose();
                    return JsonConvert.SerializeObject(result);
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Capture failed: {ex.Message}");
                }
            });
        }

        private string ActionViews()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                var views = rhinoDoc.Views.Select(v => new
                {
                    name = v.MainViewport.Name,
                    isActive = v == rhinoDoc.Views.ActiveView,
                    width = v.ActiveViewport.Size.Width,
                    height = v.ActiveViewport.Size.Height,
                    displayMode = v.ActiveViewport.DisplayMode?.EnglishName ?? "Unknown",
                    projection = v.MainViewport.IsParallelProjection ? "Parallel" : "Perspective"
                }).ToList();

                return JsonConvert.SerializeObject(new { success = true, count = views.Count, views });
            });
        }

        #region Helper Methods

        private RectangleF? GetContentBounds(GH_Document doc, int padding)
        {
            var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool hasContent = false;

            foreach (var obj in doc.Objects)
            {
                if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds)) continue;
                hasContent = true;
                var b = obj.Attributes.Bounds;
                minX = Math.Min(minX, b.Left);
                minY = Math.Min(minY, b.Top);
                maxX = Math.Max(maxX, b.Right);
                maxY = Math.Max(maxY, b.Bottom);
            }

            if (!hasContent) return null;
            return new RectangleF(minX - padding, minY - padding, (maxX - minX) + padding * 2, (maxY - minY) + padding * 2);
        }

        private Bitmap CaptureCanvasRegion(GH_Canvas canvas, RectangleF canvasBounds)
        {
            // Calculate zoom to fit content within the actual canvas window with margin
            int canvasW = canvas.Width;
            int canvasH = canvas.Height;
            const float marginFactor = 0.9f; // Leave 10% margin on each side
            var center = new PointF(canvasBounds.X + canvasBounds.Width / 2, canvasBounds.Y + canvasBounds.Height / 2);
            float zoom = Math.Min((float)canvasW / canvasBounds.Width, (float)canvasH / canvasBounds.Height) * marginFactor;

            // Focus needs pre-scaled coordinates: Focus(center * zoom) then set Zoom
            // Because GH_Viewport MidPoint_displayed = MidPoint_internal / zoom
            var scaledCenter = new PointF(center.X * zoom, center.Y * zoom);

            canvas.Viewport.Focus(scaledCenter);
            canvas.Viewport.Zoom = zoom;
            canvas.Viewport.ComputeProjection();

            // Force the viewport change to take effect
            canvas.Invalidate();
            canvas.Refresh();
            Application.DoEvents();
            System.Threading.Thread.Sleep(150);
            canvas.Refresh();
            Application.DoEvents();

            // Capture screen buffer
            Bitmap buf = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Control);
            if (buf != null && !IsBlackImage(buf))
            {
                var result = new Bitmap(buf);
                buf.Dispose();
                return result;
            }
            buf?.Dispose();

            // Fallback to Export mode
            buf = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Export);
            if (buf != null && !IsBlackImage(buf))
            {
                var result = new Bitmap(buf);
                buf.Dispose();
                return result;
            }
            buf?.Dispose();

            DebugLog.Warn("CaptureCanvasRegion: Both capture modes returned black images");
            return null;
        }

        private bool IsBlackImage(Bitmap bitmap)
        {
            // Sample a grid of pixels to check if image is all black
            int sampleSize = Math.Min(10, Math.Min(bitmap.Width, bitmap.Height));
            if (sampleSize < 2) return true;

            int stepX = Math.Max(1, bitmap.Width / sampleSize);
            int stepY = Math.Max(1, bitmap.Height / sampleSize);

            for (int x = stepX; x < bitmap.Width - stepX; x += stepX)
            {
                for (int y = stepY; y < bitmap.Height - stepY; y += stepY)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    // If any pixel has color (not pure black), image is not black
                    if (pixel.R > 10 || pixel.G > 10 || pixel.B > 10)
                        return false;
                }
            }
            return true;
        }

        private void EnsureDirectory(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private ImageFormat GetImageFormat(string path) =>
            Path.GetExtension(path)?.ToLowerInvariant() switch
            {
                ".png" => ImageFormat.Png,
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => null
            };

        private string GetTempImagePath(string prefix, string ext)
        {
            var dir = Path.Combine(Path.GetTempPath(), "Cordyceps");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
        }

        #endregion
    }
}
