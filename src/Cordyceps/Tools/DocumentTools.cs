using System;
using System.ComponentModel;
using System.IO;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Rhino;

namespace Cordyceps.Tools
{
    /// <summary>
    /// Document-level operations (save, load, clear)
    /// </summary>
    [McpServerToolType]
    public class DocumentTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;

        public DocumentTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Get information about the current Grasshopper document")]
        public string GetDocumentInfo()
        {
            _server?.RecordCommand("get_document_info");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                // Count different object types
                int componentCount = 0;
                int paramCount = 0;
                int groupCount = 0;
                int otherCount = 0;

                foreach (var obj in doc.Objects)
                {
                    if (obj is IGH_Component)
                        componentCount++;
                    else if (obj is Grasshopper.Kernel.Special.GH_Group)
                        groupCount++;
                    else if (obj is IGH_Param)
                        paramCount++;
                    else
                        otherCount++;
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    filePath = doc.FilePath ?? "(unsaved)",
                    displayName = doc.DisplayName,
                    isModified = doc.IsModified,
                    objectCount = doc.ObjectCount,
                    components = componentCount,
                    parameters = paramCount,
                    groups = groupCount,
                    other = otherCount,
                    enabled = doc.Enabled
                });
            });
        }

        [McpServerTool, Description("Clear all objects from the Grasshopper canvas")]
        public string ClearDocument()
        {
            _server?.RecordCommand("clear_document");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                int removedCount = doc.ObjectCount;

                // Remove all objects
                doc.RemoveObjects(doc.Objects, true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    cleared = true,
                    removedCount
                });
            });
        }

        [McpServerTool, Description("Save the current Grasshopper document to a .gh or .ghx file")]
        public string SaveDocument(
            [Description("File path with .gh or .ghx extension")] string filePath)
        {
            _server?.RecordCommand("save_document");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "File path is required" });
                }

                // Validate extension
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext != ".gh" && ext != ".ghx")
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "File must have .gh or .ghx extension" });
                }

                try
                {
                    // Ensure directory exists
                    string dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    // Save the document - set file path on document then save
                    doc.FilePath = filePath;
                    GH_DocumentIO docIO = new GH_DocumentIO(doc);
                    bool success = docIO.Save();

                    if (success)
                    {
                        return JsonConvert.SerializeObject(new
                        {
                            success = true,
                            filePath,
                            objectCount = doc.ObjectCount
                        });
                    }
                    else
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "Save operation failed" });
                    }
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Save failed: {ex.Message}" });
                }
            });
        }

        [McpServerTool, Description("Load a Grasshopper document from a .gh or .ghx file")]
        public string LoadDocument(
            [Description("File path to load")] string filePath)
        {
            _server?.RecordCommand("load_document");
            return _context.ExecuteOnUiThread(() =>
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "File path is required" });
                }

                if (!File.Exists(filePath))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"File not found: {filePath}" });
                }

                try
                {
                    // Create document IO
                    GH_DocumentIO docIO = new GH_DocumentIO();
                    bool opened = docIO.Open(filePath);

                    if (!opened || docIO.Document == null)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "Failed to open document" });
                    }

                    // Replace current document
                    var doc = docIO.Document;

                    // Assign to canvas
                    if (Instances.ActiveCanvas != null)
                    {
                        Instances.ActiveCanvas.Document = doc;
                        Instances.ActiveCanvas.Invalidate();
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        filePath,
                        objectCount = doc.ObjectCount
                    });
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Load failed: {ex.Message}" });
                }
            });
        }

        [McpServerTool, Description("Create a new empty Grasshopper document")]
        public string NewDocument()
        {
            _server?.RecordCommand("new_document");
            return _context.ExecuteOnUiThread(() =>
            {
                try
                {
                    var newDoc = new GH_Document();

                    if (Instances.ActiveCanvas != null)
                    {
                        Instances.ActiveCanvas.Document = newDoc;
                        Instances.ActiveCanvas.Invalidate();
                    }

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        created = true
                    });
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Failed to create document: {ex.Message}" });
                }
            });
        }

        [McpServerTool, Description("Enable or disable the Grasshopper solver")]
        public string SetSolverEnabled(
            [Description("True to enable, false to disable")] bool enabled)
        {
            _server?.RecordCommand("set_solver_enabled");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                doc.Enabled = enabled;

                if (enabled)
                {
                    doc.NewSolution(true);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    enabled = doc.Enabled
                });
            });
        }

        [McpServerTool, Description("Trigger a solution recompute on all components")]
        public string RecomputeSolution()
        {
            _server?.RecordCommand("recompute_solution");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                doc.NewSolution(true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    recomputed = true
                });
            });
        }
    }
}
