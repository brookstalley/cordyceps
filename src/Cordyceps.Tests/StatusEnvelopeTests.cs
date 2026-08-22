using System;
using Cordyceps.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="StatusEnvelope"/>. The governing contract is <em>totality</em>:
    /// the status block is added to every tool's result at one choke point, so no shape of input
    /// may throw or silently damage the tool's own payload. A liveness feature that can break an
    /// unrelated tool result is worse than no liveness feature.
    /// </summary>
    public class StatusEnvelopeTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        private static HostStatus HealthyStatus() => new HostStatus
        {
            Ui = UiLiveness.Responsive,
            DocumentName = "wall-study.gh",
            DocumentId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ServerListening = true,
            Port = 26929,
            UptimeSeconds = 90,
            CommandCount = 7,
            Hint = "Healthy.",
        };

        private static HostStatus ModalStatus() => new HostStatus
        {
            Ui = UiLiveness.Blocked,
            ModalInferred = true,
            LastHeartbeatUtc = T0,
            HeartbeatAgeMs = 30000,
            DocumentName = "wall-study.gh",
            Hint = "A modal dialog is almost certainly open.",
        };

        private static HostStatus SolvingStatus() => new HostStatus
        {
            Ui = UiLiveness.Blocked,
            Solving = true,
            SolvingSince = T0,
            DocumentName = "heavy.gh",
            Hint = "Wait and retry.",
        };

        // ---------------------------------------------------------------- compact rendering

        [Fact]
        public void ToCompactJson_WhenHealthy_OmitsTheHintAndModalFlag()
        {
            // The compact block rides on every response; a healthy one must stay tiny.
            var json = StatusEnvelope.ToCompactJson(HealthyStatus());

            Assert.Equal("wall-study.gh", (string)json["document"]);
            Assert.Equal("responsive", (string)json["ui"]);
            Assert.False((bool)json["solving"]);
            Assert.Null(json["hint"]);
            Assert.Null(json["modal_inferred"]);
            Assert.Null(json["solving_since"]);
        }

        [Fact]
        public void ToCompactJson_WhenModalInferred_CarriesTheFlagAndHint()
        {
            var json = StatusEnvelope.ToCompactJson(ModalStatus());

            Assert.Equal("blocked", (string)json["ui"]);
            Assert.True((bool)json["modal_inferred"]);
            Assert.Equal("A modal dialog is almost certainly open.", (string)json["hint"]);
        }

        [Fact]
        public void ToCompactJson_WhenSolving_CarriesSolvingSince()
        {
            var json = StatusEnvelope.ToCompactJson(SolvingStatus());

            Assert.True((bool)json["solving"]);
            Assert.Equal("2026-08-21T12:00:00.0000000Z", (string)json["solving_since"]);
            Assert.Null(json["modal_inferred"]);
            Assert.Equal("Wait and retry.", (string)json["hint"]);
        }

        [Fact]
        public void ToCompactJson_WithNullStatus_ReturnsNull()
        {
            Assert.Null(StatusEnvelope.ToCompactJson(null));
        }

        // ---------------------------------------------------------------- full rendering

        [Fact]
        public void ToFullJson_HasAllThreeLayers()
        {
            var json = StatusEnvelope.ToFullJson(HealthyStatus());

            Assert.True((bool)json["healthy"]);
            Assert.NotNull(json["rhino"]);
            Assert.NotNull(json["grasshopper"]);
            Assert.NotNull(json["cordyceps"]);

            Assert.True((bool)json["rhino"]["alive"]);
            Assert.True((bool)json["rhino"]["ui_responsive"]);
            Assert.False((bool)json["rhino"]["modal_inferred"]);

            Assert.False((bool)json["grasshopper"]["solving"]);
            Assert.Equal("wall-study.gh", (string)json["grasshopper"]["document"]["name"]);
            Assert.Equal("11111111-2222-3333-4444-555555555555", (string)json["grasshopper"]["document"]["id"]);

            Assert.True((bool)json["cordyceps"]["listening"]);
            Assert.Equal(26929, (int)json["cordyceps"]["port"]);
            Assert.Equal(90, (int)json["cordyceps"]["uptime_seconds"]);
            Assert.Equal(7, (int)json["cordyceps"]["command_count"]);
        }

        [Fact]
        public void ToFullJson_WithNoHeartbeat_EmitsNullsRatherThanThrowing()
        {
            var json = StatusEnvelope.ToFullJson(new HostStatus { Ui = UiLiveness.Unknown, Hint = "x" });

            Assert.Equal(JTokenType.Null, json["rhino"]["last_heartbeat"].Type);
            Assert.Equal(JTokenType.Null, json["rhino"]["heartbeat_age_ms"].Type);
            Assert.Equal(JTokenType.Null, json["grasshopper"]["solving_since"].Type);
            Assert.Equal(JTokenType.Null, json["grasshopper"]["document"]["id"].Type);
            Assert.Equal("unknown", (string)json["rhino"]["ui"]);
        }

        [Fact]
        public void ToFullJson_WithNullStatus_ReturnsNull()
        {
            Assert.Null(StatusEnvelope.ToFullJson(null));
        }

        // ---------------------------------------------------------------- Inject totality

        [Fact]
        public void Inject_IntoAnObject_AddsTheStatusMember()
        {
            var result = StatusEnvelope.InjectCompact("{\"success\":true,\"id\":\"abc\"}", HealthyStatus());

            var obj = JObject.Parse(result);
            Assert.True((bool)obj["success"]);
            Assert.Equal("abc", (string)obj["id"]);
            Assert.Equal("wall-study.gh", (string)obj["status"]["document"]);
        }

        [Fact]
        public void Inject_PreservesEveryExistingMember()
        {
            const string original = "{\"success\":false,\"error\":\"nope\",\"items\":[1,2,3],\"nested\":{\"a\":1}}";

            var obj = JObject.Parse(StatusEnvelope.InjectCompact(original, HealthyStatus()));

            Assert.False((bool)obj["success"]);
            Assert.Equal("nope", (string)obj["error"]);
            Assert.Equal(3, ((JArray)obj["items"]).Count);
            Assert.Equal(1, (int)obj["nested"]["a"]);
        }

        [Fact]
        public void Inject_EmitsCompactJson_NoWhitespaceAdded()
        {
            var result = StatusEnvelope.Inject("{\"a\":1}", new JObject { ["ui"] = "responsive" });

            Assert.Equal("{\"a\":1,\"status\":{\"ui\":\"responsive\"}}", result);
        }

        [Fact]
        public void Inject_IntoAnArray_ReturnsItUnchanged()
        {
            // An array has nowhere to put a member; damaging it would be worse than omitting status.
            const string original = "[1,2,3]";

            Assert.Equal(original, StatusEnvelope.InjectCompact(original, HealthyStatus()));
        }

        [Fact]
        public void Inject_IntoABareScalar_ReturnsItUnchanged()
        {
            Assert.Equal("42", StatusEnvelope.InjectCompact("42", HealthyStatus()));
            Assert.Equal("\"hello\"", StatusEnvelope.InjectCompact("\"hello\"", HealthyStatus()));
            Assert.Equal("true", StatusEnvelope.InjectCompact("true", HealthyStatus()));
            Assert.Equal("null", StatusEnvelope.InjectCompact("null", HealthyStatus()));
        }

        [Fact]
        public void Inject_IntoMalformedJson_ReturnsItUnchanged()
        {
            const string original = "{not json at all";

            Assert.Equal(original, StatusEnvelope.InjectCompact(original, HealthyStatus()));
        }

        [Fact]
        public void Inject_IntoHelpText_ReturnsItUnchanged()
        {
            // action='help' returns a plain-text block, not JSON.
            const string help = "gh_inspect actions:\n  status - ...\n  log - ...";

            Assert.Equal(help, StatusEnvelope.InjectCompact(help, HealthyStatus()));
        }

        [Fact]
        public void Inject_IntoTrailingGarbage_ReturnsItUnchanged()
        {
            // Re-serializing only the first value would silently truncate the caller's payload.
            const string original = "{\"a\":1} trailing";

            Assert.Equal(original, StatusEnvelope.InjectCompact(original, HealthyStatus()));
        }

        [Fact]
        public void Inject_IntoNullOrEmpty_ReturnsTheInput()
        {
            Assert.Null(StatusEnvelope.InjectCompact(null, HealthyStatus()));
            Assert.Equal("", StatusEnvelope.InjectCompact("", HealthyStatus()));
            Assert.Equal("   ", StatusEnvelope.InjectCompact("   ", HealthyStatus()));
        }

        [Fact]
        public void Inject_WithNullStatus_ReturnsTheInput()
        {
            const string original = "{\"success\":true}";

            Assert.Equal(original, StatusEnvelope.Inject(original, null));
            Assert.Equal(original, StatusEnvelope.InjectCompact(original, null));
        }

        [Fact]
        public void Inject_IntoAnEmptyObject_Works()
        {
            var obj = JObject.Parse(StatusEnvelope.InjectCompact("{}", HealthyStatus()));

            Assert.Equal("wall-study.gh", (string)obj["status"]["document"]);
        }

        [Fact]
        public void Inject_WhenStatusKeyIsTaken_MovesAsideRatherThanOverwriting()
        {
            // Clobbering a tool's own data to make room for a diagnostic would be silent data loss.
            const string original = "{\"success\":true,\"status\":\"OK\"}";

            var obj = JObject.Parse(StatusEnvelope.InjectCompact(original, HealthyStatus()));

            Assert.Equal("OK", (string)obj["status"]);
            Assert.Equal("wall-study.gh", (string)obj["host_status"]["document"]);
        }

        [Fact]
        public void Inject_WhenBothKeysAreTaken_ReturnsTheInputUnchanged()
        {
            const string original = "{\"status\":\"OK\",\"host_status\":\"mine\"}";

            var obj = JObject.Parse(StatusEnvelope.InjectCompact(original, HealthyStatus()));

            Assert.Equal("OK", (string)obj["status"]);
            Assert.Equal("mine", (string)obj["host_status"]);
        }

        [Fact]
        public void Inject_Twice_DoesNotStackBlocks()
        {
            var once = StatusEnvelope.InjectCompact("{\"success\":true}", HealthyStatus());
            var twice = StatusEnvelope.InjectCompact(once, ModalStatus());

            var obj = JObject.Parse(twice);
            // The first block stays put; the second moves aside rather than corrupting it.
            Assert.Equal("responsive", (string)obj["status"]["ui"]);
            Assert.Equal("blocked", (string)obj["host_status"]["ui"]);
        }

        // ---------------------------------------------------------------- round-trip fidelity

        [Fact]
        public void Inject_DoesNotReinterpretDateLikeStrings()
        {
            // Newtonsoft's default DateParseHandling would turn this into a DateTime and re-emit it
            // in a different shape — a silent wire-format change to a tool that never asked for one.
            const string original = "{\"success\":true,\"id\":\"2026-08-21T12:00:00Z\"}";

            // Asserted on the emitted text, not on a re-parse: a default JObject.Parse would apply
            // the very reinterpretation this pins against.
            var injected = StatusEnvelope.InjectCompact(original, HealthyStatus());

            Assert.Contains("\"id\":\"2026-08-21T12:00:00Z\"", injected);
        }

        [Fact]
        public void Inject_PreservesNumericPayloads()
        {
            const string original = "{\"x\":1.5,\"n\":9007199254740993,\"neg\":-0.25,\"z\":0}";

            var obj = JObject.Parse(StatusEnvelope.InjectCompact(original, HealthyStatus()));

            Assert.Equal(1.5, (double)obj["x"]);
            Assert.Equal(9007199254740993L, (long)obj["n"]);
            Assert.Equal(-0.25, (double)obj["neg"]);
            Assert.Equal(0, (int)obj["z"]);
        }

        [Fact]
        public void Inject_PreservesUnicodeAndEscapes()
        {
            const string original = "{\"name\":\"Kreis \\u00f8 \\\"quoted\\\"\",\"path\":\"C:\\\\tmp\\\\a.gh\"}";

            var obj = JObject.Parse(StatusEnvelope.InjectCompact(original, HealthyStatus()));

            Assert.Equal("Kreis ø \"quoted\"", (string)obj["name"]);
            Assert.Equal("C:\\tmp\\a.gh", (string)obj["path"]);
        }

        [Fact]
        public void Inject_KeepsTheResultParseableByTheErrorDetector()
        {
            // The choke point injects before McpResultFormatter decides isError; a status block must
            // never change whether a tool's failure is reported as a failure.
            var failure = StatusEnvelope.InjectCompact("{\"success\":false,\"error\":\"boom\"}", ModalStatus());
            var ok = StatusEnvelope.InjectCompact("{\"success\":true}", HealthyStatus());

            Assert.True(McpResultFormatter.IsErrorResult(failure));
            Assert.False(McpResultFormatter.IsErrorResult(ok));
        }

        [Fact]
        public void Inject_HandlesALargePayloadWithoutLoss()
        {
            var items = new JArray();
            for (int i = 0; i < 2000; i++)
                items.Add(new JObject { ["id"] = i, ["name"] = "component-" + i });
            var original = new JObject { ["success"] = true, ["components"] = items }
                .ToString(Formatting.None);

            var obj = JObject.Parse(StatusEnvelope.InjectCompact(original, HealthyStatus()));

            Assert.Equal(2000, ((JArray)obj["components"]).Count);
            Assert.Equal("component-1999", (string)obj["components"][1999]["name"]);
            Assert.Equal("wall-study.gh", (string)obj["status"]["document"]);
        }

        [Fact]
        public void Inject_EndToEnd_FromDerivedState()
        {
            // The path the server actually takes: derive from cached state, then inject.
            var state = new SolverState(() => T0, TimeSpan.FromSeconds(5));
            var doc = Guid.NewGuid();
            state.Heartbeat(doc, "live.gh");

            var status = state.Derive(new StatusInputs { ServerListening = true, Port = 26929 });
            var obj = JObject.Parse(StatusEnvelope.InjectCompact("{\"success\":true}", status));

            Assert.Equal("live.gh", (string)obj["status"]["document"]);
            Assert.Equal("responsive", (string)obj["status"]["ui"]);
            Assert.False((bool)obj["status"]["solving"]);
        }
    }
}
