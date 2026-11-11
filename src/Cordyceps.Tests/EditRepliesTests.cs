using System;
using System.Collections.Generic;
using Cordyceps.Core;
using Cordyceps.Core.Edits;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="EditReplies"/>: what an edit adds to its body's reply on commit, and the
    /// reply of a rolled-back edit.
    /// </summary>
    public class EditRepliesTests
    {
        [Fact]
        public void RecordName_SaysCordycepsAndTheCall()
        {
            Assert.Equal("Cordyceps: gh_wire connect", EditReplies.RecordName("gh_wire", "connect"));
        }

        [Fact]
        public void Committed_AddsUndoAfterTheBodysFields()
        {
            var body = EditReplies.ParseBody("{\"success\":true,\"disconnected\":true}");

            var reply = JObject.Parse(EditReplies.Committed(body, "Cordyceps: gh_wire disconnect", null));

            Assert.Equal(new[] { "success", "disconnected", "undo" }, PropertyNames(reply));
            Assert.Equal("Cordyceps: gh_wire disconnect", (string)reply["undo"]);
            Assert.Null(reply["solve"]);
        }

        [Fact]
        public void Committed_WithNothingPushed_HasNoUndo()
        {
            var body = EditReplies.ParseBody("{\"success\":true,\"clearedCount\":0}");

            var reply = JObject.Parse(EditReplies.Committed(body, null, null));

            Assert.Null(reply["undo"]);
            Assert.True((bool)reply["success"]);
        }

        [Fact]
        public void Committed_KeepsAFailedBodysSuccessFalse_WhenSomeIdsCommitted()
        {
            var body = EditReplies.ParseBody("{\"success\":false,\"succeeded\":1,\"failed\":1}");

            var reply = JObject.Parse(EditReplies.Committed(body, "Cordyceps: gh_wire connect", null));

            Assert.False((bool)reply["success"]);
            Assert.Equal("Cordyceps: gh_wire connect", (string)reply["undo"]);
        }

        [Fact]
        public void Committed_ReportsARefusedSolveWithoutFailingTheEdit()
        {
            var body = EditReplies.ParseBody("{\"success\":true}");
            var refused = SolvePolicy.ScheduledField(SolveState.SolverDisabled);

            var reply = JObject.Parse(EditReplies.Committed(body, "Cordyceps: gh_wire connect", refused));

            Assert.True((bool)reply["success"]);
            Assert.Equal("solver_disabled", (string)reply["solve"]["state"]);
            Assert.NotNull(reply["solve"]["remedy"]);
        }

        [Fact]
        public void Committed_LeavesABodysOwnSolveAlone()
        {
            var body = EditReplies.ParseBody("{\"success\":true,\"solve\":{\"state\":\"scheduled\"}}");

            var reply = JObject.Parse(EditReplies.Committed(body, "x", SolvePolicy.ScheduledField(SolveState.SolutionsLocked)));

            Assert.Equal("scheduled", (string)reply["solve"]["state"]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("\"text\"")]
        public void ParseBody_RefusesAnythingButAJsonObject(string body)
        {
            Assert.Throws<InvalidOperationException>(() => EditReplies.ParseBody(body));
        }

        [Fact]
        public void RolledBack_Complete_SaysNothingChanged()
        {
            var report = new RollbackReport(3, new List<EditFailure>());

            var reply = JObject.Parse(EditReplies.RolledBack("Cordyceps: gh_wire connect", "Object reference not set", refused: false, report));

            Assert.False((bool)reply["success"]);
            Assert.True((bool)reply["rolledBack"]);
            Assert.Null(reply["rollbackFailures"]);
            var error = (string)reply["error"];
            Assert.StartsWith("Cordyceps: gh_wire connect failed: Object reference not set.", error);
            Assert.Contains("every change this call had made was rolled back", error);
        }

        [Fact]
        public void RolledBack_Refused_UsesTheReasonAsTheWholeError()
        {
            var report = new RollbackReport(0, new List<EditFailure>());

            var reply = JObject.Parse(EditReplies.RolledBack("Cordyceps: gh_canvas zoomable", "Could not add port 3 of 5", refused: true, report));

            Assert.Equal("Could not add port 3 of 5. Nothing was changed.", (string)reply["error"]);
            Assert.True((bool)reply["rolledBack"]);
        }

        [Fact]
        public void RolledBack_Incomplete_ListsEveryFailureAndSaysCheckTheCanvas()
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var report = new RollbackReport(2, new List<EditFailure>
            {
                new EditFailure(EditStep.Undo, "Parameter could not be found", EditKind.Wires, a),
                new EditFailure(EditStep.Undo, "Parameter is of a wrong type", EditKind.PersistentData, b),
            });

            var reply = JObject.Parse(EditReplies.RolledBack("Cordyceps: gh_wire clear", "boom", refused: false, report));

            Assert.False((bool)reply["success"]);
            Assert.False((bool)reply["rolledBack"]);
            var error = (string)reply["error"];
            Assert.Contains("2 of 4 change(s) could not be undone", error);
            Assert.Contains("Ctrl+Z does not cover this call", error);
            var failures = (JArray)reply["rollbackFailures"];
            Assert.Equal(2, failures.Count);
            Assert.Equal("undo", (string)failures[0]["step"]);
            Assert.Equal("wires", (string)failures[0]["kind"]);
            Assert.Equal(a.ToString(), (string)failures[0]["id"]);
            Assert.Equal("Parameter could not be found", (string)failures[0]["error"]);
            Assert.Equal("persistentData", (string)failures[1]["kind"]);
        }

        [Fact]
        public void ParseBody_KeepsDateShapedStringsExactly()
        {
            // A nickname can look like a date; Json.NET's default parse would turn it into a date
            // and write it back in another form. Asserted on the emitted text, not on a re-parse.
            const string body = "{\"success\":true,\"name\":\"2026-01-01\",\"at\":\"2026-08-21T12:00:00.1234567+02:00\"}";

            var reply = EditReplies.Committed(EditReplies.ParseBody(body), null, null);

            Assert.Equal(body, reply);
        }

        [Fact]
        public void Committed_WithAWarning_SaysTheEditStands_AndNamesEveryFailedStep()
        {
            var body = EditReplies.ParseBody("{\"success\":true}");
            var warnings = new List<EditFailure>
            {
                new EditFailure(EditStep.Expire, "x", id: Guid.NewGuid()),
                new EditFailure(EditStep.Schedule, "y"),
            };

            var reply = JObject.Parse(EditReplies.Committed(body, "Cordyceps: gh_wire connect", null, warnings));

            Assert.True((bool)reply["success"]);
            Assert.Equal("Cordyceps: gh_wire connect", (string)reply["undo"]);
            var warning = (string)reply["warning"];
            Assert.StartsWith("The edit was made and Ctrl+Z undoes it, but marking the changed objects for recompute and scheduling the solve failed (x; y).", warning);
            Assert.Contains("gh_document(action='recompute')", warning);
        }

        [Fact]
        public void Committed_WithoutWarnings_HasNoWarningField()
        {
            var reply = JObject.Parse(EditReplies.Committed(EditReplies.ParseBody("{\"success\":true}"), "x", null, new List<EditFailure>()));

            Assert.Null(reply["warning"]);
        }

        [Fact]
        public void CommitFailureReason_NamesWhatFailed()
        {
            Assert.Equal("recording it for undo failed (boom)", EditReplies.CommitFailureReason(new EditFailure(EditStep.Push, "boom")));
            Assert.Equal("removing an object whose add did not finish failed (boom)",
                EditReplies.CommitFailureReason(new EditFailure(EditStep.DiscardUnfinishedAdd, "boom")));
        }

        [Fact]
        public void RolledBack_CompleteButWithAFailedStepAfterTheUndo_SaysNothingChanged_AndListsTheStep()
        {
            var report = new RollbackReport(1, new List<EditFailure> { new EditFailure(EditStep.Schedule, "cannot schedule") });

            var reply = JObject.Parse(EditReplies.RolledBack("edit", "boom", refused: false, report));

            Assert.True((bool)reply["rolledBack"]);
            var error = (string)reply["error"];
            Assert.Contains("every change this call had made was rolled back", error);
            Assert.Contains("After the rollback, scheduling the solve failed", error);
            Assert.DoesNotContain("Ctrl+Z does not cover", error);
            var failure = (JObject)Assert.Single((JArray)reply["rollbackFailures"]);
            Assert.Equal("schedule", (string)failure["step"]);
            Assert.Null(failure["kind"]);
            Assert.Null(failure["id"]);
        }

        private static IEnumerable<string> PropertyNames(JObject obj)
        {
            foreach (var p in obj.Properties()) yield return p.Name;
        }
    }
}
