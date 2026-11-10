// The catalog adapter (BuildCatalog) is adapted from Hyperceps
// (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cordyceps.Core.Naming;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Cordyceps.Core
{
    /// <summary>
    /// The Grasshopper side of component types: reads the installed types into a host-free
    /// <see cref="TypeCatalog"/>, creates objects, and searches. Which type a name selects is
    /// decided only by <see cref="TypeNameRule"/>; this class carries no matching rule of its own.
    /// </summary>
    public static class ComponentRegistry
    {
        // Constants
        private const int MAX_COMPONENTS_LIST = 100;
        private const int MAX_SEARCH_RESULTS = 50;

        /// <summary>
        /// Every installed type as a <see cref="TypeCandidate"/>. Built fresh on each call: it is a
        /// single pass over the proxies, and a cache keyed on anything cheaper than the proxy set
        /// itself can go stale when a plugin loads. Must run on the UI thread.
        /// </summary>
        public static TypeCatalog BuildCatalog()
        {
            var proxies = Instances.ComponentServer.ObjectProxies;
            var deprecation = DeprecationRegistry.Instance;
            var libraries = new Dictionary<Guid, string>();
            var candidates = new List<TypeCandidate>(proxies.Count);

            foreach (var proxy in proxies)
            {
                if (proxy?.Desc == null) continue;

                bool deprecated = IsDeprecated(proxy, deprecation);

                if (!libraries.TryGetValue(proxy.LibraryGuid, out var plugin))
                {
                    var info = Instances.ComponentServer.FindAssembly(proxy.LibraryGuid);
                    plugin = info == null ? null : info.IsCoreLibrary ? "Grasshopper" : info.Name;
                    libraries[proxy.LibraryGuid] = plugin;
                }

                var upgrade = deprecation.GetUpgradeInfo(proxy.Guid);
                candidates.Add(new TypeCandidate
                {
                    Name = proxy.Desc.Name,
                    Category = proxy.Desc.Category,
                    SubCategory = proxy.Desc.SubCategory,
                    Description = proxy.Desc.Description,
                    Guid = proxy.Guid,
                    Deprecated = deprecated,
                    Plugin = plugin,
                    UpgradeTo = upgrade?.ToName,
                    UpgradeToGuid = upgrade?.ToGuid
                });
            }

            return new TypeCatalog(candidates);
        }

        /// <summary>
        /// Whether a type counts as deprecated. The one test, used by both the catalog the name
        /// rule resolves against and by search, so search never calls current a type that add
        /// treats as deprecated.
        /// </summary>
        private static bool IsDeprecated(IGH_ObjectProxy proxy, DeprecationRegistry deprecation) =>
            proxy.Obsolete || deprecation.IsDeprecated(proxy.Guid) || HasObsoleteTypeName(proxy);

        /// <summary>
        /// Grasshopper keeps superseded components as classes whose names carry <c>OBSOLETE</c>,
        /// sometimes without setting the proxy's flag. A proxy whose type cannot be loaded is judged
        /// on its flags alone.
        /// </summary>
        private static bool HasObsoleteTypeName(IGH_ObjectProxy proxy)
        {
            try
            {
                return proxy.Type?.Name.IndexOf("OBSOLETE", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex) when (ex is TypeLoadException || ex is FileNotFoundException
                                       || ex is FileLoadException || ex is BadImageFormatException)
            {
                return false;
            }
        }

        /// <summary>Resolve a type name against the installed types with the strict rule.</summary>
        public static TypeResolution ResolveType(string type) => TypeNameRule.Resolve(type, BuildCatalog());

        /// <summary>
        /// Create the object a type name selects. Either a success or a failure, never both:
        /// <list type="bullet">
        /// <item>Success: <c>component</c> is set (not yet in any document), <c>error</c> is null,
        /// and <c>resolution</c> is resolved, its <see cref="TypeResolution.Match"/> describing the
        /// type created.</item>
        /// <item>Failure: <c>component</c> is null and <c>error</c> says why (from
        /// <see cref="TypeNameRule.Explain"/>, or that a resolved type could not be instantiated).</item>
        /// </list>
        /// Nothing is created unless the name resolves to exactly one type.
        /// </summary>
        public static (IGH_DocumentObject component, TypeResolution resolution, string error) TryCreate(string type)
        {
            var resolution = ResolveType(type);

            if (resolution.Kind == TypeResolutionKind.GuidNotInstalled && resolution.Guid.HasValue)
            {
                // A GUID selects that type even when no listed proxy carries it. The emitted object
                // then describes the type, so the result is an ordinary resolved success.
                var byGuid = CreateByGuid(resolution.Guid.Value);
                if (byGuid != null)
                {
                    resolution.Kind = TypeResolutionKind.Resolved;
                    resolution.Match = new TypeCandidate
                    {
                        Name = byGuid.Name,
                        Category = byGuid.Category,
                        SubCategory = byGuid.SubCategory,
                        Description = byGuid.Description,
                        Guid = resolution.Guid.Value,
                        Deprecated = byGuid.Obsolete
                    };
                    resolution.UsedDeprecated = byGuid.Obsolete;
                    return (Prepare(byGuid), resolution, null);
                }
            }

            if (!resolution.IsResolved)
                return (null, resolution, TypeNameRule.Explain(resolution));

            var component = CreateByGuid(resolution.Match.Guid);
            if (component == null)
            {
                DebugLog.Error($"gh_canvas add: '{resolution.Text}' resolved to {resolution.Match.Describe()}, " +
                               "but the component server returned no object for that GUID (EmitObject returned null).");
                return (null, resolution,
                    $"'{resolution.Match.PathText}' ({resolution.Match.Guid:D}) resolved but could not be instantiated; " +
                    "see gh_inspect(action='log').");
            }

            return (Prepare(component), resolution, null);
        }

        /// <summary>A new slider starts at 0..1 with value 0.5, as it always has here.</summary>
        private static IGH_DocumentObject Prepare(IGH_DocumentObject obj)
        {
            if (obj is GH_NumberSlider slider)
                slider.SetInitCode("0.0 < 0.5 < 1.0");
            return obj;
        }

        /// <summary>
        /// Create a component by GUID
        /// </summary>
        public static IGH_DocumentObject CreateByGuid(Guid guid)
        {
            return Instances.ComponentServer.EmitObject(guid) as IGH_DocumentObject;
        }

        /// <summary>
        /// Search for available components matching a query. This returns candidates for a person
        /// or agent to choose from; it never selects one to act on, so substring matching is
        /// deliberate here.
        /// </summary>
        public static List<ComponentInfo> SearchComponents(string query)
        {
            var results = new List<ComponentInfo>();
            var deprecationRegistry = DeprecationRegistry.Instance;

            if (string.IsNullOrWhiteSpace(query))
            {
                // Return all component names, non-deprecated first
                var proxies = Instances.ComponentServer.ObjectProxies
                    .Select(p => new { Proxy = p, IsDeprecated = IsDeprecated(p, deprecationRegistry) })
                    .OrderBy(x => x.IsDeprecated ? 1 : 0)  // Non-deprecated first
                    .ThenBy(x => x.Proxy.Desc.Name)
                    .Take(MAX_COMPONENTS_LIST);

                foreach (var item in proxies)
                {
                    var upgradeInfo = deprecationRegistry.GetUpgradeInfo(item.Proxy.Guid);
                    results.Add(new ComponentInfo
                    {
                        Name = item.Proxy.Desc.Name,
                        Description = item.Proxy.Desc.Description,
                        Category = item.Proxy.Desc.Category,
                        SubCategory = item.Proxy.Desc.SubCategory,
                        Guid = item.Proxy.Guid.ToString(),
                        Deprecated = item.IsDeprecated,
                        UpgradeTo = upgradeInfo?.ToName,
                        UpgradeToGuid = upgradeInfo?.ToGuid.ToString()
                    });
                }
            }
            else
            {
                // Search for matching components, non-deprecated first
                var normalizedQuery = query.ToLowerInvariant();
                var matches = Instances.ComponentServer.ObjectProxies
                    .Where(p => p.Desc.Name.ToLowerInvariant().Contains(normalizedQuery)
                             || (p.Desc.Description?.ToLowerInvariant().Contains(normalizedQuery) ?? false))
                    .Select(p => new { Proxy = p, IsDeprecated = IsDeprecated(p, deprecationRegistry) })
                    .OrderBy(x => x.IsDeprecated ? 1 : 0)  // Non-deprecated first
                    .ThenBy(x => x.Proxy.Desc.Name.Length)  // Then by name length
                    .Take(MAX_SEARCH_RESULTS);

                foreach (var item in matches)
                {
                    var upgradeInfo = deprecationRegistry.GetUpgradeInfo(item.Proxy.Guid);
                    results.Add(new ComponentInfo
                    {
                        Name = item.Proxy.Desc.Name,
                        Description = item.Proxy.Desc.Description,
                        Category = item.Proxy.Desc.Category,
                        SubCategory = item.Proxy.Desc.SubCategory,
                        Guid = item.Proxy.Guid.ToString(),
                        Deprecated = item.IsDeprecated,
                        UpgradeTo = upgradeInfo?.ToName,
                        UpgradeToGuid = upgradeInfo?.ToGuid.ToString()
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Determine the role of a component based on category and subcategory
        /// </summary>
        public static string GetRole(string category, string subCategory) =>
            TypeCandidate.RoleOf(category, subCategory);
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
        public bool Deprecated { get; set; }
        public string UpgradeTo { get; set; }  // Name of replacement component if deprecated
        public string UpgradeToGuid { get; set; }  // GUID of replacement component if deprecated
    }
}
