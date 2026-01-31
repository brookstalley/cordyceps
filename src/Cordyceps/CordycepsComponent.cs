using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using Cordyceps.Core;
using Grasshopper.Kernel;
using Rhino;

// CA1416: System.Drawing.Bitmap works cross-platform in Rhino/Grasshopper context
#pragma warning disable CA1416

namespace Cordyceps
{
    /// <summary>
    /// Cordyceps MCP component - exposes Grasshopper to Claude via MCP protocol
    /// </summary>
    public class CordycepsComponent : GH_Component
    {
        private static readonly object _lock = new object();
        // Support multiple servers on different ports
        private static readonly Dictionary<int, McpServer> _servers = new Dictionary<int, McpServer>();
        private static readonly Dictionary<int, CordycepsComponent> _portOwners = new Dictionary<int, CordycepsComponent>();

        // Track which port this instance is using (0 = not running)
        private int _myPort = 0;

        /// <summary>
        /// Initialize a new instance of the CordycepsComponent class
        /// </summary>
        public CordycepsComponent()
            : base("Cordyceps", "MCP",
                   "Grasshopper MCP Bridge - Claude takes control of Grasshopper",
                   "Params", "Util")
        {
        }

        /// <summary>
        /// Register input parameters
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("HttpPort", "P",
                "Port for MCP HTTP server", GH_ParamAccess.item, 26929);
            pManager.AddIntegerParameter("DebugLevel", "D",
                "Logging verbosity: 0=server start/stop only, 1+=request/response details", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Register output parameters
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("About", "A",
                "Component info: name, version, compile date/time, repo URL", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "S",
                "Server status: listening state, port, command count, endpoint URL", GH_ParamAccess.item);
            pManager.AddTextParameter("LastCommand", "L",
                "Most recent command received", GH_ParamAccess.item);
        }

        /// <summary>
        /// Solve the component
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int port = 26929;
            int debugLevel = 0;
            if (!DA.GetData(0, ref port)) return;
            DA.GetData(1, ref debugLevel);

            // Set global debug level for logging
            DebugLog.DebugLevel = debugLevel;

            bool isBlocked = false;
            McpServer myServer = null;

            lock (_lock)
            {
                // Check if this port is already owned by a DIFFERENT component
                if (_portOwners.TryGetValue(port, out var owner) && owner != this)
                {
                    isBlocked = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        $"Port {port} is already in use by another Cordyceps component. Change this component's port input to use a different port.");
                }
                else
                {
                    // If we were previously using a different port, release it
                    if (_myPort != 0 && _myPort != port)
                    {
                        DebugLog.WriteLine($"Port changed from {_myPort} to {port}, restarting server...", "INFO", 1);
                        StopServer(_myPort);
                    }

                    // Start server on requested port if not already running
                    if (!_servers.TryGetValue(port, out myServer) || !myServer.IsRunning)
                    {
                        myServer = StartServer(port);
                    }

                    // Register this component as the owner of this port
                    _portOwners[port] = this;
                    _myPort = port;
                }
            }

            // Set outputs (outside lock to avoid potential deadlock)
            DA.SetData(0, GetAboutInfo());
            if (isBlocked)
            {
                DA.SetData(1, $"Server: BLOCKED\nPort {port} is owned by another Cordyceps component.\nChange port input to use a different port.");
                DA.SetData(2, "(blocked)");
            }
            else
            {
                DA.SetData(1, GetStatusInfo(myServer));
                DA.SetData(2, myServer?.LastCommand ?? "(server not running)");
            }
        }

        /// <summary>
        /// Called when component is disabled or removed
        /// </summary>
        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);

            lock (_lock)
            {
                if (_myPort != 0)
                {
                    // Only stop if we're the owner of this port
                    if (_portOwners.TryGetValue(_myPort, out var owner) && owner == this)
                    {
                        StopServer(_myPort);
                    }
                    _myPort = 0;
                }
            }
        }

        /// <summary>
        /// Start the MCP server on specified port
        /// </summary>
        private static McpServer StartServer(int port)
        {
            if (_servers.TryGetValue(port, out var existing) && existing.IsRunning)
                return existing;

            var server = new McpServer();
            server.Start(port);
            _servers[port] = server;
            return server;
        }

        /// <summary>
        /// Stop the MCP server on specified port
        /// </summary>
        private static void StopServer(int port)
        {
            if (_servers.TryGetValue(port, out var server))
            {
                server.Stop();
                _servers.Remove(port);
            }
            _portOwners.Remove(port);
        }

        /// <summary>
        /// Get version and information string for About output
        /// </summary>
        private static string GetAboutInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;

            // Get build timestamp from assembly informational version
            string buildDateTime = "Unknown";
            var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (infoVersionAttr != null)
            {
                var infoVersion = infoVersionAttr.InformationalVersion;
                // Parse build timestamp from SourceRevisionId (format: "1.0.0+build20260126143000")
                int buildIndex = infoVersion.IndexOf("+build");
                if (buildIndex >= 0 && infoVersion.Length >= buildIndex + 20)
                {
                    var timestamp = infoVersion.Substring(buildIndex + 6, 14);
                    if (DateTime.TryParseExact(timestamp, "yyyyMMddHHmmss", null,
                        System.Globalization.DateTimeStyles.None, out var dt))
                    {
                        buildDateTime = dt.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Cordyceps v{version}");
            sb.AppendLine($"Built: {buildDateTime}");
            sb.AppendLine("https://github.com/brookstalley/cordyceps");

            return sb.ToString();
        }

        /// <summary>
        /// Get server status information
        /// </summary>
        private static string GetStatusInfo(McpServer server)
        {
            if (server == null || !server.IsRunning)
            {
                return "Server: NOT RUNNING";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Server: LISTENING (MCP Protocol)");
            sb.AppendLine($"Port: {server.Port}");
            sb.AppendLine($"Commands received: {server.CommandCount}");
            sb.AppendLine($"MCP endpoint: http://localhost:{server.Port}/mcp");

            return sb.ToString();
        }

        /// <summary>
        /// Expire the component to refresh outputs (refreshes all active instances).
        /// Called from HTTP worker threads - uses fire-and-forget UI thread invocation
        /// to avoid potential deadlock if UI thread is waiting on _lock.
        /// </summary>
        public static void RefreshComponent()
        {
            // Collect instances under lock, then invoke UI thread update outside lock
            // to prevent deadlock if UI thread is waiting for _lock
            List<CordycepsComponent> instances;
            lock (_lock)
            {
                instances = new List<CordycepsComponent>(_portOwners.Values);
            }

            // Fire-and-forget: queue UI thread work without blocking
            RhinoApp.InvokeOnUiThread(new Action(() =>
            {
                foreach (var instance in instances)
                {
                    try
                    {
                        instance?.ExpireSolution(true);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Warn($"RefreshComponent failed: {ex.Message}");
                    }
                }
            }));
        }

        /// <summary>
        /// Component GUID
        /// </summary>
        public override Guid ComponentGuid => new Guid("c0d1c3e5-0001-0001-0001-000000000001");

        /// <summary>
        /// Component icon (red/white spiral)
        /// </summary>
        protected override Bitmap Icon
        {
            get
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("Cordyceps.Resources.CordycepsIcon.png");
                return stream != null ? new Bitmap(stream) : null;
            }
        }

        /// <summary>
        /// Exposure level in toolbar
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }
}
