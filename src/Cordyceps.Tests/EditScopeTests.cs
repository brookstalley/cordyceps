using System;
using System.Linq;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="EditScope{TSession}"/>: how an edit body is run, joined, committed or
    /// rolled back, and which reply each path gives.
    /// </summary>
    public class EditScopeTests
    {
        private readonly object _doc = new object();
        private readonly EditScope<FakeSession> _scope = new EditScope<FakeSession>();
        private readonly FakeSink _sink = new FakeSink();
        private FakeSession _session;
        private int _opened;

        private FakeSession Open()
        {
            _opened++;
            _session = new FakeSession(_sink);
            return _session;
        }

        private string Run(Func<FakeSession, string> body) => _scope.Run(_doc, "Cordyceps: gh_wire connect", Open, body);

        private const string Ok = "{\"success\":true}";

        // ---------------------------------------------------------------- commit: undo and solve

        [Fact]
        public void Run_ThatRecordsAChange_CommitsIt_AndAddsUndo()
        {
            var obj = new FakeObj("A");

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, obj);
                obj.Value = "changed";
                return Ok;
            }));

            Assert.True((bool)reply["success"]);
            Assert.Equal("Cordyceps: gh_wire connect", (string)reply["undo"]);
            Assert.Single(_sink.Pushed);
            Assert.Equal("changed", obj.Value);
            Assert.False(_scope.IsOpen);
        }

        [Fact]
        public void Run_ThatRecordsNothing_HasNoUndo()
        {
            var reply = JObject.Parse(Run(s => Ok));

            Assert.Null(reply["undo"]);
            Assert.Empty(_sink.Pushed);
        }

        [Fact]
        public void Run_AddsSolve_OnlyWhenASolveWasAskedForAndRefused()
        {
            var refused = SolvePolicy.ScheduledField(SolveState.SolverDisabled);

            // Refused, but nothing changed, so no solve was asked for.
            var quiet = JObject.Parse(Run(s => { s.SolveRefusal = refused; return Ok; }));
            Assert.Null(quiet["solve"]);

            // A change asked for a solve, which the solver switch refused.
            var obj = new FakeObj("A");
            var reported = JObject.Parse(Run(s =>
            {
                s.SolveRefusal = refused;
                s.Edits.Record(EditKind.Wires, obj);
                return Ok;
            }));
            Assert.Equal("solver_disabled", (string)reported["solve"]["state"]);
            Assert.True((bool)reported["success"]);

            // Scheduled normally: no solve field.
            var scheduled = JObject.Parse(Run(s => { s.Edits.Record(EditKind.Wires, new FakeObj("B")); return Ok; }));
            Assert.Null(scheduled["solve"]);
        }

        // ---------------------------------------------------------------- rollback: refused vs unexpected

        [Fact]
        public void Run_WhoseBodyThrowsARollbackException_IsAnsweredWithItsOwnMessage()
        {
            var obj = new FakeObj("A");

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, obj);
                obj.Value = "changed";
                throw new EditRollbackException("Port 3 of 5 could not be added");
            }));

            Assert.False((bool)reply["success"]);
            Assert.True((bool)reply["rolledBack"]);
            Assert.StartsWith("Port 3 of 5 could not be added.", (string)reply["error"]);
            Assert.Equal("original", obj.Value);
            Assert.Null(_session.RollbackCause);
        }

        [Fact]
        public void Run_WhoseBodyThrowsAnythingElse_IsAnsweredAsAnUnexpectedFailure()
        {
            var obj = new FakeObj("A");

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, obj);
                obj.Value = "changed";
                throw new NullReferenceException("Object reference not set");
            }));

            Assert.True((bool)reply["rolledBack"]);
            Assert.StartsWith("Cordyceps: gh_wire connect failed: Object reference not set.", (string)reply["error"]);
            Assert.Equal("original", obj.Value);
            Assert.IsType<NullReferenceException>(_session.RollbackCause);
            Assert.Empty(_sink.Pushed);
            Assert.False(_scope.IsOpen);
        }

        [Fact]
        public void Run_WhoseBodyReturnsSomethingOtherThanAJsonObject_RollsBack()
        {
            var obj = new FakeObj("A");

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, obj);
                obj.Value = "changed";
                return "[1,2]";
            }));

            Assert.True((bool)reply["rolledBack"]);
            Assert.Equal("original", obj.Value);
        }

        [Fact]
        public void Run_WhoseFollowUpAfterARollbackThrows_KeepsTheRollbackReport()
        {
            var a = new FakeObj("A");
            var b = new FakeObj("B");
            _sink.FailUndoOf.Add(b);

            var reply = JObject.Parse(_scope.Run(_doc, "edit", () =>
            {
                var s = Open();
                s.ThrowAfterRollback = true;
                return s;
            }, s =>
            {
                s.Edits.Record(EditKind.Wires, a);
                s.Edits.Record(EditKind.Wires, b);
                throw new InvalidOperationException("boom");
            }));

            Assert.False((bool)reply["rolledBack"]);
            var steps = ((JArray)reply["rollbackFailures"]).Select(f => (string)f["step"]).ToList();
            Assert.Equal(new[] { EditStep.Undo, EditStep.AfterRollback }, steps);
            Assert.Equal("original", a.Value);
        }

        // ---------------------------------------------------------------- commit failures

        [Fact]
        public void Run_WhoseCommitCannotPushTheRecord_RollsBack_AndSaysTheEditWasNotRecorded()
        {
            var obj = new FakeObj("A");
            _sink.FailSteps.Add("push");

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, obj);
                obj.Value = "changed";
                return Ok;
            }));

            Assert.False((bool)reply["success"]);
            Assert.True((bool)reply["rolledBack"]);
            Assert.Contains("recording it for undo failed (cannot push)", (string)reply["error"]);
            Assert.Null(reply["undo"]);
            Assert.Equal("original", obj.Value);
            Assert.Equal(1, _session.RollbacksSeen);
        }

        [Fact]
        public void Run_WhoseScheduleThrowsAfterThePush_Succeeds_WithUndoAndAWarningNamingTheStep()
        {
            var obj = new FakeObj("A");
            _sink.FailSteps.Add("schedule");

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, obj);
                obj.Value = "changed";
                return "{\"success\":true,\"warning\":\"own\"}";
            }));

            Assert.True((bool)reply["success"]);
            Assert.Equal("Cordyceps: gh_wire connect", (string)reply["undo"]);
            var warning = (string)reply["warning"];
            Assert.StartsWith("own ", warning);
            Assert.Contains("scheduling the solve failed (cannot schedule)", warning);
            Assert.Contains("Ctrl+Z undoes it", warning);
            Assert.Null(reply["rolledBack"]);
            Assert.Equal("changed", obj.Value);
        }

        [Fact]
        public void Run_WhoseFollowUpAfterACommitThrows_Succeeds_WithAWarning()
        {
            var reply = JObject.Parse(_scope.Run(_doc, "edit", () =>
            {
                var s = Open();
                s.ThrowAfterCommit = true;
                return s;
            }, s => { s.Edits.Record(EditKind.NickName, new FakeObj("A")); return Ok; }));

            Assert.True((bool)reply["success"]);
            Assert.Equal("edit", (string)reply["undo"]);
            Assert.Contains("logging the edit failed (cannot log)", (string)reply["warning"]);
        }

        // ---------------------------------------------------------------- nesting

        [Fact]
        public void Run_InsideAnOpenEditOfTheSameDocument_JoinsIt_AsOneUndoStep()
        {
            var outer = new FakeObj("Outer");
            var inner = new FakeObj("Inner");
            string innerReply = null;

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, outer);
                innerReply = _scope.Run(_doc, "inner", () => throw new InvalidOperationException("a joined edit opens no session"), joined =>
                {
                    Assert.Same(s, joined);
                    joined.Edits.Record(EditKind.Wires, inner);
                    return "{\"success\":true,\"inner\":true}";
                });
                return Ok;
            }));

            Assert.Equal("{\"success\":true,\"inner\":true}", innerReply);
            Assert.Equal(1, _opened);
            var record = Assert.Single(_sink.Pushed);
            Assert.Equal(new[] { "Outer", "Inner" }, record.Select(a => a.Target.Name));
            Assert.Equal("Cordyceps: gh_wire connect", (string)reply["undo"]);
        }

        [Fact]
        public void Run_InsideAnOpenEditOfAnotherDocument_Throws_AndTheOuterEditRollsBack()
        {
            var obj = new FakeObj("A");
            InvalidOperationException refused = null;

            var reply = JObject.Parse(Run(s =>
            {
                s.Edits.Record(EditKind.Wires, obj);
                obj.Value = "changed";
                refused = Assert.Throws<InvalidOperationException>(() =>
                    _scope.Run(new object(), "other", Open, other => Ok));
                throw refused;
            }));

            Assert.Contains("one call edits one document", refused.Message);
            Assert.Equal(1, _opened);
            Assert.True((bool)reply["rolledBack"]);
            Assert.Equal("original", obj.Value);
        }

        // ---------------------------------------------------------------- the scope closes

        [Fact]
        public void Run_ClosesTheScopeAndEndsTheSession_EvenWhenBeginThrows()
        {
            Assert.Throws<InvalidOperationException>(() => _scope.Run(_doc, "edit", () =>
            {
                var s = Open();
                s.Edits.Begin(); // a second Begin inside Run throws
                return s;
            }, s => Ok));

            Assert.False(_scope.IsOpen);
            Assert.Equal(1, _session.EnsureEndedCalls);

            // The next edit opens a fresh session.
            Run(s => Ok);
            Assert.Equal(2, _opened);
        }
    }
}
