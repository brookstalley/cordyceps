using Cordyceps.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests;

/// <summary>
/// Tests for <see cref="ResponseHelpers"/> — the JSON response shape every MCP tool returns
/// (ToolHelpers keeps thin forwarders, so these tests cover the tool-facing behavior).
/// </summary>
public class ResponseHelpersTests
{
    [Fact]
    public void ErrorResponse_IsValidJsonWithSuccessFalseAndErrorText()
    {
        var json = ResponseHelpers.ErrorResponse("something went wrong");

        var parsed = JObject.Parse(json);
        Assert.False(parsed.Value<bool>("success"));
        Assert.Equal("something went wrong", parsed.Value<string>("error"));
    }

    [Fact]
    public void ErrorResponse_EscapesMessageSafely()
    {
        var json = ResponseHelpers.ErrorResponse("quote \" and \\ backslash");

        var parsed = JObject.Parse(json);
        Assert.Equal("quote \" and \\ backslash", parsed.Value<string>("error"));
    }

    [Fact]
    public void SimpleSuccess_IsValidJsonWithOnlySuccessTrue()
    {
        var json = ResponseHelpers.SimpleSuccess();

        var parsed = JObject.Parse(json);
        Assert.True(parsed.Value<bool>("success"));
        Assert.Single(parsed.Properties());
    }

    [Fact]
    public void SuccessResponse_SerializesDataWithSuccessField()
    {
        var json = ResponseHelpers.SuccessResponse(new { success = true, count = 3, name = "slider" });

        var parsed = JObject.Parse(json);
        Assert.True(parsed.Value<bool>("success"));
        Assert.Equal(3, parsed.Value<int>("count"));
        Assert.Equal("slider", parsed.Value<string>("name"));
    }
}
