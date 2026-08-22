using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
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
    public partial class GhDocumentTool
    {
        private readonly GrasshopperContext _context;

        // Snapshot storage (GHD-6M2J): bounded with oldest-first eviction — snapshots are full
        // document serializations, and with undo/redo disabled every documented mutation
        // workflow funnels through this store, so it must not grow for the life of the Rhino
        // session. Written under ExecuteOnUiThread (snapshot/revert/delete); the list path
        // reads it off the HTTP worker thread (SnapshotStore is internally locked).
        private const int MaxSnapshots = 20;
        private static readonly SnapshotStore _snapshots = new SnapshotStore(MaxSnapshots);

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
                    Example = "action='clear'",
                    Tips = new[] { "Inside a cluster editor, cluster input/output hooks are preserved (reported as preservedClusterHooks) so the cluster interface stays intact" }
                },
                ["solver"] = new ActionInfo
                {
                    Name = "solver",
                    Description = "Enable or disable the solver",
                    Required = new[] { "enabled" },
                    Example = "action='solver', enabled=false",
                    Tips = new[] {
                        "Disable before bulk operations for performance",
                        "enabled accepts true/false, 1/0, yes/no (case-insensitive); anything else is a validation error"
                    }
                },
                ["recompute"] = new ActionInfo
                {
                    Name = "recompute",
                    Description = "Trigger a solution recompute (cluster-safe). Rejected while a solution is already running",
                    Example = "action='recompute'",
                    Tips = new[]
                    {
                        "If a solution is already running you get success=false with solving=true and solving_since — wait for it to finish and call again, do not retry in a tight loop.",
                        "Use gh_inspect(action='liveness') to see whether the host is busy solving or blocked by a modal dialog."
                    }
                },
                ["undo"] = new ActionInfo
                {
                    Name = "undo",
                    Description = "DISABLED (threading issues) - always returns an error. Use action='snapshot' before changes and action='revert' to restore instead",
                    Example = "action='snapshot', name='before_changes' (instead of undo)"
                },
                ["redo"] = new ActionInfo
                {
                    Name = "redo",
                    Description = "DISABLED (threading issues) - always returns an error. Use snapshots (action='snapshot'/'revert') instead",
                    Example = "action='revert', name='before_changes' (instead of redo)"
                },
                ["snapshot"] = new ActionInfo
                {
                    Name = "snapshot",
                    Description = "Create a named snapshot of current state. At most 20 snapshots are kept: saving a new name beyond that evicts the oldest (evicted names are returned in `evicted`); re-using a name replaces that snapshot",
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
                    Description = "List all available snapshots (oldest first, max 20 kept)",
                    Example = "action='snapshots'"
                },
                ["snapshot_delete"] = new ActionInfo
                {
                    Name = "snapshot_delete",
                    Description = "Delete a named snapshot to free memory / make room under the 20-snapshot cap",
                    Required = new[] { "name" },
                    Example = "action='snapshot_delete', name='before_changes'"
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

        [McpServerTool, Description("Document operations. Actions: info|save|clear|solver|recompute|undo|redo|snapshot|revert|snapshots|snapshot_delete|capture_canvas|capture_viewport|capture_region|capture_views|help")]
        public string GhDocument(
            [Description("Action to perform")] string action,
            [Description("File path for save/capture")] string path = null,
            [Description("Solver enabled state (required for solver action)")] string enabled = null,
            [Description("Snapshot name")] string name = null,
            // Capture parameters
            [Description("View name for viewport capture")] string view = null,
            [Description("Auto-fit content (true/false)")] string fit = null,
            [Description("Padding around content")] int padding = 50,
            [Description("Output width")] int width = 0,
            [Description("Output height")] int height = 0,
            [Description("Transparent background (true/false)")] string transparent = null,
            [Description("Region xMin")] double xMin = double.NaN,
            [Description("Region yMin")] double yMin = double.NaN,
            [Description("Region xMax")] double xMax = double.NaN,
            [Description("Region yMax")] double yMax = double.NaN)
        {
            if (string.Equals(action, "help", StringComparison.OrdinalIgnoreCase))
                return UnifiedToolHelpers.GenerateHelp(ToolInfo);

            // Parse capture parameters
            bool fitBool = ToolHelpers.ParseBool(fit, true);
            bool transparentBool = ToolHelpers.ParseBool(transparent, false);

            var providedParams = UnifiedToolHelpers.BuildParams(
                ("path", path),
                ("enabled", enabled),
                ("name", name),
                ("view", view),
                ("fit", fit),
                ("padding", padding != 50 ? (object)padding : null),
                ("width", width != 0 ? (object)width : null),
                ("height", height != 0 ? (object)height : null),
                ("transparent", transparent),
                ("xMin", double.IsNaN(xMin) ? null : (object)xMin),
                ("yMin", double.IsNaN(yMin) ? null : (object)yMin),
                ("xMax", double.IsNaN(xMax) ? null : (object)xMax),
                ("yMax", double.IsNaN(yMax) ? null : (object)yMax)
            );

            var validationError = UnifiedToolHelpers.ValidateAction(ToolInfo, action, providedParams);
            if (validationError != null)
                return validationError;

            // Parse enabled strictly for solver ('enabled' is required for it): an unrecognized
            // string must be a validation error, not silently coerced to false.
            bool enabledBool = true;
            if (string.Equals(action, "solver", StringComparison.OrdinalIgnoreCase))
            {
                if (!ToolHelpers.TryParseBool(enabled, out enabledBool))
                    return ToolHelpers.ErrorResponse($"Invalid 'enabled' value: '{enabled}'. Use true/false, 1/0, or yes/no.");
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
                "snapshot_delete" => ActionSnapshotDelete(name),
                // Capture actions
                "capture_canvas" => ActionCaptureCanvas(path, fitBool, padding),
                "capture_viewport" => ActionCaptureViewport(path, view, width, height, transparentBool),
                "capture_region" => ActionCaptureRegion(path, (float)xMin, (float)yMin, (float)xMax, (float)yMax),
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
                // Use GetActiveObjects to filter out phantom/orphaned objects for accurate counts
                var userObjects = ToolHelpers.GetActiveObjects(doc, infraIds).ToList();

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

                if (!GhArchiveSave.TryGetFormat(path, out var format))
                    return ToolHelpers.ErrorResponse("File must have .gh or .ghx extension");

                try
                {
                    var archive = new GH_IO.Serialization.GH_Archive();
                    if (!archive.AppendObject(doc, "Definition"))
                        return ToolHelpers.ErrorResponse("Failed to serialize document");

                    var (overwrite, rememberPath) = GhArchiveSave.WriteFlags;
                    if (!archive.WriteToFile(path, overwrite, rememberPath))
                        return ToolHelpers.ErrorResponse("Failed to write file");

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        path,
                        format
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

                // Inside a cluster editor, doc.Objects includes the cluster's input/output hooks.
                // They are not Cordyceps infrastructure but deleting them destroys the cluster's
                // interface — exclude them and report the exclusion.
                HashSet<Guid> hookIds = null;
                if (doc.Owner != null)
                {
                    hookIds = new HashSet<Guid>(doc.ClusterInputHooks().Select(h => h.InstanceGuid));
                    foreach (var hook in doc.ClusterOutputHooks())
                        hookIds.Add(hook.InstanceGuid);
                }

                var toRemove = doc.Objects
                    .Where(o => !ToolHelpers.IsCordycepsInfrastructure(o, infraIds)
                                && (hookIds == null || !hookIds.Contains(o.InstanceGuid)))
                    .ToList();

                int count = toRemove.Count;
                foreach (var obj in toRemove)
                {
                    doc.RemoveObject(obj, false);
                }

                doc.NewSolution(false);

                if (hookIds != null)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        removedCount = count,
                        preservedClusterHooks = hookIds.Count,
                        note = "Inside a cluster editor: cluster input/output hooks were preserved to keep the cluster interface intact"
                    });
                }

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
                {
                    // Changes made while solver was disabled already expired affected
                    // components (and cascaded downstream). In a cluster, NewSolution(false)
                    // processes only those expired objects without touching input hooks.
                    // On main canvas, NewSolution(true) for full recompute (original behavior).
                    if (doc.Owner != null)
                        doc.NewSolution(false);
                    else
                        doc.NewSolution(true);
                }

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    solverEnabled = enabled
                });
            });
        }

        private string ActionRecompute()
        {
            // Refuse rather than queue or block while a solution is already running, or while the
            // UI thread is stuck behind a dialog. Queueing would give the caller no completion
            // signal; blocking would hold the request open for the whole solve, which is the
            // unbounded silence this refusal exists to replace. The result names the running
            // document and when it started so the caller can decide how long to wait.
            var liveness = SolverState.Shared.Derive();
            if (liveness.Solving || liveness.ModalInferred)
                return StatusEnvelope.BusyResult(liveness);

            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                ToolHelpers.ClusterSafeRecompute(doc);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    recomputed = true
                });
            });
        }

        private string ActionUndo()
        {
            // Undo is formally cut (2026-07-02 janitor close-out), not pending work: the
            // Grasshopper undo system interacts with the UI thread in ways that cause the HTTP
            // response to be disposed before it can be sent. Snapshots (action='snapshot'/
            // 'revert') are the supported alternative.
            return ToolHelpers.ErrorResponse("Undo is disabled (threading issues with the Grasshopper undo stack). Use snapshots instead: gh_document(action='snapshot', name='...') before changes, gh_document(action='revert', name='...') to restore.");
        }

        private string ActionRedo()
        {
            // Redo is formally cut alongside undo — see ActionUndo for details.
            return ToolHelpers.ErrorResponse("Redo is disabled (threading issues with the Grasshopper undo stack). Use snapshots instead (action='snapshot'/'revert').");
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

                    var evicted = _snapshots.Save(snapshotName, data);

                    // Eviction destroys a full document backup; the agent sees it in the
                    // response's `evicted` field, but log it too so an operator debugging a
                    // later "Snapshot not found" has a trail via gh_inspect(action='log').
                    if (evicted.Count > 0)
                        DebugLog.WriteLine($"Snapshot cap ({_snapshots.MaxSnapshots}) reached: evicted {string.Join(", ", evicted)}", "INFO", 0);

                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        name = snapshotName,
                        sizeBytes = data.Length,
                        totalSnapshots = _snapshots.Count,
                        maxSnapshots = _snapshots.MaxSnapshots,
                        evicted
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

                if (!_snapshots.TryGet(name, out var data))
                    return ToolHelpers.ErrorResponse($"Snapshot not found: {name}. Available: {string.Join(", ", _snapshots.List().Select(s => s.Name))}");

                // Without a canvas there is nothing to install the reverted document into —
                // fail up front instead of deserializing and silently reporting success.
                if (Instances.ActiveCanvas == null)
                    return ToolHelpers.ErrorResponse("No active Grasshopper canvas");

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
            var snapshots = _snapshots.List().Select(s => new
            {
                name = s.Name,
                sizeBytes = s.SizeBytes,
                createdAtUtc = s.CreatedAtUtc
            }).ToList();

            return JsonConvert.SerializeObject(new
            {
                success = true,
                count = snapshots.Count,
                maxSnapshots = _snapshots.MaxSnapshots,
                snapshots
            });
        }

        private string ActionSnapshotDelete(string name)
        {
            return _context.ExecuteOnUiThread(() =>
            {
                if (string.IsNullOrEmpty(name))
                    return ToolHelpers.ErrorResponse("Snapshot name is required");

                // A missing name is an error, never silent success — the caller's mental model
                // of the store would otherwise drift.
                if (!_snapshots.Remove(name))
                    return ToolHelpers.ErrorResponse($"Snapshot not found: {name}. Available: {string.Join(", ", _snapshots.List().Select(s => s.Name))}");

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    deleted = name,
                    totalSnapshots = _snapshots.Count
                });
            });
        }

        // Capture actions and helper methods are in GhDocumentTool.Capture.cs
    }
}
