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
    /// Document state management (snapshots)
    /// </summary>
    [McpServerToolType]
    public class StateTools
    {
        private readonly GrasshopperContext _context;
        private readonly McpServer _server;
        private static readonly Dictionary<string, string> Snapshots = new Dictionary<string, string>();

        public StateTools(GrasshopperContext context, McpServer server)
        {
            _context = context;
            _server = server;
        }

        [McpServerTool, Description("Create a snapshot of the current Grasshopper document state")]
        public string Snapshot(
            [Description("Name for the snapshot (auto-generated if not provided)")] string name = null)
        {
            _server?.RecordCommand("snapshot");
            return _context.ExecuteOnUiThread(() =>
            {
                var doc = _context.GetActiveDocument();
                if (doc == null)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper document" });
                }

                string snapshotName = name ?? Guid.NewGuid().ToString();

                try
                {
                    var io = new GH_DocumentIO(doc);
                    string path = Path.Combine(Path.GetTempPath(), $"cordyceps_snapshot_{snapshotName}.gh");

                    // Temporarily set file path for save
                    string originalPath = doc.FilePath;
                    doc.FilePath = path;

                    if (!io.Save())
                    {
                        doc.FilePath = originalPath;
                        return JsonConvert.SerializeObject(new { success = false, error = "Failed to save snapshot" });
                    }

                    doc.FilePath = originalPath;
                    Snapshots[snapshotName] = path;

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        snapshotId = snapshotName,
                        name = snapshotName, // Alias for backwards compatibility
                        path = path,
                        snapshotCount = Snapshots.Count
                    });
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Failed to create snapshot: {ex.Message}" });
                }
            });
        }

        [McpServerTool, Description("Revert to a previously created snapshot")]
        public string RevertSnapshot(
            [Description("Name of the snapshot to revert to")] string name)
        {
            _server?.RecordCommand("revert_snapshot");
            return _context.ExecuteOnUiThread(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = "Snapshot name is required" });
                }

                if (!Snapshots.TryGetValue(name, out var path))
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Snapshot not found: {name}" });
                }

                if (!File.Exists(path))
                {
                    Snapshots.Remove(name);
                    return JsonConvert.SerializeObject(new { success = false, error = $"Snapshot file no longer exists: {path}" });
                }

                try
                {
                    var io = new GH_DocumentIO();
                    if (!io.Open(path) || io.Document == null)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "Failed to load snapshot" });
                    }

                    var canvas = Instances.ActiveCanvas;
                    if (canvas == null)
                    {
                        return JsonConvert.SerializeObject(new { success = false, error = "No active Grasshopper canvas" });
                    }

                    canvas.Document = io.Document;
                    canvas.Document.NewSolution(false);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        name = name,
                        restored = true
                    });
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { success = false, error = $"Failed to revert: {ex.Message}" });
                }
            });
        }

        [McpServerTool, Description("List all available snapshots")]
        public string ListSnapshots()
        {
            _server?.RecordCommand("list_snapshots");

            var snapshots = new List<object>();
            foreach (var kvp in Snapshots)
            {
                bool exists = File.Exists(kvp.Value);
                snapshots.Add(new
                {
                    name = kvp.Key,
                    path = kvp.Value,
                    exists
                });
            }

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = snapshots.Count,
                snapshots
            });
        }

        [McpServerTool, Description("Delete a snapshot")]
        public string DeleteSnapshot(
            [Description("Name of the snapshot to delete")] string name)
        {
            _server?.RecordCommand("delete_snapshot");

            if (string.IsNullOrEmpty(name))
            {
                return JsonConvert.SerializeObject(new { success = false, error = "Snapshot name is required" });
            }

            if (!Snapshots.TryGetValue(name, out var path))
            {
                return JsonConvert.SerializeObject(new { success = false, error = $"Snapshot not found: {name}" });
            }

            // Try to delete the file
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { /* Ignore file deletion errors */ }

            Snapshots.Remove(name);

            return JsonConvert.SerializeObject(new
            {
                success = true,
                name = name,
                deleted = true
            });
        }
    }
}
