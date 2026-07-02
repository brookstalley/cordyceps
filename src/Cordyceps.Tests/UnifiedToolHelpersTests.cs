using System.Collections.Generic;
using Cordyceps.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Cordyceps.Tests;

public class UnifiedToolHelpersValidateActionTests
{
    private static UnifiedToolInfo SampleTool() => new UnifiedToolInfo
    {
        ToolName = "gh_sample",
        Description = "Sample tool",
        Actions = new Dictionary<string, ActionInfo>
        {
            ["add"] = new ActionInfo
            {
                Name = "add",
                Description = "Add a thing",
                Required = new[] { "type" },
                Optional = new[] { "x", "y" },
                Example = "action='add', type='Circle'"
            },
            ["list"] = new ActionInfo { Name = "list", Description = "List things" }
        }
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReturnsError_WhenActionMissing(string action)
    {
        var result = UnifiedToolHelpers.ValidateAction(SampleTool(), action, new Dictionary<string, object>());
        var obj = JObject.Parse(result);
        Assert.False(obj["success"].Value<bool>());
        Assert.Equal("Missing required parameter: action", obj["error"].Value<string>());
        // Surfaces the available actions to guide the caller.
        Assert.Contains("add", obj["availableActions"].ToObject<string[]>());
        Assert.Contains("list", obj["availableActions"].ToObject<string[]>());
    }

    [Fact]
    public void ReturnsError_WhenActionUnknown()
    {
        var result = UnifiedToolHelpers.ValidateAction(SampleTool(), "frobnicate", new Dictionary<string, object>());
        var obj = JObject.Parse(result);
        Assert.False(obj["success"].Value<bool>());
        Assert.Equal("Unknown action: 'frobnicate'", obj["error"].Value<string>());
    }

    [Fact]
    public void ReturnsError_WhenRequiredParamMissing()
    {
        var result = UnifiedToolHelpers.ValidateAction(SampleTool(), "add", new Dictionary<string, object>());
        var obj = JObject.Parse(result);
        Assert.False(obj["success"].Value<bool>());
        Assert.Equal("Missing required params for 'add': type", obj["error"].Value<string>());
        Assert.Equal("action='add', type='Circle'", obj["example"].Value<string>());
    }

    [Fact]
    public void TreatsNullValuedRequiredParam_AsMissing()
    {
        var provided = new Dictionary<string, object> { ["type"] = null };
        var result = UnifiedToolHelpers.ValidateAction(SampleTool(), "add", provided);
        var obj = JObject.Parse(result);
        Assert.False(obj["success"].Value<bool>());
        Assert.Contains("type", obj["error"].Value<string>());
    }

    [Fact]
    public void ReturnsNull_WhenValid()
    {
        var provided = new Dictionary<string, object> { ["type"] = "Circle" };
        Assert.Null(UnifiedToolHelpers.ValidateAction(SampleTool(), "add", provided));
    }

    [Theory]
    [InlineData("Add")]
    [InlineData("ADD")]
    [InlineData("aDd")]
    public void MatchesActionCaseInsensitively_LikeDispatch(string action)
    {
        // Dispatch lowercases the action, so validation must accept any casing of a known
        // action — and still enforce its required params.
        var provided = new Dictionary<string, object> { ["type"] = "Circle" };
        Assert.Null(UnifiedToolHelpers.ValidateAction(SampleTool(), action, provided));
    }

    [Fact]
    public void EnforcesRequiredParams_ForMixedCaseAction()
    {
        var result = UnifiedToolHelpers.ValidateAction(SampleTool(), "ADD", new Dictionary<string, object>());
        var obj = JObject.Parse(result);
        Assert.False(obj["success"].Value<bool>());
        Assert.Contains("type", obj["error"].Value<string>());
    }

    [Fact]
    public void ReturnsNull_WhenActionHasNoRequiredParams()
    {
        Assert.Null(UnifiedToolHelpers.ValidateAction(SampleTool(), "list", new Dictionary<string, object>()));
    }
}

public class UnifiedToolHelpersGetParamTests
{
    [Fact]
    public void ReturnsDefault_WhenKeyMissing()
    {
        var p = new Dictionary<string, object>();
        Assert.Equal(42, UnifiedToolHelpers.GetParam(p, "n", 42));
    }

    [Fact]
    public void ReturnsDefault_WhenValueNull()
    {
        var p = new Dictionary<string, object> { ["n"] = null };
        Assert.Equal(42, UnifiedToolHelpers.GetParam(p, "n", 42));
    }

    [Fact]
    public void ReturnsValue_OnDirectTypeMatch()
    {
        var p = new Dictionary<string, object> { ["s"] = "hello" };
        Assert.Equal("hello", UnifiedToolHelpers.GetParam<string>(p, "s"));
    }

    [Fact]
    public void ConvertsIntToDouble()
    {
        var p = new Dictionary<string, object> { ["d"] = 5 };
        Assert.Equal(5.0, UnifiedToolHelpers.GetParam<double>(p, "d"));
    }

    [Fact]
    public void ConvertsDoubleToInt()
    {
        var p = new Dictionary<string, object> { ["i"] = 3.0 };
        Assert.Equal(3, UnifiedToolHelpers.GetParam<int>(p, "i"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("anything-else", false)]
    public void ParsesBoolFromString(string value, bool expected)
    {
        var p = new Dictionary<string, object> { ["b"] = value };
        Assert.Equal(expected, UnifiedToolHelpers.GetParam<bool>(p, "b"));
    }

    [Fact]
    public void ReturnsBool_OnDirectBool()
    {
        var p = new Dictionary<string, object> { ["b"] = true };
        Assert.True(UnifiedToolHelpers.GetParam<bool>(p, "b"));
    }

    [Fact]
    public void ConvertsNonStringToString()
    {
        var p = new Dictionary<string, object> { ["s"] = 123 };
        Assert.Equal("123", UnifiedToolHelpers.GetParam<string>(p, "s"));
    }

    [Fact]
    public void DeserializesComplexTypeViaJson()
    {
        var p = new Dictionary<string, object> { ["list"] = new[] { 1, 2, 3 } };
        var result = UnifiedToolHelpers.GetParam<List<int>>(p, "list");
        Assert.Equal(new List<int> { 1, 2, 3 }, result);
    }

    [Fact]
    public void ReturnsDefault_WhenConversionThrows()
    {
        // "abc" -> int goes through Convert.ToInt32, which throws FormatException;
        // the catch in UnifiedToolHelpers.GetParam swallows it and returns the default.
        var p = new Dictionary<string, object> { ["n"] = "abc" };
        Assert.Equal(-1, UnifiedToolHelpers.GetParam(p, "n", -1));
    }
}

public class UnifiedToolHelpersGenerateHelpTests
{
    [Fact]
    public void IncludesToolMetadataAndActions()
    {
        var tool = new UnifiedToolInfo
        {
            ToolName = "gh_sample",
            Description = "Sample",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["add"] = new ActionInfo
                {
                    Description = "Add",
                    Required = new[] { "type" },
                    Optional = new[] { "x" },
                    Example = "ex",
                    Tips = new[] { "tip1" }
                }
            },
            Notes = new[] { "note1" }
        };

        var obj = JObject.Parse(UnifiedToolHelpers.GenerateHelp(tool));
        Assert.Equal("gh_sample", obj["tool"].Value<string>());
        Assert.Equal("Sample", obj["description"].Value<string>());

        var add = obj["actions"]["add"];
        Assert.Equal("Add", add["description"].Value<string>());
        Assert.Contains("type", add["required"].ToObject<string[]>());
        Assert.Contains("x", add["optional"].ToObject<string[]>());
        Assert.Equal("ex", add["example"].Value<string>());
        Assert.Contains("tip1", add["tips"].ToObject<string[]>());
        Assert.Contains("note1", obj["notes"].ToObject<string[]>());
    }

    [Fact]
    public void OmitsEmptyOptionalFields()
    {
        var tool = new UnifiedToolInfo
        {
            ToolName = "gh_sample",
            Description = "Sample",
            Actions = new Dictionary<string, ActionInfo>
            {
                ["list"] = new ActionInfo { Description = "List" }
            }
        };

        var obj = JObject.Parse(UnifiedToolHelpers.GenerateHelp(tool));
        var list = (JObject)obj["actions"]["list"];
        Assert.True(list.ContainsKey("description"));
        Assert.False(list.ContainsKey("required"));
        Assert.False(list.ContainsKey("optional"));
        Assert.False(list.ContainsKey("example"));
        Assert.False(list.ContainsKey("tips"));
        // notes omitted at the top level when none are set
        Assert.False(((JObject)obj).ContainsKey("notes"));
    }
}

public class UnifiedToolHelpersBuildParamsTests
{
    [Fact]
    public void IncludesNonNull_SkipsNull()
    {
        var dict = UnifiedToolHelpers.BuildParams(
            ("a", "x"),
            ("b", null),
            ("c", 3));

        Assert.True(dict.ContainsKey("a"));
        Assert.False(dict.ContainsKey("b"));
        Assert.Equal(3, dict["c"]);
    }
}
