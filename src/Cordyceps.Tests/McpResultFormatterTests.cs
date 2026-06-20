using System;
using System.Reflection;
using Cordyceps.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests;

public class McpResultFormatterIsErrorResultTests
{
    [Fact]
    public void ReturnsTrue_WhenSuccessIsFalse()
    {
        Assert.True(McpResultFormatter.IsErrorResult("{\"success\":false}"));
    }

    [Fact]
    public void ReturnsTrue_WhenSuccessIsFalse_WithErrorAndExtraFields()
    {
        var json = "{\"success\":false,\"error\":\"boom\",\"items\":[1,2,3]}";
        Assert.True(McpResultFormatter.IsErrorResult(json));
    }

    [Fact]
    public void ReturnsFalse_WhenSuccessIsTrue()
    {
        Assert.False(McpResultFormatter.IsErrorResult("{\"success\":true}"));
    }

    [Fact]
    public void ReturnsFalse_WhenSuccessIsTrue_WithData()
    {
        Assert.False(McpResultFormatter.IsErrorResult("{\"success\":true,\"count\":5}"));
    }

    [Fact]
    public void ReturnsFalse_WhenSuccessFieldAbsent()
    {
        Assert.False(McpResultFormatter.IsErrorResult("{\"items\":[1,2,3]}"));
    }

    [Fact]
    public void ReturnsFalse_WhenSuccessIsStringNotBoolean()
    {
        // Tools always emit a boolean success; a string "false" is not the contract,
        // so we deliberately do not treat it as an error.
        Assert.False(McpResultFormatter.IsErrorResult("{\"success\":\"false\"}"));
    }

    [Fact]
    public void ReturnsFalse_WhenNotJson()
    {
        // e.g. an action='help' plain-text payload.
        Assert.False(McpResultFormatter.IsErrorResult("This is help text, not JSON."));
    }

    [Fact]
    public void ReturnsFalse_WhenJsonArrayNotObject()
    {
        Assert.False(McpResultFormatter.IsErrorResult("[{\"success\":false}]"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsFalse_WhenNullOrWhitespace(string input)
    {
        Assert.False(McpResultFormatter.IsErrorResult(input));
    }
}

public class McpResultFormatterFormatExceptionResultTests
{
    [Fact]
    public void ProducesSuccessFalseWithMessage()
    {
        var result = McpResultFormatter.FormatExceptionResult(new InvalidOperationException("kaboom"));

        var obj = JObject.Parse(result);
        Assert.False(obj["success"].Value<bool>());
        Assert.Equal("kaboom", obj["error"].Value<string>());
    }

    [Fact]
    public void UnwrapsTargetInvocationException_ToInnerMessage()
    {
        var inner = new ArgumentException("real cause");
        var wrapped = new TargetInvocationException(inner);

        var result = McpResultFormatter.FormatExceptionResult(wrapped);

        var obj = JObject.Parse(result);
        Assert.Equal("real cause", obj["error"].Value<string>());
    }

    [Fact]
    public void UsesOuterMessage_WhenTargetInvocationExceptionHasNoInner()
    {
        var wrapped = new TargetInvocationException("outer only", null);

        var result = McpResultFormatter.FormatExceptionResult(wrapped);

        var obj = JObject.Parse(result);
        Assert.Equal("outer only", obj["error"].Value<string>());
    }

    [Fact]
    public void Output_IsRecognizedAsError_RoundTrip()
    {
        // The formatted exception payload must itself be classified as an error,
        // so the boundary reports isError=true consistently.
        var result = McpResultFormatter.FormatExceptionResult(new Exception("x"));
        Assert.True(McpResultFormatter.IsErrorResult(result));
    }
}
