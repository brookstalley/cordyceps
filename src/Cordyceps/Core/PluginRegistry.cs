using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;

namespace Cordyceps.Core
{
    /// <summary>
    /// Registry of known Grasshopper plugins with documentation URLs.
    /// </summary>
    public class PluginRegistry
    {
        // volatile: the double-checked Instance read happens off-lock on concurrent HTTP
        // worker threads; without it a thread could observe a partially-published reference.
        private static volatile PluginRegistry _instance;
        private static readonly object _lock = new object();

        /// <summary>
        /// Known plugins with their metadata
        /// </summary>
        private readonly Dictionary<string, PluginInfo> _knownPlugins = new Dictionary<string, PluginInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Kangaroo"] = new PluginInfo
            {
                Name = "Kangaroo Physics",
                Description = "Physics simulation and form-finding for Grasshopper",
                Category = "Kangaroo",
                DocumentationUrl = "https://www.food4rhino.com/en/app/kangaroo-physics",
                Author = "Daniel Piker"
            },
            ["Kangaroo2"] = new PluginInfo
            {
                Name = "Kangaroo 2",
                Description = "Physics simulation and form-finding (version 2)",
                Category = "Kangaroo2",
                DocumentationUrl = "https://www.food4rhino.com/en/app/kangaroo-physics",
                Author = "Daniel Piker"
            },
            ["Ladybug"] = new PluginInfo
            {
                Name = "Ladybug Tools",
                Description = "Environmental analysis for sustainable building design",
                Category = "Ladybug",
                DocumentationUrl = "https://www.ladybug.tools/",
                Author = "Ladybug Tools LLC"
            },
            ["Honeybee"] = new PluginInfo
            {
                Name = "Honeybee",
                Description = "Energy and daylight simulation",
                Category = "Honeybee",
                DocumentationUrl = "https://www.ladybug.tools/honeybee.html",
                Author = "Ladybug Tools LLC"
            },
            ["Pufferfish"] = new PluginInfo
            {
                Name = "Pufferfish",
                Description = "Advanced mesh and surface utilities, tweens, and morphing",
                Category = "Pufferfish",
                DocumentationUrl = "https://www.food4rhino.com/en/app/pufferfish",
                Author = "Michael Pryor"
            },
            ["LunchBox"] = new PluginInfo
            {
                Name = "LunchBox",
                Description = "Mathematical, paneling, and geometry tools",
                Category = "LunchBox",
                DocumentationUrl = "https://www.food4rhino.com/en/app/lunchbox",
                Author = "Nathan Miller"
            },
            ["Weaverbird"] = new PluginInfo
            {
                Name = "Weaverbird",
                Description = "Mesh topology and subdivision tools",
                Category = "Weaverbird",
                DocumentationUrl = "https://www.giuliopiacentino.com/weaverbird/",
                Author = "Giulio Piacentino"
            },
            ["Anemone"] = new PluginInfo
            {
                Name = "Anemone",
                Description = "Looping and iteration in Grasshopper",
                Category = "Anemone",
                DocumentationUrl = "https://www.food4rhino.com/en/app/anemone",
                Author = "Mateusz Zwierzycki"
            },
            ["Elefront"] = new PluginInfo
            {
                Name = "Elefront",
                Description = "Rhino object management, baking with attributes, referencing",
                Category = "Elefront",
                DocumentationUrl = "https://www.food4rhino.com/en/app/elefront",
                Author = "Ramon van der Heijden"
            },
            ["Human"] = new PluginInfo
            {
                Name = "Human",
                Description = "User interface, display, and interaction tools",
                Category = "Human",
                DocumentationUrl = "https://www.food4rhino.com/en/app/human",
                Author = "Andrew Heumann"
            },
            ["Heteroptera"] = new PluginInfo
            {
                Name = "Heteroptera",
                Description = "Advanced data manipulation and tree operations",
                Category = "Heteroptera",
                DocumentationUrl = "https://www.food4rhino.com/en/app/heteroptera",
                Author = "Erick Cassuto"
            },
            ["Metahopper"] = new PluginInfo
            {
                Name = "Metahopper",
                Description = "Grasshopper definition manipulation and utilities",
                Category = "Metahopper",
                DocumentationUrl = "https://www.food4rhino.com/en/app/metahopper",
                Author = "Andrew Heumann"
            },
            ["MeshPlus"] = new PluginInfo
            {
                Name = "Mesh+",
                Description = "Extended mesh editing and analysis tools",
                Category = "Mesh+",
                DocumentationUrl = "https://www.food4rhino.com/en/app/mesh",
                Author = "David Stasiuk"
            },
            ["Dendro"] = new PluginInfo
            {
                Name = "Dendro",
                Description = "Volumetric modeling using OpenVDB",
                Category = "Dendro",
                DocumentationUrl = "https://www.food4rhino.com/en/app/dendro",
                Author = "ecr labs"
            },
            ["Karamba"] = new PluginInfo
            {
                Name = "Karamba3D",
                Description = "Structural analysis and optimization",
                Category = "Karamba",
                DocumentationUrl = "https://www.karamba3d.com/",
                Author = "Clemens Preisinger"
            },
            ["Octopus"] = new PluginInfo
            {
                Name = "Octopus",
                Description = "Multi-objective optimization using evolutionary algorithms",
                Category = "Octopus",
                DocumentationUrl = "https://www.food4rhino.com/en/app/octopus",
                Author = "Robert Vierlinger"
            },
            ["Galapagos"] = new PluginInfo
            {
                Name = "Galapagos",
                Description = "Evolutionary solver (built-in to Grasshopper)",
                Category = "Params",
                DocumentationUrl = "https://www.grasshopper3d.com/profiles/blogs/evolutionary-principles-applied-to-problem-solving-using",
                Author = "David Rutten",
                IsBuiltIn = true
            },
            ["TreeSloth"] = new PluginInfo
            {
                Name = "TreeSloth",
                Description = "Data tree manipulation tools",
                Category = "TreeSloth",
                DocumentationUrl = "https://www.food4rhino.com/en/app/tree-sloth",
                Author = "David Stasiuk"
            },
            ["Bifocals"] = new PluginInfo
            {
                Name = "Bifocals",
                Description = "Component name labels on canvas",
                Category = "Bifocals",
                DocumentationUrl = "https://www.food4rhino.com/en/app/bifocals",
                Author = "Marc Syp"
            },
            ["Telepathy"] = new PluginInfo
            {
                Name = "Telepathy",
                Description = "Wireless data transfer between definitions",
                Category = "Telepathy",
                DocumentationUrl = "https://www.food4rhino.com/en/app/telepathy",
                Author = "Mathias Sønderskov Schaltz"
            }
        };

        // Published only on a fully-successful scan (volatile: read off-lock by callers).
        private volatile Dictionary<string, string> _installedPlugins;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static PluginRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PluginRegistry();
                        }
                    }
                }
                return _instance;
            }
        }

        private PluginRegistry() { }

        /// <summary>
        /// Get information about a known plugin
        /// </summary>
        public PluginInfo GetPluginInfo(string pluginName)
        {
            return _knownPlugins.TryGetValue(pluginName, out var info) ? info : null;
        }

        /// <summary>
        /// Get plugin info for a component based on its category
        /// </summary>
        public PluginInfo GetPluginForCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return null;

            return _knownPlugins.Values.FirstOrDefault(p =>
                p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get plugin info for a component
        /// </summary>
        public PluginInfo GetPluginForComponent(IGH_ObjectProxy proxy)
        {
            if (proxy?.Desc == null) return null;

            // Check by category
            var byCategory = GetPluginForCategory(proxy.Desc.Category);
            if (byCategory != null) return byCategory;

            // Could extend to check assembly info, but category is usually reliable
            return null;
        }

        /// <summary>
        /// Get all known plugins
        /// </summary>
        public IReadOnlyList<PluginInfo> GetAllKnownPlugins()
        {
            return _knownPlugins.Values.ToList();
        }

        /// <summary>
        /// Scan for installed plugins that match known plugins
        /// </summary>
        public Dictionary<string, string> GetInstalledPlugins()
        {
            var cached = _installedPlugins;
            if (cached != null) return cached;

            // Build into a local and publish only on a fully-successful scan — an exception
            // mid-scan must not leave a partial dictionary cached as if it were complete.
            var scanned = new Dictionary<string, string>();

            try
            {
                var categories = new HashSet<string>();

                foreach (var proxy in Instances.ComponentServer.ObjectProxies)
                {
                    if (proxy?.Desc?.Category != null)
                    {
                        categories.Add(proxy.Desc.Category);
                    }
                }

                foreach (var category in categories)
                {
                    var plugin = GetPluginForCategory(category);
                    if (plugin != null && !scanned.ContainsKey(plugin.Name))
                    {
                        scanned[plugin.Name] = plugin.DocumentationUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Warn($"Error scanning installed plugins: {ex.Message}");
                // Return the partial result for this call, but don't cache it — the next
                // call retries the scan.
                return scanned;
            }

            lock (_lock)
            {
                if (_installedPlugins == null)
                    _installedPlugins = scanned;
                return _installedPlugins;
            }
        }

        /// <summary>
        /// Check if a category belongs to a known plugin
        /// </summary>
        public bool IsKnownPluginCategory(string category)
        {
            return GetPluginForCategory(category) != null;
        }
    }

    /// <summary>
    /// Information about a Grasshopper plugin
    /// </summary>
    public class PluginInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public string DocumentationUrl { get; set; }
        public string Author { get; set; }
        public bool IsBuiltIn { get; set; }
    }
}
