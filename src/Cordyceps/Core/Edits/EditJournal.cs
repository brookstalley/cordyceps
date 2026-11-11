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

    /// <summary>One change a rollback could not undo.</summary>
    internal sealed class RollbackFailure
    {
        public RollbackFailure(EditKind kind, Guid id, string error)
        {
            Kind = kind;
            Id = id;
            Error = error;
        }

        public EditKind Kind { get; }
        public Guid Id { get; }
        public string Error { get; }
    }

    /// <summary>What a rollback did: how many changes it undid and which it could not.</summary>
    internal sealed class RollbackReport
    {
        public RollbackReport(int undone, IReadOnlyList<RollbackFailure> failures)
        {
            Undone = undone;
            Failures = failures;
        }

        /// <summary>Changes undone successfully.</summary>
        public int Undone { get; }

        public IReadOnlyList<RollbackFailure> Failures { get; }

        /// <summary>True when every change was undone, so the document is as it was before the edit.</summary>
        public bool Complete => Failures.Count == 0;
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
    /// <item>commit pushes one record and skips an empty one, and expires each object once;</item>
    /// <item>rollback undoes in reverse, carries on past an action that fails, and reports every failure.</item>
    /// </list>
    /// Single-threaded: a transaction lives inside one UI-thread call.
    /// </summary>
    internal sealed class EditJournal<TTarget, TAction> where TTarget : class
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

        /// <summary>True when the transaction has recorded at least one undoable change.</summary>
        public bool HasChanges
        {
            get
            {
                foreach (var e in _entries)
                    if (!e.Pending) return true;
                return false;
            }
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
        /// known to the rollback, which removes it.
        /// </summary>
        public void Add(TTarget target, Action add)
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
            _removed.Remove(id);

            add();

            entry.Action = _sink.Capture(EditKind.Add, target);
            entry.Pending = false;
            Expire(target);
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
            if (_expireIds.Remove(id))
                _expire.RemoveAll(t => _sink.IdOf(t) == id);
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
        /// Finish the edit: restore the solver, push one record holding every action (none when
        /// nothing was recorded), expire each object once, then schedule one solve. An add whose
        /// action was never recorded (its add threw and the caller carried on) is removed first, as
        /// a rollback would: it never becomes part of the record.
        /// </summary>
        public void Commit()
        {
            EnsureApplying();

            foreach (var pending in _entries.FindAll(e => e.Pending))
            {
                _sink.RemoveAdded(pending.Target);
                _entries.Remove(pending);
                _added.Remove(pending.Id);
                if (_expireIds.Remove(pending.Id))
                    _expire.RemoveAll(t => _sink.IdOf(t) == pending.Id);
            }

            _phase = Phase.Committed;
            _sink.EndApply(rolledBack: false);

            var actions = new List<TAction>(_entries.Count);
            foreach (var e in _entries) actions.Add(e.Action);
            if (actions.Count > 0)
                _sink.Push(actions);

            foreach (var target in _expire)
                _sink.Expire(target);

            if (actions.Count > 0 || _expire.Count > 0)
                _sink.ScheduleSolve();
        }

        /// <summary>
        /// Undo everything this transaction did, newest first, carrying on past a change that
        /// cannot be undone; then restore the solver and the modified flag, and recompute what
        /// survived. Nothing is pushed, so the undo and redo stacks are untouched.
        /// </summary>
        public RollbackReport Rollback()
        {
            EnsureApplying();
            _phase = Phase.RolledBack;

            var failures = new List<RollbackFailure>();
            int undone = 0;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                try
                {
                    if (e.Pending) _sink.RemoveAdded(e.Target);
                    else _sink.Undo(e.Action);
                    undone++;
                }
                catch (Exception ex) // prawduct:allow prawduct/broad-except -- rollback boundary: an action that cannot be undone is reported, and the rest are still undone
                {
                    failures.Add(new RollbackFailure(e.Kind, e.Id, ex.Message));
                }
            }

            _sink.EndApply(rolledBack: true);

            // Undoing a change leaves its object needing a solve; objects this edit added are gone.
            var survivors = _expire.FindAll(t => !_added.Contains(_sink.IdOf(t)));
            foreach (var target in survivors)
                _sink.Expire(target);
            if (_entries.Count > 0)
                _sink.ScheduleSolve();

            return new RollbackReport(undone, failures);
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
