using System;
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
    public partial class GhDocumentTool
    {
        #region Capture Actions

        private string ActionCaptureCanvas(string path, bool fit, int padding)
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
                        if (bitmap != null && IsBlackImage(bitmap))
                        {
                            bitmap.Dispose();
                            bitmap = null;
                        }
                    }

                    if (bitmap == null)
                        return ToolHelpers.ErrorResponse("Canvas capture returned black image. Ensure the Grasshopper window is visible and not minimized.");

                    // try/finally so the bitmap is disposed even when Save throws (the outer
                    // catch turns that into an error response).
                    try
                    {
                        bitmap.Save(actualPath, format);
                        var result = new { success = true, filePath = actualPath, width = bitmap.Width, height = bitmap.Height, hint = "Use Read tool to view image" };
                        return JsonConvert.SerializeObject(result);
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Capture failed: {ex.Message}");
                }
            });
        }

        private string ActionCaptureViewport(string path, string view, int width, int height, bool transparent)
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

                    // try/finally so the bitmap is disposed even when Save throws (the outer
                    // catch turns that into an error response).
                    try
                    {
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
                        return JsonConvert.SerializeObject(result);
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Capture failed: {ex.Message}");
                }
            });
        }

        private string ActionCaptureRegion(string path, float xMin, float yMin, float xMax, float yMax)
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

                    // try/finally so the bitmap is disposed even when Save throws (the outer
                    // catch turns that into an error response).
                    try
                    {
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
                        return JsonConvert.SerializeObject(result);
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Capture failed: {ex.Message}");
                }
            });
        }

        private string ActionCaptureViews()
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

        #endregion

        #region Capture Helper Methods

        private RectangleF? GetContentBounds(GH_Document doc, int padding)
        {
            var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool hasContent = false;

            // Use GetActiveObjects to filter out phantom/orphaned objects
            foreach (var obj in ToolHelpers.GetActiveObjects(doc, infraIds))
            {
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
            int canvasW = canvas.Width;
            int canvasH = canvas.Height;
            const float marginFactor = 0.9f;
            var center = new PointF(canvasBounds.X + canvasBounds.Width / 2, canvasBounds.Y + canvasBounds.Height / 2);
            float zoom = Math.Min((float)canvasW / canvasBounds.Width, (float)canvasH / canvasBounds.Height) * marginFactor;

            var scaledCenter = new PointF(center.X * zoom, center.Y * zoom);

            canvas.Viewport.Focus(scaledCenter);
            canvas.Viewport.Zoom = zoom;
            canvas.Viewport.ComputeProjection();

            canvas.Invalidate();
            canvas.Refresh();
            Application.DoEvents();
            System.Threading.Thread.Sleep(150);
            canvas.Refresh();
            Application.DoEvents();

            Bitmap buf = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Control);
            if (buf != null && !IsBlackImage(buf))
            {
                var result = new Bitmap(buf);
                buf.Dispose();
                return result;
            }
            buf?.Dispose();

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

        private static bool IsBlackImage(Bitmap bitmap)
        {
            int sampleSize = Math.Min(10, Math.Min(bitmap.Width, bitmap.Height));
            if (sampleSize < 2) return true;

            int stepX = Math.Max(1, bitmap.Width / sampleSize);
            int stepY = Math.Max(1, bitmap.Height / sampleSize);

            for (int x = stepX; x < bitmap.Width - stepX; x += stepX)
            {
                for (int y = stepY; y < bitmap.Height - stepY; y += stepY)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.R > 10 || pixel.G > 10 || pixel.B > 10)
                        return false;
                }
            }
            return true;
        }

        private static void EnsureDirectory(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        private static ImageFormat GetImageFormat(string path) =>
            Path.GetExtension(path)?.ToLowerInvariant() switch
            {
                ".png" => ImageFormat.Png,
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => null
            };

        private static string GetTempImagePath(string prefix, string ext)
        {
            var dir = Path.Combine(Path.GetTempPath(), "Cordyceps");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
        }

        #endregion
    }
}
