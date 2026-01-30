using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Cordyceps.Core;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Rhino;

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
                }
            },
            Notes = new[]
            {
                "IMPORTANT: Disable solver before bulk operations with action='solver', enabled=false",
                "Re-enable solver when done with action='solver', enabled=true"
            }
        };

        public GhDocumentTool(GrasshopperContext context)
        {
            _context = context;
        }

        [McpServerTool, Description("Document operations. Actions: info|save|clear|solver|recompute|undo|redo|snapshot|revert|snapshots|help")]
        public string GhDocument(
            [Description("Action to perform")] string action,
            [Description("File path for save")] string path = null,
            [Description("Solver enabled state (required for solver action)")] string enabled = null,
            [Description("Snapshot name")] string name = null)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("path", path),
                ("enabled", enabled),
                ("name", name)
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
    }
}
