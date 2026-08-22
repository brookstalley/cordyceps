using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cordyceps.Core
{
    /// <summary>
    /// Pure, host-free JSON-RPC 2.0 response-envelope policy for the MCP HTTP transport
    /// (BCL System.Text.Json only, unit-tested in Cordyceps.Tests). Extracted from
    /// <c>McpServer.HandleMcpPostAsync</c> so the id-echo contract can't silently regress:
    ///
    /// <para>- Notifications are detected from an ABSENT <c>id</c> property only. An explicit
    /// <c>"id": null</c> is NOT a notification — it still gets a response, with <c>"id": null</c>
    /// echoed back.</para>
    ///
    /// <para>- The id is echoed losslessly via <see cref="JsonElement.Clone"/>: long, fractional
    /// (1.0 stays 1.0, not 1), string, and null ids round-trip exactly as the client sent them.</para>
    ///
    /// <para>- A boxed <see cref="JsonElement"/> of <see cref="JsonValueKind.Null"/> serializes as
    /// <c>null</c> but is not a null reference, so the <c>WhenWritingNull</c> serializer option in
    /// <see cref="Serialize"/> does not strip the <c>"id"</c> property.</para>
    ///
    /// <para>Callers compute <see cref="EchoId"/> BEFORE dispatching the method: echoing up front
    /// means a malformed id can never destroy the response after the tool's side effects ran
    /// (which would make the client retry and double-apply).</para>
    /// </summary>
    public static class JsonRpcEnvelope
    {
        // Matches the serialization the transport has always used: compact output, member names
        // verbatim (no naming policy — payload fields are already authored at their wire names).
        //
        // WhenWritingNull does NOT drop a null "result" here, despite how it reads: the condition
        // applies to POCO properties, and the envelope is a Dictionary<string, object>, whose values
        // it does not govern. That is the behavior we want — JSON-RPC 2.0 requires a response to
        // carry either "result" or "error", so omitting a null result would emit a malformed
        // response. Pinned by JsonRpcWireFormatTests; do not "fix" this to omit nulls.
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// JSON-RPC 2.0: a request is a notification (MUST NOT receive a response) only when the
        /// <c>id</c> property is absent. An explicit <c>"id": null</c> is not a notification.
        /// </summary>
        public static bool IsNotification(JsonElement request)
        {
            bool hasId = request.TryGetProperty("id", out var id);
            return !hasId || id.ValueKind == JsonValueKind.Undefined;
        }

        /// <summary>
        /// The id to echo in the response: a lossless <see cref="JsonElement.Clone"/> of the
        /// request id (safe to use after the source JsonDocument is disposed), or null when the
        /// request is a notification (no response is sent at all).
        /// </summary>
        public static JsonElement? EchoId(JsonElement request)
        {
            if (IsNotification(request))
                return null;
            return request.GetProperty("id").Clone();
        }

        /// <summary>
        /// Build the JSON-RPC 2.0 response envelope. Emits <c>error</c> (with
        /// <paramref name="errorCode"/>/<paramref name="errorMessage"/>) when
        /// <paramref name="errorMessage"/> is non-null, otherwise <c>result</c>. The
        /// <c>id</c> member is included exactly when <paramref name="responseId"/> has a value —
        /// including an explicit <c>"id": null</c> when the client sent null.
        /// </summary>
        public static Dictionary<string, object> Build(JsonElement? responseId, object result, string errorMessage, int errorCode)
        {
            var responseObj = new Dictionary<string, object> { ["jsonrpc"] = "2.0" };

            if (responseId.HasValue)
                responseObj["id"] = responseId.Value;

            if (errorMessage != null)
            {
                responseObj["error"] = new Dictionary<string, object>
                {
                    ["code"] = errorCode,
                    ["message"] = errorMessage
                };
            }
            else
            {
                responseObj["result"] = result;
            }

            return responseObj;
        }

        /// <summary>
        /// Serialize a response envelope with the transport's serializer options (compact,
        /// WhenWritingNull — see <see cref="Build"/> for why a null id still survives).
        /// </summary>
        public static string Serialize(Dictionary<string, object> response)
        {
            return JsonSerializer.Serialize(response, SerializerOptions);
        }
    }
}
