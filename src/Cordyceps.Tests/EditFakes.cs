using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core.Edits;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Fakes for the edit transaction tests: an object with one piece of state, an undo action that
    /// snapshots it, a sink that logs every call and can be told to throw at any step, and a session
    /// for <see cref="EditScope{TSession}"/>.
    /// </summary>
    internal sealed class FakeObj
    {
        public FakeObj(string name, FakeObj owner = null)
        {
            Name = name;
            Owner = owner;
        }

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; }
        public FakeObj Owner { get; }
        public string Value { get; set; } = "original";
        public bool InDocument { get; set; } = true;
        public List<FakeObj> WiredInputs { get; } = new List<FakeObj>();
    }

    internal sealed class FakeAct
    {
        public EditKind Kind;
        public FakeObj Target;
        public string Snapshot;
    }

    internal sealed class FakeSink : IUndoSink<FakeObj, FakeAct>
    {
        public List<string> Log { get; } = new List<string>();
        public List<IReadOnlyList<FakeAct>> Pushed { get; } = new List<IReadOnlyList<FakeAct>>();
        public List<FakeObj> Expired { get; } = new List<FakeObj>();
        public int Schedules { get; private set; }
        public HashSet<FakeObj> FailUndoOf { get; } = new HashSet<FakeObj>();
        public HashSet<FakeObj> FailCaptureOf { get; } = new HashSet<FakeObj>();
        public HashSet<FakeObj> FailExpireOf { get; } = new HashSet<FakeObj>();
        public HashSet<FakeObj> FailRemoveAddedOf { get; } = new HashSet<FakeObj>();

        /// <summary>Steps that throw: <c>push</c>, <c>schedule</c>, <c>end</c> (either kind) or <c>end-rollback</c>.</summary>
        public HashSet<string> FailSteps { get; } = new HashSet<string>();

        public Guid IdOf(FakeObj target) => target.Id;
        public FakeObj OwnerOf(FakeObj target) => target.Owner ?? target;
        public IEnumerable<FakeObj> WiredInputs(FakeObj target) => target.WiredInputs;

        public FakeAct Capture(EditKind kind, FakeObj target)
        {
            if (FailCaptureOf.Contains(target)) throw new InvalidOperationException($"cannot capture {target.Name}");
            Log.Add($"capture {kind} {target.Name}={target.Value}");
            return new FakeAct { Kind = kind, Target = target, Snapshot = target.Value };
        }

        public void Undo(FakeAct action)
        {
            Log.Add($"undo {action.Kind} {action.Target.Name}");
            if (FailUndoOf.Contains(action.Target)) throw new InvalidOperationException($"cannot undo {action.Target.Name}");
            switch (action.Kind)
            {
                case EditKind.Add: action.Target.InDocument = false; break;
                case EditKind.Remove: action.Target.InDocument = true; break;
                default: action.Target.Value = action.Snapshot; break;
            }
        }

        public void RemoveAdded(FakeObj target)
        {
            Log.Add($"remove-added {target.Name}");
            if (FailRemoveAddedOf.Contains(target)) throw new InvalidOperationException($"cannot remove {target.Name}");
            target.InDocument = false;
        }

        public void BeginApply() => Log.Add("begin");

        public void EndApply(bool rolledBack)
        {
            Log.Add($"end rolledBack={rolledBack}");
            if (FailSteps.Contains("end") || (rolledBack && FailSteps.Contains("end-rollback")))
                throw new InvalidOperationException("cannot restore");
        }

        public void Push(IReadOnlyList<FakeAct> actions)
        {
            Log.Add($"push {string.Join(",", actions.Select(a => $"{a.Kind}:{a.Target.Name}"))}");
            if (FailSteps.Contains("push")) throw new InvalidOperationException("cannot push");
            Pushed.Add(actions.ToList());
        }

        public void Expire(FakeObj target)
        {
            Log.Add($"expire {target.Name}");
            if (FailExpireOf.Contains(target)) throw new InvalidOperationException($"cannot expire {target.Name}");
            Expired.Add(target);
        }

        public void ScheduleSolve()
        {
            Log.Add("schedule");
            if (FailSteps.Contains("schedule")) throw new InvalidOperationException("cannot schedule");
            Schedules++;
        }

        /// <summary>Grasshopper's undo of a whole pushed record: actions in reverse.</summary>
        public void UndoRecord(IReadOnlyList<FakeAct> record)
        {
            for (int i = record.Count - 1; i >= 0; i--) Undo(record[i]);
        }
    }

    internal sealed class FakeSession : IEditSession
    {
        public FakeSession(FakeSink sink)
        {
            Sink = sink;
            Edits = new EditJournal<FakeObj, FakeAct>(sink);
        }

        public FakeSink Sink { get; }
        public EditJournal<FakeObj, FakeAct> Edits { get; }
        public IEditJournal Journal => Edits;
        public JObject SolveRefusal { get; set; }
        public bool ThrowAfterRollback { get; set; }
        public bool ThrowAfterCommit { get; set; }
        public Exception RollbackCause { get; private set; }
        public int RollbacksSeen { get; private set; }
        public int EnsureEndedCalls { get; private set; }

        public void AfterRollback(RollbackReport report, string reason, Exception cause)
        {
            RollbacksSeen++;
            RollbackCause = cause;
            if (ThrowAfterRollback) throw new InvalidOperationException("cannot redraw");
        }

        public void AfterCommit(CommitOutcome outcome)
        {
            if (ThrowAfterCommit) throw new InvalidOperationException("cannot log");
        }

        public void EnsureEnded() => EnsureEndedCalls++;
    }
}
