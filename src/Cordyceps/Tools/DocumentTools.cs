using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
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

                // Get infrastructure IDs to exclude from counts
                var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);

                // Count different object types (excluding infrastructure)
                var visibleObjects = doc.Objects.Where(obj => !infraIds.Contains(obj.InstanceGuid)).ToList();
                int componentCount = visibleObjects.OfType<IGH_Component>().Count();
                int groupCount = visibleObjects.OfType<Grasshopper.Kernel.Special.GH_Group>().Count();
                int paramCount = visibleObjects.OfType<IGH_Param>().Count();
                int otherCount = visibleObjects.Count - componentCount - groupCount - paramCount;
                int visibleObjectCount = visibleObjects.Count;

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    filePath = doc.FilePath ?? "(unsaved)",
                    displayName = doc.DisplayName,
                    isModified = doc.IsModified,
                    objectCount = visibleObjectCount,
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
                var objectsToRemove = doc.Objects
                    .Where(obj => !infraIds.Contains(obj.InstanceGuid))
                    .ToList();

                int preservedCount = doc.ObjectCount - objectsToRemove.Count;

                // Remove non-infrastructure objects
                doc.RemoveObjects(objectsToRemove, true);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    cleared = true,
                    removedCount = objectsToRemove.Count,
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

                if (!RequestValidator.ValidateRequired(filePath, "filePath", out var pathError))
                    return ToolHelpers.ErrorResponse(pathError);

                // Validate extension
                if (!RequestValidator.ValidateFileExtension(filePath, new[] { ".gh", ".ghx" }, out var extError))
                    return ToolHelpers.ErrorResponse(extError);

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

        [McpServerTool, Description("Undo the last action on the Grasshopper canvas. Only available after at least one MCP operation has been performed.")]
        public string Undo()
        {
            _server?.RecordCommand("undo");
            return _context.ExecuteOnUiThread(() =>
            {
                // Protect against undoing before any MCP operations (would undo adding Cordyceps itself)
                // CommandCount includes this undo call plus the call from HandleToolCallAsync (2 total),
                // so we need at least 3+ commands to have had a real operation before this undo
                if (_server == null || _server.CommandCount <= 2)
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Undo is not available until after MCP operations have been performed",
                        undoCount = 0,
                        redoCount = 0
                    });

                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var undoServer = doc.UndoServer;
                if (undoServer == null)
                    return ToolHelpers.ErrorResponse("Undo server not available");

                if (undoServer.UndoCount == 0)
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Nothing to undo",
                        undoCount = 0,
                        redoCount = undoServer.RedoCount
                    });

                undoServer.PerformUndo();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    undone = true,
                    undoCount = undoServer.UndoCount,
                    redoCount = undoServer.RedoCount
                });
            });
        }

        [McpServerTool, Description("Redo a previously undone action on the Grasshopper canvas")]
        public string Redo()
        {
            _server?.RecordCommand("redo");
            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var undoServer = doc.UndoServer;
                if (undoServer == null)
                    return ToolHelpers.ErrorResponse("Undo server not available");

                if (undoServer.RedoCount == 0)
                    return JsonConvert.SerializeObject(new
                    {
                        success = false,
                        error = "Nothing to redo",
                        undoCount = undoServer.UndoCount,
                        redoCount = 0
                    });

                undoServer.PerformRedo();

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    redone = true,
                    undoCount = undoServer.UndoCount,
                    redoCount = undoServer.RedoCount
                });
            });
        }
    }
}
