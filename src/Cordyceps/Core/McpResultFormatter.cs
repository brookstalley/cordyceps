using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// Pure helpers for formatting tool results at the MCP boundary.
    /// Host-independent (no Grasshopper/Rhino references) so it can be unit-tested
    /// — see <c>Cordyceps.Tests</c>, which links this file directly.
    /// </summary>
    public static class McpResultFormatter
    {
        /// <summary>
        /// Determine whether a tool result string represents a failure, for the
        /// MCP <c>isError</c> flag. A result is an error iff it parses as a JSON
        /// object whose <c>success</c> member is the boolean <c>false</c>
        /// (the response contract: every tool returns a JSON object with a boolean
        /// <c>success</c> field). Unparseable, non-object, missing <c>success</c>,
        /// non-boolean <c>success</c>, or <c>success: true</c> ⇒ not an error — this
        /// preserves the prior default for help text and other non-contract payloads.
        /// </summary>
        public static bool IsErrorResult(string resultText)
        {
            if (string.IsNullOrWhiteSpace(resultText))
                return false;

            try
            {
                var token = JToken.Parse(resultText);
                if (token is JObject obj &&
                    obj.TryGetValue("success", out var success) &&
                    success.Type == JTokenType.Boolean)
                {
                    return !success.Value<bool>();
                }
            }
            catch (JsonException)
            {
                // Result is not JSON (e.g. an action='help' or plain-text payload):
                // we can't determine failure, so treat it as a non-error.
            }

            return false;
        }

        /// <summary>
        /// Format an exception thrown by a tool method into the standard
        /// <c>{ success: false, error }</c> payload, so a tool-execution failure
        /// surfaces as a tool result (<c>isError: true</c>) rather than a JSON-RPC
        /// protocol error. Unwraps reflection's <see cref="TargetInvocationException"/>
        /// to report the real cause's message.
        /// </summary>
        public static string FormatExceptionResult(Exception ex)
        {
            var cause = (ex is TargetInvocationException && ex.InnerException != null)
                ? ex.InnerException
                : ex;
            return JsonConvert.SerializeObject(new { success = false, error = cause.Message });
        }
    }
}
