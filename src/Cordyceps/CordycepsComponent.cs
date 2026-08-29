using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Threading;
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

        /// <summary>
        /// How long the refresh expire is deferred by. Matches the delay used elsewhere in this
        /// file: long enough for the current solution to unwind, short enough to feel immediate.
        /// </summary>
        private const int REFRESH_SOLUTION_DELAY_MS = 10;

        /// <summary>
        /// How often the UI-thread heartbeat is stamped. One queued no-op per second is negligible
        /// next to what Grasshopper already does per frame, and it bounds how long a caller can be
        /// unsure whether the host is wedged.
        /// </summary>
        private const int HEARTBEAT_INTERVAL_MS = 1000;

        // Heartbeat timer, shared by every instance (one Rhino process, one UI thread). Guarded
        // by _lock, so it starts with the first server and stops when the last one goes away.
        private static System.Threading.Timer _heartbeatTimer;

        /// <summary>
        /// How long an unrun heartbeat stamp blocks further ones. A wedged UI thread must not
        /// accumulate a stamp per second, but the gate must also expire: if a queued stamp is
        /// ever dropped (host teardown, a torn-down message loop) an unconditional gate would
        /// leave the heartbeat frozen and report a perfectly healthy host as blocked forever.
        /// </summary>
        private static readonly TimeSpan HEARTBEAT_QUEUE_GATE = TimeSpan.FromSeconds(30);

        // Ticks when the outstanding stamp was queued, or 0 when none is outstanding.
        private static long _heartbeatQueuedTicks;

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
                    EnsureHeartbeat();
                }
            }

            // Attach the liveness feed (idempotent). Done here, on the UI thread and outside the
            // lock, because Grasshopper's document server is UI-thread-only and may not exist yet
            // when this component is first constructed.
            if (!isBlocked)
                SolutionWatcher.Start();

            // Set outputs (outside lock to avoid potential deadlock)
            DA.SetData(0, GetAboutInfo());
            if (isBlocked)
            {
                DA.SetData(1, $"Server: BLOCKED\nPort {port} is owned by another Cordyceps component.\nChange port input to use a different port.");
                DA.SetData(2, "(blocked)");
            }
            else if (myServer != null && !myServer.IsRunning && !string.IsNullOrEmpty(myServer.StartError))
            {
                // The server failed to bind/start (e.g. the port is held by a non-Cordyceps
                // process). Surface the actionable reason instead of a bare "NOT RUNNING".
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, myServer.StartError);
                DA.SetData(1, $"Server: FAILED TO START\n{myServer.StartError}");
                DA.SetData(2, "(server failed to start)");
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
            ReleaseServer();
        }

        /// <summary>
        /// Start the shared UI-thread heartbeat if it is not already running. Must be called
        /// holding <see cref="_lock"/>.
        /// </summary>
        private static void EnsureHeartbeat()
        {
            if (_heartbeatTimer != null) return;

            _heartbeatTimer = new System.Threading.Timer(
                _ => StampHeartbeat(), null, HEARTBEAT_INTERVAL_MS, HEARTBEAT_INTERVAL_MS);
        }

        /// <summary>
        /// Stop the heartbeat once no component owns a port — nothing reads it then, and a timer
        /// outliving the last server would keep the plugin's threads alive. Must be called holding
        /// <see cref="_lock"/>.
        /// </summary>
        private static void StopHeartbeatIfIdle()
        {
            if (_portOwners.Count > 0 || _heartbeatTimer == null) return;

            _heartbeatTimer.Dispose();
            _heartbeatTimer = null;
        }

        /// <summary>
        /// Queue a heartbeat stamp onto the Rhino UI thread. The stamp <em>landing</em> is the
        /// evidence that the UI thread is draining its queue, which is why this queues and never
        /// waits: a wedged UI thread must leave the heartbeat stale, not block the timer.
        /// </summary>
        private static void StampHeartbeat()
        {
            // At most one stamp outstanding, but claim expires — see HEARTBEAT_QUEUE_GATE.
            var now = DateTime.UtcNow.Ticks;
            var queuedAt = Interlocked.Read(ref _heartbeatQueuedTicks);
            if (queuedAt != 0 && now - queuedAt < HEARTBEAT_QUEUE_GATE.Ticks) return;
            if (Interlocked.CompareExchange(ref _heartbeatQueuedTicks, now, queuedAt) != queuedAt) return;

            try
            {
                RhinoApp.InvokeOnUiThread(new Action(() =>
                {
                    try
                    {
                        // Read on the UI thread and cache it, so the off-thread status path never
                        // has to touch a Grasshopper object to name the document it describes.
                        var doc = Grasshopper.Instances.ActiveCanvas?.Document;
                        SolverState.Shared.Heartbeat(doc?.DocumentID, doc?.DisplayName);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _heartbeatQueuedTicks, 0);
                    }
                }));
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- timer-callback boundary: an escaping exception from a System.Threading.Timer callback terminates the Rhino process, and queueing can fail during host shutdown
            {
                Interlocked.Exchange(ref _heartbeatQueuedTicks, 0);
                DebugLog.Debug($"Heartbeat stamp could not be queued: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the owner document moves into a different context. Grasshopper does NOT
        /// call RemovedFromDocument when a document is closed/unloaded, so without this hook a
        /// closed document left the server running and the port owned — re-opening the file then
        /// blocked the port. Stop the server on Close/Unloaded; on Open/Loaded a solution is
        /// scheduled so the normal SolveInstance lifecycle (the single startup path) restarts it.
        /// </summary>
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            base.DocumentContextChanged(document, context);

            switch (context)
            {
                case GH_DocumentContext.Close:
                case GH_DocumentContext.Unloaded:
                    DebugLog.WriteLine($"Document context '{context}' — releasing MCP server on port {_myPort}", "INFO", 1);
                    ReleaseServer();
                    break;

                case GH_DocumentContext.Open:
                case GH_DocumentContext.Loaded:
                    // A document re-entering the canvas doesn't necessarily re-solve, so expire
                    // this component via a scheduled solution; SolveInstance restarts the server.
                    bool needsRestart;
                    lock (_lock)
                    {
                        // Restart when we hold no port (normal reopen path) OR when we still
                        // own a port whose cached server isn't running (StartServer caches a
                        // failed instance while _portOwners/_myPort stay set — without this,
                        // a dead server isn't restarted until a manual recompute).
                        needsRestart = _myPort == 0
                            || !(_servers.TryGetValue(_myPort, out var s) && s.IsRunning);
                    }
                    if (needsRestart && document != null)
                        document.ScheduleSolution(10, d => ExpireSolution(false));
                    break;
            }
        }

        /// <summary>
        /// Shared teardown for RemovedFromDocument and document Close/Unloaded: stop the server
        /// (if this instance owns the port) and release port ownership.
        /// </summary>
        private void ReleaseServer()
        {
            bool idle;
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
                StopHeartbeatIfIdle();
                idle = _portOwners.Count == 0;
            }

            // Detach the liveness feed once no bridge remains — outside the lock, because the
            // watcher takes its own and nothing should hold two at once.
            if (idle)
                SolutionWatcher.Stop();
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
        ///
        /// <para>The expire is <em>scheduled</em>, never immediate. Grasshopper pumps messages
        /// during a solution, so a queued UI action can run while the document is mid-solve;
        /// <c>ExpireSolution(true)</c> there expires an object inside a running solution and
        /// raises Grasshopper's modal breakpoint dialog, which nothing but a human clicking a
        /// button will clear. Since this runs on every MCP call, any call landing during a solve
        /// could end an unattended session. <c>ScheduleSolution</c> is the host-sanctioned way to
        /// ask for a recompute from outside the solver: it waits for the current solution to
        /// finish, and the callback's <c>ExpireSolution(false)</c> never kicks one of its own.</para>
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
                    if (instance == null) continue;

                    try
                    {
                        var document = instance.OnPingDocument();
                        if (document == null) continue;

                        document.ScheduleSolution(REFRESH_SOLUTION_DELAY_MS, d => SafeExpire(instance));
                    }
                    catch (Exception ex) // prawduct:allow prawduct/broad-except -- fire-and-forget UI callback: an escaping exception here would surface as an unhandled exception on the Rhino UI thread, and a failed status refresh must never break the MCP call that triggered it
                    {
                        DebugLog.Warn($"RefreshComponent failed: {ex.Message}");
                    }
                }
            }));
        }

        /// <summary>
        /// Mark an instance expired from inside Grasshopper's solution scheduler. The instance may
        /// have been removed between scheduling and the callback, and anything escaping here would
        /// surface inside the scheduler rather than at the MCP call that caused it.
        /// </summary>
        private static void SafeExpire(CordycepsComponent instance)
        {
            try
            {
                if (instance?.OnPingDocument() == null) return;
                instance.ExpireSolution(false);
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- scheduled-solution callback boundary: an escaping exception would fault Grasshopper's solution scheduler, and a missed status refresh must never do that
            {
                DebugLog.Warn($"Scheduled status refresh failed: {ex.Message}");
            }
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
