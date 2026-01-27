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

        #region Display Name Helpers

        /// <summary>
        /// Get a display name for a component, with proper fallback chain:
        /// NickName (if not empty) -> Name (if not empty) -> Type name -> GUID
        /// </summary>
        /// <param name="obj">The document object</param>
        /// <returns>A non-empty display name</returns>
        public static string GetDisplayName(IGH_DocumentObject obj)
        {
            if (obj == null) return "null";

            // Try nickname first
            if (!string.IsNullOrEmpty(obj.NickName))
                return obj.NickName;

            // Then name
            if (!string.IsNullOrEmpty(obj.Name))
                return obj.Name;

            // Then type name
            var typeName = obj.GetType().Name;
            if (!string.IsNullOrEmpty(typeName))
                return typeName;

            // Last resort: GUID
            return obj.InstanceGuid.ToString();
        }

        /// <summary>
        /// Check if a component is the Cordyceps MCP server component (internal, should be filtered from results)
        /// </summary>
        /// <param name="obj">The document object to check</param>
        /// <returns>True if this is the Cordyceps component</returns>
        public static bool IsCordycepsComponent(IGH_DocumentObject obj)
        {
            if (obj == null) return false;

            var typeName = obj.GetType().Name;
            return typeName == "CordycepsComponent" || obj.Name == "Cordyceps" || obj.Name == "MCP";
        }

        /// <summary>
        /// Get the set of GUIDs for the Cordyceps component, all components directly connected to it,
        /// and any groups containing those components.
        /// These are internal infrastructure components that should be filtered from user-facing results.
        /// </summary>
        /// <param name="doc">The Grasshopper document</param>
        /// <returns>HashSet of GUIDs to filter out</returns>
        public static HashSet<Guid> GetCordycepsInfrastructureIds(GH_Document doc)
        {
            var infraIds = new HashSet<Guid>();
            if (doc == null) return infraIds;

            // Find the Cordyceps component
            IGH_Component cordycepsComp = null;
            foreach (var obj in doc.Objects)
            {
                if (obj is IGH_Component comp && IsCordycepsComponent(comp))
                {
                    cordycepsComp = comp;
                    infraIds.Add(comp.InstanceGuid);
                    break;
                }
            }

            if (cordycepsComp == null) return infraIds;

            // Find all components connected TO the Cordyceps component (sources of its inputs)
            foreach (var input in cordycepsComp.Params.Input)
            {
                foreach (var source in input.Sources)
                {
                    var sourceObj = source.Attributes?.GetTopLevel?.DocObject;
                    if (sourceObj != null)
                    {
                        infraIds.Add(sourceObj.InstanceGuid);
                    }
                }
            }

            // Find all components connected FROM the Cordyceps component (recipients of its outputs)
            foreach (var output in cordycepsComp.Params.Output)
            {
                foreach (var recipient in output.Recipients)
                {
                    var recipientObj = recipient.Attributes?.GetTopLevel?.DocObject;
                    if (recipientObj != null)
                    {
                        infraIds.Add(recipientObj.InstanceGuid);
                    }
                }
            }

            // Find any groups that contain infrastructure components
            foreach (var obj in doc.Objects)
            {
                if (obj is Grasshopper.Kernel.Special.GH_Group group)
                {
                    var memberIds = group.ObjectIDs;
                    if (memberIds != null)
                    {
                        // If any member of this group is infrastructure, the whole group is infrastructure
                        foreach (var memberId in memberIds)
                        {
                            if (infraIds.Contains(memberId))
                            {
                                infraIds.Add(group.InstanceGuid);
                                break;
                            }
                        }
                    }
                }
            }

            return infraIds;
        }

        /// <summary>
        /// Check if a component is part of the Cordyceps infrastructure (the component itself or connected to it)
        /// </summary>
        /// <param name="obj">The document object to check</param>
        /// <param name="infraIds">Pre-computed set of infrastructure IDs from GetCordycepsInfrastructureIds</param>
        /// <returns>True if this component should be filtered</returns>
        public static bool IsCordycepsInfrastructure(IGH_DocumentObject obj, HashSet<Guid> infraIds)
        {
            if (obj == null) return false;
            return infraIds.Contains(obj.InstanceGuid);
        }

        /// <summary>
        /// Check if a component is a compact type that should have relaxed vertical spacing checks
        /// (e.g., Number Slider, Value List)
        /// </summary>
        /// <param name="obj">The document object to check</param>
        /// <returns>True if this is a compact component type</returns>
        public static bool IsCompactComponent(IGH_DocumentObject obj)
        {
            if (obj == null) return false;

            var typeName = obj.GetType().Name;
            var name = obj.Name ?? "";

            // Number sliders and value lists are intentionally compact
            return typeName.Contains("NumberSlider") ||
                   typeName.Contains("ValueList") ||
                   name == "Number Slider" ||
                   name == "Value List" ||
                   typeName == "GH_NumberSlider" ||
                   typeName == "GH_ValueList";
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
