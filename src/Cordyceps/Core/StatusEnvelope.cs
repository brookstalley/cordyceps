using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// Renders a <see cref="HostStatus"/> as JSON and folds a compact form into a tool's result
    /// string, so every MCP response carries "which document, and is the host healthy?" without
    /// touching any of the 19 tool files.
    ///
    /// <para>Every method here is total: a status block is an addition to someone else's payload,
    /// so a malformed, non-object or already-occupied result must come back usable rather than
    /// throw. A liveness feature that can break an unrelated tool result is worse than no liveness
    /// feature.</para>
    ///
    /// <para>Host-independent (no Grasshopper/Rhino references, no <c>DebugLog</c>) so the totality
    /// contract is unit-tested — <c>Cordyceps.Tests</c> links this file directly.
    /// Follows <see cref="McpResultFormatter"/>'s style: Newtonsoft, narrow catches,
    /// <see cref="Formatting.None"/> output.</para>
    /// </summary>
    public static class StatusEnvelope
    {
        /// <summary>The property the compact status block normally occupies.</summary>
        public const string StatusKey = "status";

        /// <summary>
        /// Fallback property used when a tool's own payload already has a <c>status</c> member.
        /// Overwriting the tool's data to make room for a diagnostic would be a silent data loss,
        /// so the block moves aside instead.
        /// </summary>
        public const string FallbackStatusKey = "host_status";

        /// <summary>
        /// The compact block that rides on every tool response. Deliberately small — it is added to
        /// hundreds of payloads per session — carrying only what changes an agent's next move:
        /// which document was acted on, whether the solver is busy, and whether the host needs a
        /// human. Healthy responses omit the hint entirely.
        /// </summary>
        public static JObject ToCompactJson(HostStatus status)
        {
            if (status == null) return null;

            var obj = new JObject
            {
                ["document"] = Str(status.DocumentName),
                ["ui"] = UiText(status.Ui),
                ["solving"] = status.Solving,
            };

            if (status.SolvingSince != null)
                obj["solving_since"] = Iso(status.SolvingSince);
            // Named only when a DIFFERENT file is the one solving — otherwise it is noise on every
            // response, and when it does appear it is the thing the caller needs to know.
            if (status.SolvingDocumentName != null && status.SolvingDocumentName != status.DocumentName)
                obj["solving_document"] = status.SolvingDocumentName;
            if (status.ModalInferred)
                obj["modal_inferred"] = true;
            if (!status.IsHealthy)
                obj["hint"] = status.Hint;

            return obj;
        }

        /// <summary>
        /// The full three-layer block, for the deliberate probe and the health endpoint. One layer
        /// per thing that can independently be wrong: the Rhino host, the Grasshopper solver, and
        /// the Cordyceps server itself.
        /// </summary>
        public static JObject ToFullJson(HostStatus status)
        {
            if (status == null) return null;

            return new JObject
            {
                ["healthy"] = status.IsHealthy,
                ["hint"] = Str(status.Hint),
                ["rhino"] = new JObject
                {
                    ["alive"] = status.RhinoAlive,
                    ["ui"] = UiText(status.Ui),
                    ["ui_responsive"] = status.Ui == UiLiveness.Responsive,
                    ["modal_inferred"] = status.ModalInferred,
                    ["last_heartbeat"] = Iso(status.LastHeartbeatUtc),
                    ["heartbeat_age_ms"] = status.HeartbeatAgeMs == null
                        ? JValue.CreateNull()
                        : new JValue(status.HeartbeatAgeMs.Value),
                },
                ["grasshopper"] = new JObject
                {
                    ["solving"] = status.Solving,
                    ["solving_since"] = Iso(status.SolvingSince),
                    // "document" is the focused one — what tools act on. "solving_document" is
                    // whichever definition is holding the UI thread, which need not be the same.
                    ["document"] = new JObject
                    {
                        ["name"] = Str(status.DocumentName),
                        ["id"] = Str(status.DocumentId?.ToString()),
                    },
                    ["solving_document"] = new JObject
                    {
                        ["name"] = Str(status.SolvingDocumentName),
                        ["id"] = Str(status.SolvingDocumentId?.ToString()),
                    },
                },
                ["cordyceps"] = new JObject
                {
                    ["listening"] = status.ServerListening,
                    ["port"] = status.Port,
                    ["in_flight_requests"] = status.InFlightRequests,
                    ["uptime_seconds"] = status.UptimeSeconds,
                    ["command_count"] = status.CommandCount,
                },
            };
        }

        /// <summary>
        /// The tool result for a deliberate liveness probe: <c>success:true</c> plus the full
        /// three-layer block. Always a success, however unhealthy the host is — the probe's job is
        /// to <em>report</em> a wedged host, so reporting one is the probe working, not failing.
        /// </summary>
        public static string ProbeResult(HostStatus status)
        {
            var payload = ToFullJson(status) ?? new JObject();
            payload.AddFirst(new JProperty("success", true));
            return payload.ToString(Formatting.None);
        }

        /// <summary>
        /// The tool result for an action that refuses to run because the host is busy or blocked.
        ///
        /// <para>Refusing beats queueing or blocking: a hidden queue gives the caller no completion
        /// signal, and blocking reproduces exactly the unbounded silence this feature exists to
        /// remove. The caller gets <c>solving</c> / <c>solving_since</c> so it can decide whether
        /// to wait, and the full status block so it can tell "wait" from "fetch a human".</para>
        /// </summary>
        public static string BusyResult(HostStatus status)
        {
            if (status == null)
                return "{\"success\":false,\"error\":\"The host is busy.\"}";

            return new JObject
            {
                ["success"] = false,
                ["error"] = Str(status.Hint),
                ["solving"] = status.Solving,
                ["solving_since"] = Iso(status.SolvingSince),
                ["status"] = ToFullJson(status),
            }.ToString(Formatting.None);
        }

        /// <summary>
        /// Add the compact block for <paramref name="status"/> to a tool's JSON result string.
        /// Convenience wrapper over <see cref="Inject(string, JObject)"/>.
        /// </summary>
        public static string InjectCompact(string toolJson, HostStatus status)
            => Inject(toolJson, ToCompactJson(status));

        /// <summary>
        /// Add <paramref name="status"/> to a tool's JSON result string under <c>status</c> (or
        /// <see cref="FallbackStatusKey"/> if the tool already uses that name), and re-serialize.
        ///
        /// <para>Returns <paramref name="toolJson"/> unchanged whenever it cannot be augmented
        /// safely: null/blank input, a payload that is not JSON (an <c>action='help'</c> text
        /// block), a JSON value that is not an object (an array or a bare scalar has nowhere to
        /// put a member), or an object that already occupies both key names. The caller gets a
        /// slightly less informative result, never a broken one.</para>
        /// </summary>
        public static string Inject(string toolJson, JObject status)
        {
            if (status == null) return toolJson;
            if (string.IsNullOrWhiteSpace(toolJson)) return toolJson;

            JToken token;
            try
            {
                token = ParsePreservingText(toolJson);
            }
            catch (JsonException)
            {
                // Not JSON at all (help text, a plain message). Nothing to inject into.
                return toolJson;
            }

            if (!(token is JObject obj))
                return toolJson;

            string key;
            if (obj[StatusKey] == null) key = StatusKey;
            else if (obj[FallbackStatusKey] == null) key = FallbackStatusKey;
            else return toolJson; // both taken — leave the tool's data alone

            obj[key] = status;
            return obj.ToString(Formatting.None);
        }

        /// <summary>
        /// Parse without letting Newtonsoft reinterpret the payload on the way through. Injection
        /// re-serializes someone else's result, so anything the parser "helpfully" converts would
        /// silently change the wire format of a tool that never asked for a status block:
        /// <see cref="DateParseHandling.None"/> keeps an ISO-8601-looking string a string, and
        /// <see cref="FloatParseHandling.Double"/> keeps numbers in the same representation the
        /// tool serialized them from.
        /// </summary>
        private static JToken ParsePreservingText(string json)
        {
            using (var stringReader = new StringReader(json))
            using (var reader = new JsonTextReader(stringReader)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
            })
            {
                var token = JToken.ReadFrom(reader);

                // Trailing content means this was not a single JSON document; treating it as one
                // would silently truncate the caller's payload.
                if (reader.Read())
                    throw new JsonReaderException("Unexpected content after the JSON value.");

                return token;
            }
        }

        private static string UiText(UiLiveness ui)
        {
            switch (ui)
            {
                case UiLiveness.Responsive: return "responsive";
                case UiLiveness.Blocked: return "blocked";
                default: return "unknown";
            }
        }

        /// <summary>
        /// A JSON string, or a genuine JSON null for a missing value. Newtonsoft types a
        /// <c>JValue((string)null)</c> as <c>String</c>, which reads back as a present-but-empty
        /// member; consumers checking for absence deserve a real null.
        /// </summary>
        private static JToken Str(string value)
            => value == null ? (JToken)JValue.CreateNull() : new JValue(value);

        private static JToken Iso(DateTime? value)
            => value == null
                ? (JToken)JValue.CreateNull()
                : new JValue(value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
    }
}
