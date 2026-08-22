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

    /// <summary>An in-progress solution: which document, and when it started.</summary>
    public sealed class DocumentSolve
    {
        public DocumentSolve(Guid documentId, string documentName, DateTime startedUtc)
        {
            DocumentId = documentId;
            DocumentName = documentName;
            StartedUtc = startedUtc;
        }

        public Guid DocumentId { get; }
        public string DocumentName { get; }
        public DateTime StartedUtc { get; }
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
        /// True when the UI thread is blocked and no solution is running. Nothing else routinely
        /// holds the UI thread that long, so a modal dialog is the overwhelmingly likely cause —
        /// and a modal dialog needs a human, which is the one thing an unattended agent cannot
        /// summon. This inference is the whole reason the heartbeat exists.
        /// </summary>
        public bool ModalInferred { get; set; }

        public DateTime? LastHeartbeatUtc { get; set; }
        public int? HeartbeatAgeMs { get; set; }

        // --- grasshopper layer ---

        public bool Solving { get; set; }
        public DateTime? SolvingSince { get; set; }

        /// <summary>
        /// The document this status describes: the solving document when one is solving, otherwise
        /// the last document seen focused. Reported on every response because Grasshopper tools
        /// follow the focused canvas, so an agent that never sees the name cannot tell it is
        /// editing a different file than it believes.
        /// </summary>
        public string DocumentName { get; set; }

        public Guid? DocumentId { get; set; }

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

        // 0 means "never stamped". Written by the UI thread, read by HTTP worker threads.
        private long _lastHeartbeatTicks;

        // Immutable payload swapped as a whole, so a reader never sees a half-updated pair.
        private ActiveDocumentRef _activeDocument;

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
        /// Record that <paramref name="documentId"/> began solving. Re-entering while that document
        /// is already marked solving keeps the ORIGINAL start time, so <c>solving_since</c> reports
        /// how long the caller has actually been waiting rather than restarting the clock on a
        /// nested or re-raised event.
        /// </summary>
        public void BeginSolution(Guid documentId, string documentName)
        {
            var started = _clock();
            _solving.AddOrUpdate(
                documentId,
                _ => new DocumentSolve(documentId, documentName, started),
                (_, existing) => new DocumentSolve(documentId, documentName ?? existing.DocumentName, existing.StartedUtc));
        }

        /// <summary>
        /// Record that <paramref name="documentId"/> finished solving. Ending a document that was
        /// never marked solving is a no-op: the state must be self-correcting, because a missed
        /// start event must not make a later end throw, and a stuck "solving" flag would report a
        /// healthy bridge as permanently busy.
        /// </summary>
        public void EndSolution(Guid documentId) => _solving.TryRemove(documentId, out _);

        /// <summary>
        /// Drop all state for a document that is closing. Without this, a document unloaded
        /// mid-solve (so its <c>SolutionEnd</c> never arrives) would be reported as solving
        /// forever.
        /// </summary>
        public void ForgetDocument(Guid documentId)
        {
            EndSolution(documentId);
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

            // The key inference: only a solve legitimately holds the UI thread for this long, so a
            // blocked UI with nothing solving means something is waiting on a human.
            var modalInferred = ui == UiLiveness.Blocked && !solving;

            var active = ActiveDocument;
            var status = new HostStatus
            {
                RhinoAlive = true,
                Ui = ui,
                ModalInferred = modalInferred,
                LastHeartbeatUtc = lastHeartbeat,
                HeartbeatAgeMs = lastHeartbeat == null
                    ? (int?)null
                    : (int)Math.Max(0, (now - lastHeartbeat.Value).TotalMilliseconds),
                Solving = solving,
                SolvingSince = solve?.StartedUtc,
                DocumentName = solve?.DocumentName ?? active?.DocumentName,
                DocumentId = solve?.DocumentId ?? active?.DocumentId,
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
            var doc = string.IsNullOrEmpty(status.DocumentName) ? "the document" : $"'{status.DocumentName}'";

            switch (status.Ui)
            {
                case UiLiveness.Unknown:
                    return "No UI heartbeat has been recorded yet, so host responsiveness is unknown. "
                         + "This is normal for the first moment after the Cordyceps component is placed.";

                case UiLiveness.Blocked when status.Solving:
                    return $"Grasshopper is solving {doc} and the UI thread is busy with it. "
                         + "Wait and retry — this is a busy bridge, not a dead one. "
                         + "If it persists far beyond the expected solve time, ask a human to check Rhino for an open dialog.";

                case UiLiveness.Blocked:
                    return $"The Rhino UI thread has not responded for {FormatAge(status.HeartbeatAgeMs)} and no solution is running. "
                         + "A modal dialog is almost certainly open and needs a human to dismiss it — "
                         + "document-touching calls will block until it is cleared. Do not keep retrying.";

                case UiLiveness.Responsive when status.Solving:
                    return $"Grasshopper is solving {doc}; the host is responsive.";

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
