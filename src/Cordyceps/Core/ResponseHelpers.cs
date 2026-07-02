using Newtonsoft.Json;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free JSON response formatting shared by every MCP tool. Extracted from
    /// <see cref="ToolHelpers"/> so the response shape can be unit-tested — this file is
    /// linked directly into Cordyceps.Tests, so it must stay free of Grasshopper/Rhino/DebugLog
    /// dependencies (BCL + Newtonsoft only).
    /// <see cref="ToolHelpers"/> keeps thin forwarders so existing call sites are unchanged.
    /// </summary>
    public static class ResponseHelpers
    {
        /// <summary>
        /// Create a success JSON response with optional data.
        /// </summary>
        /// <param name="data">Anonymous object with response data (should include success=true)</param>
        /// <returns>JSON string</returns>
        public static string SuccessResponse(object data)
        {
            return JsonConvert.SerializeObject(data);
        }

        /// <summary>
        /// Create an error JSON response.
        /// </summary>
        /// <param name="message">Error message</param>
        /// <returns>JSON string with success=false and error message</returns>
        public static string ErrorResponse(string message)
        {
            return JsonConvert.SerializeObject(new { success = false, error = message });
        }

        /// <summary>
        /// Create a simple success response with just success=true.
        /// </summary>
        public static string SimpleSuccess()
        {
            return JsonConvert.SerializeObject(new { success = true });
        }
    }
}
