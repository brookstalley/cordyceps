// Portions of this file are adapted from Hyperceps (src/Cordyceps/Tools/Batch/BatchOps.cs,
// ApplyContext and DeleteOp; BatchRunner.cs, Apply and Rollback), under the MIT License:
//
//   Copyright (c) 2026 Brooks Talley
//   Copyright (c) 2026 Yibin Yang
//
//   Permission is hereby granted, free of charge, to any person obtaining a copy of this software
//   and associated documentation files (the "Software"), to deal in the Software without
//   restriction, including without limitation the rights to use, copy, modify, merge, publish,
//   distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
//   Software is furnished to do so, subject to the following conditions:
//
//   The above copyright notice and this permission notice shall be included in all copies or
//   substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
//   BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//   NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
//   DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;

namespace Cordyceps.Core.Edits
{
    /// <summary>
    /// What an undo action restores. Each kind is one of Grasshopper's own undo actions; the
    /// journal keeps at most one per (kind, object) in a transaction.
    /// </summary>
    public enum EditKind
    {
        /// <summary>An input's complete, ordered source list.</summary>
        Wires,

        /// <summary>The whole object, archived (ports, settings, values) and its wires.</summary>
        Generic,

        NickName,

        /// <summary>The object's position on the canvas.</summary>
        Pivot,

        /// <summary>Whether the object draws its geometry in the Rhino viewport.</summary>
        Preview,

        /// <summary>Whether the object is enabled (not locked).</summary>
        Enabled,

        /// <summary>A param's data mapping (flatten, graft), reverse and simplify.</summary>
        DataModifiers,

        /// <summary>A param's internally stored (persistent) values.</summary>
        PersistentData,

        /// <summary>An object added to the document.</summary>
        Add,

        /// <summary>An object removed from the document.</summary>
        Remove,
    }

    /// <summary>
    /// The host side of an <see cref="EditJournal{TTarget, TAction}"/>: it captures and undoes
    /// Grasshopper's undo actions and touches the document. The journal decides when each is
    /// called; the sink never decides anything.
    /// </summary>
    internal interface IUndoSink<TTarget, TAction>
    {
        Guid IdOf(TTarget target);

        /// <summary>The top-level object a target belongs to: a port's component, or the target itself.</summary>
        TTarget OwnerOf(TTarget target);

        /// <summary>
        /// The inputs whose source lists change when <paramref name="target"/> is rebuilt or
        /// removed: its own inputs, then every input its outputs feed.
        /// </summary>
        IEnumerable<TTarget> WiredInputs(TTarget target);

        /// <summary>Capture the undo action of <paramref name="kind"/> for the target's current state.</summary>
        TAction Capture(EditKind kind, TTarget target);

        /// <summary>Undo one action. Throws when it cannot.</summary>
        void Undo(TAction action);

        /// <summary>Remove an object whose add started but was never recorded, if it is in the document.</summary>
        void RemoveAdded(TTarget target);

        /// <summary>Pause whatever must not run while the edit is half applied (the solver).</summary>
        void BeginApply();

        /// <summary>Restore what <see cref="BeginApply"/> paused; after a rollback, also what the edit's side effects changed (the modified flag).</summary>
        void EndApply(bool rolledBack);

        /// <summary>Push one undo record holding <paramref name="actions"/>, oldest first.</summary>
        void Push(IReadOnlyList<TAction> actions);

        /// <summary>Mark an object as needing recomputation, without solving.</summary>
        void Expire(TTarget target);

        /// <summary>Ask for one solve of whatever is expired.</summary>
        void ScheduleSolve();
    }

    /// <summary>The steps of an edit's end that can fail, as replies name them.</summary>
    internal static class EditStep
    {
        /// <summary>Undoing one recorded change during a rollback.</summary>
        public const string Undo = "undo";

        /// <summary>Removing, at commit, an object whose add never finished.</summary>
        public const string DiscardUnfinishedAdd = "discardUnfinishedAdd";

        /// <summary>Pushing the undo record.</summary>
        public const string Push = "push";

        /// <summary>Restoring the solver switch (and, after a rollback, the modified flag).</summary>
        public const string Restore = "restore";

        /// <summary>Marking a changed object for recompute.</summary>
        public const string Expire = "expire";

        /// <summary>Scheduling the solve.</summary>
        public const string Schedule = "schedule";

        /// <summary>The host's own work after a rollback (logging, redrawing the canvas).</summary>
        public const string AfterRollback = "afterRollback";

        /// <summary>The host's own work after a commit (logging).</summary>
        public const string AfterCommit = "afterCommit";
    }

    /// <summary>
    /// One step of an edit's end that threw: an undo action that could not be undone (with the
    /// change's kind and object), or a restore, expire or schedule step (without them).
    /// </summary>
    internal sealed class EditFailure
    {
        public EditFailure(string step, string error, EditKind? kind = null, Guid? id = null)
        {
            Step = step ?? throw new ArgumentNullException(nameof(step));
            Error = error;
            Kind = kind;
            Id = id;
        }

        public string Step { get; }
        public string Error { get; }
        public EditKind? Kind { get; }
        public Guid? Id { get; }
    }

    /// <summary>What a rollback did: how many changes it undid and what failed along the way.</summary>
    internal sealed class RollbackReport
    {
        public RollbackReport(int undone, IReadOnlyList<EditFailure> failures)
        {
            Undone = undone;
            Failures = failures ?? throw new ArgumentNullException(nameof(failures));
        }

        /// <summary>Changes undone successfully.</summary>
        public int Undone { get; }

        /// <summary>Every step that threw, in the order it happened: undo actions first, then the steps after them.</summary>
        public IReadOnlyList<EditFailure> Failures { get; }

        /// <summary>
        /// True when every recorded change was undone, so the canvas content is as it was before
        /// the edit. A step after the undo that failed (restoring the solver, scheduling the solve)
        /// is still listed in <see cref="Failures"/>, but leaves the content restored.
        /// </summary>
        public bool Complete
        {
            get
            {
                foreach (var f in Failures)
                    if (f.Step == EditStep.Undo) return false;
                return true;
            }
        }

        /// <summary>This report with one more failure appended.</summary>
        public RollbackReport With(EditFailure failure)
            => new RollbackReport(Undone, new List<EditFailure>(Failures) { failure });
    }

    /// <summary>
    /// What a commit did. Either the edit stands (a record was pushed, or there was nothing to
    /// push), perhaps with <see cref="Warnings"/> from the steps after the push; or a step up to and
    /// including the push failed and the edit was rolled back instead (<see cref="Rollback"/> set).
    /// </summary>
    internal sealed class CommitOutcome
    {
        private CommitOutcome(bool pushed, bool scheduled, IReadOnlyList<EditFailure> warnings, EditFailure failure, RollbackReport rollback)
        {
            Pushed = pushed;
            Scheduled = scheduled;
            Warnings = warnings;
            Failure = failure;
            Rollback = rollback;
        }

        public static CommitOutcome Committed(bool pushed, bool scheduled, IReadOnlyList<EditFailure> warnings)
            => new CommitOutcome(pushed, scheduled, warnings ?? Array.Empty<EditFailure>(), null, null);

        public static CommitOutcome RolledBack(EditFailure failure, RollbackReport rollback)
            => new CommitOutcome(false, false, Array.Empty<EditFailure>(),
                failure ?? throw new ArgumentNullException(nameof(failure)),
                rollback ?? throw new ArgumentNullException(nameof(rollback)));

        /// <summary>True when one undo record was pushed. Decided here and nowhere else.</summary>
        public bool Pushed { get; }

        /// <summary>True when a solve was asked for and the request did not throw.</summary>
        public bool Scheduled { get; }

        /// <summary>Steps after the push that threw. The edit stands; these say what to check.</summary>
        public IReadOnlyList<EditFailure> Warnings { get; }

        /// <summary>The step that threw before the record was pushed; null when the edit stands.</summary>
        public EditFailure Failure { get; }

        /// <summary>The rollback that a failed commit ran instead; null when the edit stands.</summary>
        public RollbackReport Rollback { get; }
    }

    /// <summary>The lifecycle of one transaction, as <see cref="EditScope{TSession}"/> drives it.</summary>
    internal interface IEditJournal
    {
        void Begin();
        CommitOutcome Commit();
        RollbackReport Rollback();
    }

    /// <summary>
    /// The ordering rules of one edit transaction, kept free of Grasshopper so they hold for every
    /// tool by construction and are tested through a fake sink:
    /// <list type="bullet">
    /// <item>an undo action is captured before its mutation (every recorder is called before the change it covers);</item>
    /// <item>one action per (kind, object): the first capture holds the state from before the transaction;</item>
    /// <item>no action for a change to an object added in the same transaction, since undoing the add removes it;</item>
    /// <item>an add is tracked before it starts, so a rollback removes an object whose add threw partway;</item>
    /// <item>a rebuilt or removed object's wires are recorded before the object, so undo restores them after it;</item>
    /// <item>commit pushes one record and skips an empty one, and expires each object once; a failure up to the push rolls back, one after it is a warning;</item>
    /// <item>rollback undoes in reverse, carries on past a step that fails, and reports every failure.</item>
    /// </list>
    /// Single-threaded: a transaction lives inside one UI-thread call.
    /// </summary>
    internal sealed class EditJournal<TTarget, TAction> : IEditJournal where TTarget : class
    {
        private enum Phase { NotStarted, Applying, Committed, RolledBack }

        /// <summary>One undoable step, oldest first. A pending add has no action yet.</summary>
        private sealed class Entry
        {
            public EditKind Kind;
            public TTarget Target;
            public Guid Id;
            public TAction Action;
            public bool Pending;
        }

        private readonly IUndoSink<TTarget, TAction> _sink;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<(EditKind, Guid)> _recorded = new HashSet<(EditKind, Guid)>();
        private readonly HashSet<Guid> _added = new HashSet<Guid>();
        private readonly HashSet<Guid> _removed = new HashSet<Guid>();
        private readonly List<TTarget> _expire = new List<TTarget>();
        private readonly HashSet<Guid> _expireIds = new HashSet<Guid>();
        private Phase _phase = Phase.NotStarted;

        public EditJournal(IUndoSink<TTarget, TAction> sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>Start applying: the sink pauses the solver. Called once, before the first change.</summary>
        public void Begin()
        {
            if (_phase != Phase.NotStarted)
                throw new InvalidOperationException("This edit transaction has already begun.");
            _sink.BeginApply();
            _phase = Phase.Applying;
        }

        /// <summary>
        /// Record the undo action of <paramref name="kind"/> for <paramref name="target"/>. Call it
        /// before the change it covers. A repeat for the same (kind, object), a change to an object
        /// added in this transaction, and a change to one removed in it record nothing.
        /// <see cref="EditKind.Generic"/> first records the wires of every input it touches, so undo
        /// restores the object's ports before it reconnects them.
        /// </summary>
        public void Record(EditKind kind, TTarget target)
        {
            if (kind == EditKind.Add || kind == EditKind.Remove)
                throw new ArgumentException($"{kind} is recorded by {(kind == EditKind.Add ? nameof(Add) : nameof(RecordRemove))}, which pairs it with its mutation.", nameof(kind));
            if (target == null) throw new ArgumentNullException(nameof(target));
            EnsureApplying();

            if (IsRemoved(target)) return;

            if (kind == EditKind.Generic)
            {
                foreach (var input in _sink.WiredInputs(target))
                    Record(EditKind.Wires, input);
            }

            if (ExpiresSolution(kind))
                Expire(_sink.OwnerOf(target));

            if (IsAdded(target)) return;
            Take(kind, target);
        }

        /// <summary>
        /// Add an object: track it, run <paramref name="add"/>, then record the add. Tracking first
        /// means an <paramref name="add"/> that throws after inserting the object still leaves it
        /// known to the rollback, which removes it. An <paramref name="add"/> that returns false
        /// (the document refused the object) inserted nothing, so nothing is recorded and the object
        /// is forgotten: its id may be a pre-existing object's, which must not be mistaken for one
        /// this edit added. Returns whether the object was added.
        /// </summary>
        public bool Add(TTarget target, Func<bool> add)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (add == null) throw new ArgumentNullException(nameof(add));
            EnsureApplying();

            var id = _sink.IdOf(target);
            if (_added.Contains(id))
                throw new InvalidOperationException("This object was already added in this edit.");

            var entry = new Entry { Kind = EditKind.Add, Target = target, Id = id, Pending = true };
            _entries.Add(entry);
            _added.Add(id);
            bool wasRemoved = _removed.Remove(id);

            if (!add())
            {
                _entries.Remove(entry);
                _added.Remove(id);
                if (wasRemoved) _removed.Add(id);
                return false;
            }

            entry.Action = _sink.Capture(EditKind.Add, target);
            entry.Pending = false;
            Expire(target);
            return true;
        }

        /// <summary>
        /// Record the removal of <paramref name="target"/>; call it before removing the object. Its
        /// wires (its inputs and every input it feeds) are recorded first, so undo brings the object
        /// back before restoring them, whatever order several removed objects come back in.
        /// Removing an object added in this transaction drops its add instead: the net change is nothing.
        /// </summary>
        public void RecordRemove(TTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            EnsureApplying();

            var id = _sink.IdOf(target);
            if (_removed.Contains(id)) return;

            foreach (var input in _sink.WiredInputs(target))
                Record(EditKind.Wires, input);

            if (_added.Remove(id))
                _entries.RemoveAll(e => e.Kind == EditKind.Add && e.Id == id);
            else
                Take(EditKind.Remove, target);

            _removed.Add(id);
            DropExpire(id);
        }

        /// <summary>
        /// Recompute <paramref name="target"/> when the edit commits. Recording a change that affects
        /// the solution (wires, a generic change, enabled, data modifiers, stored values, an add)
        /// already expires the changed object; call this for anything else that must recompute.
        /// </summary>
        public void Expire(TTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            EnsureApplying();
            var id = _sink.IdOf(target);
            if (_removed.Contains(id)) return;
            if (_expireIds.Add(id))
                _expire.Add(target);
        }

        /// <summary>
        /// Finish the edit. Up to and including the push, a step that throws rolls the edit back
        /// and the outcome says so: a change left in place without its undo record is exactly the
        /// unrecorded edit that breaks Ctrl+Z for every recorded one. First, an add whose action was
        /// never recorded (its add threw and the caller carried on) is removed, as a rollback would;
        /// then one record holding every action is pushed (none when nothing was recorded). Once
        /// pushed, the edit stands: restoring the solver, expiring each changed object once, and
        /// scheduling one solve each run even when an earlier one throws, and a step that throws
        /// becomes a warning rather than a failure.
        /// </summary>
        public CommitOutcome Commit()
        {
            EnsureApplying();

            var step = EditStep.DiscardUnfinishedAdd;
            List<TAction> actions;
            try
            {
                foreach (var pending in _entries.FindAll(e => e.Pending))
                {
                    _sink.RemoveAdded(pending.Target);
                    _entries.Remove(pending);
                    _added.Remove(pending.Id);
                    DropExpire(pending.Id);
                }

                actions = new List<TAction>(_entries.Count);
                foreach (var e in _entries) actions.Add(e.Action);

                step = EditStep.Push;
                if (actions.Count > 0)
                    _sink.Push(actions);
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- commit boundary: nothing is recorded for undo until the push succeeds, so whatever threw before it rolls the edit back
            {
                return CommitOutcome.RolledBack(new EditFailure(step, ex.Message), Rollback());
            }

            _phase = Phase.Committed;
            bool pushed = actions.Count > 0;
            var warnings = new List<EditFailure>();

            Attempt(warnings, EditStep.Restore, () => _sink.EndApply(rolledBack: false));
            foreach (var target in _expire)
                Attempt(warnings, EditStep.Expire, () => _sink.Expire(target), _sink.IdOf(target));
            bool scheduled = (pushed || _expire.Count > 0)
                && Attempt(warnings, EditStep.Schedule, () => _sink.ScheduleSolve());

            return CommitOutcome.Committed(pushed, scheduled, warnings);
        }

        /// <summary>
        /// Undo everything this transaction did, newest first, carrying on past a change that
        /// cannot be undone; then restore the solver and the modified flag, recompute what survived,
        /// and schedule one solve when anything was undone or expired. Nothing is pushed, so the
        /// undo and redo stacks are untouched. Every step that throws is in the report, and the
        /// steps after it still run: the report is never lost to a late failure.
        /// </summary>
        public RollbackReport Rollback()
        {
            EnsureApplying();
            _phase = Phase.RolledBack;

            var failures = new List<EditFailure>();
            int undone = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (Attempt(failures, EditStep.Undo, () =>
                    {
                        if (e.Pending) _sink.RemoveAdded(e.Target);
                        else _sink.Undo(e.Action);
                    }, e.Id, e.Kind))
                {
                    undone++;
                }
            }

            Attempt(failures, EditStep.Restore, () => _sink.EndApply(rolledBack: true));

            // Undoing a change leaves its object needing a solve; objects this edit added are gone.
            var survivors = _expire.FindAll(t => !_added.Contains(_sink.IdOf(t)));
            foreach (var target in survivors)
                Attempt(failures, EditStep.Expire, () => _sink.Expire(target), _sink.IdOf(target));
            if (undone > 0 || survivors.Count > 0)
                Attempt(failures, EditStep.Schedule, () => _sink.ScheduleSolve());

            return new RollbackReport(undone, failures);
        }

        /// <summary>
        /// Run one step of an edit's end; when it throws, note the failure and return false so the
        /// steps after it still run.
        /// </summary>
        private static bool Attempt(List<EditFailure> failures, string step, Action run, Guid? id = null, EditKind? kind = null)
        {
            try
            {
                run();
                return true;
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- edit-end boundary: a step that throws is reported, and the remaining restore, expire and schedule steps still run
            {
                failures.Add(new EditFailure(step, ex.Message, kind, id));
                return false;
            }
        }

        private void DropExpire(Guid id)
        {
            if (_expireIds.Remove(id))
                _expire.RemoveAll(t => _sink.IdOf(t) == id);
        }

        /// <summary>Whether changing <paramref name="kind"/> makes the object's outputs stale.</summary>
        internal static bool ExpiresSolution(EditKind kind)
        {
            switch (kind)
            {
                case EditKind.Wires:
                case EditKind.Generic:
                case EditKind.Enabled:
                case EditKind.DataModifiers:
                case EditKind.PersistentData:
                case EditKind.Add:
                    return true;
                default:
                    return false;
            }
        }

        private void Take(EditKind kind, TTarget target)
        {
            var id = _sink.IdOf(target);
            if (_recorded.Contains((kind, id))) return;
            // Marked only once captured: a capture that throws records nothing, so a retry can.
            var action = _sink.Capture(kind, target);
            _recorded.Add((kind, id));
            _entries.Add(new Entry { Kind = kind, Target = target, Id = id, Action = action });
        }

        private bool IsAdded(TTarget target)
            => _added.Contains(_sink.IdOf(target)) || _added.Contains(_sink.IdOf(_sink.OwnerOf(target)));

        private bool IsRemoved(TTarget target)
            => _removed.Contains(_sink.IdOf(target)) || _removed.Contains(_sink.IdOf(_sink.OwnerOf(target)));

        private void EnsureApplying()
        {
            switch (_phase)
            {
                case Phase.Applying: return;
                case Phase.NotStarted: throw new InvalidOperationException("This edit transaction has not begun.");
                default: throw new InvalidOperationException($"This edit transaction is already {(_phase == Phase.Committed ? "committed" : "rolled back")}.");
            }
        }
    }
}
