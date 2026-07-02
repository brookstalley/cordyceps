using System;

namespace Cordyceps.Core
{
    /// <summary>
    /// Single source of truth for the MCP server lifecycle (MCP-9F3Q). Previously the state was
    /// reconstructed from three interdependent signals (<c>IsRunning</c> + <c>StartError</c> +
    /// <c>_context</c>); the enum makes invalid combinations unrepresentable and gives teardown
    /// work (e.g. a background drain) an explicit <see cref="Stopping"/> phase to live in.
    ///
    /// Host-independent (no Grasshopper/Rhino references, no <c>DebugLog</c>) so the transition
    /// rules can be unit-tested — <c>Cordyceps.Tests</c> links this file directly. The owner of
    /// the state field is <see cref="McpServer"/>.
    /// </summary>
    public enum ServerState
    {
        /// <summary>Initial state, and the terminal state after a clean teardown.</summary>
        Stopped,

        /// <summary>Start() is in progress (tool discovery, listener bind).</summary>
        Starting,

        /// <summary>The listener is up and serving requests.</summary>
        Running,

        /// <summary>
        /// Teardown initiated: the listener is stopped and the port is free, but in-flight
        /// handlers may still be draining before shared state is released.
        /// </summary>
        Stopping,

        /// <summary>The last Start() failed; <c>McpServer.StartError</c> carries the reason.</summary>
        Failed,
    }

    /// <summary>
    /// The legal lifecycle transitions, as pure predicates so <see cref="McpServer"/> guards and
    /// the tests share one definition instead of each hand-coding the rules.
    /// </summary>
    public static class ServerStateTransitions
    {
        /// <summary>A server can (re)start only from a fully torn-down or failed state.</summary>
        public static bool CanStart(ServerState state)
            => state == ServerState.Stopped || state == ServerState.Failed;

        /// <summary>Only a running server can begin teardown; Stop() elsewhere is a no-op.</summary>
        public static bool CanStop(ServerState state)
            => state == ServerState.Running;
    }
}
