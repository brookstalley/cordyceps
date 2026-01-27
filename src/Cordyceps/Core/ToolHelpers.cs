using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Newtonsoft.Json;

namespace Cordyceps.Core
{
    /// <summary>
    /// Centralized helper methods for MCP tool implementations.
    /// Reduces code duplication across tool classes for common patterns.
    /// </summary>
    public static class ToolHelpers
    {
        #region Document Validation

        /// <summary>
        /// Try to get the active Grasshopper document.
        /// </summary>
        /// <param name="context">The GrasshopperContext to use</param>
        /// <param name="doc">Output: the active document if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if document exists, false otherwise</returns>
        public static bool TryGetActiveDocument(GrasshopperContext context, out GH_Document doc, out string error)
        {
            doc = context?.GetActiveDocument();
            if (doc == null)
            {
                error = "No active Grasshopper document";
                return false;
            }
            error = null;
            return true;
        }

        #endregion

        #region GUID Parsing

        /// <summary>
        /// Try to parse a string as a GUID.
        /// </summary>
        /// <param name="id">The string to parse</param>
        /// <param name="guid">Output: the parsed GUID if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseGuid(string id, out Guid guid, out string error)
        {
            if (string.IsNullOrEmpty(id))
            {
                guid = Guid.Empty;
                error = "Component ID is required";
                return false;
            }

            if (!Guid.TryParse(id, out guid))
            {
                error = "Invalid component ID format";
                return false;
            }
            error = null;
            return true;
        }

        #endregion

        #region Component Lookup

        /// <summary>
        /// Try to find a component in the document by GUID.
        /// </summary>
        /// <param name="doc">The document to search</param>
        /// <param name="guid">The GUID to find</param>
        /// <param name="obj">Output: the found object if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if component found, false otherwise</returns>
        public static bool TryFindComponent(GH_Document doc, Guid guid, out IGH_DocumentObject obj, out string error)
        {
            obj = doc?.FindObject(guid, true);
            if (obj == null)
            {
                error = $"Component not found: {guid}";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Combined helper: get active document + parse GUID + find component.
        /// Most common pattern used in tool methods.
        /// </summary>
        /// <param name="context">The GrasshopperContext to use</param>
        /// <param name="id">The component ID string to parse</param>
        /// <param name="obj">Output: the found object if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if all steps succeeded, false otherwise</returns>
        public static bool TryGetComponent(GrasshopperContext context, string id, out IGH_DocumentObject obj, out string error)
        {
            obj = null;

            if (!TryGetActiveDocument(context, out var doc, out error))
                return false;

            if (!TryParseGuid(id, out var guid, out error))
                return false;

            if (!TryFindComponent(doc, guid, out obj, out error))
                return false;

            return true;
        }

        /// <summary>
        /// Combined helper with document output: get active document + parse GUID + find component.
        /// Use when you also need the document reference.
        /// </summary>
        public static bool TryGetComponentWithDoc(GrasshopperContext context, string id,
            out GH_Document doc, out IGH_DocumentObject obj, out string error)
        {
            doc = null;
            obj = null;

            if (!TryGetActiveDocument(context, out doc, out error))
                return false;

            if (!TryParseGuid(id, out var guid, out error))
                return false;

            if (!TryFindComponent(doc, guid, out obj, out error))
                return false;

            return true;
        }

        #endregion

        #region JSON Response Helpers

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

        #endregion

        #region JSON Deserialization Helpers

        /// <summary>
        /// Try to deserialize a JSON string to a list with null checking.
        /// </summary>
        /// <typeparam name="T">Element type</typeparam>
        /// <param name="json">JSON string to parse</param>
        /// <param name="result">Output: the parsed list if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if deserialization succeeded and result is not null/empty, false otherwise</returns>
        public static bool TryDeserializeList<T>(string json, out List<T> result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "JSON input is required";
                return false;
            }

            try
            {
                result = JsonConvert.DeserializeObject<List<T>>(json);
                if (result == null)
                {
                    error = "Failed to parse JSON array";
                    return false;
                }
                return true;
            }
            catch (JsonException ex)
            {
                error = $"Invalid JSON format: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Try to deserialize a JSON string to an array with null checking.
        /// </summary>
        /// <typeparam name="T">Element type</typeparam>
        /// <param name="json">JSON string to parse</param>
        /// <param name="result">Output: the parsed array if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if deserialization succeeded and result is not null, false otherwise</returns>
        public static bool TryDeserializeArray<T>(string json, out T[] result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "JSON input is required";
                return false;
            }

            try
            {
                result = JsonConvert.DeserializeObject<T[]>(json);
                if (result == null)
                {
                    error = "Failed to parse JSON array";
                    return false;
                }
                return true;
            }
            catch (JsonException ex)
            {
                error = $"Invalid JSON format: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Try to parse a JSON array of GUIDs.
        /// </summary>
        /// <param name="json">JSON array of GUID strings</param>
        /// <param name="guids">Output: list of parsed GUIDs if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if all GUIDs parsed successfully, false otherwise</returns>
        public static bool TryParseGuidArray(string json, out List<Guid> guids, out string error)
        {
            guids = null;

            if (!TryDeserializeArray<string>(json, out var idStrings, out error))
                return false;

            guids = new List<Guid>(idStrings.Length);
            foreach (var idStr in idStrings)
            {
                if (!Guid.TryParse(idStr, out var guid))
                {
                    error = $"Invalid GUID in array: {idStr}";
                    guids = null;
                    return false;
                }
                guids.Add(guid);
            }

            return true;
        }

        #endregion
    }
}
