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
                foreach (var proxy in Instances.ComponentServer.ObjectProxies.Take(100))
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
                    .Take(50);

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
}
