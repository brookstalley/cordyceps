using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper;
using Grasshopper.Kernel;

namespace Cordyceps.Core
{
    /// <summary>
    /// Registry for tracking deprecated components and their upgrade paths.
    /// Scans loaded assemblies for IGH_UpgradeObject implementations.
    /// </summary>
    public class DeprecationRegistry
    {
        private static DeprecationRegistry _instance;
        private static readonly object _lock = new object();

        private readonly Dictionary<Guid, UpgradeInfo> _upgrades = new Dictionary<Guid, UpgradeInfo>();
        private readonly HashSet<Guid> _hiddenComponents = new HashSet<Guid>();
        private bool _initialized;

        /// <summary>
        /// Singleton instance
        /// </summary>
        public static DeprecationRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new DeprecationRegistry();
                        }
                    }
                }
                return _instance;
            }
        }

        private DeprecationRegistry() { }

        /// <summary>
        /// Initialize the registry by scanning loaded assemblies
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    ScanForUpgradeObjects();
                    ScanForHiddenComponents();
                    _initialized = true;
                    DebugLog.Info($"DeprecationRegistry: Found {_upgrades.Count} upgrade paths, {_hiddenComponents.Count} hidden components");
                }
                catch (Exception ex)
                {
                    DebugLog.Error($"DeprecationRegistry initialization failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Scan all loaded assemblies for IGH_UpgradeObject implementations
        /// </summary>
        private void ScanForUpgradeObjects()
        {
            var upgradeObjectType = typeof(IGH_UpgradeObject);

            // Get all loaded assemblies that reference Grasshopper
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && ReferencesGrasshopper(a))
                .ToList();

            foreach (var assembly in assemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!upgradeObjectType.IsAssignableFrom(type)) continue;
                        if (type.IsAbstract || type.IsInterface) continue;

                        // Check for parameterless constructor
                        var ctor = type.GetConstructor(Type.EmptyTypes);
                        if (ctor == null) continue;

                        try
                        {
                            var upgrader = (IGH_UpgradeObject)Activator.CreateInstance(type);

                            var fromGuid = upgrader.UpgradeFrom;
                            var toGuid = upgrader.UpgradeTo;
                            var version = upgrader.Version;

                            if (fromGuid != Guid.Empty && toGuid != Guid.Empty)
                            {
                                // Get names for the components
                                string fromName = GetComponentName(fromGuid);
                                string toName = GetComponentName(toGuid);

                                _upgrades[fromGuid] = new UpgradeInfo
                                {
                                    FromGuid = fromGuid,
                                    FromName = fromName,
                                    ToGuid = toGuid,
                                    ToName = toName,
                                    Version = version
                                };

                                DebugLog.Debug($"Found upgrade: {fromName} ({fromGuid}) → {toName} ({toGuid})");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Warn($"Failed to instantiate upgrade object {type.Name}: {ex.Message}");
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // Some assemblies can't be fully loaded - skip them
                    DebugLog.Debug($"Skipped partially-loadable assembly {assembly.GetName().Name} while scanning for upgrades");
                }
                catch (Exception ex)
                {
                    DebugLog.Warn($"Error scanning assembly {assembly.GetName().Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Scan for components with Hidden exposure (typically deprecated)
        /// </summary>
        private void ScanForHiddenComponents()
        {
            try
            {
                foreach (var proxy in Instances.ComponentServer.ObjectProxies)
                {
                    if (proxy.Exposure == GH_Exposure.hidden)
                    {
                        _hiddenComponents.Add(proxy.Guid);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Warn($"Error scanning hidden components: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if an assembly references Grasshopper
        /// </summary>
        private bool ReferencesGrasshopper(Assembly assembly)
        {
            try
            {
                return assembly.GetReferencedAssemblies()
                    .Any(a => a.Name == "Grasshopper" || a.Name == "GH_IO");
            }
            catch (Exception ex)
            {
                DebugLog.Debug($"ReferencesGrasshopper: could not read references of {assembly?.GetName().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the name of a component by GUID
        /// </summary>
        private string GetComponentName(Guid guid)
        {
            try
            {
                var proxy = Instances.ComponentServer.ObjectProxies
                    .FirstOrDefault(p => p.Guid == guid);
                return proxy?.Desc?.Name ?? guid.ToString();
            }
            catch (Exception ex)
            {
                DebugLog.Debug($"GetComponentName({guid}) failed: {ex.Message}");
                return guid.ToString();
            }
        }

        /// <summary>
        /// Check if a component is deprecated
        /// </summary>
        public bool IsDeprecated(Guid componentGuid)
        {
            Initialize();
            return _upgrades.ContainsKey(componentGuid) || _hiddenComponents.Contains(componentGuid);
        }

        /// <summary>
        /// Check if a component is deprecated by name
        /// </summary>
        public bool IsDeprecated(string componentName)
        {
            Initialize();
            var guid = GetComponentGuidByName(componentName);
            return guid.HasValue && IsDeprecated(guid.Value);
        }

        /// <summary>
        /// Get upgrade information for a deprecated component
        /// </summary>
        public UpgradeInfo GetUpgradeInfo(Guid componentGuid)
        {
            Initialize();
            return _upgrades.TryGetValue(componentGuid, out var info) ? info : null;
        }

        /// <summary>
        /// Get upgrade information by component name
        /// </summary>
        public UpgradeInfo GetUpgradeInfo(string componentName)
        {
            Initialize();
            var guid = GetComponentGuidByName(componentName);
            return guid.HasValue ? GetUpgradeInfo(guid.Value) : null;
        }

        /// <summary>
        /// Get all known upgrade paths
        /// </summary>
        public IReadOnlyList<UpgradeInfo> GetAllUpgrades()
        {
            Initialize();
            return _upgrades.Values.ToList();
        }

        /// <summary>
        /// Get component GUID by name
        /// </summary>
        private Guid? GetComponentGuidByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            try
            {
                var proxy = Instances.ComponentServer.ObjectProxies
                    .FirstOrDefault(p => p.Desc.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                return proxy?.Guid;
            }
            catch (Exception ex)
            {
                DebugLog.Debug($"GetComponentGuidByName('{name}') failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Information about an upgrade path from one component to another
    /// </summary>
    public class UpgradeInfo
    {
        public Guid FromGuid { get; set; }
        public string FromName { get; set; }
        public Guid ToGuid { get; set; }
        public string ToName { get; set; }
        public DateTime Version { get; set; }
    }
}
