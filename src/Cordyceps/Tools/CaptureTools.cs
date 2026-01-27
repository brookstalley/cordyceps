using System;
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
            [Description("If true, use transparent background (PNG only)")] bool transparent = false)
        {
            _server?.RecordCommand("capture_viewport");

            return _context.ExecuteOnUiThread(() =>
            {
                var rhinoDoc = RhinoDoc.ActiveDoc;
                if (rhinoDoc == null)
                    return ToolHelpers.ErrorResponse("No active Rhino document");

                // Auto-generate temp path if not provided
                var actualPath = outputPath;
                if (string.IsNullOrEmpty(actualPath))
                {
                    actualPath = GetTempImagePath("viewport", transparent ? ".png" : ".png");
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

                    Bitmap bitmap;

                    // Use ViewCapture for custom dimensions or transparency
                    if (width > 0 || height > 0 || transparent)
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
        /// Creates an output bitmap with the correct aspect ratio for the requested region,
        /// regardless of the canvas control's window size.
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
                // Wide region
                outputWidth = Math.Min(MaxDimension, Math.Max(MinDimension, (int)canvasBounds.Width));
                outputHeight = Math.Max(MinDimension, (int)(outputWidth / aspectRatio));
            }
            else
            {
                // Tall region
                outputHeight = Math.Min(MaxDimension, Math.Max(MinDimension, (int)canvasBounds.Height));
                outputWidth = Math.Max(MinDimension, (int)(outputHeight * aspectRatio));
            }

            // Save current viewport state
            var originalMidPoint = canvas.Viewport.MidPoint;
            var originalZoom = canvas.Viewport.Zoom;

            try
            {
                // Strategy: Set the viewport so that the requested canvas region maps exactly
                // to the control's visible area, then capture and resize to output dimensions.

                // Center viewport on requested region
                var center = new PointF(
                    canvasBounds.X + canvasBounds.Width / 2,
                    canvasBounds.Y + canvasBounds.Height / 2
                );

                int controlWidth = canvas.Width > 0 ? canvas.Width : 1920;
                int controlHeight = canvas.Height > 0 ? canvas.Height : 1080;

                // Calculate zoom to fit region within control (use smaller ratio so all content fits)
                float zoomX = (float)controlWidth / canvasBounds.Width;
                float zoomY = (float)controlHeight / canvasBounds.Height;
                float zoom = Math.Min(zoomX, zoomY);

                canvas.Viewport.MidPoint = center;
                canvas.Viewport.Zoom = zoom;

                // Force synchronous redraw - this is critical for the capture to work
                canvas.Invalidate();
                canvas.Update();
                System.Windows.Forms.Application.DoEvents();
                System.Threading.Thread.Sleep(100);
                System.Windows.Forms.Application.DoEvents();

                // Use Control mode to capture what's actually visible on screen
                // Export mode may use different rendering settings that ignore our viewport
                using (var controlBitmap = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Control))
                {
                    if (controlBitmap == null)
                    {
                        DebugLog.Error("CaptureCanvasRegion: GetCanvasScreenBuffer returned null");
                        return null;
                    }

                    DebugLog.Info($"CaptureCanvasRegion: Buffer={controlBitmap.Width}x{controlBitmap.Height}, " +
                                  $"Output={outputWidth}x{outputHeight}, Zoom={zoom:F3}");

                    // The content should now fill the control bitmap (minus any letterboxing)
                    // Calculate the actual content area within the bitmap
                    float contentWidthPx = canvasBounds.Width * zoom;
                    float contentHeightPx = canvasBounds.Height * zoom;

                    // Content is centered in the control
                    float contentLeft = (controlBitmap.Width - contentWidthPx) / 2f;
                    float contentTop = (controlBitmap.Height - contentHeightPx) / 2f;

                    // Clamp to valid bitmap coordinates
                    contentLeft = Math.Max(0, contentLeft);
                    contentTop = Math.Max(0, contentTop);
                    contentWidthPx = Math.Min(contentWidthPx, controlBitmap.Width - contentLeft);
                    contentHeightPx = Math.Min(contentHeightPx, controlBitmap.Height - contentTop);

                    DebugLog.Info($"CaptureCanvasRegion: SrcRect=({contentLeft:F1}, {contentTop:F1}, " +
                                  $"{contentWidthPx:F1}, {contentHeightPx:F1})");

                    // Create output bitmap with correct aspect ratio
                    var outputBitmap = new Bitmap(outputWidth, outputHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    using (var graphics = Graphics.FromImage(outputBitmap))
                    {
                        // Fill with canvas background color first
                        graphics.Clear(Color.FromArgb(212, 208, 200));

                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                        // Source: the content portion of the control bitmap
                        var srcRect = new RectangleF(contentLeft, contentTop, contentWidthPx, contentHeightPx);

                        // Destination: the full output bitmap
                        var destRect = new RectangleF(0, 0, outputWidth, outputHeight);

                        // Draw cropped/scaled content to output
                        graphics.DrawImage(controlBitmap, destRect, srcRect, GraphicsUnit.Pixel);
                    }

                    return outputBitmap;
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
        private ImageFormat GetImageFormat(string filePath)
        {
            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            switch (ext)
            {
                case ".png":
                    return ImageFormat.Png;
                case ".jpg":
                case ".jpeg":
                    return ImageFormat.Jpeg;
                case ".bmp":
                    return ImageFormat.Bmp;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Generate a temp file path for captured images.
        /// </summary>
        private string GetTempImagePath(string prefix, string extension)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "Cordyceps");
            if (!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"{prefix}_{timestamp}{extension}";
            return Path.Combine(tempDir, filename);
        }

        #endregion
    }
}
