using System;
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
    /// The replies of an edit transaction: the body's own reply plus <c>undo</c> (and <c>solve</c>
    /// when the solver refused) on commit, and a rollback report on failure. Host-free so every
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
        /// The body's reply as a JSON object. A body that returns anything else is a programming
        /// error, raised here, inside the transaction, so the edit rolls back rather than commits a
        /// change whose reply cannot carry its <c>undo</c>.
        /// </summary>
        public static JObject ParseBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("The edit returned no reply.");
            JToken token;
            try
            {
                token = JToken.Parse(body);
            }
            catch (JsonReaderException ex)
            {
                throw new InvalidOperationException($"The edit's reply is not JSON: {ex.Message}", ex);
            }
            if (token is JObject obj) return obj;
            throw new InvalidOperationException("The edit's reply is not a JSON object.");
        }

        /// <summary>
        /// A committed edit's reply: the body's fields, then <c>undo</c> naming the record when one
        /// was pushed, then <c>solve</c> when the solve was refused (a solver switch is off). A
        /// refused solve never fails the edit: the change was made, and a human may have switched
        /// the solver off on purpose. A body that already reports its own <c>solve</c> keeps it.
        /// </summary>
        public static string Committed(JObject body, string undoName, JObject refusedSolve)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (undoName != null) body["undo"] = undoName;
            if (refusedSolve != null && body["solve"] == null) body["solve"] = refusedSolve;
            return body.ToString(Formatting.None);
        }

        /// <summary>
        /// A rolled-back edit's reply. <paramref name="refused"/> is an <see cref="EditRollbackException"/>,
        /// whose message is the whole reason; anything else failed unexpectedly and is named as such.
        /// <c>rolledBack</c> is true only when every change was undone; otherwise
        /// <c>rollbackFailures</c> lists what was not, and the error says the canvas needs checking.
        /// </summary>
        public static string RolledBack(string recordName, string reason, bool refused, RollbackReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var error = refused ? reason : $"{recordName} failed: {reason}";
            error = error.TrimEnd();
            if (!error.EndsWith(".")) error += ".";

            if (report.Complete)
            {
                error += report.Undone > 0
                    ? " Nothing was changed: every change this call had made was rolled back."
                    : " Nothing was changed.";
            }
            else
            {
                var total = report.Undone + report.Failures.Count;
                error += $" The rollback did not complete: {report.Failures.Count} of {total} change(s) could not be undone (rollbackFailures). "
                       + "Check the canvas; Ctrl+Z does not cover this call.";
            }

            var reply = new JObject
            {
                ["success"] = false,
                ["error"] = error,
                ["rolledBack"] = report.Complete,
            };
            if (!report.Complete)
            {
                reply["rollbackFailures"] = new JArray(report.Failures.Select(f => new JObject
                {
                    ["kind"] = KindName(f.Kind),
                    ["id"] = f.Id.ToString(),
                    ["error"] = f.Error,
                }));
            }
            return reply.ToString(Formatting.None);
        }

        /// <summary>The kind as a reply names it: <c>wires</c>, <c>nickName</c>, <c>persistentData</c>.</summary>
        public static string KindName(EditKind kind)
        {
            var name = kind.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }
}
