using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;

namespace Cordyceps.Core
{
    /// <summary>
    /// Registry for looking up Grasshopper components by name or GUID
    /// </summary>
    public static class ComponentRegistry
    {
        // Constants
        private const int MAX_COMPONENTS_LIST = 100;
        private const int MAX_SEARCH_RESULTS = 50;

        // Common component name aliases
        private static readonly Dictionary<string, string> NameAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Script components
            { "c#", "C# Script" },
            { "csharp", "C# Script" },
            { "csharp script", "C# Script" },
            { "python", "Python 3 Script" },
            { "python script", "Python 3 Script" },
            { "ghpython", "Python 3 Script" },

            // Planes
            { "plane", "XY Plane" },
            { "xyplane", "XY Plane" },
            { "xy", "XY Plane" },
            { "xzplane", "XZ Plane" },
            { "xz", "XZ Plane" },
            { "yzplane", "YZ Plane" },
            { "yz", "YZ Plane" },

            // Geometry
            { "cube", "Box" },
            { "rect", "Rectangle" },
            { "circ", "Circle" },
            { "cyl", "Cylinder" },
            { "pt", "Point" },
            { "ln", "Line" },
            { "crv", "Curve" },

            // Parameters
            { "slider", "Number Slider" },
            { "numberslider", "Number Slider" },
            { "num", "Number" },
            { "int", "Integer" },
            { "bool", "Boolean" },
            { "str", "Text" },
            { "string", "Text" },

            // Stream filter
            { "streamfilter", "Stream Filter" },
            { "filter", "Stream Filter" },
        };

        // Known component GUIDs (for common components that might be hard to find by name)
        private static readonly Dictionary<string, Guid> KnownGuids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
        {
            { "C# Script", new Guid("b6ba1144-02d6-4a2d-b53c-ec62e290eeb7") },
            { "Python 3 Script", new Guid("c9b2d725-6f87-4b07-af90-bd9aefef68eb") },
            { "Stream Filter", new Guid("fb97f83d-9f77-4c6c-b134-1ddfec17e8aa") },
        };

        /// <summary>
        /// Create a component by type name or GUID
        /// </summary>
        public static IGH_DocumentObject CreateComponent(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentException("Component type is required");
            }

            // Check if type is a GUID
            if (Guid.TryParse(type, out Guid guid))
            {
                return CreateByGuid(guid);
            }

            // Normalize the type name
            string normalizedType = NormalizeTypeName(type);

            // Try known GUIDs first (most reliable)
            if (KnownGuids.TryGetValue(normalizedType, out Guid knownGuid))
            {
                var component = CreateByGuid(knownGuid);
                if (component != null) return component;
            }

            // Try creating by name
            return CreateByName(normalizedType);
        }

        /// <summary>
        /// Create a component by GUID
        /// </summary>
        public static IGH_DocumentObject CreateByGuid(Guid guid)
        {
            return Instances.ComponentServer.EmitObject(guid) as IGH_DocumentObject;
        }

        /// <summary>
        /// Create a component by name (searches component server)
        /// </summary>
        public static IGH_DocumentObject CreateByName(string name)
        {
            // Special handling for built-in parameter types
            switch (name.ToLowerInvariant())
            {
                case "panel":
                    return new GH_Panel();

                case "number slider":
                    try
                    {
                        DebugLog.Debug("Creating GH_NumberSlider...");
                        var slider = new GH_NumberSlider();
                        DebugLog.Debug("Setting init code...");
                        slider.SetInitCode("0.0 < 0.5 < 1.0");
                        DebugLog.Debug("Number slider created successfully");
                        return slider;
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Error($"Failed to create Number Slider: {ex.Message}");
                        DebugLog.Error($"Stack: {ex.StackTrace}");
                        throw;
                    }

                case "point":
                    return new Param_Point();

                case "curve":
                    return new Param_Curve();

                case "line":
                    return new Param_Line();

                case "circle":
                    return new Param_Circle();

                case "number":
                    return new Param_Number();

                case "integer":
                    return new Param_Integer();

                case "boolean":
                    return new Param_Boolean();

                case "text":
                    return new Param_String();

                case "brep":
                    return new Param_Brep();

                case "mesh":
                    return new Param_Mesh();

                case "surface":
                    return new Param_Surface();

                case "geometry":
                    return new Param_Geometry();
            }

            // Search component server for matching proxy
            var proxy = Instances.ComponentServer.ObjectProxies
                .FirstOrDefault(p => p.Desc.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (proxy != null)
            {
                return proxy.CreateInstance() as IGH_DocumentObject;
            }

            // Try fuzzy match
            proxy = Instances.ComponentServer.ObjectProxies
                .FirstOrDefault(p => p.Desc.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

            return proxy?.CreateInstance() as IGH_DocumentObject;
        }

        /// <summary>
        /// Search for available components matching a query
        /// </summary>
        public static List<ComponentInfo> SearchComponents(string query)
        {
            var results = new List<ComponentInfo>();

            if (string.IsNullOrWhiteSpace(query))
            {
                // Return all component names
                foreach (var proxy in Instances.ComponentServer.ObjectProxies.Take(MAX_COMPONENTS_LIST))
                {
                    results.Add(new ComponentInfo
                    {
                        Name = proxy.Desc.Name,
                        Description = proxy.Desc.Description,
                        Category = proxy.Desc.Category,
                        SubCategory = proxy.Desc.SubCategory,
                        Guid = proxy.Guid.ToString()
                    });
                }
            }
            else
            {
                // Search for matching components
                var normalizedQuery = query.ToLowerInvariant();
                var matches = Instances.ComponentServer.ObjectProxies
                    .Where(p => p.Desc.Name.ToLowerInvariant().Contains(normalizedQuery)
                             || (p.Desc.Description?.ToLowerInvariant().Contains(normalizedQuery) ?? false))
                    .OrderBy(p => p.Desc.Name.Length)
                    .Take(MAX_SEARCH_RESULTS);

                foreach (var proxy in matches)
                {
                    results.Add(new ComponentInfo
                    {
                        Name = proxy.Desc.Name,
                        Description = proxy.Desc.Description,
                        Category = proxy.Desc.Category,
                        SubCategory = proxy.Desc.SubCategory,
                        Guid = proxy.Guid.ToString()
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Normalize a type name using aliases
        /// </summary>
        private static string NormalizeTypeName(string type)
        {
            // Remove whitespace and normalize
            var normalized = type.Trim();

            // Check aliases
            if (NameAliases.TryGetValue(normalized, out string aliased))
            {
                return aliased;
            }

            return normalized;
        }

        /// <summary>
        /// Get all components that exactly match a name, with full details for disambiguation.
        /// Supports category-qualified names like "Curve/Circle" or "Circle (Curve)".
        /// </summary>
        public static List<ComponentMatch> GetExactMatches(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return new List<ComponentMatch>();

            var normalized = NormalizeTypeName(type.Trim());
            string categoryFilter = null;

            // Check for category-qualified format: "Category/Name" or "Name (Category)"
            if (normalized.Contains("/"))
            {
                var parts = normalized.Split('/');
                if (parts.Length == 2)
                {
                    categoryFilter = parts[0].Trim();
                    normalized = parts[1].Trim();
                }
            }
            else if (normalized.Contains("(") && normalized.EndsWith(")"))
            {
                var parenIndex = normalized.LastIndexOf('(');
                categoryFilter = normalized.Substring(parenIndex + 1).TrimEnd(')').Trim();
                normalized = normalized.Substring(0, parenIndex).Trim();
            }

            // Find all exact name matches
            var matches = Instances.ComponentServer.ObjectProxies
                .Where(p => p.Desc.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                .Where(p => categoryFilter == null ||
                            p.Desc.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var results = new List<ComponentMatch>();

            foreach (var proxy in matches)
            {
                var match = CreateComponentMatch(proxy);
                if (match != null)
                    results.Add(match);
            }

            // Sort: components before parameters (more commonly desired)
            return results.OrderBy(m => m.Role == "parameter" ? 1 : 0).ToList();
        }

        /// <summary>
        /// Create a ComponentMatch with full details from a proxy
        /// </summary>
        private static ComponentMatch CreateComponentMatch(IGH_ObjectProxy proxy)
        {
            if (proxy == null) return null;

            var match = new ComponentMatch
            {
                Name = proxy.Desc.Name,
                Description = proxy.Desc.Description,
                Category = proxy.Desc.Category,
                SubCategory = proxy.Desc.SubCategory,
                Guid = proxy.Guid.ToString(),
                Role = GetRole(proxy.Desc.Category, proxy.Desc.SubCategory),
                Inputs = new List<ParameterInfo>(),
                Outputs = new List<ParameterInfo>()
            };

            // Try to get input/output details by creating a temporary instance
            try
            {
                var instance = proxy.CreateInstance();
                if (instance is IGH_Component comp)
                {
                    foreach (var input in comp.Params.Input)
                    {
                        match.Inputs.Add(new ParameterInfo
                        {
                            Name = input.Name,
                            Nickname = input.NickName,
                            Type = input.TypeName,
                            Access = input.Access.ToString().ToLower(),
                            Optional = input.Optional
                        });
                    }

                    foreach (var output in comp.Params.Output)
                    {
                        match.Outputs.Add(new ParameterInfo
                        {
                            Name = output.Name,
                            Nickname = output.NickName,
                            Type = output.TypeName
                        });
                    }
                }
            }
            catch
            {
                // Ignore errors creating instances - still return basic info
            }

            return match;
        }

        /// <summary>
        /// Determine the role of a component based on category and subcategory
        /// </summary>
        public static string GetRole(string category, string subCategory)
        {
            if (category == "Params")
            {
                if (subCategory == "Input")
                    return "input";
                return "parameter";
            }
            return "component";
        }

        /// <summary>
        /// Try to create a component, returning disambiguation info if multiple matches exist.
        /// Returns (component, null) on success, (null, matches) if disambiguation needed, (null, null) if not found.
        /// </summary>
        public static (IGH_DocumentObject component, List<ComponentMatch> matches) TryCreateComponent(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return (null, null);

            // If it's a GUID, create directly - no ambiguity
            if (Guid.TryParse(type, out Guid guid))
            {
                var component = CreateByGuid(guid);
                return (component, null);
            }

            // Check for known GUIDs first (unambiguous)
            var normalized = NormalizeTypeName(type);
            if (KnownGuids.TryGetValue(normalized, out Guid knownGuid))
            {
                var component = CreateByGuid(knownGuid);
                if (component != null)
                    return (component, null);
            }

            // Special handling for Panel and Number Slider (unambiguous)
            var lowerName = normalized.ToLowerInvariant();
            if (lowerName == "panel")
                return (new GH_Panel(), null);
            if (lowerName == "number slider")
            {
                var slider = new GH_NumberSlider();
                slider.SetInitCode("0.0 < 0.5 < 1.0");
                return (slider, null);
            }

            // Get all exact matches
            var matches = GetExactMatches(type);

            if (matches.Count == 0)
            {
                // Try fuzzy match as fallback
                var proxy = Instances.ComponentServer.ObjectProxies
                    .FirstOrDefault(p => p.Desc.Name.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0);

                if (proxy != null)
                {
                    var component = proxy.CreateInstance() as IGH_DocumentObject;
                    return (component, null);
                }

                return (null, null);  // Not found
            }

            if (matches.Count == 1)
            {
                // Unambiguous - create it
                var component = CreateByGuid(Guid.Parse(matches[0].Guid));
                return (component, null);
            }

            // Multiple matches - return disambiguation info
            return (null, matches);
        }
    }

    /// <summary>
    /// Information about a Grasshopper component
    /// </summary>
    public class ComponentInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public string Guid { get; set; }
    }

    /// <summary>
    /// Detailed match information for disambiguation
    /// </summary>
    public class ComponentMatch
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public string Guid { get; set; }
        public string Role { get; set; }  // "component", "parameter", or "input"
        public List<ParameterInfo> Inputs { get; set; }
        public List<ParameterInfo> Outputs { get; set; }
    }

    /// <summary>
    /// Parameter information for inputs/outputs
    /// </summary>
    public class ParameterInfo
    {
        public string Name { get; set; }
        public string Nickname { get; set; }
        public string Type { get; set; }
        public string Access { get; set; }
        public bool Optional { get; set; }
    }
}
