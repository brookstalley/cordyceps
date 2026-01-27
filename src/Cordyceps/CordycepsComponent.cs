using System;
using System.Drawing;
using System.Reflection;
using System.Text;
using Cordyceps.Core;
using Grasshopper.Kernel;
using Rhino;

namespace Cordyceps
{
    /// <summary>
    /// Cordyceps MCP component - exposes Grasshopper to Claude via MCP protocol
    /// </summary>
    public class CordycepsComponent : GH_Component
    {
        private static readonly object _lock = new object();
        private static McpServer _mcpServer;
        private static CordycepsComponent _activeInstance;
        private static int _currentPort = 8080;

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
                "Port for MCP HTTP/SSE server", GH_ParamAccess.item, 8080);
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
            int port = 8080;
            if (!DA.GetData(0, ref port)) return;

            lock (_lock)
            {
                // Track active instance for callbacks
                _activeInstance = this;

                // Handle port change
                if (_mcpServer != null && _mcpServer.IsRunning && port != _currentPort)
                {
                    RhinoApp.WriteLine($"Cordyceps: Port changed from {_currentPort} to {port}, restarting server...");
                    StopServer();
                }

                // Start server if not running (component being enabled starts the server)
                if (_mcpServer == null || !_mcpServer.IsRunning)
                {
                    _currentPort = port;
                    StartServer(port);
                }
            }

            // Set outputs (outside lock to avoid potential deadlock)
            DA.SetData(0, GetAboutInfo());
            DA.SetData(1, GetStatusInfo());
            DA.SetData(2, _mcpServer?.LastCommand ?? "(server not running)");
        }

        /// <summary>
        /// Called when component is disabled or removed
        /// </summary>
        public override void RemovedFromDocument(GH_Document document)
        {
            base.RemovedFromDocument(document);

            lock (_lock)
            {
                if (_activeInstance == this)
                {
                    StopServer();
                    _activeInstance = null;
                }
            }
        }

        /// <summary>
        /// Start the MCP server
        /// </summary>
        private static void StartServer(int port)
        {
            if (_mcpServer != null && _mcpServer.IsRunning) return;

            _mcpServer = new McpServer();
            _mcpServer.Start(port);
        }

        /// <summary>
        /// Stop the MCP server
        /// </summary>
        private static void StopServer()
        {
            _mcpServer?.Stop();
            _mcpServer = null;
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
        private static string GetStatusInfo()
        {
            if (_mcpServer == null || !_mcpServer.IsRunning)
            {
                return "Server: NOT RUNNING";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Server: LISTENING (MCP Protocol)");
            sb.AppendLine($"Port: {_mcpServer.Port}");
            sb.AppendLine($"Commands received: {_mcpServer.CommandCount}");
            sb.AppendLine($"SSE endpoint: http://localhost:{_mcpServer.Port}/sse");

            return sb.ToString();
        }

        /// <summary>
        /// Expire the component to refresh outputs
        /// </summary>
        public static void RefreshComponent()
        {
            CordycepsComponent instance;
            lock (_lock)
            {
                instance = _activeInstance;
            }

            if (instance != null)
            {
                RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    try
                    {
                        instance?.ExpireSolution(true);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Warn($"RefreshComponent failed: {ex.Message}");
                    }
                }));
            }
        }

        /// <summary>
        /// Component GUID
        /// </summary>
        public override Guid ComponentGuid => new Guid("c0d1c3e5-0001-0001-0001-000000000001");

        /// <summary>
        /// Expose icon (null for now)
        /// </summary>
        protected override Bitmap Icon => null;

        /// <summary>
        /// Exposure level in toolbar
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;
    }
}
