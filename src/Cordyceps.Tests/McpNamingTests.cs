using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

/// <summary>
/// Tests for <see cref="McpNaming"/> — the PascalCase → snake_case conversion that derives
/// every MCP tool name from its C# method name.
/// </summary>
public class McpNamingTests
{
    /// <summary>
    /// Contract: the seven real tool method names map to exactly these MCP tool names.
    /// A regression here silently renames the entire MCP tool surface for every client,
    /// so these pairs are pinned explicitly.
    /// </summary>
    [Theory]
    [InlineData("GhCanvas", "gh_canvas")]
    [InlineData("GhWire", "gh_wire")]
    [InlineData("GhDocument", "gh_document")]
    [InlineData("GhScript", "gh_script")]
    [InlineData("GhInspect", "gh_inspect")]
    [InlineData("RhinoScene", "rhino_scene")]
    [InlineData("RhinoRender", "rhino_render")]
    public void RealToolNames_MapToPinnedMcpNames(string methodName, string expectedToolName)
    {
        Assert.Equal(expectedToolName, McpNaming.ToSnakeCase(methodName));
    }

    [Theory]
    [InlineData("Foo", "foo")]
    [InlineData("foo", "foo")]
    [InlineData("FooBarBaz", "foo_bar_baz")]
    [InlineData("", "")]
    public void GeneralConversion_InsertsUnderscoreBeforeInteriorUppercase(string input, string expected)
    {
        Assert.Equal(expected, McpNaming.ToSnakeCase(input));
    }
}
