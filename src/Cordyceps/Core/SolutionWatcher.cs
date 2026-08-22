using System;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;

namespace Cordyceps.Core
{
    /// <summary>
    /// Feeds <see cref="SolverState"/> from Grasshopper's per-document solution events.
    ///
    /// <para>It watches <em>every</em> open document, not just the one holding the Cordyceps
    /// component. Documents share the single Rhino UI thread, so a heavy solve in a definition
    /// with no bridge component in it blocks MCP calls exactly as much as one in the bridge's own
    /// document — and if that solve went unrecorded, the status model would see a blocked UI with
    /// nothing solving and wrongly report a modal dialog needing a human. Watching the whole
    /// document server keeps "busy" and "blocked" honest whichever definition is being solved.</para>
    ///
    /// <para>Every subscription is paired: <c>DocumentRemoved</c> detaches and forgets that
    /// document's state, and <see cref="Stop"/> detaches everything, so nothing survives an
    /// open/close cycle. Host-coupled (it touches Grasshopper types), so it lives here and not in
    /// the test project's linked set; the decisions it feeds are tested through
    /// <see cref="SolverState"/>.</para>
    ///
    /// <para>All methods must be called on the Rhino UI thread — Grasshopper's document server is
    /// not thread-safe. The internal lock guards against re-entrancy, not against off-thread use.</para>
    /// </summary>
    internal static class SolutionWatcher
    {
        private static readonly object _gate = new object();

        // Reference identity: two distinct documents are never "equal" for watching purposes, and
        // GH_Document makes no guarantees about value equality.
        private static readonly HashSet<GH_Document> _watched =
            new HashSet<GH_Document>(ReferenceEqualityComparer.Instance);

        private static bool _started;

        /// <summary>
        /// Begin watching the document server and every document already open. Idempotent, and a
        /// no-op while Grasshopper's document server is not yet available — the caller runs on
        /// every solve, so a later attempt succeeds.
        /// </summary>
        public static void Start()
        {
            lock (_gate)
            {
                if (_started) return;

                var server = Instances.DocumentServer;
                if (server == null) return;

                server.DocumentAdded += OnDocumentAdded;
                server.DocumentRemoved += OnDocumentRemoved;
                _started = true;

                // Cast explicitly: GH_DocumentServer's own GetEnumerator is the non-generic one,
                // so foreach would otherwise iterate as object.
                foreach (var document in (IEnumerable<GH_Document>)server)
                    Watch(document);
            }
        }

        /// <summary>
        /// Detach from the document server and from every watched document, dropping their cached
        /// solve state. Called when the last bridge component goes away; without it the handlers
        /// would outlive the server that reads them.
        /// </summary>
        public static void Stop()
        {
            lock (_gate)
            {
                var server = Instances.DocumentServer;
                if (server != null)
                {
                    server.DocumentAdded -= OnDocumentAdded;
                    server.DocumentRemoved -= OnDocumentRemoved;
                }

                foreach (var document in new List<GH_Document>(_watched))
                    Unwatch(document);

                _watched.Clear();
                _started = false;
            }
        }

        /// <summary>Number of documents currently watched. For diagnostics and tests of the host wiring.</summary>
        public static int WatchedCount
        {
            get { lock (_gate) return _watched.Count; }
        }

        private static void OnDocumentAdded(GH_DocumentServer sender, GH_Document doc)
        {
            lock (_gate) Watch(doc);
        }

        private static void OnDocumentRemoved(GH_DocumentServer sender, GH_Document doc)
        {
            lock (_gate) Unwatch(doc);
        }

        // Callers hold _gate.
        private static void Watch(GH_Document document)
        {
            if (document == null || !_watched.Add(document)) return;

            document.SolutionStart += OnSolutionStart;
            document.SolutionEnd += OnSolutionEnd;
        }

        // Callers hold _gate.
        private static void Unwatch(GH_Document document)
        {
            if (document == null || !_watched.Remove(document)) return;

            document.SolutionStart -= OnSolutionStart;
            document.SolutionEnd -= OnSolutionEnd;

            // A document closed mid-solution never raises SolutionEnd, and a stuck "solving" flag
            // would report a healthy bridge as permanently busy.
            SolverState.Shared.ForgetDocument(document.DocumentID);
        }

        private static void OnSolutionStart(object sender, GH_SolutionEventArgs e)
        {
            var document = e?.Document ?? sender as GH_Document;
            if (document == null) return;

            try
            {
                SolverState.Shared.BeginSolution(document.DocumentID, document.DisplayName);
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- runs inside Grasshopper's solution dispatch: a throw here would abort the user's solve, so failing liveness bookkeeping is logged and swallowed rather than taking the solve down with it
            {
                DebugLog.Warn($"Recording solution start failed: {ex.Message}");
            }
        }

        private static void OnSolutionEnd(object sender, GH_SolutionEventArgs e)
        {
            var document = e?.Document ?? sender as GH_Document;
            if (document == null) return;

            try
            {
                SolverState.Shared.EndSolution(document.DocumentID);
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- runs inside Grasshopper's solution dispatch: a throw here would abort the user's solve, so failing liveness bookkeeping is logged and swallowed rather than taking the solve down with it
            {
                DebugLog.Warn($"Recording solution end failed: {ex.Message}");
            }
        }
    }
}
