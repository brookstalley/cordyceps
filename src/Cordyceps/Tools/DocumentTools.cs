using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;

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
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

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

        [McpServerTool, Description("Clear all objects from the Grasshopper canvas except the Cordyceps MCP infrastructure (the server component, its connected inputs/outputs, and containing group)")]
        public string ClearDocument()
        {
            _server?.RecordCommand("clear_document");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                // Get the set of Cordyceps infrastructure IDs to preserve
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                // Collect objects to remove (everything except infrastructure)
                var objectsToRemove = new List<IGH_DocumentObject>();
                foreach (var obj in doc.Objects)
                {
                    if (!infraIds.Contains(obj.InstanceGuid))
                    {
                        objectsToRemove.Add(obj);
                    }
                }

                int removedCount = objectsToRemove.Count;
                int preservedCount = doc.ObjectCount - removedCount;

                // Remove non-infrastructure objects
                doc.RemoveObjects(objectsToRemove, true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    cleared = true,
                    removedCount,
                    preservedCount,
                    note = "Cordyceps MCP infrastructure was preserved"
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
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

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

        // LoadDocument was removed - loading a document would replace the canvas and destroy
        // the MCP server component, breaking connectivity. Use clear_document + add components
        // to build definitions programmatically while preserving the MCP connection.

        // NewDocument was removed - it would destroy the MCP server component, breaking connectivity.
        // Use ClearDocument instead, which preserves the Cordyceps infrastructure.

        [McpServerTool, Description("Enable or disable the Grasshopper solver")]
        public string SetSolverEnabled(
            [Description("True to enable, false to disable")] bool enabled)
        {
            _server?.RecordCommand("set_solver_enabled");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

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
    }
}
