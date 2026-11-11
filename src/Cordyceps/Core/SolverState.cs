using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;

namespace Cordyceps.Core
{
    /// <summary>
    /// How responsive the Rhino UI thread is, judged from the age of the last heartbeat stamp.
    /// </summary>
    public enum UiLiveness
    {
        /// <summary>No heartbeat has ever been stamped, so responsiveness cannot be judged.</summary>
        Unknown,

        /// <summary>The UI thread stamped the heartbeat recently — it is draining its queue.</summary>
        Responsive,

        /// <summary>
        /// The UI thread has not stamped the heartbeat within the staleness window. It is either
        /// busy solving or wedged behind something that is not going to clear on its own.
        /// </summary>
        Blocked,
    }

    /// <summary>An in-progress solution: which document, when it started, and how deeply nested.</summary>
    public sealed class DocumentSolve
    {
        public DocumentSolve(Guid documentId, string documentName, DateTime startedUtc, int depth = 1)
        {
            DocumentId = documentId;
            DocumentName = documentName;
            StartedUtc = startedUtc;
            Depth = depth;
        }

        public Guid DocumentId { get; }
        public string DocumentName { get; }
        public DateTime StartedUtc { get; }

        /// <summary>
        /// How many solution starts are open. More than one when a solution starts inside another
        /// (a component calling <c>NewSolution</c> from its own solve); the document is solving
        /// until every one of them has ended.
        /// </summary>
        public int Depth { get; }
    }

    /// <summary>
    /// The last solve a document finished, as Grasshopper reported it at <c>SolutionEnd</c>.
    /// Immutable and swapped whole, so a reader never sees a generation from one solve paired with
    /// the aborted flag of another.
    /// </summary>
    public sealed class SolveRecord
    {
        public SolveRecord(long generation, DateTime? endedUtc, TimeSpan? span, bool aborted)
        {
            Generation = generation;
            EndedUtc = endedUtc;
            Span = span;
            Aborted = aborted;
        }

        /// <summary>
        /// How many solves this document has finished since it was first tracked. A waiter compares
        /// it with the value it saw when it scheduled, so "a solve ended after I asked" needs no
        /// event subscription of its own.
        /// </summary>
        public long Generation { get; }

        /// <summary>When the last solve ended; null while no solve has ended since tracking began.</summary>
        public DateTime? EndedUtc { get; }

        /// <summary>How long Grasshopper says the last solve took, when it said.</summary>
        public TimeSpan? Span { get; }

        /// <summary>
        /// True when the last solve was stopped before it finished (the human pressed Escape, or
        /// something asked Grasshopper to abort). Read from <c>GH_Document.AbortRequested</c> at
        /// <c>SolutionEnd</c>: Grasshopper overwrites <c>SolutionState = Aborted</c> with
        /// <c>PostProcess</c> before raising that event, so the solution state never shows it.
        /// </summary>
        public bool Aborted { get; }
    }

    /// <summary>
    /// A claim on "the next solve of this document to finish": the document and the generation a
    /// finished solve must pass. Taken when a solve is scheduled, redeemed by <see cref="SolveWait"/>.
    /// </summary>
    public readonly struct SolveTicket
    {
        public SolveTicket(Guid documentId, long generation)
        {
            DocumentId = documentId;
            Generation = generation;
        }

        public Guid DocumentId { get; }

        /// <summary>The generation a finished solve must exceed to answer this ticket.</summary>
        public long Generation { get; }
    }

    /// <summary>
    /// One lock-free reading of a document's solve state, for a waiter: whether it is still
    /// tracked, its last finished solve, and whether it is solving now.
    /// </summary>
    public sealed class SolveObservation
    {
        /// <summary>The reading for a document that is not tracked (never ticketed, or forgotten).</summary>
        public static readonly SolveObservation Untracked = new SolveObservation(false, null, false);

        public SolveObservation(bool tracked, SolveRecord record, bool solving)
        {
            Tracked = tracked;
            Record = record;
            Solving = solving;
        }

        public bool Tracked { get; }
        public SolveRecord Record { get; }
        public bool Solving { get; }
    }

    /// <summary>The document the bridge last saw focused, captured on the UI thread.</summary>
    public sealed class ActiveDocumentRef
    {
        public ActiveDocumentRef(Guid? documentId, string documentName)
        {
            DocumentId = documentId;
            DocumentName = documentName;
        }

        public Guid? DocumentId { get; }
        public string DocumentName { get; }
    }

    /// <summary>
    /// The parts of the status picture that only <c>McpServer</c> knows: its own listening state
    /// and traffic counters. Passed in so <see cref="SolverState.Derive"/> stays a pure function of
    /// its inputs and can be unit-tested without a server.
    /// </summary>
    public sealed class StatusInputs
    {
        public bool ServerListening { get; set; }
        public int Port { get; set; }
        public int InFlightRequests { get; set; }
        public int UptimeSeconds { get; set; }
        public int CommandCount { get; set; }
    }

    /// <summary>
    /// The three-layer answer to "is the bridge alive, or just busy?" — one layer each for the
    /// Rhino host, the Grasshopper solver, and the Cordyceps server.
    /// </summary>
    public sealed class HostStatus
    {
        // --- rhino layer ---

        /// <summary>
        /// True whenever this status was produced at all: the answer is computed off the UI thread
        /// from cached state, so producing one proves the Rhino process is running even when its
        /// UI thread is wedged.
        /// </summary>
        public bool RhinoAlive { get; set; } = true;

        public UiLiveness Ui { get; set; }

        /// <summary>
        /// True when the UI thread is blocked, no solution is running, and Cordyceps is not itself
        /// occupying the UI thread. Nothing else routinely holds it that long, so a modal dialog is
        /// the overwhelmingly likely cause — and a modal dialog needs a human, which is the one
        /// thing an unattended agent cannot summon. This inference is the whole reason the
        /// heartbeat exists.
        ///
        /// <para>The third condition is not a refinement, it is load-bearing. Tool bodies run ON
        /// the UI thread, so a long one (a large bake, a viewport capture, a big save) starves the
        /// heartbeat exactly like a dialog would. Without the check, a perfectly healthy host mid-bake
        /// reports a dialog that does not exist, and the guidance attached to that state tells the
        /// agent to stop and fetch a human.</para>
        /// </summary>
        public bool ModalInferred { get; set; }

        /// <summary>
        /// True when Cordyceps is running its own work on the UI thread. Distinguishes "the host is
        /// busy because we asked it to be" from "the host is stuck on something only a human can
        /// clear" — two states that look identical from a stale heartbeat alone.
        /// </summary>
        public bool UiWorkInProgress { get; set; }

        public DateTime? LastHeartbeatUtc { get; set; }
        public int? HeartbeatAgeMs { get; set; }

        // --- grasshopper layer ---

        public bool Solving { get; set; }
        public DateTime? SolvingSince { get; set; }

        /// <summary>
        /// The focused document — the one Grasshopper tools act on. Reported on every response
        /// because tools follow whichever canvas tab the human focused, so an agent that never
        /// sees the name cannot tell it is editing a different file than it believes.
        /// </summary>
        public string DocumentName { get; set; }

        public Guid? DocumentId { get; set; }

        /// <summary>
        /// The document that is solving, when one is — not necessarily the focused one. Several
        /// definitions share the single Rhino UI thread, so a solve in a file the agent is not
        /// touching still blocks it, and naming the wrong file would send it looking in the wrong
        /// place. Null when idle.
        /// </summary>
        public string SolvingDocumentName { get; set; }

        public Guid? SolvingDocumentId { get; set; }

        // --- cordyceps layer ---

        public bool ServerListening { get; set; }
        public int Port { get; set; }
        public int InFlightRequests { get; set; }
        public int UptimeSeconds { get; set; }
        public int CommandCount { get; set; }

        /// <summary>
        /// One sentence telling the caller what to do about the state above — wait, or fetch a
        /// human. The distinction an agent cannot make from a timeout alone.
        /// </summary>
        public string Hint { get; set; }

        /// <summary>True when nothing is degraded: the UI is responsive and no solve is running.</summary>
        public bool IsHealthy => Ui == UiLiveness.Responsive && !Solving;
    }

    /// <summary>
    /// Cached liveness state for the MCP bridge: which documents are solving, and when the Rhino
    /// UI thread last proved it was draining its queue.
    ///
    /// <para>The point of this class is that reading it costs nothing and blocks on nothing. The
    /// UI thread writes it (from Grasshopper's per-document solution events and a heartbeat tick);
    /// HTTP worker threads read it without marshaling and without taking the document lock, so a
    /// caller still gets an answer while the UI thread is wedged. That is the difference between
    /// "busy, wait" and "dead, give up" — which is otherwise indistinguishable from silence.</para>
    ///
    /// <para>Solve state is keyed by document id because Grasshopper raises
    /// <c>SolutionStart</c>/<c>SolutionEnd</c> per document and several documents can be open at
    /// once; a single global flag would report the wrong document's solve.</para>
    ///
    /// <para>Host-independent (no Grasshopper/Rhino references, no <c>DebugLog</c>) so the
    /// transitions and the modal inference are unit-tested — <c>Cordyceps.Tests</c> links this file
    /// directly. The clock is injected so staleness tests are deterministic.</para>
    /// </summary>
    public sealed class SolverState
    {
        /// <summary>
        /// How long the UI thread may go without stamping the heartbeat before it counts as
        /// blocked. Comfortably longer than the tick interval, so ordinary UI work (a redraw, a
        /// short solve) does not trip it, but short enough that a caller learns about a wedged
        /// host in seconds rather than minutes.
        /// </summary>
        public static readonly TimeSpan DefaultHeartbeatStaleAfter = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The instance the host wires up. Static because the solution events, the heartbeat tick,
        /// and every reader live in different objects but describe one Rhino process.
        /// </summary>
        public static SolverState Shared { get; } = new SolverState();

        private readonly Func<DateTime> _clock;
        private readonly TimeSpan _staleAfter;

        private readonly ConcurrentDictionary<Guid, DocumentSolve> _solving =
            new ConcurrentDictionary<Guid, DocumentSolve>();

        // The last finished solve per document. Kept apart from _solving because it must outlive
        // the solve it describes: the waiter, outline and tree read it after the solve has ended.
        private readonly ConcurrentDictionary<Guid, SolveRecord> _lastSolve =
            new ConcurrentDictionary<Guid, SolveRecord>();

        // Serialises the begin/end/forget writes, which read a document's depth and write it back.
        // Readers never take it: they read the dictionaries, whose entries are immutable.
        private readonly object _solveWriteGate = new object();

        // 0 means "never stamped". Written by the UI thread, read by HTTP worker threads.
        private long _lastHeartbeatTicks;

        // Immutable payload swapped as a whole, so a reader never sees a half-updated pair.
        private ActiveDocumentRef _activeDocument;

        // Supplies the running server's own counters. See PublishServerSnapshot.
        private Func<StatusInputs> _serverSnapshot;

        /// <summary>
        /// Depth of Cordyceps's own work currently occupying the UI thread. A depth, not a flag,
        /// because concurrent HTTP handlers each marshal independently and the last one to finish
        /// must be the one that clears it.
        /// </summary>
        private int _uiWorkDepth;

        /// <summary>True while any Cordyceps operation is executing on the UI thread.</summary>
        public bool UiWorkInProgress => Volatile.Read(ref _uiWorkDepth) > 0;

        /// <summary>
        /// Mark the start of work that occupies the UI thread. Call from the marshaling choke point
        /// only, and always pair with <see cref="EndUiWork"/> in a finally — a leaked increment
        /// suppresses modal inference for the rest of the session, which is the failure mode this
        /// counter exists to prevent.
        ///
        /// <para>Deliberately NOT called by the connection probe: the probe never touches the UI
        /// thread, so counting it would make the inference report "busy with our own work" during
        /// the very call asking whether a human is needed.</para>
        /// </summary>
        public void BeginUiWork() => Interlocked.Increment(ref _uiWorkDepth);

        /// <summary>Mark the end of UI-thread work. Never drops below zero.</summary>
        public void EndUiWork()
        {
            if (Interlocked.Decrement(ref _uiWorkDepth) < 0)
                Interlocked.Exchange(ref _uiWorkDepth, 0);
        }

        public SolverState(Func<DateTime> clock = null, TimeSpan? heartbeatStaleAfter = null)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
            _staleAfter = heartbeatStaleAfter ?? DefaultHeartbeatStaleAfter;
        }

        /// <summary>The staleness window this instance judges heartbeats against.</summary>
        public TimeSpan HeartbeatStaleAfter => _staleAfter;

        /// <summary>The last heartbeat stamp, or <c>null</c> if none has been recorded.</summary>
        public DateTime? LastHeartbeatUtc
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastHeartbeatTicks);
                return ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        /// <summary>The document last seen focused, or <c>null</c> if none has been recorded.</summary>
        public ActiveDocumentRef ActiveDocument => Volatile.Read(ref _activeDocument);

        /// <summary>True when any open document is mid-solution.</summary>
        public bool AnySolving => !_solving.IsEmpty;

        /// <summary>True when the named document is mid-solution.</summary>
        public bool IsSolving(Guid documentId) => _solving.ContainsKey(documentId);

        /// <summary>
        /// The solve to report when several documents are solving: the one that started first,
        /// since that is the one a caller has been waiting on longest. <c>null</c> when idle.
        /// </summary>
        public DocumentSolve ActiveSolve
        {
            get
            {
                DocumentSolve earliest = null;
                foreach (var solve in _solving.Values)
                {
                    if (earliest == null || solve.StartedUtc < earliest.StartedUtc)
                        earliest = solve;
                }
                return earliest;
            }
        }

        /// <summary>Number of documents currently mid-solution.</summary>
        public int SolvingCount => _solving.Count;

        /// <summary>
        /// Record that <paramref name="documentId"/> began solving. A start while that document is
        /// already solving is a nested solution: it deepens the solve and keeps the ORIGINAL start
        /// time, so <c>solving_since</c> reports how long the caller has actually been waiting
        /// rather than restarting the clock.
        /// </summary>
        public void BeginSolution(Guid documentId, string documentName)
        {
            var started = _clock();
            lock (_solveWriteGate)
            {
                _solving[documentId] = _solving.TryGetValue(documentId, out var existing)
                    ? new DocumentSolve(documentId, documentName ?? existing.DocumentName, existing.StartedUtc, existing.Depth + 1)
                    : new DocumentSolve(documentId, documentName, started);
            }
        }

        /// <summary>
        /// Record that one of <paramref name="documentId"/>'s solution starts ended, and how.
        ///
        /// <para>Inside a nested solve, an end only makes the solve shallower: the document is
        /// still solving and nothing is published, so a waiter cannot take the inner solve's end
        /// for the outer one's. The end that brings the depth to zero clears "solving" and
        /// advances the generation, carrying that (outermost) solve's span and aborted flag.</para>
        ///
        /// <para>An end with no open start must not throw, and it still advances the generation:
        /// Grasshopper raised it, so a solve did finish whose start went unseen. That happens when
        /// the watcher attaches mid-solve, as it does from inside the bridge component's own first
        /// solve. A second, unmatched end of the same solve is not something Grasshopper raises:
        /// each solution raises one start and one end.</para>
        /// </summary>
        /// <param name="documentId">The document whose solve ended.</param>
        /// <param name="span">How long Grasshopper says the solve took, when known.</param>
        /// <param name="aborted">True when the solve was stopped before it finished.</param>
        public void EndSolution(Guid documentId, TimeSpan? span = null, bool aborted = false)
        {
            var ended = _clock();
            lock (_solveWriteGate)
            {
                if (_solving.TryGetValue(documentId, out var open) && open.Depth > 1)
                {
                    _solving[documentId] = new DocumentSolve(documentId, open.DocumentName, open.StartedUtc, open.Depth - 1);
                    return;
                }

                // Clear "solving" before publishing the new generation, the reverse of the order
                // Observe reads them in: a waiter that sees the new generation and then reads "not
                // solving" is looking at a finished solve, never at the gap between the two writes.
                _solving.TryRemove(documentId, out _);
                _lastSolve.AddOrUpdate(
                    documentId,
                    _ => new SolveRecord(1, ended, span, aborted),
                    (_, existing) => new SolveRecord(existing.Generation + 1, ended, span, aborted));
            }
        }

        /// <summary>
        /// The last solve <paramref name="documentId"/> finished, or <c>null</c> when none has been
        /// recorded or the document was forgotten.
        /// </summary>
        public SolveRecord LastSolve(Guid documentId)
            => _lastSolve.TryGetValue(documentId, out var record) ? record : null;

        /// <summary>
        /// True when the last solve recorded for <paramref name="documentId"/> was aborted. The one
        /// place outline and tree read "the last solution was aborted" from.
        /// </summary>
        public bool LastSolutionAborted(Guid documentId) => LastSolve(documentId)?.Aborted ?? false;

        /// <summary>
        /// Claim the next solve of <paramref name="documentId"/> to finish. Call on the UI thread
        /// when the solve is scheduled, after the expiry that solve must pick up.
        ///
        /// <para>A solve already running when the ticket is taken does not answer it: that solve
        /// began before the expiry, so it may not include it, and the scheduled one runs after it.
        /// The ticket then asks for one generation more.</para>
        ///
        /// <para>Taking a ticket starts tracking the document (generation 0 when nothing has ended
        /// yet). That is what lets a waiter tell "not finished yet" from "forgotten": a tracked
        /// document's record disappears only through <see cref="ForgetDocument"/>.</para>
        /// </summary>
        public SolveTicket TakeTicket(Guid documentId)
        {
            lock (_solveWriteGate)
            {
                var record = _lastSolve.GetOrAdd(documentId, _ => new SolveRecord(0, null, null, false));
                var generation = IsSolving(documentId) ? record.Generation + 1 : record.Generation;
                return new SolveTicket(documentId, generation);
            }
        }

        /// <summary>
        /// One reading of <paramref name="documentId"/> for a waiter, without a lock. The record is
        /// read before the solving flag, the reverse of the order <see cref="EndSolution"/> writes
        /// them, so a new generation seen with "not solving" belongs to a finished solve.
        /// </summary>
        public SolveObservation Observe(Guid documentId)
        {
            var record = LastSolve(documentId);
            if (record == null) return SolveObservation.Untracked;
            return new SolveObservation(true, record, IsSolving(documentId));
        }

        /// <summary>
        /// Drop all state for a document that is closing. Without this, a document unloaded
        /// mid-solve (so its <c>SolutionEnd</c> never arrives) would be reported as solving
        /// forever, and a waiter on it would wait out its whole budget instead of learning it is
        /// gone.
        /// </summary>
        public void ForgetDocument(Guid documentId)
        {
            lock (_solveWriteGate)
            {
                _solving.TryRemove(documentId, out _);
                _lastSolve.TryRemove(documentId, out _);
            }
            var active = Volatile.Read(ref _activeDocument);
            if (active != null && active.DocumentId == documentId)
                Volatile.Write(ref _activeDocument, null);
        }

        /// <summary>
        /// Stamp the heartbeat. Called from the Rhino UI thread — the stamp landing is itself the
        /// evidence that the UI thread is draining its queue, so this must never be called from a
        /// worker thread or the signal means nothing.
        /// </summary>
        public void Heartbeat() => Interlocked.Exchange(ref _lastHeartbeatTicks, _clock().Ticks);

        /// <summary>
        /// Stamp the heartbeat and cache the focused document's identity. The identity is captured
        /// here, on the UI thread, so the off-thread status path never has to touch a Grasshopper
        /// object to name the document it is describing.
        /// </summary>
        public void Heartbeat(Guid? documentId, string documentName)
        {
            Volatile.Write(ref _activeDocument, new ActiveDocumentRef(documentId, documentName));
            Heartbeat();
        }

        /// <summary>
        /// Register the running server's counter source, so surfaces that hold no server reference
        /// (the tool classes) can still report the Cordyceps layer. The provider must be cheap and
        /// must not throw — it is expected to read volatile counters and nothing else; callers that
        /// can log guard the call anyway, because a diagnostic must never break a tool result.
        /// </summary>
        public void PublishServerSnapshot(Func<StatusInputs> provider)
            => Volatile.Write(ref _serverSnapshot, provider);

        /// <summary>
        /// Withdraw a previously-published provider. Only clears if <paramref name="provider"/> is
        /// still the registered one, so a server shutting down cannot unpublish its replacement.
        /// </summary>
        public void ClearServerSnapshot(Func<StatusInputs> provider)
            => Interlocked.CompareExchange(ref _serverSnapshot, null, provider);

        /// <summary>
        /// The published server counters, or an all-zero "not listening" snapshot when no server
        /// has published — which is itself the truthful answer.
        /// </summary>
        public StatusInputs ServerSnapshot()
            => Volatile.Read(ref _serverSnapshot)?.Invoke() ?? new StatusInputs();

        /// <summary>
        /// Classify a heartbeat stamp against a clock reading. Pure and static so the staleness
        /// boundary can be tested exactly, without waiting on wall-clock time.
        /// </summary>
        public static UiLiveness Classify(DateTime? lastHeartbeatUtc, DateTime nowUtc, TimeSpan staleAfter)
        {
            if (lastHeartbeatUtc == null) return UiLiveness.Unknown;

            // A stamp from the future (clock adjustment) is treated as fresh rather than as a
            // wildly negative age that would read as blocked.
            var age = nowUtc - lastHeartbeatUtc.Value;
            return age > staleAfter ? UiLiveness.Blocked : UiLiveness.Responsive;
        }

        /// <summary>
        /// Build the three-layer status using whatever server counters have been published. The
        /// form every surface uses; <see cref="Derive(StatusInputs)"/> is the pure core beneath it.
        /// </summary>
        public HostStatus Derive() => Derive(ServerSnapshot());

        /// <summary>
        /// Build the three-layer status from cached state plus the server's own counters. Reads no
        /// Grasshopper object and takes no lock, so it answers in microseconds no matter what the
        /// UI thread is doing.
        /// </summary>
        public HostStatus Derive(StatusInputs inputs)
        {
            if (inputs == null) inputs = new StatusInputs();

            var now = _clock();
            var lastHeartbeat = LastHeartbeatUtc;
            var ui = Classify(lastHeartbeat, now, _staleAfter);
            var solve = ActiveSolve;
            var solving = solve != null;

            // The key inference: a blocked UI thread means a human is needed only once both benign
            // explanations are excluded — a running solve, and Cordyceps's own work. Tool bodies
            // execute on the UI thread, so a long bake or capture starves the heartbeat exactly as
            // a dialog does; without the second exclusion a healthy host reports a dialog that is
            // not there, and the caller is told to stop and find a human.
            var uiWorkInProgress = UiWorkInProgress;
            var modalInferred = ui == UiLiveness.Blocked && !solving && !uiWorkInProgress;

            var active = ActiveDocument;
            var status = new HostStatus
            {
                RhinoAlive = true,
                Ui = ui,
                ModalInferred = modalInferred,
                UiWorkInProgress = uiWorkInProgress,
                LastHeartbeatUtc = lastHeartbeat,
                HeartbeatAgeMs = lastHeartbeat == null
                    ? (int?)null
                    : (int)Math.Max(0, (now - lastHeartbeat.Value).TotalMilliseconds),
                Solving = solving,
                SolvingSince = solve?.StartedUtc,
                // The solving document stands in for the focused one only before the first
                // heartbeat, so the name is never blank when something is known.
                DocumentName = active?.DocumentName ?? solve?.DocumentName,
                DocumentId = active?.DocumentId ?? solve?.DocumentId,
                SolvingDocumentName = solve?.DocumentName,
                SolvingDocumentId = solve?.DocumentId,
                ServerListening = inputs.ServerListening,
                Port = inputs.Port,
                InFlightRequests = inputs.InFlightRequests,
                UptimeSeconds = inputs.UptimeSeconds,
                CommandCount = inputs.CommandCount,
            };

            status.Hint = BuildHint(status);
            return status;
        }

        /// <summary>
        /// The actionable sentence for each cell of the liveness truth table. Kept here rather than
        /// in the caller so every surface (tool result, probe, health endpoint) says the same thing.
        /// </summary>
        internal static string BuildHint(HostStatus status)
        {
            // Name the SOLVING document in solve hints — with several definitions open it is not
            // necessarily the one the caller is working in, and that is the point worth saying.
            var solvingDoc = string.IsNullOrEmpty(status.SolvingDocumentName)
                ? "a document"
                : $"'{status.SolvingDocumentName}'";

            switch (status.Ui)
            {
                case UiLiveness.Unknown:
                    return "No UI heartbeat has been recorded yet, so host responsiveness is unknown. "
                         + "This is normal for the first moment after the Cordyceps component is placed.";

                case UiLiveness.Blocked when status.Solving:
                    return $"Grasshopper is solving {solvingDoc} and the UI thread is busy with it. "
                         + "Wait and retry — this is a busy bridge, not a dead one. "
                         + "If it persists far beyond the expected solve time, ask a human to check Rhino for an open dialog.";

                case UiLiveness.Blocked when status.UiWorkInProgress:
                    return $"A Cordyceps operation is running on the Rhino UI thread and has held it for "
                         + $"{FormatAge(status.HeartbeatAgeMs)}. This is our own work, not a stuck host — "
                         + "long bakes, viewport captures and large saves all look like this. Wait for it to finish.";

                case UiLiveness.Blocked:
                    return $"The Rhino UI thread has not responded for {FormatAge(status.HeartbeatAgeMs)}, "
                         + "no solution is running, and Cordyceps is not doing anything. "
                         + "A modal dialog is almost certainly open and needs a human to dismiss it — "
                         + "document-touching calls will block until it is cleared. Do not keep retrying.";

                case UiLiveness.Responsive when status.Solving:
                    return $"Grasshopper is solving {solvingDoc}; the host is responsive.";

                default:
                    return "Healthy: the Rhino UI thread is responsive and no solution is running.";
            }
        }

        private static string FormatAge(int? ageMs)
        {
            if (ageMs == null) return "an unknown time";
            var seconds = ageMs.Value / 1000.0;
            return seconds < 90
                ? seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s"
                : (seconds / 60.0).ToString("0.#", CultureInfo.InvariantCulture) + " min";
        }
    }
}
