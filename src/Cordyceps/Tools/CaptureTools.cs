using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;

// CA1416: System.Drawing APIs work cross-platform in Rhino/Grasshopper context (Mono on macOS)
#pragma warning disable CA1416

namespace Cordyceps.Tools
{
    /// <summary>
    /// Tools for capturing images of the Grasshopper canvas and Rhino viewport.
    /// These allow LLMs to "see" both the visual programming graph and the resulting geometry.
    /// </summary>
    [McpServerToolType]
    public class CaptureTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public CaptureTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Capture the Grasshopper canvas to an image file. Returns the file path. Use the Read tool to view the captured image.")]
        public string CaptureCanvas(
            [Description("File path to save the image (supports .png, .jpg, .bmp). If omitted, saves to a temp file.")] string outputPath = null,
            [Description("If true, auto-zoom to fit all components before capture (default true)")] bool fitContent = true,
            [Description("Padding around content in pixels when fitContent is true (default 50)")] int padding = 50)
        {
            _server?.RecordCommand("capture_canvas");

            return _context.ExecuteOnUiThread(() =>
            {
                var canvas = Instances.ActiveCanvas;
                if (canvas == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper canvas");

                var doc = canvas.Document;
                if (doc == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper document");

                // Auto-generate temp path if not provided
                var actualPath = outputPath;
                if (string.IsNullOrEmpty(actualPath))
                {
                    actualPath = GetTempImagePath("canvas", ".png");
                }

                try
                {
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(actualPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    // Get image format from extension
                    var format = GetImageFormat(actualPath);
                    if (format == null)
                        return ToolHelpers.ErrorResponse("Unsupported image format. Use .png, .jpg, .jpeg, or .bmp");

                    Bitmap bitmap;

                    if (fitContent && doc.ObjectCount > 0)
                    {
                        // Calculate bounding box of all user objects (excludes Cordyceps infrastructure)
                        var bounds = GetContentBounds(doc, padding);
                        if (bounds.HasValue)
                        {
                            bitmap = CaptureCanvasRegion(canvas, bounds.Value);
                        }
                        else
                        {
                            // No user content, fall back to screen buffer
                            bitmap = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Export);
                        }
                    }
                    else
                    {
                        // Capture current view using the screen buffer method
                        bitmap = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Export);
                    }

                    if (bitmap == null)
                        return ToolHelpers.ErrorResponse("Failed to capture canvas image");

                    // Save the bitmap
                    bitmap.Save(actualPath, format);

                    var result = new
                    {
                        success = true,
                        filePath = actualPath,
                        width = bitmap.Width,
                        height = bitmap.Height,
                        format = format.ToString(),
                        hint = "Use the Read tool to view this image file"
                    };

                    bitmap.Dispose();

                    return JsonConvert.SerializeObject(result);
                }
                catch (Exception ex)
                {
                    DebugLog.Error($"CaptureCanvas failed: {ex.Message}");
                    return ToolHelpers.ErrorResponse($"Failed to capture canvas: {ex.Message}");
                }
            });
        }

        [McpServerTool, Description("Capture the Rhino viewport to an image file. This captures the 3D geometry preview including Grasshopper preview geometry.")]
        public string CaptureViewport(
            [Description("File path to save the image (supports .png, .jpg, .bmp)")] string outputPath = null,
            [Description("View name to capture (e.g., 'Perspective', 'Top', 'Front', 'Right'). Defaults to active view.")] string view = null,
            [Description("Output image width in pixels (default: current viewport width)")] int width = 0,
            [Description("Output image height in pixels (default: current viewport height)")] int height = 0,
            [Description("If true, use transparent background (PNG only)")] bool transparent = false,
            [Description("For Raytraced views: minimum render passes to wait for before capture (0 = no wait)")] int waitForRender = 0,
            [Description("For Raytraced views: timeout in seconds when waiting for render (default: 30)")] int renderTimeout = 30)
        {
            _server?.RecordCommand("capture_viewport");

            // If waitForRender is requested, wait for raytraced rendering first
            if (waitForRender > 0)
            {
                var waitResult = WaitForRaytracedRender(view, waitForRender, renderTimeout);
                if (waitResult != null && !waitResult.StartsWith("{\"success\":true"))
                {
                    // Wait failed - return the error
                    return waitResult;
                }
            }

            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                // Auto-generate temp path if not provided
                var actualPath = string.IsNullOrEmpty(outputPath)
                    ? GetTempImagePath("viewport", ".png")
                    : outputPath;

                try
                {
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(actualPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    // Get image format from extension
                    var format = GetImageFormat(actualPath);
                    if (format == null)
                        return ToolHelpers.ErrorResponse("Unsupported image format. Use .png, .jpg, .jpeg, or .bmp");

                    // Find the requested view
                    RhinoView targetView = null;
                    if (!string.IsNullOrEmpty(view))
                    {
                        targetView = rhinoDoc.Views.Find(view, false);
                        if (targetView == null)
                        {
                            // Try case-insensitive match
                            targetView = rhinoDoc.Views
                                .FirstOrDefault(v => v.MainViewport.Name.Equals(view, StringComparison.OrdinalIgnoreCase));
                        }
                        if (targetView == null)
                        {
                            var availableViews = string.Join(", ", rhinoDoc.Views.Select(v => v.MainViewport.Name));
                            return ToolHelpers.ErrorResponse($"View '{view}' not found. Available views: {availableViews}");
                        }
                    }
                    else
                    {
                        targetView = rhinoDoc.Views.ActiveView;
                    }

                    if (targetView == null)
                        return ToolHelpers.ErrorResponse("No active view available");

                    // Get render status info if raytraced
                    int? renderPasses = null;
                    var displayMode = targetView.ActiveViewport.DisplayMode;
                    bool isRaytraced = displayMode?.EnglishName?.Equals("Raytraced", StringComparison.OrdinalIgnoreCase) ?? false;
                    if (isRaytraced)
                    {
                        var realtimeMode = targetView.RealtimeDisplayMode;
                        renderPasses = realtimeMode?.LastRenderedPass();
                    }

                    Bitmap bitmap;

                    // Use ViewCapture for custom dimensions, transparency, or Raytraced mode
                    // Note: CaptureToBitmap() doesn't capture Raytraced content properly
                    if (width > 0 || height > 0 || transparent || isRaytraced)
                    {
                        var viewCapture = new ViewCapture
                        {
                            Width = width > 0 ? width : targetView.ActiveViewport.Size.Width,
                            Height = height > 0 ? height : targetView.ActiveViewport.Size.Height,
                            ScaleScreenItems = false,
                            TransparentBackground = transparent
                        };
                        bitmap = viewCapture.CaptureToBitmap(targetView);
                    }
                    else
                    {
                        // Use simple capture for default dimensions
                        bitmap = targetView.CaptureToBitmap();
                    }

                    if (bitmap == null)
                        return ToolHelpers.ErrorResponse("Failed to capture viewport image");

                    // Save the bitmap
                    bitmap.Save(actualPath, format);

                    var result = new
                    {
                        success = true,
                        filePath = actualPath,
                        viewName = targetView.MainViewport.Name,
                        width = bitmap.Width,
                        height = bitmap.Height,
                        format = format.ToString(),
                        transparent,
                        isRaytraced,
                        renderPasses,
                        hint = "Use the Read tool to view this image file"
                    };

                    bitmap.Dispose();

                    return JsonConvert.SerializeObject(result);
                }
                catch (Exception ex)
                {
                    DebugLog.Error($"CaptureViewport failed: {ex.Message}");
                    return ToolHelpers.ErrorResponse($"Failed to capture viewport: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Wait for raytraced rendering to reach minimum passes or timeout.
        /// </summary>
        private string WaitForRaytracedRender(string view, int minPasses, int timeoutSeconds)
        {
            var startTime = DateTime.Now;
            var timeoutMs = timeoutSeconds * 1000;
            var pollIntervalMs = 100;

            while (true)
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                if (elapsed >= timeoutMs)
                {
                    // Timeout reached - return success anyway (capture with current state)
                    return null;
                }

                // Check current status
                var status = _context.ExecuteOnUiThread<(int passes, bool ready, string error)>(() =>
                {
                    var rhinoDoc = RhinoDoc.ActiveDoc;
                    if (rhinoDoc == null)
                        return (0, false, "No active Rhino document");

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
                        return (0, false, $"View '{view ?? "active"}' not found");

                    var displayMode = targetView.ActiveViewport.DisplayMode;
                    bool isRaytraced = displayMode?.EnglishName?.Equals("Raytraced", StringComparison.OrdinalIgnoreCase) ?? false;

                    if (!isRaytraced)
                        return (0, true, null); // Not raytraced, don't wait

                    var realtimeMode = targetView.RealtimeDisplayMode;
                    if (realtimeMode == null)
                        return (0, false, null);

                    int currentPass = realtimeMode.LastRenderedPass();
                    bool isComplete = realtimeMode.IsCompleted();

                    if (currentPass >= minPasses || isComplete)
                        return (currentPass, true, null);

                    return (currentPass, false, null);
                });

                if (status.error != null)
                    return ToolHelpers.ErrorResponse(status.error);

                if (status.ready) // Ready to capture
                    return null;

                // Wait before next poll
                Thread.Sleep(pollIntervalMs);
            }
        }

        [McpServerTool, Description("Capture a specific region of the Grasshopper canvas by coordinates.")]
        public string CaptureCanvasRegion(
            [Description("File path to save the image (supports .png, .jpg, .bmp)")] string outputPath,
            [Description("Left coordinate (X minimum) in canvas units")] float xMin,
            [Description("Top coordinate (Y minimum) in canvas units")] float yMin,
            [Description("Right coordinate (X maximum) in canvas units")] float xMax,
            [Description("Bottom coordinate (Y maximum) in canvas units")] float yMax)
        {
            _server?.RecordCommand("capture_canvas_region");

            return _context.ExecuteOnUiThread(() =>
            {
                var canvas = Instances.ActiveCanvas;
                if (canvas == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper canvas");

                // Validate coordinates
                if (xMax <= xMin || yMax <= yMin)
                    return ToolHelpers.ErrorResponse("Invalid region: xMax must be > xMin and yMax must be > yMin");

                // Auto-generate temp path if not provided
                var actualPath = outputPath;
                if (string.IsNullOrEmpty(actualPath))
                {
                    actualPath = GetTempImagePath("canvas_region", ".png");
                }

                try
                {
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(actualPath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    var format = GetImageFormat(actualPath);
                    if (format == null)
                        return ToolHelpers.ErrorResponse("Unsupported image format. Use .png, .jpg, .jpeg, or .bmp");

                    var bounds = new RectangleF(xMin, yMin, xMax - xMin, yMax - yMin);
                    var bitmap = CaptureCanvasRegion(canvas, bounds);

                    if (bitmap == null)
                        return ToolHelpers.ErrorResponse("Failed to capture canvas region");

                    bitmap.Save(actualPath, format);

                    var result = new
                    {
                        success = true,
                        filePath = actualPath,
                        region = new { xMin, yMin, xMax, yMax },
                        width = bitmap.Width,
                        height = bitmap.Height,
                        format = format.ToString(),
                        hint = "Use the Read tool to view this image file"
                    };

                    bitmap.Dispose();

                    return JsonConvert.SerializeObject(result);
                }
                catch (Exception ex)
                {
                    DebugLog.Error($"CaptureCanvasRegion failed: {ex.Message}");
                    return ToolHelpers.ErrorResponse($"Failed to capture canvas region: {ex.Message}");
                }
            });
        }

        [McpServerTool, Description("Get list of available Rhino views/viewports that can be captured.")]
        public string GetAvailableViews()
        {
            _server?.RecordCommand("get_available_views");

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
                    projectionMode = v.MainViewport.IsParallelProjection ? "Parallel" : "Perspective"
                }).ToList();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    count = views.Count,
                    views
                });
            });
        }

        #region Helper Methods

        /// <summary>
        /// Get the bounding box of all content on the canvas with padding.
        /// Returns null if there are no user components on the canvas.
        /// </summary>
        private RectangleF? GetContentBounds(GH_Document doc, int padding)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasContent = false;

            // Get infrastructure IDs to exclude
            var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

            foreach (var obj in doc.Objects)
            {
                // Skip Cordyceps infrastructure
                if (ToolHelpers.IsCordycepsInfrastructure(obj, infraIds))
                    continue;

                hasContent = true;
                var bounds = obj.Attributes.Bounds;
                minX = Math.Min(minX, bounds.Left);
                minY = Math.Min(minY, bounds.Top);
                maxX = Math.Max(maxX, bounds.Right);
                maxY = Math.Max(maxY, bounds.Bottom);
            }

            // Return null if no user content found
            if (!hasContent)
                return null;

            // Add padding
            return new RectangleF(
                minX - padding,
                minY - padding,
                (maxX - minX) + (padding * 2),
                (maxY - minY) + (padding * 2)
            );
        }

        /// <summary>
        /// Capture a specific region of the canvas to a bitmap.
        /// Uses Grasshopper's built-in high-resolution export functionality
        /// which renders properly regardless of window visibility.
        /// </summary>
        private Bitmap CaptureCanvasRegion(GH_Canvas canvas, RectangleF canvasBounds)
        {
            const int MaxDimension = 4096;
            const int MinDimension = 100;

            // Calculate output size with correct aspect ratio (1 pixel per canvas unit, capped)
            float aspectRatio = canvasBounds.Width / canvasBounds.Height;
            int outputWidth, outputHeight;

            if (aspectRatio >= 1.0f)
            {
                outputWidth = Math.Min(MaxDimension, Math.Max(MinDimension, (int)canvasBounds.Width));
                outputHeight = Math.Max(MinDimension, (int)(outputWidth / aspectRatio));
            }
            else
            {
                outputHeight = Math.Min(MaxDimension, Math.Max(MinDimension, (int)canvasBounds.Height));
                outputWidth = Math.Max(MinDimension, (int)(outputHeight * aspectRatio));
            }

            // Save current viewport state
            var originalMidPoint = canvas.Viewport.MidPoint;
            var originalZoom = canvas.Viewport.Zoom;

            try
            {
                // Center viewport on requested region
                var center = new PointF(
                    canvasBounds.X + canvasBounds.Width / 2,
                    canvasBounds.Y + canvasBounds.Height / 2
                );

                // Calculate zoom to fit region within output (1:1 pixel mapping)
                float zoomX = (float)outputWidth / canvasBounds.Width;
                float zoomY = (float)outputHeight / canvasBounds.Height;
                float zoom = Math.Min(zoomX, zoomY);

                canvas.Viewport.MidPoint = center;
                canvas.Viewport.Zoom = zoom;

                // Force synchronous layout and paint
                canvas.Invalidate(true);
                canvas.Update();
                Application.DoEvents();
                System.Threading.Thread.Sleep(50);
                Application.DoEvents();

                // Bring the Grasshopper window to front to ensure it renders
                var ghWindow = canvas.FindForm();
                if (ghWindow != null)
                {
                    ghWindow.BringToFront();
                    ghWindow.Activate();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                    Application.DoEvents();
                }

                // Try GetCanvasScreenBuffer first - works if canvas is visible
                using (var screenBuffer = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Control))
                {
                    if (screenBuffer != null)
                    {
                        DebugLog.Info($"CaptureCanvasRegion: GetCanvasScreenBuffer succeeded, {screenBuffer.Width}x{screenBuffer.Height}");

                        // Copy the buffer to our output bitmap, scaling/cropping as needed
                        var result = new Bitmap(outputWidth, outputHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(result))
                        {
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                            // Calculate source rectangle - the viewport should now show our region
                            // The screen buffer is the full canvas control, centered on our region
                            float srcWidth = canvasBounds.Width * zoom;
                            float srcHeight = canvasBounds.Height * zoom;
                            float srcX = (screenBuffer.Width - srcWidth) / 2f;
                            float srcY = (screenBuffer.Height - srcHeight) / 2f;

                            // Clamp to valid range
                            srcX = Math.Max(0, srcX);
                            srcY = Math.Max(0, srcY);
                            srcWidth = Math.Min(srcWidth, screenBuffer.Width - srcX);
                            srcHeight = Math.Min(srcHeight, screenBuffer.Height - srcY);

                            var srcRect = new RectangleF(srcX, srcY, srcWidth, srcHeight);
                            var destRect = new RectangleF(0, 0, outputWidth, outputHeight);

                            g.DrawImage(screenBuffer, destRect, srcRect, GraphicsUnit.Pixel);
                        }
                        return result;
                    }
                }

                // Fallback: try Export mode
                DebugLog.Warn("Control mode screen buffer failed, trying Export mode");
                using (var screenBuffer = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Export))
                {
                    if (screenBuffer == null)
                    {
                        DebugLog.Error("Both Control and Export screen buffers returned null");
                        return null;
                    }

                    var result = new Bitmap(outputWidth, outputHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(screenBuffer, 0, 0, outputWidth, outputHeight);
                    }
                    return result;
                }
            }
            finally
            {
                // Restore original viewport state
                canvas.Viewport.MidPoint = originalMidPoint;
                canvas.Viewport.Zoom = originalZoom;
                canvas.Refresh();
            }
        }

        /// <summary>
        /// Get the appropriate ImageFormat from file extension.
        /// </summary>
        private ImageFormat GetImageFormat(string filePath) =>
            Path.GetExtension(filePath)?.ToLowerInvariant() switch
            {
                ".png" => ImageFormat.Png,
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                _ => null
            };

        /// <summary>
        /// Generate a temp file path for captured images.
        /// </summary>
        private string GetTempImagePath(string prefix, string extension)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "Cordyceps");
            Directory.CreateDirectory(tempDir); // No-op if exists
            return Path.Combine(tempDir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
        }

        #endregion
    }
}
