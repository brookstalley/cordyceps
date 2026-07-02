using System;
using System.Drawing;
using System.Globalization;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

/// <summary>
/// Tests for <see cref="ParseHelpers"/> — the host-free wire-format parsing extracted from
/// ToolHelpers (which keeps thin forwarders, so these tests cover the tool-facing behavior).
/// </summary>
public class ParseHelpersGuidTests
{
    [Fact]
    public void ValidGuid_Parses()
    {
        Assert.True(ParseHelpers.TryParseGuid("01234567-89ab-cdef-0123-456789abcdef", out var guid, out var error));
        Assert.Equal(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), guid);
        Assert.Null(error);
    }

    [Fact]
    public void BracedGuid_Parses()
    {
        Assert.True(ParseHelpers.TryParseGuid("{01234567-89ab-cdef-0123-456789abcdef}", out var guid, out _));
        Assert.Equal(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), guid);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("01234567-89ab-cdef-0123")]
    [InlineData("zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz")]
    public void MalformedGuid_FailsWithFormatError(string input)
    {
        Assert.False(ParseHelpers.TryParseGuid(input, out _, out var error));
        Assert.Equal("Invalid component ID format", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingGuid_FailsWithRequiredError(string input)
    {
        Assert.False(ParseHelpers.TryParseGuid(input, out var guid, out var error));
        Assert.Equal(Guid.Empty, guid);
        Assert.Equal("Component ID is required", error);
    }
}

public class ParseHelpersGuidArrayTests
{
    private const string G1 = "11111111-1111-1111-1111-111111111111";
    private const string G2 = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public void ValidArray_ParsesAllGuids()
    {
        Assert.True(ParseHelpers.TryParseGuidArray($"[\"{G1}\",\"{G2}\"]", out var guids, out var error));
        Assert.Equal(new[] { Guid.Parse(G1), Guid.Parse(G2) }, guids);
        Assert.Null(error);
    }

    [Fact]
    public void MixedArray_OneBadGuid_FailsNamingTheBadEntry()
    {
        Assert.False(ParseHelpers.TryParseGuidArray($"[\"{G1}\",\"nope\"]", out var guids, out var error));
        Assert.Null(guids);
        Assert.Equal("Invalid GUID in array: nope", error);
    }

    [Fact]
    public void MalformedJson_FailsWithJsonError()
    {
        Assert.False(ParseHelpers.TryParseGuidArray("[\"abc\"", out var guids, out var error));
        Assert.Null(guids);
        Assert.StartsWith("Invalid JSON format", error);
    }

    [Fact]
    public void EmptyJsonArray_SucceedsWithEmptyList()
    {
        Assert.True(ParseHelpers.TryParseGuidArray("[]", out var guids, out _));
        Assert.Empty(guids);
    }

    [Fact]
    public void EmptyInput_FailsWithRequiredError()
    {
        Assert.False(ParseHelpers.TryParseGuidArray("", out _, out var error));
        Assert.Equal("JSON input is required", error);
    }
}

public class ParseHelpersColorTests
{
    [Theory]
    [InlineData("#FF8000", 255, 128, 0)]
    [InlineData("FF8000", 255, 128, 0)]  // hex without leading '#'
    [InlineData("#ff8000", 255, 128, 0)] // lowercase hex digits
    [InlineData("#000000", 0, 0, 0)]
    public void HexRrGgBb_Parses(string input, int r, int g, int b)
    {
        Assert.True(ParseHelpers.TryParseColor(input, out var color));
        Assert.Equal((r, g, b), (color.R, color.G, color.B));
    }

    [Fact]
    public void RgbTriplet_Parses()
    {
        Assert.True(ParseHelpers.TryParseColor("255, 128, 0", out var color));
        Assert.Equal((255, 128, 0), ((int)color.R, (int)color.G, (int)color.B));
    }

    [Fact]
    public void RgbTriplet_OutOfRangeComponents_AreClamped()
    {
        Assert.True(ParseHelpers.TryParseColor("300,-5,0", out var color));
        Assert.Equal((255, 0, 0), ((int)color.R, (int)color.G, (int)color.B));
    }

    [Theory]
    [InlineData("Red", 255, 0, 0)]
    [InlineData("red", 255, 0, 0)] // named lookup is case-insensitive
    [InlineData("Blue", 0, 0, 255)]
    public void NamedColor_Parses(string input, int r, int g, int b)
    {
        Assert.True(ParseHelpers.TryParseColor(input, out var color));
        Assert.Equal((r, g, b), ((int)color.R, (int)color.G, (int)color.B));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("notacolor")]
    [InlineData("#F00")]       // 3-digit shorthand hex is not supported
    [InlineData("#80FF8000")]  // 8-digit AARRGGBB is not supported
    [InlineData("#GGGGGG")]    // right length, invalid hex digits
    [InlineData("1,2")]        // wrong arity for RGB triplet
    public void UnsupportedOrGarbage_Fails(string input)
    {
        Assert.False(ParseHelpers.TryParseColor(input, out _));
    }

    [Fact]
    public void ColorToHex_FormatsAsHashRrGgBb()
    {
        Assert.Equal("#123456", ParseHelpers.ColorToHex(Color.FromArgb(0x12, 0x34, 0x56)));
    }

    [Fact]
    public void ColorToHex_RoundTripsThroughTryParseColor()
    {
        var original = Color.FromArgb(18, 52, 86);
        Assert.True(ParseHelpers.TryParseColor(ParseHelpers.ColorToHex(original), out var parsed));
        Assert.Equal((original.R, original.G, original.B), (parsed.R, parsed.G, parsed.B));
    }
}

public class ParseHelpersBoolTests
{
    // Lenient form: unrecognized input falls back to the default.
    [Theory]
    [InlineData("true", false, true)]
    [InlineData("TRUE", false, true)]
    [InlineData("false", true, false)]
    [InlineData("1", false, true)]
    [InlineData("0", true, false)]
    [InlineData("yes", false, false)]   // yes/no are NOT accepted by the lenient form
    [InlineData("garbage", false, false)]
    [InlineData("garbage", true, true)]
    [InlineData("", true, true)]
    [InlineData(null, false, false)]
    public void ParseBool_Lenient(string input, bool defaultValue, bool expected)
    {
        Assert.Equal(expected, ParseHelpers.ParseBool(input, defaultValue));
    }

    // Strict form: recognized values parse, anything else is a failure the caller must surface.
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData(" yes ", true)] // trimmed
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    public void TryParseBool_RecognizedValues_Parse(string input, bool expected)
    {
        Assert.True(ParseHelpers.TryParseBool(input, out var result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2")]
    [InlineData("garbage")]
    [InlineData("truthy")]
    public void TryParseBool_UnrecognizedValues_Fail(string input)
    {
        Assert.False(ParseHelpers.TryParseBool(input, out _));
    }
}

public class ParseHelpersXyzTests
{
    [Fact]
    public void ValidTriple_Parses()
    {
        Assert.True(ParseHelpers.TryParseXyz("1.5,2,3", out var x, out var y, out var z));
        Assert.Equal((1.5, 2.0, 3.0), (x, y, z));
    }

    [Fact]
    public void WhitespaceAroundComponents_IsTrimmed()
    {
        Assert.True(ParseHelpers.TryParseXyz(" 1.5 , -2.25 , 3 ", out var x, out var y, out var z));
        Assert.Equal((1.5, -2.25, 3.0), (x, y, z));
    }

    [Fact]
    public void ExponentNotation_Parses()
    {
        Assert.True(ParseHelpers.TryParseXyz("1e2,0,0", out var x, out _, out _));
        Assert.Equal(100.0, x);
    }

    [Fact]
    public void ParsesInvariantCulture_EvenUnderCommaDecimalLocale()
    {
        // The wire format uses '.' as the decimal separator regardless of host locale.
        // Under de-DE (',' decimal separator) current-culture parsing would read "1.5" as 15
        // or fail; the invariant-culture contract keeps it 1.5.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(ParseHelpers.TryParseXyz("1.5,2,3", out var x, out var y, out var z));
            Assert.Equal((1.5, 2.0, 3.0), (x, y, z));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1,2")]       // too few components
    [InlineData("1,2,3,4")]   // too many components
    [InlineData("a,b,c")]     // not numbers
    [InlineData("1,2,z")]     // one bad component
    public void InvalidInput_Fails(string input)
    {
        Assert.False(ParseHelpers.TryParseXyz(input, out _, out _, out _));
    }
}

public class ParseHelpersDeserializeTests
{
    [Fact]
    public void List_ValidJson_Parses()
    {
        Assert.True(ParseHelpers.TryDeserializeList<int>("[1,2,3]", out var result, out var error));
        Assert.Equal(new[] { 1, 2, 3 }, result);
        Assert.Null(error);
    }

    [Fact]
    public void List_EmptyArray_SucceedsWithEmptyList()
    {
        // An empty array is valid input — distinguished from parse failure (false + error).
        Assert.True(ParseHelpers.TryDeserializeList<int>("[]", out var result, out _));
        Assert.Empty(result);
    }

    [Fact]
    public void List_MalformedJson_FailsWithJsonError()
    {
        Assert.False(ParseHelpers.TryDeserializeList<int>("[1,2", out var result, out var error));
        Assert.Null(result);
        Assert.StartsWith("Invalid JSON format", error);
    }

    [Fact]
    public void List_JsonNullLiteral_FailsWithParseError()
    {
        Assert.False(ParseHelpers.TryDeserializeList<int>("null", out _, out var error));
        Assert.Equal("Failed to parse JSON array", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void List_MissingInput_FailsWithRequiredError(string input)
    {
        Assert.False(ParseHelpers.TryDeserializeList<int>(input, out _, out var error));
        Assert.Equal("JSON input is required", error);
    }

    [Fact]
    public void Array_ValidJson_Parses()
    {
        Assert.True(ParseHelpers.TryDeserializeArray<string>("[\"a\",\"b\"]", out var result, out _));
        Assert.Equal(new[] { "a", "b" }, result);
    }

    [Fact]
    public void Array_EmptyArray_SucceedsWithEmptyArray()
    {
        Assert.True(ParseHelpers.TryDeserializeArray<string>("[]", out var result, out _));
        Assert.Empty(result);
    }

    [Fact]
    public void Array_MalformedJson_FailsWithJsonError()
    {
        Assert.False(ParseHelpers.TryDeserializeArray<string>("{oops", out var result, out var error));
        Assert.Null(result);
        Assert.StartsWith("Invalid JSON format", error);
    }
}
