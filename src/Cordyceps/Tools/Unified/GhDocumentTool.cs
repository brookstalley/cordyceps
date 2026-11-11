using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        // document serializations, and with undo/redo closed to agents every agent-side restore
        // funnels through this store, so it must not grow for the life of the Rhino session. Written under ExecuteOnUiThread (snapshot/revert/delete); the list path
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
                    Example = "action='info'",
                    Tips = new[]
                    {
                        "solverEnabled is this document's own solver switch (gh_document(action='solver') sets it). solutionsLocked is Grasshopper's global switch: true when the solver is locked in Grasshopper (or Rhino is mid-command), which Cordyceps never changes. A solve runs only when solverEnabled is true and solutionsLocked is false."
                    }
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
                    Tips = new[]
                    {
                        "Inside a cluster editor, cluster input/output hooks are preserved (reported as preservedClusterHooks) so the cluster interface stays intact",
                        "The follow-up solve is scheduled, not waited for: the reply carries solve.state='scheduled' (or solver_disabled / solutions_locked / not_watched with solve.remedy when it could not be scheduled) and returns before the solve runs. Call action='recompute' before reading results if you need them settled",
                        "One undo step: Ctrl+Z brings the whole canvas back, wired. A clear with nothing to remove has no solve and no undo; a reply with a warning has no solve (check action='info', then recompute)",
                        "Refused before anything is removed when an object does not report itself as in this document, since its removal could not be recorded for undo"
                    }
                },
                ["solver"] = new ActionInfo
                {
                    Name = "solver",
                    Description = "Enable or disable this document's solver. Enabling solves the document and waits for it like recompute, reporting how the solve ended",
                    Required = new[] { "enabled" },
                    Optional = new[] { "timeout" },
                    Example = "action='solver', enabled=false OR action='solver', enabled=true, timeout=120",
                    Tips = new[] {
                        "Disable before bulk operations for performance",
                        "enabled accepts true/false, 1/0, yes/no (case-insensitive); anything else is a validation error",
                        "enabled=true waits up to timeout seconds (1-300, default 30) and replies with solverEnabled=true and the same solve object and states as recompute; success is true only when solve.state is completed. The switch is on whatever the solve did: on over_budget, poll gh_inspect(action='connection') before reading results",
                        "solve.state='solutions_locked' means Grasshopper's global solver lock is on, which only a human can clear (solve.remedy says so)"
                    }
                },
                ["recompute"] = new ActionInfo
                {
                    Name = "recompute",
                    Description = "Recompute the document (cluster-safe) and wait up to timeout seconds for the solve, reporting how it ended. Rejected while a solution is already running, or while the UI thread is blocked by a modal dialog",
                    Optional = new[] { "timeout" },
                    Example = "action='recompute' OR action='recompute', timeout=120",
                    Tips = new[]
                    {
                        "timeout: whole seconds to wait, 1-300, default 30 (a whole-valued number such as 120.0 is accepted). Out of range or not a whole number is refused before anything is touched.",
                        "The reply has solve: {state, elapsedMs, solveMs, errors, warnings, remedy}. elapsedMs is how long Cordyceps waited; solveMs is Grasshopper's own solve time, present when a solve ended. state is completed | aborted | over_budget | not_started | solver_disabled | solutions_locked | not_watched | document_closed; success and recomputed are true only for completed. errors/warnings count objects at that runtime-message level, as gh_inspect(action='status') does.",
                        "aborted: someone pressed Escape in Grasshopper (or asked for an abort), so outputs may be partial. A human usually does that on purpose: ask before recomputing again.",
                        "over_budget: the solve is STILL RUNNING and Cordyceps cannot cancel it. Poll gh_inspect(action='connection') until grasshopper.solving is false, then gh_inspect(action='status'). Pass a larger timeout next time.",
                        "not_started: the wait ran out with nothing solving and no solve finished, so the scheduled solve never ran (the solver was locked meanwhile, the Grasshopper editor was unavailable, or the document raises no solution events). Check gh_inspect(action='connection') and status, then retry.",
                        "solver_disabled: this document's solver is off; gh_document(action='solver', enabled=true) turns it on. solutions_locked: Grasshopper's global solver lock is on (or Rhino is mid-command); Cordyceps never changes it, so ask a human.",
                        "not_watched: Cordyceps could not watch the document's solution events (the Cordyceps component's watcher is not running), so it scheduled nothing. Make sure the Cordyceps component is on the canvas and enabled, then retry.",
                        "If a solution is already running you get success=false with solving=true and solving_since — wait for it to finish and call again, do not retry in a tight loop.",
                        "The wait holds no lock: other calls keep working while it runs, and gh_inspect(action='connection') still tells a busy solver from a modal dialog."
                    }
                },
                ["undo"] = new ActionInfo
                {
                    Name = "undo",
                    Description = "Not available to agents - always returns an error. Each Cordyceps edit is one Grasshopper undo step that a human undoes with Ctrl+Z; to restore state yourself, take action='snapshot' before changes and action='revert' to restore",
                    Example = "action='snapshot', name='before_changes' (instead of undo)"
                },
                ["redo"] = new ActionInfo
                {
                    Name = "redo",
                    Description = "Not available to agents - always returns an error. A human redoes an undone Cordyceps edit with Grasshopper's Redo (Ctrl+Y on Windows, Cmd+Shift+Z on macOS); to restore state yourself, use snapshots (action='snapshot'/'revert')",
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
                    Description = "Revert to a named snapshot, then solve the reverted document and wait for it like recompute, reporting how the solve ended",
                    Required = new[] { "name" },
                    Optional = new[] { "timeout" },
                    Example = "action='revert', name='before_changes'",
                    Tips = new[] {
                        "Waits up to timeout seconds (1-300, default 30) and replies with revertedTo and the same solve object and states as recompute; success is true only when solve.state is completed. The revert itself has happened whatever the solve did",
                        "Not undoable with Ctrl+Z: revert installs a new document, whose undo history starts empty, so edits made before the revert can no longer be undone either"
                    }
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
                "Capture supports .png, .jpg, .bmp formats",
                "Undo: clear is one Grasshopper undo step a human reverts with Ctrl+Z; solver, revert and the capture actions record none. undo and redo are not callable by agents: snapshot and revert are the agent-side restore"
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
            [Description("Region yMax")] double yMax = double.NaN,
            [Description("recompute, solver (enabled=true) and revert: seconds to wait for the solve, 1-300 (default 30)")] string timeout = null)
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
                ("yMax", double.IsNaN(yMax) ? null : (object)yMax),
                ("timeout", timeout)
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

            // Validated before anything touches the document: an out-of-range or non-numeric
            // timeout is refused, never clamped or defaulted.
            int timeoutSeconds = SolvePolicy.DefaultTimeoutSeconds;
            if (SolvePolicy.WaitsForSolve(action, enabledBool))
            {
                if (!SolvePolicy.TryParseTimeout(timeout, out timeoutSeconds, out var timeoutError))
                    return ToolHelpers.ErrorResponse(timeoutError);
            }

            return action.ToLowerInvariant() switch
            {
                "info" => ActionInfo(),
                "save" => ActionSave(path),
                "clear" => ActionClear(),
                "solver" => ActionSolver(enabledBool, timeoutSeconds),
                "recompute" => ActionRecompute(timeoutSeconds),
                "undo" => ActionUndo(),
                "redo" => ActionRedo(),
                "snapshot" => ActionSnapshot(name),
                "revert" => ActionRevert(name, timeoutSeconds),
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
                    // The global switch is separate from the document's own, and only a human can
                    // clear it, so it is reported beside it rather than folded into one flag.
                    solutionsLocked = !GH_Document.EnableSolutions,
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

                string Reply()
                {
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

                    return JsonConvert.SerializeObject(new { success = true, removedCount = count });
                }

                // Nothing to remove changes nothing: no undo step and no solve.
                if (count == 0)
                    return Reply();

                // A removal is recorded against this document, so an object that does not report
                // itself as in it (an orphan) cannot be recorded; it is refused before anything is
                // removed rather than discovered partway through.
                var orphans = toRemove.Where(o => !ReferenceEquals(o.OnPingDocument(), doc)).ToList();
                if (orphans.Count > 0)
                    return ToolHelpers.ErrorResponse(
                        $"Nothing was cleared: {orphans.Count} object(s) on the canvas do not report themselves as in this document, so their removal cannot be recorded for undo: " +
                        string.Join(", ", orphans.Take(5).Select(o => $"'{o.NickName}' ({o.InstanceGuid})")) +
                        (orphans.Count > 5 ? ", ..." : ""));

                // One undo step: each removal records the object's wires and then the object, so
                // Ctrl+Z brings the whole canvas back, wired. The commit schedules one solve rather
                // than solving here, which would hold the document lock for the whole solve; the
                // transaction reports a solve only when a solver switch refused it, so the scheduled
                // case is named here.
                var reply = _context.Edit(doc, EditReplies.RecordName("gh_document", "clear"), tx =>
                {
                    foreach (var obj in toRemove)
                        tx.RemoveObject(obj);
                    return Reply();
                });
                return SolvePolicy.WithScheduledEditSolve(reply);
            });
        }

        private string ActionSolver(bool enabled, int timeoutSeconds)
        {
            SolveScheduling scheduling = null;
            var early = _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                doc.Enabled = enabled;

                if (!enabled)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        success = true,
                        solverEnabled = false
                    });
                }

                // Changes made while the solver was off already expired what they touched (and
                // cascaded downstream). On a main canvas everything is recomputed; in a cluster
                // editor only those expired objects, so the input hooks keep the data the parent
                // cluster gave them.
                scheduling = SolveCoordinator.Schedule(doc, expireAll: doc.Owner == null);
                return null;
            });
            if (early != null)
                return early;

            // Waits like recompute, so a caller that reads results after turning the solver on
            // reads a solved document.
            return SolvePolicy.WaitedReply(AwaitSolve(scheduling, timeoutSeconds),
                new JObject { ["solverEnabled"] = true });
        }

        private string ActionRecompute(int timeoutSeconds)
        {
            // Refuse rather than queue while a solution is already running, or while the UI thread
            // is stuck behind a dialog: a recompute queued behind an unknown solve could not say
            // which solve its answer describes. The result names the running document and when it
            // started so the caller can decide how long to wait.
            var liveness = SolverState.Shared.Derive();
            if (liveness.Solving || liveness.ModalInferred)
                return StatusEnvelope.BusyResult(liveness);

            SolveScheduling scheduling = null;
            var failure = _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var error))
                    return ToolHelpers.ErrorResponse(error);

                scheduling = SolveCoordinator.Schedule(doc, expireAll: true);
                return null;
            });
            if (failure != null)
                return failure;

            return SolvePolicy.RecomputeReply(AwaitSolve(scheduling, timeoutSeconds));
        }

        /// <summary>
        /// The wait every solve-waiting action shares (recompute, solver on, revert). Called after
        /// the short UI call that scheduled the solve has returned, so the document lock and the
        /// UI-work bracket are released while Grasshopper solves: this thread waits holding
        /// nothing, then makes one more short UI call to count what the solve left behind.
        /// </summary>
        private SolveOutcome AwaitSolve(SolveScheduling scheduling, int timeoutSeconds)
        {
            if (scheduling.Refusal != null)
                return new SolveOutcome(scheduling.Refusal.Value, elapsedMs: 0, documentEnabled: scheduling.DocumentEnabled);

            var ticket = scheduling.Ticket.Value;
            var outcome = SolvePolicy.FromWaitResult(SolveCoordinator.Wait(ticket, TimeSpan.FromSeconds(timeoutSeconds)));

            if (!SolvePolicy.ReadsMessages(outcome.State))
                return outcome;

            var counts = _context.ExecuteOnUiThread(() => SolveCoordinator.CountRuntimeMessages(ticket.DocumentId));
            return counts == null ? outcome : outcome.WithCounts(counts.Value.Errors, counts.Value.Warnings);
        }

        private string ActionUndo()
        {
            // Agents do not perform undo: running Grasshopper's undo from an HTTP call interacts
            // with the UI thread in ways that dispose the response before it is sent. Every edit
            // still records one undo step for the human's Ctrl+Z, and snapshots are the agent-side
            // restore.
            return ToolHelpers.ErrorResponse("Undo is not available to agents. Each Cordyceps call that changes the canvas is one Grasshopper undo step, which a human undoes with Ctrl+Z (Cmd+Z on macOS) in Grasshopper. To restore state yourself, use snapshots: gh_document(action='snapshot', name='...') before changes, gh_document(action='revert', name='...') to restore.");
        }

        private string ActionRedo()
        {
            // Redo is closed to agents for the same reason as undo — see ActionUndo.
            return ToolHelpers.ErrorResponse("Redo is not available to agents. A human redoes an undone Cordyceps call with Grasshopper's Redo (Ctrl+Y on Windows, Cmd+Shift+Z on macOS). To restore state yourself, use snapshots (action='snapshot'/'revert').");
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

        private string ActionRevert(string name, int timeoutSeconds)
        {
            SolveScheduling scheduling = null;
            var early = _context.ExecuteOnUiThread(() =>
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

                    // Replace the canvas's document and schedule a full solve of it; the wait
                    // happens below, outside this UI call and the document lock.
                    var canvas = Instances.ActiveCanvas;
                    canvas.Document = newDoc;
                    scheduling = SolveCoordinator.Schedule(newDoc, expireAll: true);
                    return null;
                }
                catch (Exception ex)
                {
                    return ToolHelpers.ErrorResponse($"Revert failed: {ex.Message}");
                }
            });
            if (early != null)
                return early;

            // Waits like recompute, so a caller that reads results after reverting reads the
            // reverted definition solved.
            return SolvePolicy.WaitedReply(AwaitSolve(scheduling, timeoutSeconds),
                new JObject { ["revertedTo"] = name });
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
