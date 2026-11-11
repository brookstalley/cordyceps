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
        private static (FakeSink Sink, EditJournal<FakeObj, FakeAct> Journal) Started()
        {
            var sink = new FakeSink();
            var journal = new EditJournal<FakeObj, FakeAct>(sink);
            journal.Begin();
            return (sink, journal);
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
            var obj = new FakeObj("A");

            journal.Record(kind, obj);
            obj.Value = "changed";
            var outcome = journal.Commit();

            Assert.True(outcome.Pushed);
            var record = Assert.Single(sink.Pushed);
            var action = Assert.Single(record, a => a.Target == obj);
            Assert.Equal(kind, action.Kind);
            Assert.Equal("original", action.Snapshot);

            // Ctrl+Z on the pushed record gives the original back.
            sink.UndoRecord(record);
            Assert.Equal("original", obj.Value);
        }

        [Theory]
        [MemberData(nameof(RecordedKinds))]
        public void Record_ThenRollback_RestoresTheStateBeforeTheMutation(EditKind kind)
        {
            var (sink, journal) = Started();
            var obj = new FakeObj("A");

            journal.Record(kind, obj);
            obj.Value = "changed";
            var report = journal.Rollback();

            Assert.True(report.Complete);
            Assert.Empty(report.Failures);
            Assert.Equal("original", obj.Value);
            Assert.Empty(sink.Pushed);
        }

        [Theory]
        [InlineData(EditKind.Add)]
        [InlineData(EditKind.Remove)]
        public void Record_RefusesAddAndRemove_WhichArePairedWithTheirMutation(EditKind kind)
        {
            var (_, journal) = Started();
            Assert.Throws<ArgumentException>(() => journal.Record(kind, new FakeObj("A")));
        }

        [Fact]
        public void Record_WhoseCaptureThrows_RecordsNothing_AndARetryRecords()
        {
            var (sink, journal) = Started();
            var obj = new FakeObj("A");
            sink.FailCaptureOf.Add(obj);

            Assert.Throws<InvalidOperationException>(() => journal.Record(EditKind.NickName, obj));

            sink.FailCaptureOf.Clear();
            journal.Record(EditKind.NickName, obj);
            var outcome = journal.Commit();

            Assert.True(outcome.Pushed);
            Assert.Single(Assert.Single(sink.Pushed));
        }

        // ---------------------------------------------------------------- one record per (kind, id)

        [Fact]
        public void Record_SameKindAndObjectTwice_KeepsOnlyTheFirst_SoUndoReturnsThePreTransactionState()
        {
            var (sink, journal) = Started();
            var obj = new FakeObj("A");

            journal.Record(EditKind.Wires, obj);
            obj.Value = "first";
            journal.Record(EditKind.Wires, obj);
            obj.Value = "second";
            journal.Commit();

            var record = Assert.Single(sink.Pushed);
            var action = Assert.Single(record);
            Assert.Equal("original", action.Snapshot);
            sink.UndoRecord(record);
            Assert.Equal("original", obj.Value);
        }

        [Fact]
        public void Record_DifferentKindsOrDifferentObjects_AreEachRecorded()
        {
            var (sink, journal) = Started();
            var a = new FakeObj("A");
            var b = new FakeObj("B");

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
            var added = new FakeObj("New") { InDocument = false };
            var port = new FakeObj("New.in", owner: added);

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
            var obj = new FakeObj("New") { InDocument = false };

            var added = journal.Add(obj, () =>
            {
                sink.Log.Add("add New");
                obj.InDocument = true;
                obj.Value = "as added";
                return true;
            });
            journal.Commit();

            Assert.True(added);
            Assert.True(sink.Log.IndexOf("add New") < sink.Log.IndexOf("capture Add New=as added"));
            Assert.Equal(EditKind.Add, Assert.Single(Assert.Single(sink.Pushed)).Kind);
        }

        [Fact]
        public void Add_ThatThrowsAfterInserting_IsRemovedByTheRollback()
        {
            var (sink, journal) = Started();
            var before = new FakeObj("Before");
            var obj = new FakeObj("New") { InDocument = false };

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
            var obj = new FakeObj("New") { InDocument = false };
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
            var obj = new FakeObj("New") { InDocument = false };

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
            var obj = new FakeObj("New");
            journal.Add(obj, () => true);
            Assert.Throws<InvalidOperationException>(() => journal.Add(obj, () => true));
        }

        [Fact]
        public void Add_ThatTheDocumentRefuses_RecordsNothing_AndLeavesTheObjectWithThatIdAlone()
        {
            var (sink, journal) = Started();
            // The document refuses the new object because an existing one already has its id.
            var existing = new FakeObj("Existing");
            var duplicate = new FakeObj("Duplicate") { Id = existing.Id, InDocument = false };

            var added = journal.Add(duplicate, () => false);
            // The existing object is not mistaken for one this edit added: its change is recorded.
            journal.Record(EditKind.NickName, existing);
            existing.Value = "renamed";
            var report = journal.Rollback();

            Assert.False(added);
            Assert.DoesNotContain(sink.Log, l => l.StartsWith("capture Add"));
            Assert.DoesNotContain(sink.Log, l => l.StartsWith("remove-added") || l.StartsWith("undo Add"));
            Assert.True(existing.InDocument);
            Assert.Equal("original", existing.Value);
            Assert.True(report.Complete);
        }

        [Fact]
        public void Commit_WithAnAddWhoseFailureTheCallerSwallowed_RemovesItAndPushesNothingForIt()
        {
            var (sink, journal) = Started();
            var obj = new FakeObj("New") { InDocument = false };

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
            var outcome = journal.Commit();

            Assert.False(obj.InDocument);
            Assert.False(outcome.Pushed);
            Assert.Empty(sink.Pushed);
            Assert.Equal(0, sink.Schedules);
        }

        // ---------------------------------------------------------------- generic and remove: wires first

        [Fact]
        public void Generic_RecordsTheWiresOfEveryInputItTouchesBeforeTheObject()
        {
            var (sink, journal) = Started();
            var comp = new FakeObj("C");
            var input = new FakeObj("C.in", owner: comp);
            var downstream = new FakeObj("D.in", owner: new FakeObj("D"));
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
            var comp = new FakeObj("C");
            var input = new FakeObj("C.in", owner: comp);
            var downstream = new FakeObj("D.in", owner: new FakeObj("D"));
            comp.WiredInputs.Add(input);
            comp.WiredInputs.Add(downstream);

            journal.RecordRemove(comp);
            comp.InDocument = false;
            journal.Commit();

            var record = Assert.Single(sink.Pushed);
            Assert.Equal(new[] { "Wires:C.in", "Wires:D.in", "Remove:C" }, record.Select(x => $"{x.Kind}:{x.Target.Name}"));

            sink.Log.Clear();
            sink.UndoRecord(record);
            Assert.Equal(new[] { "undo Remove C", "undo Wires D.in", "undo Wires C.in" }, sink.Log);
            Assert.True(comp.InDocument);
        }

        [Fact]
        public void Remove_TheRemovedObjectIsNotExpired_ButWhatItFedIs_OnceEach()
        {
            var (sink, journal) = Started();
            var downstreamOwner = new FakeObj("D");
            var a = new FakeObj("A");
            var b = new FakeObj("B");
            // Both removed objects feed two inputs of the same downstream component.
            a.WiredInputs.Add(new FakeObj("D.in0", owner: downstreamOwner));
            b.WiredInputs.Add(new FakeObj("D.in1", owner: downstreamOwner));
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
            var comp = new FakeObj("C");
            var port = new FakeObj("C.in", owner: comp);

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
            var obj = new FakeObj("New") { InDocument = false };

            journal.Add(obj, () => obj.InDocument = true);
            journal.RecordRemove(obj);
            obj.InDocument = false;
            var outcome = journal.Commit();

            Assert.False(outcome.Pushed);
            Assert.Empty(sink.Pushed);
            Assert.Empty(sink.Expired);
            Assert.Equal(0, sink.Schedules);
        }

        // ---------------------------------------------------------------- rollback

        [Fact]
        public void Rollback_UndoesNewestFirst()
        {
            var (sink, journal) = Started();
            var a = new FakeObj("A");
            var b = new FakeObj("B");
            var c = new FakeObj("C");

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
            var a = new FakeObj("A");
            var b = new FakeObj("B");
            var c = new FakeObj("C");
            var d = new FakeObj("D");
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
            Assert.Equal(new Guid?[] { d.Id, b.Id }, report.Failures.Select(f => f.Id));
            Assert.All(report.Failures, f => Assert.Equal(EditKind.Generic, f.Kind));
            Assert.All(report.Failures, f => Assert.Equal(EditStep.Undo, f.Step));
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
            var target = new FakeObj("T");
            var added = new FakeObj("New") { InDocument = false };

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
        public void Rollback_WithNothingRecordedOrExpired_RestoresTheDocumentAndSchedulesNothing()
        {
            var (sink, journal) = Started();

            var report = journal.Rollback();

            Assert.True(report.Complete);
            Assert.Equal(0, report.Undone);
            Assert.Equal(new[] { "begin", "end rolledBack=True" }, sink.Log);
        }

        [Fact]
        public void Rollback_WithOnlyAnExpire_RecomputesItAndSchedulesOneSolve()
        {
            var (sink, journal) = Started();
            var obj = new FakeObj("A");

            journal.Expire(obj);
            var report = journal.Rollback();

            Assert.Equal(0, report.Undone);
            Assert.Equal(new[] { obj }, sink.Expired);
            Assert.Equal(1, sink.Schedules);
        }

        [Fact]
        public void Rollback_WhoseLateStepsThrow_KeepsEveryFailure_AndStillRunsTheRestOfTheSteps()
        {
            var (sink, journal) = Started();
            var a = new FakeObj("A");
            var b = new FakeObj("B");
            journal.Record(EditKind.Wires, a);
            journal.Record(EditKind.Wires, b);
            sink.FailUndoOf.Add(b);
            sink.FailSteps.Add("end-rollback");
            sink.FailExpireOf.Add(a);
            sink.FailSteps.Add("schedule");

            var report = journal.Rollback();

            Assert.Equal(new[] { EditStep.Undo, EditStep.Restore, EditStep.Expire, EditStep.Schedule },
                report.Failures.Select(f => f.Step));
            Assert.Equal(1, report.Undone);
            Assert.False(report.Complete);
            // b's expire still ran after a's threw.
            Assert.Contains("expire B", sink.Log);
        }

        [Fact]
        public void Rollback_WhoseOnlyFailuresAreAfterTheUndo_IsComplete_ButReportsThem()
        {
            var (sink, journal) = Started();
            journal.Record(EditKind.Wires, new FakeObj("A"));
            sink.FailSteps.Add("schedule");

            var report = journal.Rollback();

            Assert.True(report.Complete);
            Assert.Equal(EditStep.Schedule, Assert.Single(report.Failures).Step);
        }

        // ---------------------------------------------------------------- commit

        [Fact]
        public void Commit_WithNothingRecorded_PushesNothing_ExpiresNothing_SchedulesNothing()
        {
            var (sink, journal) = Started();

            var outcome = journal.Commit();

            Assert.False(outcome.Pushed);
            Assert.False(outcome.Scheduled);
            Assert.Null(outcome.Rollback);
            Assert.Equal(new[] { "begin", "end rolledBack=False" }, sink.Log);
        }

        [Fact]
        public void Commit_PushesWhilePaused_ThenRestoresTheSolver_ThenExpiresEachObjectOnce_ThenSchedulesOnce()
        {
            var (sink, journal) = Started();
            var comp = new FakeObj("C");
            var in0 = new FakeObj("C.in0", owner: comp);
            var in1 = new FakeObj("C.in1", owner: comp);
            var other = new FakeObj("P");

            journal.Record(EditKind.Wires, in0);
            journal.Record(EditKind.Wires, in1);
            journal.Record(EditKind.Wires, in0);
            journal.Expire(comp);
            journal.Record(EditKind.PersistentData, other);
            var outcome = journal.Commit();

            Assert.True(outcome.Pushed);
            Assert.True(outcome.Scheduled);
            Assert.Empty(outcome.Warnings);
            Assert.Equal(new[] { "push Wires:C.in0,Wires:C.in1,PersistentData:P", "end rolledBack=False", "expire C", "expire P", "schedule" },
                sink.Log.SkipWhile(l => !l.StartsWith("push")));
        }

        [Theory]
        [InlineData(EditKind.NickName)]
        [InlineData(EditKind.Pivot)]
        [InlineData(EditKind.Preview)]
        public void Commit_OfAChangeThatDoesNotAffectTheSolution_PushesButExpiresNothing(EditKind kind)
        {
            var (sink, journal) = Started();
            journal.Record(kind, new FakeObj("A"));
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
            var obj = new FakeObj("A");

            journal.Expire(obj);
            var outcome = journal.Commit();

            Assert.False(outcome.Pushed);
            Assert.True(outcome.Scheduled);
            Assert.Empty(sink.Pushed);
            Assert.Equal(new[] { obj }, sink.Expired);
            Assert.Equal(1, sink.Schedules);
        }

        [Fact]
        public void Commit_WhosePushThrows_RollsTheEditBack_AndSaysSo()
        {
            var (sink, journal) = Started();
            var obj = new FakeObj("A");
            journal.Record(EditKind.Wires, obj);
            obj.Value = "changed";
            sink.FailSteps.Add("push");

            var outcome = journal.Commit();

            Assert.False(outcome.Pushed);
            Assert.Equal(EditStep.Push, outcome.Failure.Step);
            Assert.Equal("cannot push", outcome.Failure.Error);
            Assert.NotNull(outcome.Rollback);
            Assert.True(outcome.Rollback.Complete);
            Assert.Equal("original", obj.Value);
            Assert.Empty(sink.Pushed);
            // Rolled back while the solver was still paused; restored once, as a rollback.
            Assert.True(sink.Log.IndexOf("undo Wires A") < sink.Log.IndexOf("end rolledBack=True"));
            Assert.DoesNotContain("end rolledBack=False", sink.Log);
        }

        [Fact]
        public void Commit_WhoseUnfinishedAddCannotBeRemoved_RollsTheEditBack()
        {
            var (sink, journal) = Started();
            var before = new FakeObj("Before");
            var obj = new FakeObj("New") { InDocument = false };
            journal.Record(EditKind.Wires, before);
            before.Value = "changed";
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
            sink.FailRemoveAddedOf.Add(obj);

            var outcome = journal.Commit();

            Assert.Equal(EditStep.DiscardUnfinishedAdd, outcome.Failure.Step);
            Assert.Equal("original", before.Value);
            Assert.Empty(sink.Pushed);
            // The rollback tried the unfinished add again and reports that it could not remove it.
            Assert.Equal(EditKind.Add, Assert.Single(outcome.Rollback.Failures).Kind);
        }

        [Fact]
        public void Commit_WhoseStepsAfterThePushThrow_Stands_WithAWarningForEach_AndRunsEveryStep()
        {
            var (sink, journal) = Started();
            var a = new FakeObj("A");
            var b = new FakeObj("B");
            journal.Record(EditKind.Wires, a);
            journal.Record(EditKind.Wires, b);
            sink.FailSteps.Add("end");
            sink.FailExpireOf.Add(a);
            sink.FailSteps.Add("schedule");

            var outcome = journal.Commit();

            Assert.True(outcome.Pushed);
            Assert.False(outcome.Scheduled);
            Assert.Null(outcome.Rollback);
            Assert.Single(sink.Pushed);
            Assert.Equal(new[] { EditStep.Restore, EditStep.Expire, EditStep.Schedule }, outcome.Warnings.Select(w => w.Step));
            Assert.Equal(a.Id, outcome.Warnings[1].Id);
            Assert.Equal(new[] { b }, sink.Expired);
            Assert.Contains("schedule", sink.Log);
            Assert.Throws<InvalidOperationException>(() => journal.Rollback());
        }

        // ---------------------------------------------------------------- lifecycle

        [Fact]
        public void Journal_RefusesChangesBeforeBeginAndAfterItCloses()
        {
            var sink = new FakeSink();
            var journal = new EditJournal<FakeObj, FakeAct>(sink);
            Assert.Throws<InvalidOperationException>(() => journal.Record(EditKind.Wires, new FakeObj("A")));

            journal.Begin();
            Assert.Throws<InvalidOperationException>(() => journal.Begin());
            journal.Commit();

            Assert.Throws<InvalidOperationException>(() => journal.Record(EditKind.Wires, new FakeObj("A")));
            Assert.Throws<InvalidOperationException>(() => journal.Commit());
            Assert.Throws<InvalidOperationException>(() => journal.Rollback());
        }

        [Fact]
        public void SecondTransaction_RecordsTheSameObjectsAfresh_AndEachUndoesOnlyItsOwnChange()
        {
            var sink = new FakeSink();
            var obj = new FakeObj("A");

            var first = new EditJournal<FakeObj, FakeAct>(sink);
            first.Begin();
            first.Record(EditKind.Wires, obj);
            obj.Value = "after first";
            first.Commit();

            var second = new EditJournal<FakeObj, FakeAct>(sink);
            second.Begin();
            second.Record(EditKind.Wires, obj);
            obj.Value = "after second";
            second.Commit();

            Assert.Equal(2, sink.Pushed.Count);
            sink.UndoRecord(sink.Pushed[1]);
            Assert.Equal("after first", obj.Value);
            sink.UndoRecord(sink.Pushed[0]);
            Assert.Equal("original", obj.Value);
        }
    }
}
