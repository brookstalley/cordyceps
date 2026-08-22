using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Rhino;
using Rhino.Display;

namespace Cordyceps.Core
{
    /// <summary>
    /// Centralized helper methods for MCP tool implementations.
    /// Reduces code duplication across tool classes for common patterns.
    /// </summary>
    public static class ToolHelpers
    {
        #region Document Operations

        /// <summary>
        /// Trigger a recompute that's safe inside cluster editors.
        /// On main canvas: normal NewSolution(true).
        /// Inside a cluster: expires only source objects (those with no non-hook upstream
        /// connections), then NewSolution(false). ExpireSolution cascades downstream through
        /// wires automatically, so all dependent objects get expired without needing to
        /// touch every object in the document. Input hooks are never expired, preserving
        /// their volatile data (populated by the parent cluster).
        /// </summary>
        public static void ClusterSafeRecompute(GH_Document doc)
        {
            if (doc.Owner != null)
            {
                // Inside a cluster editor — only expire source objects, cascade handles the rest
                var hookIds = new HashSet<Guid>(
                    doc.ClusterInputHooks().Select(h => h.InstanceGuid));

                foreach (var obj in doc.Objects)
                {
                    if (hookIds.Contains(obj.InstanceGuid)) continue;
                    if (obj is IGH_ActiveObject active && !HasNonHookSources(obj, hookIds))
                        active.ExpireSolution(false);
                }

                doc.NewSolution(false);
            }
            else
            {
                doc.NewSolution(true);
            }
        }

        /// <summary>
        /// Check if a document object has any upstream source that is NOT an input hook.
        /// Objects that return false are "source" objects in the cluster graph and need
        /// explicit expiration — everything downstream will cascade from them.
        /// </summary>
        private static bool HasNonHookSources(IGH_DocumentObject obj, HashSet<Guid> hookIds)
        {
            IEnumerable<IGH_Param> inputs;
            if (obj is IGH_Component comp)
                inputs = comp.Params.Input;
            else if (obj is IGH_Param param)
                inputs = new[] { param };
            else
                return false;

            foreach (var input in inputs)
            {
                foreach (var src in input.Sources)
                {
                    // Source is the hook itself (standalone param)
                    if (hookIds.Contains(src.InstanceGuid)) continue;
                    // Source's parent object is a hook
                    var parent = src.Attributes?.GetTopLevel?.DocObject;
                    if (parent != null && hookIds.Contains(parent.InstanceGuid)) continue;
                    return true;
                }
            }
            return false;
        }

        #endregion

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

        /// <summary>
        /// Get the document that owns a component. For components inside clusters,
        /// this returns the cluster's internal document, not the main canvas document.
        /// Always use this when calling NewSolution() after modifying a specific component.
        /// </summary>
        /// <param name="obj">The document object</param>
        /// <param name="fallback">Fallback document if OnPingDocument returns null</param>
        /// <returns>The owning document</returns>
        public static GH_Document GetOwnerDocument(IGH_DocumentObject obj, GH_Document fallback)
        {
            return obj?.OnPingDocument() ?? fallback;
        }

        #endregion

        #region GUID Parsing

        /// <summary>
        /// Try to parse a string as a GUID. Forwards to <see cref="ParseHelpers.TryParseGuid"/>
        /// (host-free, unit-tested there).
        /// </summary>
        /// <param name="id">The string to parse</param>
        /// <param name="guid">Output: the parsed GUID if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseGuid(string id, out Guid guid, out string error)
        {
            return ParseHelpers.TryParseGuid(id, out guid, out error);
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
        /// Check if an object is a valid, active document object (not orphaned/phantom).
        /// Filters out objects that may be left over from undo operations or other internal state.
        /// </summary>
        /// <param name="obj">The document object to check</param>
        /// <param name="doc">The expected parent document</param>
        /// <returns>True if the object is valid and active</returns>
        public static bool IsActiveDocumentObject(IGH_DocumentObject obj, GH_Document doc)
        {
            if (obj == null) return false;
            if (obj.Attributes == null) return false;

            // Check if the object's document reference matches the current document
            // OnPingDocument() returns null for orphaned objects
            var objDoc = obj.OnPingDocument();
            if (objDoc == null || objDoc != doc) return false;

            return true;
        }

        /// <summary>
        /// Get only the active, valid objects from a document, filtering out phantoms/orphans.
        /// </summary>
        /// <param name="doc">The Grasshopper document</param>
        /// <param name="infraIds">Pre-computed set of Cordyceps infrastructure IDs to exclude</param>
        /// <returns>Enumerable of active document objects</returns>
        public static IEnumerable<IGH_DocumentObject> GetActiveObjects(GH_Document doc, HashSet<Guid> infraIds)
        {
            if (doc == null) yield break;

            foreach (var obj in doc.Objects)
            {
                // Skip Cordyceps infrastructure
                if (IsCordycepsInfrastructure(obj, infraIds)) continue;

                // Skip orphaned/phantom objects
                if (!IsActiveDocumentObject(obj, doc)) continue;

                yield return obj;
            }
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
        /// Check if a GUID refers to Cordyceps infrastructure.
        /// </summary>
        public static bool IsProtectedId(GH_Document doc, Guid guid)
        {
            if (doc == null) return false;
            var infraIds = GetCordycepsInfrastructureIds(doc);
            return infraIds.Contains(guid);
        }

        /// <summary>
        /// Try to get a component, but fail silently (as "not found") if it's protected infrastructure.
        /// This makes infrastructure completely invisible - the LLM gets the same error whether the
        /// component doesn't exist or is protected.
        /// </summary>
        public static bool TryGetUnprotectedComponent(GrasshopperContext context, string id,
            out IGH_DocumentObject obj, out string error)
        {
            obj = null;

            if (!TryGetActiveDocument(context, out var doc, out error))
                return false;

            if (!TryParseGuid(id, out var guid, out error))
                return false;

            // Check if protected BEFORE checking if it exists
            if (IsProtectedId(doc, guid))
            {
                error = $"Component not found: {guid}";
                return false;
            }

            if (!TryFindComponent(doc, guid, out obj, out error))
                return false;

            return true;
        }

        /// <summary>
        /// Try to get a component with document reference, but fail silently if protected.
        /// </summary>
        public static bool TryGetUnprotectedComponentWithDoc(GrasshopperContext context, string id,
            out GH_Document doc, out IGH_DocumentObject obj, out string error)
        {
            doc = null;
            obj = null;

            if (!TryGetActiveDocument(context, out doc, out error))
                return false;

            if (!TryParseGuid(id, out var guid, out error))
                return false;

            // Check if protected BEFORE checking if it exists
            if (IsProtectedId(doc, guid))
            {
                error = $"Component not found: {guid}";
                return false;
            }

            if (!TryFindComponent(doc, guid, out obj, out error))
                return false;

            return true;
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

        // Forwarders to ResponseHelpers (host-free, unit-tested there).

        /// <summary>
        /// Create a success JSON response with optional data.
        /// </summary>
        /// <param name="data">Anonymous object with response data (should include success=true)</param>
        /// <returns>JSON string</returns>
        public static string SuccessResponse(object data)
        {
            return ResponseHelpers.SuccessResponse(data);
        }

        /// <summary>
        /// Create an error JSON response.
        /// </summary>
        /// <param name="message">Error message</param>
        /// <returns>JSON string with success=false and error message</returns>
        public static string ErrorResponse(string message)
        {
            return ResponseHelpers.ErrorResponse(message);
        }

        /// <summary>
        /// Create a simple success response with just success=true.
        /// </summary>
        public static string SimpleSuccess()
        {
            return ResponseHelpers.SimpleSuccess();
        }

        #endregion

        #region Undo Records

        /// <summary>
        /// Run a mutating Rhino action inside one named undo record so a single Ctrl-Z reverts
        /// the whole action instead of one internal step (RSC-6K1W — e.g. a bulk set_layer of
        /// fifty objects previously left fifty undo steps). Must be called on the UI thread
        /// (inside ExecuteOnUiThread), alongside the document mutation it brackets.
        ///
        /// Verified RhinoCommon semantics (api-notes-rhinocommon.md): BeginUndoRecord returns 0
        /// when a record is already active (nesting, or recording disabled) — EndUndoRecord is
        /// skipped in that case, so nested wrappers and native commands' own records stay safe.
        /// </summary>
        /// <param name="doc">The document being mutated; null runs the body unwrapped
        /// (the body reports its own no-document error).</param>
        /// <param name="actionName">Tool action name; the undo record appears in Rhino's undo
        /// stack as "Cordyceps &lt;actionName&gt;".</param>
        /// <param name="body">The action body to bracket.</param>
        public static string WithUndoRecord(Rhino.RhinoDoc doc, string actionName, Func<string> body)
        {
            if (doc == null) return body();

            uint serial = doc.BeginUndoRecord($"Cordyceps {actionName}");
            try
            {
                return body();
            }
            finally
            {
                if (serial != 0) doc.EndUndoRecord(serial);
            }
        }

        #endregion

        #region Component Info Helpers

        /// <summary>
        /// Get category and subcategory information for a component from its proxy.
        /// Uses ComponentGuid for accurate lookup with fallback to name-based lookup.
        /// </summary>
        /// <param name="obj">The document object</param>
        /// <param name="category">Output: the category (may be null)</param>
        /// <param name="subcategory">Output: the subcategory (may be null)</param>
        /// <returns>True if proxy info was found</returns>
        public static bool TryGetProxyInfo(IGH_DocumentObject obj, out string category, out string subcategory)
        {
            category = null;
            subcategory = null;

            if (obj == null) return false;

            IGH_ObjectProxy proxy = null;

            // Try ComponentGuid first (most accurate)
            if (obj is IGH_ActiveObject activeObj)
            {
                proxy = Instances.ComponentServer.ObjectProxies
                    .FirstOrDefault(p => p.Guid == activeObj.ComponentGuid);
            }

            // Fallback to name-based lookup
            if (proxy == null)
            {
                proxy = Instances.ComponentServer.ObjectProxies
                    .FirstOrDefault(p => p.Desc.Name == obj.Name);
            }

            if (proxy != null)
            {
                category = proxy.Desc.Category;
                subcategory = proxy.Desc.SubCategory;
                return true;
            }

            // Fallback for IGH_Component
            if (obj is IGH_Component comp)
            {
                category = comp.Category;
                subcategory = comp.SubCategory;
                return true;
            }

            // Fallback for IGH_Param
            if (obj is IGH_Param)
            {
                category = "Params";
                subcategory = "Unknown";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Build a list of parameter info objects for inputs or outputs.
        /// </summary>
        /// <param name="parameters">The parameter list</param>
        /// <param name="isInput">True for input params (include sourceCount), false for output (include recipientCount)</param>
        /// <returns>List of anonymous objects with parameter info</returns>
        public static List<object> BuildParameterList(IList<IGH_Param> parameters, bool isInput)
        {
            var result = new List<object>();
            foreach (var param in parameters)
            {
                if (isInput)
                {
                    result.Add(new
                    {
                        name = param.Name,
                        nickname = param.NickName,
                        type = param.TypeName,
                        sourceCount = param.SourceCount,
                        optional = param.Optional,
                        modifiers = BuildModifierInfo(param)
                    });
                }
                else
                {
                    result.Add(new
                    {
                        name = param.Name,
                        nickname = param.NickName,
                        type = param.TypeName,
                        recipientCount = param.Recipients.Count,
                        modifiers = BuildModifierInfo(param)
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Report a parameter's data modifiers — the Flatten/Graft/Simplify/Reverse options
        /// Grasshopper puts on a port's right-click menu. Without this, a document round-tripped
        /// through the API silently loses them and a caller cannot even detect that a Graft exists.
        /// </summary>
        /// <remarks>
        /// Emits the same lowercase mapping names <c>gh_canvas(action='modifier')</c> accepts, so
        /// a reported state feeds straight back in as an argument.
        /// </remarks>
        public static object BuildModifierInfo(IGH_Param param)
        {
            return new
            {
                mapping = DataModifiers.MappingName(ToMappingChoice(param.DataMapping)),
                simplify = param.Simplify,
                reverse = param.Reverse
            };
        }

        /// <summary>
        /// Bridge Grasshopper's <see cref="GH_DataMapping"/> to the host-free
        /// <see cref="DataMappingChoice"/>. Mapped by name rather than cast, so the host-free
        /// enum never becomes silently coupled to Grasshopper's member ordering.
        /// </summary>
        public static DataMappingChoice ToMappingChoice(GH_DataMapping mapping)
        {
            switch (mapping)
            {
                case GH_DataMapping.Flatten: return DataMappingChoice.Flatten;
                case GH_DataMapping.Graft: return DataMappingChoice.Graft;
                default: return DataMappingChoice.None;
            }
        }

        /// <summary>
        /// Bridge the host-free <see cref="DataMappingChoice"/> back to Grasshopper's
        /// <see cref="GH_DataMapping"/>.
        /// </summary>
        public static GH_DataMapping ToGhDataMapping(DataMappingChoice choice)
        {
            switch (choice)
            {
                case DataMappingChoice.Flatten: return GH_DataMapping.Flatten;
                case DataMappingChoice.Graft: return GH_DataMapping.Graft;
                default: return GH_DataMapping.None;
            }
        }

        /// <summary>
        /// Instantiate a component proxy and invoke <paramref name="project"/> with the
        /// resulting <see cref="IGH_Component"/> so callers can read its parameter metadata.
        /// Centralizes the proxy-instantiation scaffold shared by component documentation and
        /// search. On failure it logs and returns without invoking the callback — callers
        /// degrade to whatever info they already have.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the callback ran (the proxy instantiated to an <see cref="IGH_Component"/>);
        /// <c>false</c> if instantiation failed or produced a non-component, so callers can surface a
        /// "parameters unavailable" signal instead of reporting empty params as authoritative.
        /// </returns>
        public static bool WithProxyComponent(IGH_ObjectProxy proxy, Action<IGH_Component> project)
        {
            try
            {
                var instance = proxy.CreateInstance();
                if (instance is IGH_Component comp)
                {
                    project(comp);
                    return true;
                }
                return false;
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- foreign component-proxy instantiation can throw arbitrary plugin/reflection errors; log and degrade to basic info
            {
                DebugLog.Warn($"Failed to instantiate proxy '{proxy?.Desc?.Name}' ({proxy?.Guid}) for parameter enumeration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Build a basic component info dictionary with common fields.
        /// </summary>
        /// <param name="obj">The document object</param>
        /// <returns>Dictionary with id, name, nickname, type, x, y</returns>
        public static Dictionary<string, object> BuildBasicComponentInfo(IGH_DocumentObject obj)
        {
            return new Dictionary<string, object>
            {
                ["id"] = obj.InstanceGuid.ToString(),
                ["name"] = obj.Name,
                ["nickname"] = obj.NickName,
                ["type"] = obj.GetType().Name,
                ["x"] = obj.Attributes.Pivot.X,
                ["y"] = obj.Attributes.Pivot.Y
            };
        }

        /// <summary>
        /// Build full component info dictionary including inputs/outputs and category.
        /// </summary>
        /// <param name="obj">The document object</param>
        /// <param name="includeSuccess">If true, include success=true in result</param>
        /// <returns>Dictionary with full component information</returns>
        public static Dictionary<string, object> BuildFullComponentInfo(IGH_DocumentObject obj, bool includeSuccess = false)
        {
            var info = BuildBasicComponentInfo(obj);

            if (includeSuccess)
            {
                info["success"] = true;
            }

            // Get category/subcategory from proxy
            TryGetProxyInfo(obj, out var category, out var subcategory);

            if (obj is IGH_Component comp)
            {
                info["category"] = category ?? comp.Category;
                info["subcategory"] = subcategory ?? comp.SubCategory;
                info["role"] = ComponentRegistry.GetRole(
                    info["category"]?.ToString(),
                    info["subcategory"]?.ToString());
                info["inputCount"] = comp.Params.Input.Count;
                info["outputCount"] = comp.Params.Output.Count;
                info["runtimeMessageLevel"] = comp.RuntimeMessageLevel.ToString();
                info["inputs"] = BuildParameterList(comp.Params.Input, true);
                info["outputs"] = BuildParameterList(comp.Params.Output, false);
            }
            else if (obj is IGH_Param param)
            {
                info["category"] = category ?? "Params";
                info["subcategory"] = subcategory ?? "Unknown";
                info["role"] = ComponentRegistry.GetRole(
                    info["category"]?.ToString(),
                    info["subcategory"]?.ToString());
                info["sourceCount"] = param.SourceCount;
                info["recipientCount"] = param.Recipients.Count;
                info["dataCount"] = param.VolatileDataCount;
                info["modifiers"] = BuildModifierInfo(param);
            }

            return info;
        }

        /// <summary>
        /// Build component info for list views (GetAllComponents, GetComponentByNickname).
        /// Includes category/role but not full input/output details.
        /// </summary>
        public static Dictionary<string, object> BuildListComponentInfo(IGH_DocumentObject obj)
        {
            var info = BuildBasicComponentInfo(obj);

            TryGetProxyInfo(obj, out var category, out var subcategory);

            if (obj is IGH_Component comp)
            {
                info["category"] = category ?? comp.Category;
                info["subcategory"] = subcategory ?? comp.SubCategory;
                info["role"] = ComponentRegistry.GetRole(
                    info["category"]?.ToString(),
                    info["subcategory"]?.ToString());
                info["inputCount"] = comp.Params.Input.Count;
                info["outputCount"] = comp.Params.Output.Count;
                info["runtimeMessageLevel"] = comp.RuntimeMessageLevel.ToString();
            }
            else if (obj is IGH_Param param)
            {
                info["category"] = category ?? "Params";
                info["subcategory"] = subcategory ?? "Unknown";
                info["role"] = ComponentRegistry.GetRole(
                    info["category"]?.ToString(),
                    info["subcategory"]?.ToString());
                info["sourceCount"] = param.SourceCount;
                info["recipientCount"] = param.Recipients.Count;
            }

            return info;
        }

        #endregion

        #region JSON Deserialization Helpers

        // Forwarders to ParseHelpers (host-free, unit-tested there).

        /// <summary>
        /// Try to deserialize a JSON string to a list with null checking.
        /// </summary>
        /// <typeparam name="T">Element type</typeparam>
        /// <param name="json">JSON string to parse</param>
        /// <param name="result">Output: the parsed list if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if deserialization succeeded and result is not null, false otherwise</returns>
        public static bool TryDeserializeList<T>(string json, out List<T> result, out string error)
        {
            return ParseHelpers.TryDeserializeList(json, out result, out error);
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
            return ParseHelpers.TryDeserializeArray(json, out result, out error);
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
            return ParseHelpers.TryParseGuidArray(json, out guids, out error);
        }

        #endregion

        #region Rhino Document Helpers

        /// <summary>
        /// Try to get the active Rhino document.
        /// </summary>
        /// <param name="doc">Output: the active Rhino document if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if document exists, false otherwise</returns>
        public static bool TryGetRhinoDoc(out RhinoDoc doc, out string error)
        {
            doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                error = "No active Rhino document";
                return false;
            }
            error = null;
            return true;
        }

        #endregion

        #region Color Utilities

        // ColorToHex(Color), TryParseColor, ParseBool, and TryParseBool forward to
        // ParseHelpers (host-free, unit-tested there).

        /// <summary>
        /// Convert a System.Drawing.Color to hex string (#RRGGBB format).
        /// </summary>
        public static string ColorToHex(Color color)
        {
            return ParseHelpers.ColorToHex(color);
        }

        /// <summary>
        /// Convert a Rhino Color4f to hex string (#RRGGBB format).
        /// </summary>
        public static string ColorToHex(Color4f color)
        {
            return $"#{(int)(color.R * 255):X2}{(int)(color.G * 255):X2}{(int)(color.B * 255):X2}";
        }

        /// <summary>
        /// Parse color from hex (#RRGGBB or RRGGBB), RGB (R,G,B), or named color (Red, Blue, etc.) format.
        /// </summary>
        /// <param name="colorStr">Color string to parse</param>
        /// <param name="color">Output: parsed color if successful</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseColor(string colorStr, out Color color)
        {
            return ParseHelpers.TryParseColor(colorStr, out color);
        }

        /// <summary>
        /// Parse a boolean string value with a default fallback.
        /// Accepts: "true", "false", "1", "0" (case-insensitive)
        /// </summary>
        /// <param name="value">String value to parse</param>
        /// <param name="defaultValue">Default value if parsing fails</param>
        /// <returns>Parsed boolean or default value</returns>
        public static bool ParseBool(string value, bool defaultValue = false)
        {
            return ParseHelpers.ParseBool(value, defaultValue);
        }

        /// <summary>
        /// Strictly parse a boolean string. Accepts true/false, 1/0, yes/no (case-insensitive,
        /// trimmed); anything else fails. Use this where an unrecognized value must surface as a
        /// validation error; use <see cref="ParseBool"/> where a default is genuinely wanted.
        /// </summary>
        /// <param name="value">String value to parse</param>
        /// <param name="result">Output: parsed boolean if successful</param>
        /// <returns>True if the value was recognized, false otherwise</returns>
        public static bool TryParseBool(string value, out bool result)
        {
            return ParseHelpers.TryParseBool(value, out result);
        }

        /// <summary>
        /// Try to parse a Point3d from a comma-separated string "x,y,z".
        /// Thin Rhino-typed wrapper over <see cref="ParseHelpers.TryParseXyz"/> (host-free,
        /// unit-tested there), which parses components with the invariant culture ('.' decimal
        /// separator) so the wire format is stable regardless of the host machine's locale —
        /// current-culture parsing on comma-decimal locales would mis-split "10.5,0,3.2"
        /// catastrophically.
        /// </summary>
        /// <param name="value">String in "x,y,z" format</param>
        /// <param name="result">Output: parsed Point3d if successful</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParsePoint3d(string value, out Rhino.Geometry.Point3d result)
        {
            if (!ParseHelpers.TryParseXyz(value, out var x, out var y, out var z))
            {
                result = Rhino.Geometry.Point3d.Unset;
                return false;
            }

            result = new Rhino.Geometry.Point3d(x, y, z);
            return true;
        }

        #endregion
    }
}
