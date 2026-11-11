using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Cordyceps.Core.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Rhino;

namespace Cordyceps.Core
{
    /// <summary>
    /// What <see cref="SolveCoordinator.Schedule"/> did: scheduled a solve and took a ticket for it,
    /// or found a solver switch off and scheduled nothing.
    /// </summary>
    internal sealed class SolveScheduling
    {
        private SolveScheduling(SolveTicket? ticket, SolveState? refusal, bool documentEnabled)
        {
            Ticket = ticket;
            Refusal = refusal;
            DocumentEnabled = documentEnabled;
        }

        public static SolveScheduling Scheduled(SolveTicket ticket) => new SolveScheduling(ticket, null, true);

        public static SolveScheduling Refused(SolveState refusal, bool documentEnabled)
            => new SolveScheduling(null, refusal, documentEnabled);

        /// <summary>The claim on the scheduled solve; null when <see cref="Refusal"/> is set.</summary>
        public SolveTicket? Ticket { get; }

        /// <summary>The switch that stopped the solve; null when one was scheduled.</summary>
        public SolveState? Refusal { get; }

        /// <summary>The document's own solver switch when scheduling was decided.</summary>
        public bool DocumentEnabled { get; }

        /// <summary>The <c>solve</c> object for an action that does not wait.</summary>
        public Newtonsoft.Json.Linq.JObject ToField() => SolvePolicy.ScheduledField(Refusal, DocumentEnabled);
    }

    /// <summary>
    /// The one place Cordyceps starts a Grasshopper solve. It never solves synchronously: it expires
    /// what the solve must recompute and asks Grasshopper to schedule a solution, inside a short UI
    /// call, then returns. Anyone who needs the outcome waits for the ticket off the UI thread and
    /// outside the document lock (<see cref="Wait"/>).
    ///
    /// <para>Why not <c>NewSolution</c>: called inside <c>ExecuteOnUiThread</c>, it holds the
    /// document lock for the whole solve (every other request queues for up to the lock's timeout)
    /// and keeps the UI-work bracket open, which tells the liveness layer "Cordyceps is busy" when
    /// a solver-exception dialog is in fact waiting for a human.</para>
    ///
    /// <para>The decisions (which switch refuses, what an outcome means, what to tell the caller)
    /// are in host-free <see cref="SolvePolicy"/> and <see cref="SolveWait"/>; this class only
    /// talks to Grasshopper.</para>
    /// </summary>
    internal static class SolveCoordinator
    {
        /// <summary>
        /// The delay handed to <c>GH_Document.ScheduleSolution</c>. At least 1 ms: outside a
        /// running solution a delay of 0 never starts the schedule timer, so the solve would wait
        /// for some unrelated solution to trigger it.
        /// </summary>
        internal const int ScheduleDelayMs = 10;

        /// <summary>
        /// Schedule a solve of <paramref name="doc"/>. Must be called on the UI thread, inside
        /// <c>ExecuteOnUiThread</c>.
        ///
        /// <para>With <paramref name="expireAll"/>, everything is recomputed: on a main canvas
        /// through <c>ExpireSolution()</c> plus a cleared reference table, which is what
        /// <c>NewSolution(true)</c> does; inside a cluster editor only source objects are expired
        /// (see <see cref="ExpireClusterSources"/>), because the document-wide expiry also wipes the
        /// input hooks the parent cluster filled. Without it, only objects already expired are
        /// solved.</para>
        ///
        /// <para>A solve that starts before the scheduled one fires (a slider drag, another
        /// schedule) erases the schedule and solves what was expired here; its end answers the
        /// ticket, which is the right answer.</para>
        /// </summary>
        public static SolveScheduling Schedule(GH_Document doc, bool expireAll)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            // Decide before touching the document: a refused solve expires nothing. Watching is
            // asked last, and only when both switches allow a solve, so a refused solve adopts no
            // document; a solve whose end could not be seen is not scheduled at all, because the
            // only thing a waiter could ever report about it is a spent budget.
            var refusal = SolvePolicy.Refusal(doc.Enabled, GH_Document.EnableSolutions,
                watching: () => SolutionWatcher.EnsureWatched(doc));
            if (refusal != null)
            {
                if (refusal == SolveState.NotWatched)
                    DebugLog.Warn("SolveCoordinator: the solution watcher is not running, so no solve was scheduled");
                return SolveScheduling.Refused(refusal.Value, doc.Enabled);
            }

            if (expireAll)
            {
                if (doc.Owner != null)
                    ExpireClusterSources(doc);
                else
                {
                    doc.ExpireSolution();
                    doc.ClearReferenceTable();
                }
            }

            // The ticket is taken after the expiry and before the schedule: any solve that ends
            // after this point began after the expiry, so it computed it.
            var ticket = SolverState.Shared.TakeTicket(doc.DocumentID);
            doc.ScheduleSolution(ScheduleDelayMs);
            return SolveScheduling.Scheduled(ticket);
        }

        /// <summary>
        /// Wait for <paramref name="ticket"/> for at most <paramref name="budget"/>. Must NOT be
        /// called on the UI thread or inside <c>ExecuteOnUiThread</c>: the solve it waits for runs
        /// on the UI thread, and waiting under the document lock would block every other request.
        /// </summary>
        public static SolveWaitResult Wait(SolveTicket ticket, TimeSpan budget)
        {
            if (!RhinoApp.InvokeRequired)
                throw new InvalidOperationException("SolveCoordinator.Wait must not run on the Rhino UI thread: the solve it waits for runs there.");
            return SolveWait.Wait(ticket, SolverState.Shared.Observe, budget);
        }

        /// <summary>
        /// Count the objects in <paramref name="documentId"/> that <c>gh_inspect(action='status')</c>
        /// would report as ERROR and as WARNING. It walks the same population (infrastructure and
        /// phantoms left out, then <see cref="ObjectHealth.IsSubject(bool, bool)"/>) and classifies
        /// each object with the same rule (<see cref="CanvasReader.Health"/>), so a change to that
        /// rule moves these counts with status and outline. Null when the document is no longer
        /// open. Must be called on the UI thread.
        /// </summary>
        public static (int Errors, int Warnings)? CountRuntimeMessages(Guid documentId)
        {
            var doc = FindDocument(documentId);
            if (doc == null) return null;

            var infraIds = ToolHelpers.GetCordycepsInfrastructureIds(doc);
            int errors = 0, warnings = 0;
            foreach (var obj in ToolHelpers.GetActiveObjects(doc, infraIds))
            {
                if (!ObjectHealth.IsSubject(obj is GH_Group, obj is IGH_ActiveObject)) continue;
                switch (CanvasReader.Health((IGH_ActiveObject)obj).Status)
                {
                    case HealthStatus.Error: errors++; break;
                    case HealthStatus.Warning: warnings++; break;
                }
            }
            return (errors, warnings);
        }

        // The canvas's document first: a reverted snapshot is on the canvas without being in the
        // document server.
        private static GH_Document FindDocument(Guid documentId)
        {
            var onCanvas = Instances.ActiveCanvas?.Document;
            if (onCanvas != null && onCanvas.DocumentID == documentId) return onCanvas;

            var server = Instances.DocumentServer;
            if (server == null) return null;
            return ((IEnumerable<GH_Document>)server).FirstOrDefault(d => d != null && d.DocumentID == documentId);
        }

        /// <summary>
        /// Expire a cluster editor's source objects: those with no upstream source other than an
        /// input hook. Expiry cascades downstream through wires, so this reaches every dependent
        /// object without touching the hooks, whose volatile data the parent cluster supplied and
        /// a document-wide expiry would wipe.
        /// </summary>
        private static void ExpireClusterSources(GH_Document doc)
        {
            var hookIds = new HashSet<Guid>(doc.ClusterInputHooks().Select(h => h.InstanceGuid));

            foreach (var obj in doc.Objects)
            {
                if (hookIds.Contains(obj.InstanceGuid)) continue;
                if (obj is IGH_ActiveObject active && !HasNonHookSources(obj, hookIds))
                    active.ExpireSolution(false);
            }
        }

        /// <summary>
        /// True when an object has an upstream source that is NOT an input hook. Objects that return
        /// false are the cluster graph's sources and need expiring explicitly; everything downstream
        /// cascades from them.
        /// </summary>
        private static bool HasNonHookSources(IGH_DocumentObject obj, HashSet<Guid> hookIds)
        {
            IEnumerable<IGH_Param> inputs;
            if (obj is IGH_Component comp)
                inputs = comp.Params.Input;
            else if (obj is IGH_Param param)
                inputs = new[] { param };
            else
                return false;

            foreach (var input in inputs)
            {
                foreach (var src in input.Sources)
                {
                    // The source is the hook itself (a standalone param).
                    if (hookIds.Contains(src.InstanceGuid)) continue;
                    // The source's parent object is a hook.
                    var parent = src.Attributes?.GetTopLevel?.DocObject;
                    if (parent != null && hookIds.Contains(parent.InstanceGuid)) continue;
                    return true;
                }
            }
            return false;
        }
    }
}
