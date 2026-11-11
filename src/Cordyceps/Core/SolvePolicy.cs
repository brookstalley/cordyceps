using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core
{
    /// <summary>What Cordyceps reports about a solve it scheduled.</summary>
    public enum SolveState
    {
        /// <summary>Scheduled and not waited for (solver, clear, revert).</summary>
        Scheduled,

        /// <summary>The solve finished.</summary>
        Completed,

        /// <summary>The solve ended early: the human pressed Escape, or something asked it to abort.</summary>
        Aborted,

        /// <summary>The wait's budget ran out while the solve was still running or not yet started.</summary>
        OverBudget,

        /// <summary>This document's own solver is off, so a solve would compute nothing.</summary>
        SolverDisabled,

        /// <summary>Grasshopper's global solver switch is off, so no document solves at all.</summary>
        SolutionsLocked,

        /// <summary>The document closed before the solve finished, so its outcome is unknown.</summary>
        DocumentClosed,

        /// <summary>
        /// The wait ran out with nothing solving and no solve finished: the scheduled solve never ran.
        /// </summary>
        NotStarted,

        /// <summary>
        /// Cordyceps could not watch the document's solution events, so it could not have told when
        /// a solve ended; it scheduled none.
        /// </summary>
        NotWatched,
    }

    /// <summary>
    /// The decisions behind every solve Cordyceps starts: whether the solver switches allow one,
    /// what a wait's outcome means, what the caller should do about it, and the <c>solve</c> object
    /// each reply carries. <c>SolveCoordinator</c> is the host half; this half is host-free and
    /// linked into <c>Cordyceps.Tests</c>, so the whole table is tested without Grasshopper.
    /// </summary>
    public static class SolvePolicy
    {
        public const int DefaultTimeoutSeconds = 30;
        public const int MinTimeoutSeconds = 1;
        public const int MaxTimeoutSeconds = 300;

        /// <summary>
        /// Parse <c>recompute</c>'s <c>timeout</c>: absent means the default; anything else must be a
        /// whole number of seconds within range. The decision is made from the text's shape, not from
        /// whether an Int32 parse succeeds, so <c>99999999999</c> is refused as out of range rather
        /// than as "not a number", and nothing outside the range is silently clamped.
        /// </summary>
        public static bool TryParseTimeout(string raw, out int seconds, out string error)
        {
            seconds = DefaultTimeoutSeconds;
            error = null;
            if (raw == null || raw.Length == 0) return true;

            var range = $"{MinTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}-{MaxTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}";
            if (!IsAsciiInteger(raw))
            {
                error = $"timeout must be a whole number of seconds, {range} (got '{raw}'); omit it for the default of {DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
                || parsed < MinTimeoutSeconds || parsed > MaxTimeoutSeconds)
            {
                error = $"timeout must be {range} seconds (got {raw}); omit it for the default of {DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            seconds = parsed;
            return true;
        }

        /// <summary>
        /// The state that stops a solve before it is scheduled, or null when one can be scheduled
        /// and observed. The global switch is checked first: with it off Grasshopper returns before
        /// raising <c>SolutionStart</c> or <c>SolutionEnd</c>, so a wait would never end. With only
        /// the document's switch off the events fire but nothing is computed, so a "completed"
        /// answer would be a lie. Last, <paramref name="watching"/> is asked whether the solve's end
        /// can be seen; it is asked only when both switches allow a solve, because asking can start
        /// watching a document, which a refused solve must not do. Without it the solve could only
        /// ever be reported as having run out of time.
        /// </summary>
        public static SolveState? Refusal(bool documentEnabled, bool solutionsEnabled, Func<bool> watching = null)
        {
            if (!solutionsEnabled) return SolveState.SolutionsLocked;
            if (!documentEnabled) return SolveState.SolverDisabled;
            if (watching != null && !watching()) return SolveState.NotWatched;
            return null;
        }

        /// <summary>The state a finished wait reports.</summary>
        public static SolveState FromWait(SolveWaitOutcome outcome, bool aborted)
        {
            switch (outcome)
            {
                case SolveWaitOutcome.Completed:
                    return aborted ? SolveState.Aborted : SolveState.Completed;
                case SolveWaitOutcome.OverBudget:
                    return SolveState.OverBudget;
                case SolveWaitOutcome.NotStarted:
                    return SolveState.NotStarted;
                case SolveWaitOutcome.Gone:
                    return SolveState.DocumentClosed;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }
        }

        /// <summary><c>recomputed</c> is true only for a solve that finished.</summary>
        public static bool Recomputed(SolveState state) => state == SolveState.Completed;

        /// <summary>
        /// Whether to read the document's runtime messages after the wait. Only when a solve has
        /// ended: during an over-budget solve the read would queue behind it on the UI thread, the
        /// very wait the budget exists to bound, and a closed document has nothing to read.
        /// </summary>
        public static bool ReadsMessages(SolveState state)
            => state == SolveState.Completed || state == SolveState.Aborted;

        /// <summary>The wire name of <paramref name="state"/>.</summary>
        public static string Name(SolveState state)
        {
            switch (state)
            {
                case SolveState.Scheduled: return "scheduled";
                case SolveState.Completed: return "completed";
                case SolveState.Aborted: return "aborted";
                case SolveState.OverBudget: return "over_budget";
                case SolveState.SolverDisabled: return "solver_disabled";
                case SolveState.SolutionsLocked: return "solutions_locked";
                case SolveState.DocumentClosed: return "document_closed";
                case SolveState.NotStarted: return "not_started";
                case SolveState.NotWatched: return "not_watched";
                default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        /// <summary>
        /// What the caller should do about <paramref name="state"/>, naming who can act; null when
        /// there is nothing to do. <paramref name="documentEnabled"/> matters only for a locked
        /// solver, where the document's own switch may need turning on as well.
        /// </summary>
        public static string Remedy(SolveState state, bool documentEnabled = true)
        {
            switch (state)
            {
                case SolveState.Aborted:
                    return "The solve was stopped before it finished (Escape in Grasshopper, or an abort request), so outputs may be partial or stale. "
                         + "A human usually stops a solve on purpose: ask before recomputing again.";
                case SolveState.OverBudget:
                    return "The solve is still running; Cordyceps cannot cancel it. "
                         + "Poll gh_inspect(action='connection') until grasshopper.solving is false, then read results with gh_inspect(action='status'). "
                         + "recompute is refused while it runs. Pass a larger timeout (up to "
                         + MaxTimeoutSeconds.ToString(CultureInfo.InvariantCulture) + " seconds) to wait longer next time.";
                case SolveState.SolverDisabled:
                    return "This document's solver is off, so nothing was computed. "
                         + "Turn it on with gh_document(action='solver', enabled=true), which schedules a solve.";
                case SolveState.SolutionsLocked:
                    return "Grasshopper's global solver switch is off, so no document solves. Either the solver is locked in Grasshopper "
                         + "(the 'Lock Solver' toggle), which Cordyceps does not change: ask a human to unlock it; "
                         + "or Rhino cannot save right now (a command is running), which clears on its own: retry once Rhino is idle."
                         + (documentEnabled
                             ? ""
                             : " This document's own solver is off as well; once unlocked, turn it on with gh_document(action='solver', enabled=true).");
                case SolveState.DocumentClosed:
                    return "The document closed before the solve finished, so its outcome is unknown.";
                case SolveState.NotStarted:
                    return "The scheduled solve never ran: Grasshopper's 'Lock Solver' was switched on meanwhile, the Grasshopper editor window was not available to start it, or the document raises no solution events. "
                         + "Check gh_inspect(action='connection') and gh_inspect(action='status'), then retry recompute.";
                case SolveState.NotWatched:
                    return "Cordyceps could not watch this document's solution events, so it could not have told when a solve ended, and it scheduled none. "
                         + "The Cordyceps bridge component's solution watcher is not running (Grasshopper's document server is not available to it). "
                         + "Make sure the Cordyceps component is on the canvas and enabled, then retry.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The <c>solve</c> object for a reply: <c>state</c>, then whichever of <c>elapsedMs</c>,
        /// <c>errors</c>, <c>warnings</c> and <c>remedy</c> apply. Null members are left out so a
        /// reply carries only what is known.
        /// </summary>
        public static JObject SolveField(SolveState state, long? elapsedMs = null, int? errors = null, int? warnings = null, bool documentEnabled = true)
        {
            var solve = new JObject { ["state"] = Name(state) };
            if (elapsedMs.HasValue) solve["elapsedMs"] = elapsedMs.Value;
            if (errors.HasValue) solve["errors"] = errors.Value;
            if (warnings.HasValue) solve["warnings"] = warnings.Value;
            var remedy = Remedy(state, documentEnabled);
            if (remedy != null) solve["remedy"] = remedy;
            return solve;
        }

        /// <summary>
        /// The <c>solve</c> object for an action that schedules and does not wait (solver, clear,
        /// revert): <c>scheduled</c>, or the switch that stopped it.
        /// </summary>
        public static JObject ScheduledField(SolveState? refusal, bool documentEnabled = true)
            => SolveField(refusal ?? SolveState.Scheduled, documentEnabled: documentEnabled);

        /// <summary>
        /// The whole <c>recompute</c> reply. <c>success</c> and <c>recomputed</c> are true only for a
        /// completed solve; every other state is a failure the caller must act on, and says how in
        /// <c>solve.remedy</c>.
        /// </summary>
        public static string RecomputeReply(SolveState state, long? elapsedMs, int? errors, int? warnings, bool documentEnabled = true)
        {
            var completed = Recomputed(state);
            var reply = new JObject
            {
                ["success"] = completed,
                ["recomputed"] = completed,
            };
            if (!completed)
                reply["error"] = $"Recompute did not complete: the solve is {Name(state)}. See solve.remedy.";
            reply["solve"] = SolveField(state, elapsedMs, errors, warnings, documentEnabled);
            return reply.ToString(Formatting.None);
        }

        // Digits with an optional leading sign: the shape of a number, whatever its size.
        private static bool IsAsciiInteger(string text)
        {
            int start = text[0] == '+' || text[0] == '-' ? 1 : 0;
            if (start >= text.Length) return false;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9') return false;
            }
            return true;
        }
    }
}
