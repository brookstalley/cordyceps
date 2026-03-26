using System;
using System.Text.Json;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

public class GetJsonTypeTests
{
    [Theory]
    [InlineData(typeof(int), "integer")]
    [InlineData(typeof(long), "integer")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(bool), "boolean")]
    public void ReturnsCorrectJsonSchemaType(Type clrType, string expected)
    {
        Assert.Equal(expected, JsonTypeConverter.GetJsonType(clrType));
    }

    [Fact]
    public void UnknownTypeFallsBackToString()
    {
        Assert.Equal("string", JsonTypeConverter.GetJsonType(typeof(DateTime)));
    }
}

public class ConvertJsonValueTests
{
    private static JsonElement Parse(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    // --- Happy path: matching ValueKind ---

    [Fact]
    public void NumberToInt() =>
        Assert.Equal(42, JsonTypeConverter.ConvertJsonValue(Parse("42"), typeof(int)));

    [Fact]
    public void NumberToLong() =>
        Assert.Equal(9999999999L, JsonTypeConverter.ConvertJsonValue(Parse("9999999999"), typeof(long)));

    [Fact]
    public void NumberToDouble() =>
        Assert.Equal(3.14, JsonTypeConverter.ConvertJsonValue(Parse("3.14"), typeof(double)));

    [Fact]
    public void NumberToFloat() =>
        Assert.Equal(3.14f, JsonTypeConverter.ConvertJsonValue(Parse("3.14"), typeof(float)));

    [Fact]
    public void StringToString() =>
        Assert.Equal("hello", JsonTypeConverter.ConvertJsonValue(Parse("\"hello\""), typeof(string)));

    [Fact]
    public void TrueToBoolean() =>
        Assert.Equal(true, JsonTypeConverter.ConvertJsonValue(Parse("true"), typeof(bool)));

    [Fact]
    public void FalseToBoolean() =>
        Assert.Equal(false, JsonTypeConverter.ConvertJsonValue(Parse("false"), typeof(bool)));

    // --- Cross-type coercion: string -> number (the bug from issue #13) ---

    [Fact]
    public void StringToInt() =>
        Assert.Equal(300, JsonTypeConverter.ConvertJsonValue(Parse("\"300\""), typeof(int)));

    [Fact]
    public void StringToLong() =>
        Assert.Equal(300L, JsonTypeConverter.ConvertJsonValue(Parse("\"300\""), typeof(long)));

    [Fact]
    public void StringToDouble() =>
        Assert.Equal(3.14, JsonTypeConverter.ConvertJsonValue(Parse("\"3.14\""), typeof(double)));

    [Fact]
    public void StringToFloat() =>
        Assert.Equal(3.14f, JsonTypeConverter.ConvertJsonValue(Parse("\"3.14\""), typeof(float)));

    [Fact]
    public void StringToBool() =>
        Assert.Equal(true, JsonTypeConverter.ConvertJsonValue(Parse("\"true\""), typeof(bool)));

    // --- Cross-type coercion: number -> other ---

    [Fact]
    public void NumberToString() =>
        Assert.Equal("42", JsonTypeConverter.ConvertJsonValue(Parse("42"), typeof(string)));

    [Fact]
    public void NumberOneToBool() =>
        Assert.Equal(true, JsonTypeConverter.ConvertJsonValue(Parse("1"), typeof(bool)));

    [Fact]
    public void NumberZeroToBool() =>
        Assert.Equal(false, JsonTypeConverter.ConvertJsonValue(Parse("0"), typeof(bool)));

    // --- Error cases ---

    [Fact]
    public void InvalidStringToIntThrows() =>
        Assert.Throws<InvalidOperationException>(() =>
            JsonTypeConverter.ConvertJsonValue(Parse("\"not_a_number\""), typeof(int)));

    [Fact]
    public void InvalidStringToDoubleThrows() =>
        Assert.Throws<InvalidOperationException>(() =>
            JsonTypeConverter.ConvertJsonValue(Parse("\"not_a_number\""), typeof(double)));

    [Fact]
    public void InvalidStringToBoolThrows() =>
        Assert.Throws<InvalidOperationException>(() =>
            JsonTypeConverter.ConvertJsonValue(Parse("\"not_a_bool\""), typeof(bool)));

    // --- Edge cases ---

    [Fact]
    public void NegativeStringToInt() =>
        Assert.Equal(-42, JsonTypeConverter.ConvertJsonValue(Parse("\"-42\""), typeof(int)));

    [Fact]
    public void NegativeStringToDouble() =>
        Assert.Equal(-3.14, JsonTypeConverter.ConvertJsonValue(Parse("\"-3.14\""), typeof(double)));

    [Fact]
    public void BoolToString() =>
        Assert.Equal("true", JsonTypeConverter.ConvertJsonValue(Parse("true"), typeof(string)));
}
