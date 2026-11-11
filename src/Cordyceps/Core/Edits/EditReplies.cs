using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core.Edits
{
    /// <summary>
    /// Thrown inside an edit body to stop the edit, roll back every change it made, and answer
    /// with <see cref="Exception.Message"/> as the error. For a step that reports failure without
    /// throwing (a port that could not be added, an add the document refused) when what it had
    /// already changed must not stay half applied.
    /// </summary>
    internal sealed class EditRollbackException : Exception
    {
        public EditRollbackException(string message) : base(message) { }
    }

    /// <summary>
    /// The replies of an edit transaction: the body's own reply plus <c>undo</c>, <c>solve</c> and
    /// <c>warning</c> as they apply on commit, and a rollback report on failure. Host-free so every
    /// tool's edit reply has the same tested shape.
    /// </summary>
    internal static class EditReplies
    {
        /// <summary>
        /// The undo record's name, shown in Grasshopper's undo list and returned as <c>undo</c>:
        /// it says the change came from Cordyceps and which call made it.
        /// </summary>
        public static string RecordName(string tool, string action) => $"Cordyceps: {tool} {action}";

        /// <summary>
        /// The body's reply as a JSON object, parsed so that it re-serializes as it was written:
        /// a date-shaped string (an object's nickname, say) stays a string instead of becoming a
        /// date that comes back out in another form. A body that returns anything but a JSON object
        /// is a programming error, raised here, inside the transaction, so the edit rolls back
        /// rather than commits a change whose reply cannot carry its <c>undo</c>.
        /// </summary>
        public static JObject ParseBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("The edit returned no reply.");
            JToken token;
            try
            {
                token = ParseHelpers.ParseJsonKeepingText(body);
            }
            catch (JsonReaderException ex)
            {
                throw new InvalidOperationException($"The edit's reply is not JSON: {ex.Message}", ex);
            }
            if (token is JObject obj) return obj;
            throw new InvalidOperationException("The edit's reply is not a JSON object.");
        }

        /// <summary>
        /// The <c>solve</c> object a committed edit reports, from what its commit did:
        /// <list type="bullet">
        /// <item>none when the commit asked for no solve (nothing changed) or the request threw (the reply's <c>warning</c> says so);</item>
        /// <item>the refusal when a solver switch stopped the solve, for every edit: the change was made, but nothing computes until the switch is on;</item>
        /// <item><c>scheduled</c> when the solve was scheduled, only for an edit that asks for it (<paramref name="reportScheduled"/>). Scheduling is what every edit does, so most leave it out; <c>gh_document clear</c> reports it because it used to solve before replying.</item>
        /// </list>
        /// </summary>
        /// <param name="scheduled">The commit's own answer, <see cref="CommitOutcome.Scheduled"/>: a solve was asked for and the request did not throw.</param>
        /// <param name="refusal">The <c>solve</c> object of a refused request; null when the request was scheduled.</param>
        /// <param name="reportScheduled">True for an edit whose reply names a scheduled solve.</param>
        public static JObject CommitSolve(bool scheduled, JObject refusal, bool reportScheduled)
        {
            if (!scheduled) return null;
            if (refusal != null) return refusal;
            return reportScheduled ? SolvePolicy.ScheduledField(null) : null;
        }

        /// <summary>
        /// A committed edit's reply: the body's fields, then
        /// <list type="bullet">
        /// <item><c>undo</c> naming the record, when one was pushed (<paramref name="pushedRecord"/>);</item>
        /// <item><c>solve</c>, as <see cref="CommitSolve"/> decided it. A refused solve never fails the edit: the change was made, and a human may have switched the solver off on purpose. A body that reports its own <c>solve</c> keeps it;</item>
        /// <item><c>warning</c>, when a step after the push threw. The edit stands, so <c>success</c> is the body's; the warning names the step and what to check. A body's own <c>warning</c> comes first.</item>
        /// </list>
        /// </summary>
        public static string Committed(JObject body, string pushedRecord, JObject solve, IReadOnlyList<EditFailure> warnings = null)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (pushedRecord != null) body["undo"] = pushedRecord;
            if (solve != null && body["solve"] == null) body["solve"] = solve;

            if (warnings != null && warnings.Count > 0)
            {
                var text = CommitWarning(pushedRecord != null, warnings);
                var own = body["warning"]?.Type == JTokenType.String ? (string)body["warning"] : null;
                body["warning"] = string.IsNullOrEmpty(own) ? text : own + " " + text;
            }
            return body.ToString(Formatting.None);
        }

        /// <summary>
        /// The reason a commit that failed before its record was pushed gives, for
        /// <see cref="RolledBack"/>.
        /// </summary>
        public static string CommitFailureReason(EditFailure failure)
        {
            if (failure == null) throw new ArgumentNullException(nameof(failure));
            var what = failure.Step == EditStep.Push
                ? "recording it for undo"
                : "removing an object whose add did not finish";
            return $"{what} failed ({failure.Error})";
        }

        /// <summary>
        /// A rolled-back edit's reply. <paramref name="refused"/> is an <see cref="EditRollbackException"/>,
        /// whose message is the whole reason; anything else failed unexpectedly and is named as such.
        /// <c>rolledBack</c> is true only when every recorded change was undone; a step that failed
        /// after the undo (restoring the solver, scheduling the solve) leaves it true but is listed
        /// in <c>rollbackFailures</c> with its <c>step</c>, as is every change that could not be undone.
        /// </summary>
        public static string RolledBack(string recordName, string reason, bool refused, RollbackReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var error = refused ? reason : $"{recordName} failed: {reason}";
            error = (error ?? "").TrimEnd();
            if (!error.EndsWith(".")) error += ".";

            var undoFailures = report.Failures.Count(f => f.Step == EditStep.Undo);
            if (undoFailures == 0)
            {
                error += report.Undone > 0
                    ? " Nothing was changed: every change this call had made was rolled back."
                    : " Nothing was changed.";
                if (report.Failures.Count > 0)
                    error += $" After the rollback, {Describe(report.Failures)} failed (rollbackFailures); its results may be stale until the next solve.";
            }
            else
            {
                var total = report.Undone + undoFailures;
                error += $" The rollback did not complete: {undoFailures} of {total} change(s) could not be undone (rollbackFailures). "
                       + "Check the canvas; Ctrl+Z does not cover this call.";
            }

            var reply = new JObject
            {
                ["success"] = false,
                ["error"] = error,
                ["rolledBack"] = report.Complete,
            };
            if (report.Failures.Count > 0)
                reply["rollbackFailures"] = new JArray(report.Failures.Select(FailureJson));
            return reply.ToString(Formatting.None);
        }

        /// <summary>The kind as a reply names it: <c>wires</c>, <c>nickName</c>, <c>dataModifiers</c>.</summary>
        public static string KindName(EditKind kind)
        {
            var name = kind.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        private static string CommitWarning(bool pushed, IReadOnlyList<EditFailure> warnings)
        {
            var made = pushed ? "The edit was made and Ctrl+Z undoes it" : "The edit was made";
            var steps = Describe(warnings);
            var stale = warnings.Any(w => w.Step == EditStep.Expire || w.Step == EditStep.Schedule || w.Step == EditStep.Restore)
                ? " Its results may be stale: check gh_document(action='info') for the solver switch, then run gh_document(action='recompute')."
                : "";
            var detail = string.Join("; ", warnings.Select(w => w.Error).Distinct());
            return $"{made}, but {steps} failed ({detail}).{stale}";
        }

        private static string Describe(IEnumerable<EditFailure> failures)
        {
            var phrases = failures.Select(f => StepPhrase(f.Step)).Distinct().ToList();
            if (phrases.Count == 1) return phrases[0];
            return string.Join(", ", phrases.Take(phrases.Count - 1)) + " and " + phrases[phrases.Count - 1];
        }

        private static string StepPhrase(string step)
        {
            switch (step)
            {
                case EditStep.Restore: return "restoring the document's solver switch";
                case EditStep.Expire: return "marking the changed objects for recompute";
                case EditStep.Schedule: return "scheduling the solve";
                case EditStep.AfterRollback: return "redrawing the canvas";
                case EditStep.AfterCommit: return "logging the edit";
                case EditStep.Undo: return "undoing a change";
                case EditStep.Push: return "recording the edit for undo";
                case EditStep.DiscardUnfinishedAdd: return "removing an object whose add did not finish";
                default: return step;
            }
        }

        private static JObject FailureJson(EditFailure f)
        {
            var json = new JObject { ["step"] = f.Step };
            if (f.Kind.HasValue) json["kind"] = KindName(f.Kind.Value);
            if (f.Id.HasValue) json["id"] = f.Id.Value.ToString();
            json["error"] = f.Error;
            return json;
        }
    }
}
