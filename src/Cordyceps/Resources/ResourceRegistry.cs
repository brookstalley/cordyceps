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

            // Pattern resources
            RegisterEmbeddedResource("gh://patterns/radial-array", "Knowledge.Patterns.RadialArray.md",
                "Radial Array Pattern",
                "Create N copies of geometry arranged in a circle around a center point");

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
