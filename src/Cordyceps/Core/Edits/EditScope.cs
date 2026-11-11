using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core.Edits
{
    /// <summary>
    /// The host's half of one edit, as <see cref="EditScope{TSession}"/> sees it: the journal that
    /// holds the ordering rules, the solve refusal to report, and the host's follow-up work.
    /// </summary>
    internal interface IEditSession
    {
        IEditJournal Journal { get; }

        /// <summary>
        /// The <c>solve</c> object to report after the commit's solve request, when a solver switch
        /// refused it; null when it was scheduled or none was asked for.
        /// </summary>
        JObject SolveRefusal { get; }

        /// <summary>
        /// Host work after a rollback (logging, redrawing). <paramref name="cause"/> is the
        /// unexpected exception that started it, or null for a refusal or a failed commit.
        /// </summary>
        void AfterRollback(RollbackReport report, string reason, Exception cause);

        /// <summary>Host work after a commit that stands (logging its warnings).</summary>
        void AfterCommit(CommitOutcome outcome);

        /// <summary>Restore what the edit paused when a step threw before the journal could; a no-op otherwise.</summary>
        void EnsureEnded();
    }

    /// <summary>
    /// Runs edit bodies as transactions and decides every reply, free of Grasshopper so the
    /// decisions are tested: an edit reached from inside an open edit of the same document joins it
    /// (one call, one undo step) and of another document is refused; a body that throws
    /// <see cref="EditRollbackException"/> is answered with its own message, anything else as an
    /// unexpected failure, both after a rollback; a commit that fails before its record is pushed
    /// is rolled back and answered the same way, and one that fails after is answered as a success
    /// with a <c>warning</c>. A failure in the host's follow-up never replaces the reply: it is
    /// added to it. One instance per host, used on the UI thread only.
    /// </summary>
    internal sealed class EditScope<TSession> where TSession : class, IEditSession
    {
        private TSession _open;
        private object _openDocument;
        private string _openName;

        /// <summary>True while an edit is running.</summary>
        public bool IsOpen => _open != null;

        /// <summary>
        /// Run <paramref name="body"/> as one edit of <paramref name="document"/>, opening a session
        /// with <paramref name="open"/> unless an edit of the same document is already running, in
        /// which case the body joins it and its reply is returned as the body wrote it.
        /// </summary>
        public string Run(object document, string name, Func<TSession> open, Func<TSession, string> body)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("An edit needs a name for its undo record.", nameof(name));
            if (open == null) throw new ArgumentNullException(nameof(open));
            if (body == null) throw new ArgumentNullException(nameof(body));

            if (_open != null)
            {
                if (!ReferenceEquals(_openDocument, document))
                    throw new InvalidOperationException($"'{name}' edits another document while '{_openName}' is open; one call edits one document.");
                return body(_open);
            }

            var session = open() ?? throw new InvalidOperationException("The edit session could not be opened.");
            _open = session;
            _openDocument = document;
            _openName = name;
            try
            {
                session.Journal.Begin();

                JObject reply;
                try
                {
                    reply = EditReplies.ParseBody(body(session));
                }
                catch (EditRollbackException ex)
                {
                    return RolledBack(session, name, ex.Message, refused: true, session.Journal.Rollback(), cause: null);
                }
                catch (Exception ex) // prawduct:allow prawduct/broad-except -- apply boundary: whatever the body threw, its recorded changes are undone so the canvas is left as it was, and the call answers with the error
                {
                    return RolledBack(session, name, ex.Message, refused: false, session.Journal.Rollback(), cause: ex);
                }

                var outcome = session.Journal.Commit();
                if (outcome.Rollback != null)
                    return RolledBack(session, name, EditReplies.CommitFailureReason(outcome.Failure), refused: false, outcome.Rollback, cause: null);

                var warnings = new List<EditFailure>(outcome.Warnings);
                try
                {
                    session.AfterCommit(outcome);
                }
                catch (Exception ex) // prawduct:allow prawduct/broad-except -- edit-end boundary: the edit stands, so a failure in the host's follow-up becomes a warning on the reply rather than replacing it
                {
                    warnings.Add(new EditFailure(EditStep.AfterCommit, ex.Message));
                }

                return EditReplies.Committed(
                    reply,
                    outcome.Pushed ? name : null,
                    outcome.Scheduled ? session.SolveRefusal : null,
                    warnings);
            }
            finally
            {
                _open = null;
                _openDocument = null;
                _openName = null;
                session.EnsureEnded();
            }
        }

        private static string RolledBack(TSession session, string name, string reason, bool refused, RollbackReport report, Exception cause)
        {
            try
            {
                session.AfterRollback(report, reason, cause);
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- edit-end boundary: the rollback report is the reply, so a failure in the host's follow-up is added to it rather than losing it
            {
                report = report.With(new EditFailure(EditStep.AfterRollback, ex.Message));
            }
            return EditReplies.RolledBack(name, reason, refused, report);
        }
    }
}
