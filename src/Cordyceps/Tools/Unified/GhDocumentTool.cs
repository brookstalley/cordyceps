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
    /// Unified document tool - document operations, solver control, undo/redo, snapshots
    /// </summary>
    [McpServerToolType]
    public class GhDocumentTool
    {
        private readonly GrasshopperContext _context;

        // Snapshot storage
        private static readonly Dictionary<string, byte[]> _snapshots = new Dictionary<string, byte[]>();

        private static readonly UnifiedToolInfo ToolInfo = new UnifiedToolInfo
        {
            ToolName = "gh_document",
            Description = "Document operations, solver control, undo/redo, snapshots",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["info"] = new ActionInfo
                {
                    Name = "info",
                    Description = "Get information about the current document",
                    Example = "action='info'"
                },
                ["save"] = new ActionInfo
                {
                    Name = "save",
                    Description = "Save document to file",
                    Required = new[] { "path" },
                    Example = "action='save', path='/path/to/file.gh'",
                    Tips = new[] { "Use .gh for binary, .ghx for XML format" }
                },
                ["clear"] = new ActionInfo
                {
                    Name = "clear",
                    Description = "Clear all components (except Cordyceps infrastructure)",
                    Example = "action='clear'"
                },
                ["solver"] = new ActionInfo
                {
                    Name = "solver",
                    Description = "Enable or disable the solver",
                    Required = new[] { "enabled" },
                    Example = "action='solver', enabled=false",
                    Tips = new[] { "Disable before bulk operations for performance" }
                },
                ["recompute"] = new ActionInfo
                {
                    Name = "recompute",
                    Description = "Trigger a solution recompute",
                    Example = "action='recompute'"
                },
                ["undo"] = new ActionInfo
                {
                    Name = "undo",
                    Description = "Undo the last action",
                    Example = "action='undo'"
                },
                ["redo"] = new ActionInfo
                {
                    Name = "redo",
                    Description = "Redo a previously undone action",
                    Example = "action='redo'"
                },
                ["snapshot"] = new ActionInfo
                {
                    Name = "snapshot",
                    Description = "Create a named snapshot of current state",
                    Optional = new[] { "name" },
                    Example = "action='snapshot', name='before_changes'"
                },
                ["revert"] = new ActionInfo
                {
                    Name = "revert",
                    Description = "Revert to a named snapshot",
                    Required = new[] { "name" },
                    Example = "action='revert', name='before_changes'"
                },
                ["snapshots"] = new ActionInfo
                {
                    Name = "snapshots",
                    Description = "List all available snapshots",
                    Example = "action='snapshots'"
                },
                ["help"] = new ActionInfo
                {
                    Name = "help",
                    Description = "Show this help information"
                },
                // Capture actions (from gh_capture)
                ["capture_canvas"] = new ActionInfo
                {
                    Name = "capture_canvas",
                    Description = "Capture the Grasshopper canvas to an image",
                    Optional = new[] { "path", "fit", "padding" },
                    Example = "action='capture_canvas' OR action='capture_canvas', path='/tmp/canvas.png'",
                    Tips = new[] { "fit=true (default) auto-zooms to content", "Use Read tool to view captured image" }
                },
                ["capture_viewport"] = new ActionInfo
                {
                    Name = "capture_viewport",
                    Description = "Capture Rhino viewport (3D geometry preview)",
                    Optional = new[] { "path", "view", "width", "height", "transparent" },
                    Example = "action='capture_viewport' OR action='capture_viewport', view='Perspective'",
                    Tips = new[] { "view: 'Perspective', 'Top', 'Front', 'Right'", "transparent only works with PNG" }
                },
                ["capture_region"] = new ActionInfo
                {
                    Name = "capture_region",
                    Description = "Capture a specific region of the canvas",
                    Required = new[] { "xMin", "yMin", "xMax", "yMax" },
                    Optional = new[] { "path" },
                    Example = "action='capture_region', xMin=0, yMin=0, xMax=500, yMax=300"
                },
                ["capture_views"] = new ActionInfo
                {
                    Name = "capture_views",
                    Description = "List available Rhino views",
                    Example = "action='capture_views'"
                }
            },
            Notes = new[]
            {
                "IMPORTANT: Disable solver before bulk operations with action='solver', enabled=false",
                "Re-enable solver when done with action='solver', enabled=true",
                "Capture supports .png, .jpg, .bmp formats"
            }
        };

        public GhDocumentTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Document operations. Actions: info|save|clear|solver|recompute|undo|redo|snapshot|revert|snapshots|capture_canvas|capture_viewport|capture_region|capture_views|help")]
        public string GhDocument(
            [Description("Action to perform")] string action,
            [Description("File path for save/capture")] string path = null,
            [Description("Solver enabled state (required for solver action)")] string enabled = null,
            [Description("Snapshot name")] string name = null,
            // Capture parameters
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

            // Parse capture parameters
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
                ("enabled", enabled),
                ("name", name),
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

            // Parse enabled as bool for solver action
            bool enabledBool = true;
            if (!string.IsNullOrEmpty(enabled))
            {
                if (!bool.TryParse(enabled, out enabledBool))
                {
                    // Also accept "1"/"0" and case variations
                    enabledBool = enabled.Equals("1") || enabled.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }

            return action.ToLowerInvariant() switch
            {
                "info" => ActionInfo(),
                "save" => ActionSave(path),
                "clear" => ActionClear(),
                "solver" => ActionSolver(enabledBool),
                "recompute" => ActionRecompute(),
                "undo" => ActionUndo(),
                "redo" => ActionRedo(),
                "snapshot" => ActionSnapshot(name),
                "revert" => ActionRevert(name),
                "snapshots" => ActionListSnapshots(),
                // Capture actions
                "capture_canvas" => ActionCaptureCanvas(path, fitBool, paddingInt),
                "capture_viewport" => ActionCaptureViewport(path, view, widthInt, heightInt, transparentBool),
                "capture_region" => ActionCaptureRegion(path, xMinF, yMinF, xMaxF, yMaxF),
                "capture_views" => ActionCaptureViews(),
                _ => JsonConvert.SerializeObject(new { success = false, error = $"Unknown action: {action}" })
            };
        }

        private string ActionInfo()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var userObjects = doc.Objects.Where(o => !ToolHelpers.IsCordycepsInfrastructure(o, infraIds)).ToList();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    displayName = doc.DisplayName,
                    filePath = doc.FilePath,
                    isModified = doc.IsModified,
                    solverEnabled = doc.Enabled,
                    objectCount = userObjects.Count,
                    componentCount = userObjects.Count(o => o is IGH_Component),
                    parameterCount = userObjects.Count(o => o is IGH_Param && !(o is IGH_Component)),
                    groupCount = userObjects.Count(o => o is Grasshopper.Kernel.Special.GH_Group),
                    snapshotCount = _snapshots.Count
                });
            });
        }

        private string ActionSave(string path)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                if (string.IsNullOrEmpty(path))
                    return ToolHelpers.ErrorResponse("File path is required");

                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".gh" && ext != ".ghx")
                    return ToolHelpers.ErrorResponse("File must have .gh or .ghx extension");

                try
                {
                    var archive = new GH_IO.Serialization.GH_Archive();
                    if (!archive.AppendObject(doc, "Definition"))
                        return ToolHelpers.ErrorResponse("Failed to serialize document");

                    bool success = ext == ".ghx"
                        ? archive.WriteToFile(path, true, false)
                        : archive.WriteToFile(path, false, true);

                    if (!success)
                        return ToolHelpers.ErrorResponse("Failed to write file");

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        path,
                        format = ext == ".ghx" ? "XML" : "binary"
                    });
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Save failed: {ex.Message}");
                }
            });
        }

        private string ActionClear()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
                var toRemove = doc.Objects
                    .Where(o => !ToolHelpers.IsCordycepsInfrastructure(o, infraIds))
                    .ToList();

                int count = toRemove.Count;
                foreach (var obj in toRemove)
                {
                    doc.RemoveObject(obj, false);
                }

                doc.NewSolution(false);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    removedCount = count
                });
            });
        }

        private string ActionSolver(bool enabled)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                doc.Enabled = enabled;

                if (enabled)
                    doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    solverEnabled = enabled
                });
            });
        }

        private string ActionRecompute()
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    recomputed = true
                });
            });
        }

        private string ActionUndo()
        {
            // TODO: Undo has threading issues with HTTP response handling.
            // The Grasshopper undo system interacts with UI thread in ways that
            // cause the HTTP response to be disposed before it can be sent.
            // Use snapshots (action='snapshot'/'revert') as an alternative.
            return ToolHelpers.ErrorResponse("Undo is temporarily disabled due to threading issues. Use snapshots instead: gh_document(action='snapshot', name='...') to save state, gh_document(action='revert', name='...') to restore.");
        }

        private string ActionRedo()
        {
            // TODO: Redo has threading issues with HTTP response handling.
            // See ActionUndo for details.
            return ToolHelpers.ErrorResponse("Redo is temporarily disabled due to threading issues. Use snapshots instead.");
        }

        private string ActionSnapshot(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var snapshotName = name ?? $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}";

                try
                {
                    var archive = new GH_IO.Serialization.GH_Archive();
                    if (!archive.AppendObject(doc, "Definition"))
                        return ToolHelpers.ErrorResponse("Failed to serialize document");

                    var data = archive.Serialize_Binary();
                    if (data == null || data.Length == 0)
                        return ToolHelpers.ErrorResponse("Failed to serialize document to binary");

                    _snapshots[snapshotName] = data;

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        name = snapshotName,
                        sizeBytes = data.Length,
                        totalSnapshots = _snapshots.Count
                    });
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Snapshot failed: {ex.Message}");
                }
            });
        }

        private string ActionRevert(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Snapshot name is required");

                if (!_snapshots.TryGetValue(name, out var data))
                    return ToolHelpers.ErrorResponse($"Snapshot not found: {name}. Available: {string.Join(", ", _snapshots.Keys)}");

                try
                {
                    var archive = new GH_IO.Serialization.GH_Archive();
                    if (!archive.Deserialize_Binary(data))
                        return ToolHelpers.ErrorResponse("Failed to deserialize snapshot");

                    var newDoc = new GH_Document();
                    if (!archive.ExtractObject(newDoc, "Definition"))
                        return ToolHelpers.ErrorResponse("Failed to extract document from snapshot");

                    // Replace current document content
                    var canvas = Instances.ActiveCanvas;
                    if (canvas != null)
                    {
                        canvas.Document = newDoc;
                        canvas.Document.NewSolution(true);
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        revertedTo = name
                    });
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Revert failed: {ex.Message}");
                }
            });
        }

        private string ActionListSnapshots()
        {
            var snapshots = _snapshots.Select(kvp => new
            {
                name = kvp.Key,
                sizeBytes = kvp.Value.Length
            }).ToList();

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = snapshots.Count,
                snapshots
            });
        }

        #region Capture Actions (from gh_capture)

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

        private bool IsBlackImage(Bitmap bitmap)
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
