using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Cordyceps.Resources
{
    /// <summary>
    /// Registry for MCP resources. Manages static documentation resources
    /// and dynamic component resources.
    /// </summary>
    public class ResourceRegistry
    {
        private static ResourceRegistry _instance;
        private static readonly object _lock = new object();

        private readonly Dictionary<string, ResourceInfo> _staticResources = new Dictionary<string, ResourceInfo>();
        private readonly List<IResourceProvider> _dynamicProviders = new List<IResourceProvider>();

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static ResourceRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ResourceRegistry();
                            _instance.Initialize();
                        }
                    }
                }
                return _instance;
            }
        }

        private ResourceRegistry() { }

        /// <summary>
        /// Initialize the registry with embedded resources
        /// </summary>
        private void Initialize()
        {
            // Register static documentation resources from embedded markdown files
            RegisterEmbeddedResource("gh://docs/getting-started", "Knowledge.GettingStartedGuide.md",
                "Getting Started with Grasshopper",
                "Essential guide for AI agents - read this first! Covers key concepts, workflow, common mistakes, and tool overview");

            RegisterEmbeddedResource("gh://docs/data-trees", "Knowledge.DataTreesGuide.md",
                "Grasshopper Data Trees Guide",
                "Comprehensive guide to understanding Grasshopper's data tree system, paths, access modes, and common patterns");

            RegisterEmbeddedResource("gh://docs/type-system", "Knowledge.TypeSystemGuide.md",
                "Grasshopper Type System",
                "Type compatibility, coercion rules, and parameter types in Grasshopper");

            RegisterEmbeddedResource("gh://docs/best-practices", "Knowledge.BestPracticesGuide.md",
                "Grasshopper Best Practices",
                "Patterns, anti-patterns, and recommendations for effective Grasshopper development");

            RegisterEmbeddedResource("gh://docs/component-patterns", "Knowledge.ComponentPatternsGuide.md",
                "Common Component Patterns",
                "Frequently used component combinations and workflows");

            RegisterEmbeddedResource("gh://docs/canvas-layout", "Knowledge.CanvasLayoutGuide.md",
                "Canvas Layout Best Practices",
                "Component dimensions, spacing conventions, and layout patterns for readable Grasshopper definitions");

            RegisterEmbeddedResource("gh://docs/geometry-orientation", "Knowledge.GeometryOrientationGuide.md",
                "Geometry and Orientation Guide",
                "How planes work in Grasshopper, which axis components use for direction, and how to create correctly oriented geometry");

            RegisterEmbeddedResource("gh://docs/mcp-testing", "Knowledge.McpTestingGuide.md",
                "MCP Server Testing Instructions",
                "Comprehensive test instructions for validating Cordyceps MCP server functionality. Use when asked to test, validate, or debug the MCP connection.");

            RegisterEmbeddedResource("gh://docs/rendering", "Knowledge.RenderingGuide.md",
                "Rhino Rendering Pipeline",
                "Complete workflow from Grasshopper geometry to rendered output: baking, materials, viewport control, camera positioning, and frame capture");

            RegisterEmbeddedResource("gh://docs/common-errors", "Knowledge.CommonErrorsGuide.md",
                "Common Errors and Solutions",
                "Quick reference for resolving frequent issues: component not found, connection failed, data tree mismatch, solver errors, and more");

            // Pattern resources
            RegisterEmbeddedResource("gh://patterns/linear-array", "Knowledge.Patterns.LinearArray.md",
                "Linear Array Pattern",
                "Create N copies of geometry arranged in a straight line along a direction");

            RegisterEmbeddedResource("gh://patterns/grid-array", "Knowledge.Patterns.GridArray.md",
                "Grid Array Pattern",
                "Create a 2D or 3D grid of geometry with controllable spacing");

            // Register dynamic providers
            _dynamicProviders.Add(new ComponentResourceProvider());
        }

        /// <summary>
        /// Register a static resource from an embedded file
        /// </summary>
        private void RegisterEmbeddedResource(string uri, string resourceName, string name, string description)
        {
            _staticResources[uri] = new ResourceInfo
            {
                Uri = uri,
                Name = name,
                Description = description,
                MimeType = "text/markdown",
                EmbeddedResourceName = resourceName
            };
        }

        /// <summary>
        /// List all available resources
        /// </summary>
        public List<object> ListResources()
        {
            var resources = new List<object>();

            // Add static resources
            foreach (var res in _staticResources.Values)
            {
                resources.Add(new
                {
                    uri = res.Uri,
                    name = res.Name,
                    description = res.Description,
                    mimeType = res.MimeType
                });
            }

            // Add resources from dynamic providers
            foreach (var provider in _dynamicProviders)
            {
                resources.AddRange(provider.ListResources());
            }

            return resources;
        }

        /// <summary>
        /// Read a resource by URI
        /// </summary>
        public ResourceContent ReadResource(string uri)
        {
            // Check static resources first
            if (_staticResources.TryGetValue(uri, out var info))
            {
                var content = LoadEmbeddedResource(info.EmbeddedResourceName);
                if (content != null)
                {
                    return new ResourceContent
                    {
                        Uri = uri,
                        MimeType = info.MimeType,
                        Text = content
                    };
                }
                else
                {
                    // Return placeholder if resource not yet created
                    return new ResourceContent
                    {
                        Uri = uri,
                        MimeType = info.MimeType,
                        Text = $"# {info.Name}\n\n{info.Description}\n\n*Documentation content coming soon.*"
                    };
                }
            }

            // Check dynamic providers
            foreach (var provider in _dynamicProviders)
            {
                if (provider.CanHandle(uri))
                {
                    return provider.ReadResource(uri);
                }
            }

            return null;
        }

        /// <summary>
        /// Load content from an embedded resource
        /// </summary>
        private string LoadEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullName = $"Cordyceps.{resourceName}";

            // Try to find the resource
            var names = assembly.GetManifestResourceNames();
            var matchingName = names.FirstOrDefault(n => n.EndsWith(resourceName) || n == fullName);

            if (matchingName == null)
            {
                Core.DebugLog.Warn($"Embedded resource not found: {resourceName}");
                return null;
            }

            using (var stream = assembly.GetManifestResourceStream(matchingName))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Information about a static resource
        /// </summary>
        private class ResourceInfo
        {
            public string Uri { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string MimeType { get; set; }
            public string EmbeddedResourceName { get; set; }
        }
    }

    /// <summary>
    /// Content returned when reading a resource
    /// </summary>
    public class ResourceContent
    {
        public string Uri { get; set; }
        public string MimeType { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// Interface for dynamic resource providers
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>
        /// List resources this provider can serve
        /// </summary>
        IEnumerable<object> ListResources();

        /// <summary>
        /// Check if this provider can handle a URI
        /// </summary>
        bool CanHandle(string uri);

        /// <summary>
        /// Read a resource by URI
        /// </summary>
        ResourceContent ReadResource(string uri);
    }

    /// <summary>
    /// Provider for dynamic component documentation resources
    /// </summary>
    public class ComponentResourceProvider : IResourceProvider
    {
        private const string UriPrefix = "gh://component/";

        // Orientation and usage hints for specific component inputs
        // Key format: "ComponentName:InputName" (case-insensitive)
        private static readonly Dictionary<string, string> InputHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Cylinder orientation
            { "Cylinder:Base", "**Orientation:** Cylinder extends along the plane's Z-axis. To make cylinders point in direction D, use a plane where Z = D." },

            // Cone orientation
            { "Cone:Base", "**Orientation:** Cone extends along the plane's Z-axis with tip at origin. Z-axis points from tip toward base." },

            // Circle/Arc orientation
            { "Circle:Plane", "**Orientation:** Circle lies in the plane's XY surface." },
            { "Circle CNR:Normal", "**Orientation:** The normal vector becomes the plane's Z-axis. Circle lies perpendicular to this vector." },
            { "Arc:Plane", "**Orientation:** Arc lies in the plane's XY surface." },

            // Rectangle
            { "Rectangle:Plane", "**Orientation:** Rectangle lies in the plane's XY surface, centered at origin." },

            // Text
            { "Text 3D:Plane", "**Orientation:** Text faces along the plane's Z-axis (viewer direction)." },

            // Extrusion (not plane-based, but direction matters)
            { "Extrude:Direction", "**Direction:** Geometry is extruded along this vector. Length of vector determines extrusion distance." },

            // Plane construction
            { "Construct Plane:X-Axis", "**Note:** Z-axis is computed as X × Y (cross product). If you need geometry pointing in direction D, set X and Y perpendicular to D so that Z = D." },
            { "Plane Normal:Normal", "**Orientation:** This vector becomes the plane's Z-axis. X and Y axes are computed automatically perpendicular to it." },

            // Move/Transform
            { "Move:Motion", "**Translation:** Objects move in this vector direction by the vector's length." },
            { "Rotate:Angle", "**Units:** Angle is in radians. Use Pi component for conversion (Pi = 180°, 2*Pi = 360°)." },
            { "Rotate 3D:Axis", "**Rotation:** Objects rotate around this axis vector using the right-hand rule." },

            // Scale
            { "Scale:Center", "**Scaling:** Objects scale relative to this point. Points closer to center move less." },

            // Loft
            { "Loft:Curves", "**Order:** Curves are lofted in list order. First curve connects to second, second to third, etc." },
        };

        // Component-level notes that appear after inputs/outputs
        private static readonly Dictionary<string, string> ComponentNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Cylinder", "## Orientation Note\n\nThe cylinder extends along the base plane's **Z-axis**. If you want a cylinder pointing in a specific direction:\n- Use `Plane Normal` with your direction as the Normal input\n- Or use `Construct Plane` ensuring Z (computed as X × Y) points in your desired direction\n\nSee `gh://docs/geometry-orientation` for detailed guidance." },
            { "Cone", "## Orientation Note\n\nThe cone extends along the base plane's **Z-axis**, with the tip at the plane origin. The base circle is at distance `Length` along Z.\n\nSee `gh://docs/geometry-orientation` for detailed guidance." },
            { "Construct Plane", "## Usage Note\n\nThe Z-axis is automatically computed as the cross product of X and Y (X × Y). Most oriented geometry in Grasshopper uses the **Z-axis as the primary direction**.\n\nIf you need geometry pointing in direction D:\n1. Choose any X perpendicular to D\n2. Compute Y = D × X (or let Grasshopper orthogonalize)\n3. Z will equal D\n\nAlternatively, use `Plane Normal` which takes the direction directly.\n\nSee `gh://docs/geometry-orientation` for detailed guidance." },
            { "Plane Normal", "## Usage Note\n\nThis component creates a plane from a point and a normal vector. The normal becomes the **Z-axis** of the plane. X and Y axes are computed automatically.\n\nThis is often the easiest way to create planes for oriented geometry when you know the direction but don't care about rotation around that axis.\n\nSee `gh://docs/geometry-orientation` for detailed guidance." },
        };

        public IEnumerable<object> ListResources()
        {
            // Return a template resource that describes the pattern
            yield return new
            {
                uri = "gh://component/{name}",
                name = "Component Documentation",
                description = "Dynamic documentation for any Grasshopper component. Replace {name} with the component name (e.g., gh://component/Circle)",
                mimeType = "text/markdown"
            };
        }

        public bool CanHandle(string uri)
        {
            return uri?.StartsWith(UriPrefix) == true;
        }

        public ResourceContent ReadResource(string uri)
        {
            if (!CanHandle(uri)) return null;

            var componentName = uri.Substring(UriPrefix.Length);
            if (string.IsNullOrWhiteSpace(componentName)) return null;

            // Generate documentation for the component
            var content = GenerateComponentDocumentation(componentName);

            return new ResourceContent
            {
                Uri = uri,
                MimeType = "text/markdown",
                Text = content
            };
        }

        private string GenerateComponentDocumentation(string componentName)
        {
            // Use ComponentRegistry to find the component and generate docs
            var sb = new StringBuilder();
            sb.AppendLine($"# {componentName}");
            sb.AppendLine();

            try
            {
                // Try to find component info via ComponentRegistry
                var results = Core.ComponentRegistry.SearchComponents(componentName);
                var match = results.FirstOrDefault(r =>
                    r.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));

                if (match == null && results.Count > 0)
                {
                    match = results[0];
                }

                if (match != null)
                {
                    sb.AppendLine($"**Category:** {match.Category} > {match.SubCategory}");
                    sb.AppendLine();
                    sb.AppendLine($"**Description:** {match.Description}");
                    sb.AppendLine();
                    sb.AppendLine($"**GUID:** `{match.Guid}`");
                    sb.AppendLine();

                    // Try to get parameter info by creating a temporary instance
                    var component = Core.ComponentRegistry.CreateComponent(match.Guid);
                    if (component is Grasshopper.Kernel.IGH_Component ghComp)
                    {
                        sb.AppendLine("## Inputs");
                        sb.AppendLine();
                        foreach (var input in ghComp.Params.Input)
                        {
                            var optional = input.Optional ? " *(optional)*" : "";
                            sb.AppendLine($"- **{input.Name}** ({input.NickName}): {input.TypeName}{optional}");
                            if (!string.IsNullOrEmpty(input.Description))
                            {
                                sb.AppendLine($"  - {input.Description}");
                            }
                            sb.AppendLine($"  - Access: {input.Access}");

                            // Add orientation/usage hints if available
                            var hintKey = $"{match.Name}:{input.Name}";
                            if (InputHints.TryGetValue(hintKey, out var hint))
                            {
                                sb.AppendLine($"  - {hint}");
                            }
                        }
                        sb.AppendLine();

                        sb.AppendLine("## Outputs");
                        sb.AppendLine();
                        foreach (var output in ghComp.Params.Output)
                        {
                            sb.AppendLine($"- **{output.Name}** ({output.NickName}): {output.TypeName}");
                            if (!string.IsNullOrEmpty(output.Description))
                            {
                                sb.AppendLine($"  - {output.Description}");
                            }
                        }

                        // Add component-level notes if available
                        if (ComponentNotes.TryGetValue(match.Name, out var note))
                        {
                            sb.AppendLine();
                            sb.AppendLine(note);
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"Component '{componentName}' not found.");
                    sb.AppendLine();
                    sb.AppendLine("Try searching with `search_components` to find available components.");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Error generating documentation: {ex.Message}");
            }

            return sb.ToString();
        }
    }
}
