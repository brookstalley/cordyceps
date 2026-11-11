using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core.Edits;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="EditJournal{TTarget, TAction}"/>: the ordering rules of an edit
    /// transaction, driven through a fake undo sink whose actions snapshot a fake object's state
    /// and restore it on undo, so "recorded before the mutation" and "rolled back" are observable.
    /// </summary>
    public class EditJournalTests
    {
        // ---------------------------------------------------------------- fakes

        private sealed class Obj
        {
            public Obj(string name, Obj owner = null)
            {
                Name = name;
                Owner = owner;
            }

            public Guid Id { get; } = Guid.NewGuid();
            public string Name { get; }
            public Obj Owner { get; }
            public string Value { get; set; } = "original";
            public bool InDocument { get; set; } = true;
            public List<Obj> WiredInputs { get; } = new List<Obj>();
        }

        private sealed class Act
        {
            public EditKind Kind;
            public Obj Target;
            public string Snapshot;
        }

        private sealed class FakeSink : IUndoSink<Obj, Act>
        {
            public List<string> Log { get; } = new List<string>();
            public List<IReadOnlyList<Act>> Pushed { get; } = new List<IReadOnlyList<Act>>();
            public List<Obj> Expired { get; } = new List<Obj>();
            public int Schedules { get; private set; }
            public HashSet<Obj> FailUndoOf { get; } = new HashSet<Obj>();
            public HashSet<Obj> FailCaptureOf { get; } = new HashSet<Obj>();

            public Guid IdOf(Obj target) => target.Id;
            public Obj OwnerOf(Obj target) => target.Owner ?? target;
            public IEnumerable<Obj> WiredInputs(Obj target) => target.WiredInputs;

            public Act Capture(EditKind kind, Obj target)
            {
                if (FailCaptureOf.Contains(target)) throw new InvalidOperationException($"cannot capture {target.Name}");
                Log.Add($"capture {kind} {target.Name}={target.Value}");
                return new Act { Kind = kind, Target = target, Snapshot = target.Value };
            }

            public void Undo(Act action)
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

            public void RemoveAdded(Obj target)
            {
                Log.Add($"remove-added {target.Name}");
                target.InDocument = false;
            }

            public void BeginApply() => Log.Add("begin");
            public void EndApply(bool rolledBack) => Log.Add($"end rolledBack={rolledBack}");

            public void Push(IReadOnlyList<Act> actions)
            {
                Log.Add($"push {string.Join(",", actions.Select(a => $"{a.Kind}:{a.Target.Name}"))}");
                Pushed.Add(actions.ToList());
            }

            public void Expire(Obj target)
            {
                Log.Add($"expire {target.Name}");
                Expired.Add(target);
            }

            public void ScheduleSolve()
            {
                Log.Add("schedule");
                Schedules++;
            }
        }

        private static (FakeSink Sink, EditJournal<Obj, Act> Journal) Started()
        {
            var sink = new FakeSink();
            var journal = new EditJournal<Obj, Act>(sink);
            journal.Begin();
            return (sink, journal);
        }

        // Grasshopper's undo of a whole pushed record: actions in reverse.
        private static void UndoRecord(FakeSink sink, IReadOnlyList<Act> record)
        {
            for (int i = record.Count - 1; i >= 0; i--) sink.Undo(record[i]);
        }

        public static IEnumerable<object[]> RecordedKinds => new[]
        {
            EditKind.Wires, EditKind.Generic, EditKind.NickName, EditKind.Pivot, EditKind.Preview,
            EditKind.Enabled, EditKind.DataModifiers, EditKind.PersistentData,
        }.Select(k => new object[] { k });

        // ---------------------------------------------------------------- record before mutate

        [Theory]
        [MemberData(nameof(RecordedKinds))]
        public void Record_CapturesTheStateBeforeTheMutation_AndCommitPushesIt(EditKind kind)
        {
            var (sink, journal) = Started();
            var obj = new Obj("A");

            journal.Record(kind, obj);
            obj.Value = "changed";
            journal.Commit();

            var record = Assert.Single(sink.Pushed);
            var action = Assert.Single(record, a => a.Target == obj);
            Assert.Equal(kind, action.Kind);
            Assert.Equal("original", action.Snapshot);

            // Ctrl+Z on the pushed record gives the original back.
            UndoRecord(sink, record);
            Assert.Equal("original", obj.Value);
        }

        [Theory]
        [MemberData(nameof(RecordedKinds))]
        public void Record_ThenRollback_RestoresTheStateBeforeTheMutation(EditKind kind)
        {
            var (sink, journal) = Started();
            var obj = new Obj("A");

            journal.Record(kind, obj);
            obj.Value = "changed";
            var report = journal.Rollback();

            Assert.True(report.Complete);
            Assert.Equal("original", obj.Value);
            Assert.Empty(sink.Pushed);
        }

        [Theory]
        [InlineData(EditKind.Add)]
        [InlineData(EditKind.Remove)]
        public void Record_RefusesAddAndRemove_WhichArePairedWithTheirMutation(EditKind kind)
        {
            var (_, journal) = Started();
            Assert.Throws<ArgumentException>(() => journal.Record(kind, new Obj("A")));
        }

        [Fact]
        public void Record_WhoseCaptureThrows_RecordsNothing_AndARetryRecords()
        {
            var (sink, journal) = Started();
            var obj = new Obj("A");
            sink.FailCaptureOf.Add(obj);

            Assert.Throws<InvalidOperationException>(() => journal.Record(EditKind.NickName, obj));
            Assert.False(journal.HasChanges);

            sink.FailCaptureOf.Clear();
            journal.Record(EditKind.NickName, obj);
            Assert.True(journal.HasChanges);
        }

        // ---------------------------------------------------------------- one record per (kind, id)

        [Fact]
        public void Record_SameKindAndObjectTwice_KeepsOnlyTheFirst_SoUndoReturnsThePreTransactionState()
        {
            var (sink, journal) = Started();
            var obj = new Obj("A");

            journal.Record(EditKind.Wires, obj);
            obj.Value = "first";
            journal.Record(EditKind.Wires, obj);
            obj.Value = "second";
            journal.Commit();

            var record = Assert.Single(sink.Pushed);
            var action = Assert.Single(record);
            Assert.Equal("original", action.Snapshot);
            UndoRecord(sink, record);
            Assert.Equal("original", obj.Value);
        }

        [Fact]
        public void Record_DifferentKindsOrDifferentObjects_AreEachRecorded()
        {
            var (sink, journal) = Started();
            var a = new Obj("A");
            var b = new Obj("B");

            journal.Record(EditKind.NickName, a);
            journal.Record(EditKind.Pivot, a);
            journal.Record(EditKind.NickName, b);
            journal.Commit();

            Assert.Equal(new[] { "NickName:A", "Pivot:A", "NickName:B" },
                Assert.Single(sink.Pushed).Select(x => $"{x.Kind}:{x.Target.Name}"));
        }

        // ---------------------------------------------------------------- objects added in the transaction

        [Fact]
        public void Record_OnAnObjectAddedInTheTransaction_OrOnItsPort_RecordsNothing()
        {
            var (sink, journal) = Started();
            var added = new Obj("New") { InDocument = false };
            var port = new Obj("New.in", owner: added);

            journal.Add(added, () => added.InDocument = true);
            journal.Record(EditKind.NickName, added);
            journal.Record(EditKind.Generic, added);
            journal.Record(EditKind.Wires, port);
            journal.Record(EditKind.PersistentData, port);
            journal.Commit();

            var record = Assert.Single(sink.Pushed);
            var only = Assert.Single(record);
            Assert.Equal(EditKind.Add, only.Kind);
            Assert.DoesNotContain(sink.Log, l => l.StartsWith("capture NickName") || l.StartsWith("capture Wires") || l.StartsWith("capture PersistentData"));
        }

        [Fact]
        public void Add_TracksBeforeTheAdd_AndRecordsAfterIt()
        {
            var (sink, journal) = Started();
            var obj = new Obj("New") { InDocument = false };

            journal.Add(obj, () =>
            {
                sink.Log.Add("add New");
                obj.InDocument = true;
                obj.Value = "as added";
            });
            journal.Commit();

            Assert.True(sink.Log.IndexOf("add New") < sink.Log.IndexOf("capture Add New=as added"));
            Assert.Equal(EditKind.Add, Assert.Single(Assert.Single(sink.Pushed)).Kind);
        }

        [Fact]
        public void Add_ThatThrowsAfterInserting_IsRemovedByTheRollback()
        {
            var (sink, journal) = Started();
            var before = new Obj("Before");
            var obj = new Obj("New") { InDocument = false };

            journal.Record(EditKind.Wires, before);
            before.Value = "changed";
            // A plugin's ObjectAdded handler throws after Grasshopper has inserted the object.
            Assert.Throws<InvalidOperationException>(() => journal.Add(obj, () =>
            {
                obj.InDocument = true;
                throw new InvalidOperationException("handler threw");
            }));
            var report = journal.Rollback();

            Assert.True(report.Complete);
            Assert.False(obj.InDocument);
            Assert.Equal("original", before.Value);
            // Newest first: the half-done add goes before the earlier wire change is undone.
            Assert.True(sink.Log.IndexOf("remove-added New") < sink.Log.IndexOf("undo Wires Before"));
        }

        [Fact]
        public void Add_WhoseCaptureThrows_IsStillRemovedByTheRollback()
        {
            var (sink, journal) = Started();
            var obj = new Obj("New") { InDocument = false };
            sink.FailCaptureOf.Add(obj);

            Assert.Throws<InvalidOperationException>(() => journal.Add(obj, () => obj.InDocument = true));
            journal.Rollback();

            Assert.False(obj.InDocument);
            Assert.Contains("remove-added New", sink.Log);
        }

        [Fact]
        public void Add_RecordedThenRolledBack_IsUndoneThroughItsAction()
        {
            var (sink, journal) = Started();
            var obj = new Obj("New") { InDocument = false };

            journal.Add(obj, () => obj.InDocument = true);
            journal.Rollback();

            Assert.False(obj.InDocument);
            Assert.Contains("undo Add New", sink.Log);
            Assert.DoesNotContain("remove-added New", sink.Log);
        }

        [Fact]
        public void Add_TheSameObjectTwice_IsRefused()
        {
            var (_, journal) = Started();
            var obj = new Obj("New");
            journal.Add(obj, () => { });
            Assert.Throws<InvalidOperationException>(() => journal.Add(obj, () => { }));
        }

        [Fact]
        public void Commit_WithAnAddWhoseFailureTheCallerSwallowed_RemovesItAndPushesNothingForIt()
        {
            var (sink, journal) = Started();
            var obj = new Obj("New") { InDocument = false };

            try
            {
                journal.Add(obj, () =>
                {
                    obj.InDocument = true;
                    throw new InvalidOperationException("handler threw");
                });
            }
            catch (InvalidOperationException)
            {
                // A body that carries on after a failed add.
            }
            journal.Commit();

            Assert.False(obj.InDocument);
            Assert.Empty(sink.Pushed);
            Assert.Equal(0, sink.Schedules);
        }

        // ---------------------------------------------------------------- generic and remove: wires first

        [Fact]
        public void Generic_RecordsTheWiresOfEveryInputItTouchesBeforeTheObject()
        {
            var (sink, journal) = Started();
            var comp = new Obj("C");
            var input = new Obj("C.in", owner: comp);
            var downstream = new Obj("D.in", owner: new Obj("D"));
            comp.WiredInputs.Add(input);
            comp.WiredInputs.Add(downstream);

            journal.Record(EditKind.Generic, comp);
            journal.Commit();

            Assert.Equal(new[] { "Wires:C.in", "Wires:D.in", "Generic:C" },
                Assert.Single(sink.Pushed).Select(x => $"{x.Kind}:{x.Target.Name}"));
        }

        [Fact]
        public void Remove_RecordsWiresFirst_SoUndoRestoresTheObjectBeforeItsWires()
        {
            var (sink, journal) = Started();
            var comp = new Obj("C");
            var input = new Obj("C.in", owner: comp);
            var downstreamOwner = new Obj("D");
            var downstream = new Obj("D.in", owner: downstreamOwner);
            comp.WiredInputs.Add(input);
            comp.WiredInputs.Add(downstream);

            journal.RecordRemove(comp);
            comp.InDocument = false;
            journal.Commit();

            var record = Assert.Single(sink.Pushed);
            Assert.Equal(new[] { "Wires:C.in", "Wires:D.in", "Remove:C" }, record.Select(x => $"{x.Kind}:{x.Target.Name}"));

            sink.Log.Clear();
            UndoRecord(sink, record);
            Assert.Equal(new[] { "undo Remove C", "undo Wires D.in", "undo Wires C.in" }, sink.Log);
            Assert.True(comp.InDocument);
        }

        [Fact]
        public void Remove_TheRemovedObjectIsNotExpired_ButWhatItFedIs_OnceEach()
        {
            var (sink, journal) = Started();
            var downstreamOwner = new Obj("D");
            var a = new Obj("A");
            var b = new Obj("B");
            // Both removed objects feed two inputs of the same downstream component.
            a.WiredInputs.Add(new Obj("D.in0", owner: downstreamOwner));
            b.WiredInputs.Add(new Obj("D.in1", owner: downstreamOwner));
            journal.Expire(a);

            journal.RecordRemove(a);
            journal.RecordRemove(b);
            journal.Commit();

            Assert.Equal(new[] { downstreamOwner }, sink.Expired);
            Assert.Equal(1, sink.Schedules);
        }

        [Fact]
        public void Record_OnARemovedObjectOrItsPort_RecordsNothing_AndRemovingTwiceRecordsOnce()
        {
            var (sink, journal) = Started();
            var comp = new Obj("C");
            var port = new Obj("C.in", owner: comp);

            journal.RecordRemove(comp);
            journal.Record(EditKind.NickName, comp);
            journal.Record(EditKind.Wires, port);
            journal.RecordRemove(comp);
            journal.Expire(comp);
            journal.Commit();

            Assert.Equal(new[] { "Remove:C" }, Assert.Single(sink.Pushed).Select(x => $"{x.Kind}:{x.Target.Name}"));
            Assert.Empty(sink.Expired);
        }

        [Fact]
        public void Remove_OfAnObjectAddedInTheTransaction_DropsTheAdd_SoTheNetChangeIsNothing()
        {
            var (sink, journal) = Started();
            var obj = new Obj("New") { InDocument = false };

            journal.Add(obj, () => obj.InDocument = true);
            journal.RecordRemove(obj);
            obj.InDocument = false;
            journal.Commit();

            Assert.Empty(sink.Pushed);
            Assert.Empty(sink.Expired);
            Assert.Equal(0, sink.Schedules);
        }

        // ---------------------------------------------------------------- rollback

        [Fact]
        public void Rollback_UndoesNewestFirst()
        {
            var (sink, journal) = Started();
            var a = new Obj("A");
            var b = new Obj("B");
            var c = new Obj("C");

            journal.Record(EditKind.Wires, a);
            journal.Record(EditKind.NickName, b);
            journal.Record(EditKind.Pivot, c);
            journal.Rollback();

            Assert.Equal(new[] { "undo Pivot C", "undo NickName B", "undo Wires A" },
                sink.Log.Where(l => l.StartsWith("undo ")));
        }

        [Fact]
        public void Rollback_CarriesOnPastFailingActions_AndReportsEveryFailure()
        {
            var (sink, journal) = Started();
            var a = new Obj("A");
            var b = new Obj("B");
            var c = new Obj("C");
            var d = new Obj("D");
            foreach (var o in new[] { a, b, c, d })
            {
                journal.Record(EditKind.Generic, o);
                o.Value = "changed";
            }
            sink.FailUndoOf.Add(b);
            sink.FailUndoOf.Add(d);

            var report = journal.Rollback();

            Assert.False(report.Complete);
            Assert.Equal(2, report.Undone);
            Assert.Equal(new[] { d.Id, b.Id }, report.Failures.Select(f => f.Id));
            Assert.All(report.Failures, f => Assert.Equal(EditKind.Generic, f.Kind));
            Assert.Equal("cannot undo D", report.Failures[0].Error);
            // The actions after each failure were still undone.
            Assert.Equal("original", a.Value);
            Assert.Equal("original", c.Value);
            Assert.Equal("changed", b.Value);
        }

        [Fact]
        public void Rollback_RestoresTheDocumentBeforeRecomputingWhatSurvived_AndPushesNothing()
        {
            var (sink, journal) = Started();
            var target = new Obj("T");
            var added = new Obj("New") { InDocument = false };

            journal.Record(EditKind.Wires, target);
            journal.Add(added, () => added.InDocument = true);
            journal.Rollback();

            Assert.Empty(sink.Pushed);
            var end = sink.Log.IndexOf("end rolledBack=True");
            Assert.True(end > sink.Log.IndexOf("undo Wires T"));
            Assert.True(end < sink.Log.IndexOf("expire T"));
            Assert.True(sink.Log.IndexOf("expire T") < sink.Log.IndexOf("schedule"));
            // The added object is gone, so it is not recomputed.
            Assert.Equal(new[] { target }, sink.Expired);
        }

        [Fact]
        public void Rollback_WithNothingRecorded_RestoresTheDocumentAndSchedulesNothing()
        {
            var (sink, journal) = Started();

            var report = journal.Rollback();

            Assert.True(report.Complete);
            Assert.Equal(0, report.Undone);
            Assert.Equal(new[] { "begin", "end rolledBack=True" }, sink.Log);
        }

        // ---------------------------------------------------------------- commit

        [Fact]
        public void Commit_WithNothingRecorded_PushesNothing_ExpiresNothing_SchedulesNothing()
        {
            var (sink, journal) = Started();

            journal.Commit();

            Assert.False(journal.HasChanges);
            Assert.Equal(new[] { "begin", "end rolledBack=False" }, sink.Log);
        }

        [Fact]
        public void Commit_RestoresTheSolverBeforePushing_ThenExpiresEachObjectOnce_ThenSchedulesOnce()
        {
            var (sink, journal) = Started();
            var comp = new Obj("C");
            var in0 = new Obj("C.in0", owner: comp);
            var in1 = new Obj("C.in1", owner: comp);
            var other = new Obj("P");

            journal.Record(EditKind.Wires, in0);
            journal.Record(EditKind.Wires, in1);
            journal.Record(EditKind.Wires, in0);
            journal.Expire(comp);
            journal.Record(EditKind.PersistentData, other);
            journal.Commit();

            Assert.Equal(new[] { "end rolledBack=False", "push Wires:C.in0,Wires:C.in1,PersistentData:P", "expire C", "expire P", "schedule" },
                sink.Log.SkipWhile(l => !l.StartsWith("end")));
        }

        [Theory]
        [InlineData(EditKind.NickName)]
        [InlineData(EditKind.Pivot)]
        [InlineData(EditKind.Preview)]
        public void Commit_OfAChangeThatDoesNotAffectTheSolution_PushesButExpiresNothing(EditKind kind)
        {
            var (sink, journal) = Started();
            journal.Record(kind, new Obj("A"));
            journal.Commit();

            Assert.Single(sink.Pushed);
            Assert.Empty(sink.Expired);
            // The record is pushed, so one solve is still asked for; it recomputes nothing new.
            Assert.Equal(1, sink.Schedules);
        }

        [Fact]
        public void Commit_WithOnlyAnExplicitExpire_PushesNothingButSchedulesTheSolve()
        {
            var (sink, journal) = Started();
            var obj = new Obj("A");

            journal.Expire(obj);
            journal.Commit();

            Assert.Empty(sink.Pushed);
            Assert.Equal(new[] { obj }, sink.Expired);
            Assert.Equal(1, sink.Schedules);
        }

        // ---------------------------------------------------------------- lifecycle

        [Fact]
        public void Journal_RefusesChangesBeforeBeginAndAfterItCloses()
        {
            var sink = new FakeSink();
            var journal = new EditJournal<Obj, Act>(sink);
            Assert.Throws<InvalidOperationException>(() => journal.Record(EditKind.Wires, new Obj("A")));

            journal.Begin();
            Assert.Throws<InvalidOperationException>(() => journal.Begin());
            journal.Commit();

            Assert.Throws<InvalidOperationException>(() => journal.Record(EditKind.Wires, new Obj("A")));
            Assert.Throws<InvalidOperationException>(() => journal.Commit());
            Assert.Throws<InvalidOperationException>(() => journal.Rollback());
        }

        [Fact]
        public void SecondTransaction_RecordsTheSameObjectsAfresh_AndEachUndoesOnlyItsOwnChange()
        {
            var sink = new FakeSink();
            var obj = new Obj("A");

            var first = new EditJournal<Obj, Act>(sink);
            first.Begin();
            first.Record(EditKind.Wires, obj);
            obj.Value = "after first";
            first.Commit();

            var second = new EditJournal<Obj, Act>(sink);
            second.Begin();
            second.Record(EditKind.Wires, obj);
            obj.Value = "after second";
            second.Commit();

            Assert.Equal(2, sink.Pushed.Count);
            UndoRecord(sink, sink.Pushed[1]);
            Assert.Equal("after first", obj.Value);
            UndoRecord(sink, sink.Pushed[0]);
            Assert.Equal("original", obj.Value);
        }
    }
}
