// Portions of this file are adapted from Hyperceps (src/Cordyceps/Tools/Batch/BatchOps.cs,
// ApplyContext, AddOp and DeleteOp; BatchRunner.cs, Apply and Rollback), under the MIT License:
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
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core.Edits
{
    /// <summary>
    /// One Cordyceps call's changes to a Grasshopper document, applied as one undo step. Obtained
    /// only through <see cref="GrasshopperContext.Edit"/>, which runs the call's body inside it on
    /// the UI thread.
    ///
    /// <para>The rule for a body: call the recorder for a change <em>before</em> making it
    /// (<see cref="RecordWires"/> then <c>AddSource</c>), and add or remove objects through
    /// <see cref="AddObject"/> and <see cref="RemoveObject"/>, which record in the right order
    /// themselves. Recording twice is harmless, and changes to objects added in the same call need
    /// no record. Validate every input before the first change: a refused id must never reach a
    /// mutation.</para>
    ///
    /// <para>What happens around the body: the solver is paused for its whole run (a value list's
    /// or panel's own change handler would otherwise solve a half-applied edit). If the body
    /// returns, the changes are pushed as one undo record named for the call, the changed objects
    /// are expired, and one solve is scheduled through <see cref="SolveCoordinator"/>. If it
    /// throws, every recorded change is undone, newest first, and the solver and the document's
    /// modified flag are restored, so the canvas is as it was and the undo stacks are untouched.</para>
    ///
    /// <para>The ordering rules live in host-free <see cref="EditJournal{TTarget, TAction}"/>;
    /// this class is the Grasshopper side of them.</para>
    /// </summary>
    internal sealed class EditTransaction : IUndoSink<IGH_DocumentObject, IGH_UndoAction>, IEditSession
    {
        // Every transaction runs on the Rhino UI thread, one at a time, so one scope serves the
        // whole host without locking. It decides joining, rollback and every reply.
        private static readonly EditScope<EditTransaction> Scope = new EditScope<EditTransaction>();

        private readonly EditJournal<IGH_DocumentObject, IGH_UndoAction> _journal;
        private bool _paused;
        private bool _wasEnabled;
        private bool _wasModified;
        private SolveScheduling _scheduling;

        private EditTransaction(GH_Document doc, string name)
        {
            Document = doc;
            Name = name;
            _journal = new EditJournal<IGH_DocumentObject, IGH_UndoAction>(this);
        }

        /// <summary>The document every change in this transaction belongs to.</summary>
        public GH_Document Document { get; }

        /// <summary>The undo record's name, returned to the caller as <c>undo</c>.</summary>
        public string Name { get; }

        // ------------------------------------------------------------------ recorders (call BEFORE the change)

        /// <summary>Before changing an input's sources (<c>AddSource</c>, <c>RemoveSource</c>, <c>RemoveAllSources</c>, <c>ReplaceSource</c>).</summary>
        public void RecordWires(IGH_Param input) => _journal.Record(EditKind.Wires, input);

        /// <summary>
        /// Before a change no dedicated event covers: a slider's range or value, a toggle, panel
        /// text, value-list items, a group's members or colour, a component's ports or script
        /// source. Archives the whole object, and first the wires of its inputs and of every input
        /// its outputs feed, so a port the change removes gets its wires back on undo.
        /// </summary>
        public void RecordGeneric(IGH_DocumentObject obj) => _journal.Record(EditKind.Generic, obj);

        /// <summary>Before setting <c>NickName</c>.</summary>
        public void RecordNickName(IGH_DocumentObject obj) => _journal.Record(EditKind.NickName, obj);

        /// <summary>Before moving an object (<c>Attributes.Pivot</c>). For a group's members, call it per member.</summary>
        public void RecordPivot(IGH_DocumentObject obj) => _journal.Record(EditKind.Pivot, obj);

        /// <summary>Before setting <c>IGH_PreviewObject.Hidden</c>.</summary>
        public void RecordPreview(IGH_ActiveObject obj) => _journal.Record(EditKind.Preview, obj);

        /// <summary>Before setting <c>Locked</c> (enable / disable).</summary>
        public void RecordEnabled(IGH_ActiveObject obj) => _journal.Record(EditKind.Enabled, obj);

        /// <summary>Before setting a param's <c>DataMapping</c>, <c>Reverse</c> or <c>Simplify</c>.</summary>
        public void RecordDataModifiers(IGH_Param param) => _journal.Record(EditKind.DataModifiers, param);

        /// <summary>
        /// Before changing a param's internally stored values (<c>PersistentData</c>,
        /// <c>SetPersistentData</c>). Throws when the param stores none; check
        /// <see cref="UndoEvents.StoredType"/> while validating.
        /// </summary>
        public void RecordPersistentData(IGH_Param param) => _journal.Record(EditKind.PersistentData, param);

        // ------------------------------------------------------------------ structural changes (record themselves)

        /// <summary>
        /// Add <paramref name="obj"/> to the document: tracked before the add, so a rollback removes
        /// it even when the add throws partway, and recorded after it. Set it up (position, settings,
        /// values) before or after; changes to an object added in this call need no record. The
        /// document's own solve on add is not run; the commit schedules one.
        /// </summary>
        public void AddObject(IGH_DocumentObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (!_journal.Add(obj, () => Document.AddObject(obj, false)))
                throw new EditRollbackException($"Grasshopper refused to add '{obj.NickName}' to the document (an object with its id may already be there).");
        }

        /// <summary>
        /// Remove <paramref name="obj"/> from the document. Its wires (its inputs and every input it
        /// feeds) are recorded first and the object second, so undo brings it back and then
        /// reconnects it. The objects it fed are recomputed once, at commit, not once per removal.
        /// </summary>
        public void RemoveObject(IGH_DocumentObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            if (!ReferenceEquals(obj.OnPingDocument(), Document))
                throw new InvalidOperationException($"'{obj.NickName}' is not in the document this edit changes.");

            _journal.RecordRemove(obj);
            Document.RemoveObject(obj, false);
        }

        /// <summary>
        /// Recompute <paramref name="obj"/> when the edit commits. The recorders for wires, generic
        /// changes, enabled, data modifiers and stored values, and <see cref="AddObject"/>, already
        /// do this for the object they change; call it for anything else that must recompute.
        /// </summary>
        public void Expire(IGH_DocumentObject obj) => _journal.Expire(obj);

        // ------------------------------------------------------------------ running a transaction

        /// <summary>
        /// Run <paramref name="body"/> as one edit of <paramref name="doc"/>. Must be called on the
        /// UI thread (<see cref="GrasshopperContext.Edit"/> sees to it). The body returns its reply
        /// as a JSON object; the edit returns that reply plus <c>undo</c> when a record was pushed,
        /// <c>solve</c> when the solve was refused, and <c>warning</c> when a step after the push
        /// failed. A body that throws, or a commit that fails before its push, is rolled back and
        /// answered with an error and <c>rolledBack</c>. Every one of those decisions is made by
        /// host-free <see cref="EditScope{TSession}"/>.
        /// </summary>
        internal static string Run(GH_Document doc, string name, Func<EditTransaction, string> body)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            return Scope.Run(doc, name, () => new EditTransaction(doc, name), body);
        }

        // ------------------------------------------------------------------ IEditSession (called by the scope)

        IEditJournal IEditSession.Journal => _journal;

        JObject IEditSession.SolveRefusal => _scheduling?.Refusal != null ? _scheduling.ToField() : null;

        void IEditSession.AfterRollback(RollbackReport report, string reason, Exception cause)
        {
            if (cause != null)
                DebugLog.Error($"Edit '{Name}' failed and is rolled back: {cause}");
            else
                DebugLog.Info($"Edit '{Name}' rolled back: {reason}");
            foreach (var f in report.Failures)
                DebugLog.Error($"Edit '{Name}' rollback: {f.Step} failed{(f.Kind.HasValue ? $" for {EditReplies.KindName(f.Kind.Value)}" : "")}{(f.Id.HasValue ? $" on {f.Id}" : "")}: {f.Error}");
            Instances.ActiveCanvas?.Invalidate();
        }

        void IEditSession.AfterCommit(CommitOutcome outcome)
        {
            foreach (var w in outcome.Warnings)
                DebugLog.Warn($"Edit '{Name}' stands, but {w.Step} failed{(w.Id.HasValue ? $" on {w.Id}" : "")}: {w.Error}");
        }

        void IEditSession.EnsureEnded() => EndApply(rolledBack: false);

        // ------------------------------------------------------------------ IUndoSink (called by the journal)

        Guid IUndoSink<IGH_DocumentObject, IGH_UndoAction>.IdOf(IGH_DocumentObject target) => target.InstanceGuid;

        IGH_DocumentObject IUndoSink<IGH_DocumentObject, IGH_UndoAction>.OwnerOf(IGH_DocumentObject target)
            => target.Attributes?.GetTopLevel?.DocObject ?? target;

        IEnumerable<IGH_DocumentObject> IUndoSink<IGH_DocumentObject, IGH_UndoAction>.WiredInputs(IGH_DocumentObject target)
        {
            var inputs = new List<IGH_DocumentObject>(UndoEvents.Inputs(target));
            foreach (var output in UndoEvents.Outputs(target))
                inputs.AddRange(output.Recipients.ToList());
            return inputs;
        }

        IGH_UndoAction IUndoSink<IGH_DocumentObject, IGH_UndoAction>.Capture(EditKind kind, IGH_DocumentObject target)
            => UndoEvents.Create(Document, kind, target);

        void IUndoSink<IGH_DocumentObject, IGH_UndoAction>.Undo(IGH_UndoAction action) => action.Undo(Document);

        void IUndoSink<IGH_DocumentObject, IGH_UndoAction>.RemoveAdded(IGH_DocumentObject target)
        {
            if (Document.Objects.Contains(target))
                Document.RemoveObject(target, false);
        }

        void IUndoSink<IGH_DocumentObject, IGH_UndoAction>.BeginApply()
        {
            _wasEnabled = Document.Enabled;
            _wasModified = Document.IsModified;
            _paused = true;
            Document.Enabled = false;
        }

        void IUndoSink<IGH_DocumentObject, IGH_UndoAction>.EndApply(bool rolledBack) => EndApply(rolledBack);

        private void EndApply(bool rolledBack)
        {
            // Unpaused before the document is touched: a restore that throws is reported once by
            // the journal, not retried by EnsureEnded.
            bool wasPaused = _paused;
            _paused = false;
            if (wasPaused)
                Document.Enabled = _wasEnabled;
            if (rolledBack && wasPaused)
                Document.IsModified = _wasModified;
        }

        void IUndoSink<IGH_DocumentObject, IGH_UndoAction>.Push(IReadOnlyList<IGH_UndoAction> actions)
            => Document.UndoServer.PushUndoRecord(new GH_UndoRecord(Name, actions));

        void IUndoSink<IGH_DocumentObject, IGH_UndoAction>.Expire(IGH_DocumentObject target)
        {
            if (target is IGH_ActiveObject active)
                active.ExpireSolution(false);
        }

        void IUndoSink<IGH_DocumentObject, IGH_UndoAction>.ScheduleSolve()
            => _scheduling = SolveCoordinator.Schedule(Document, expireAll: false);
    }
}
