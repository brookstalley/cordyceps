using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Tests for <see cref="ServerStateTransitions"/> — the legal-transition table behind the
    /// MCP server lifecycle enum (MCP-9F3Q). The enum field itself lives in the host-coupled
    /// <c>McpServer</c>; these tests pin the shared rules its guards call.
    /// </summary>
    public class ServerStateTransitionsTests
    {
        [Theory]
        [InlineData(ServerState.Stopped, true)]
        [InlineData(ServerState.Failed, true)]
        [InlineData(ServerState.Starting, false)]
        [InlineData(ServerState.Running, false)]
        [InlineData(ServerState.Stopping, false)]
        public void CanStart_OnlyFromStoppedOrFailed(ServerState state, bool expected)
        {
            Assert.Equal(expected, ServerStateTransitions.CanStart(state));
        }

        [Theory]
        [InlineData(ServerState.Running, true)]
        [InlineData(ServerState.Stopped, false)]
        [InlineData(ServerState.Failed, false)]
        [InlineData(ServerState.Starting, false)]
        [InlineData(ServerState.Stopping, false)]
        public void CanStop_OnlyFromRunning(ServerState state, bool expected)
        {
            Assert.Equal(expected, ServerStateTransitions.CanStop(state));
        }
    }
}
