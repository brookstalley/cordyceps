using System;
using System.IO;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

public class RequestValidatorStringTests
{
    [Theory]
    [InlineData("value")]
    [InlineData(" ")] // whitespace is non-empty, so ValidateRequired accepts it
    public void ValidateRequired_ReturnsTrue_ForNonEmpty(string value)
    {
        Assert.True(RequestValidator.ValidateRequired(value, "name", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateRequired_ReturnsFalse_ForNullOrEmpty(string value)
    {
        Assert.False(RequestValidator.ValidateRequired(value, "name", out var error));
        Assert.Equal("name is required", error);
    }

    [Fact]
    public void ValidateNotWhitespace_ReturnsTrue_ForRealContent()
    {
        Assert.True(RequestValidator.ValidateNotWhitespace("hi", "name", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void ValidateNotWhitespace_ReturnsFalse_ForNullEmptyOrWhitespace(string value)
    {
        Assert.False(RequestValidator.ValidateNotWhitespace(value, "name", out var error));
        Assert.Equal("name cannot be empty or whitespace", error);
    }
}

public class RequestValidatorGuidTests
{
    [Fact]
    public void ValidateGuid_ReturnsTrue_AndParses_ForValidGuid()
    {
        var expected = Guid.NewGuid();
        Assert.True(RequestValidator.ValidateGuid(expected.ToString(), "id", out var guid, out var error));
        Assert.Equal(expected, guid);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateGuid_ReturnsFalse_ForMissing(string value)
    {
        Assert.False(RequestValidator.ValidateGuid(value, "id", out var guid, out var error));
        Assert.Equal(Guid.Empty, guid);
        Assert.Equal("id is required", error);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    public void ValidateGuid_ReturnsFalse_ForMalformed(string value)
    {
        Assert.False(RequestValidator.ValidateGuid(value, "id", out var guid, out var error));
        Assert.Equal(Guid.Empty, guid);
        Assert.Equal("Invalid id format - expected GUID", error);
    }

    [Fact]
    public void ValidateGuidFormat_DelegatesToValidateGuid()
    {
        Assert.True(RequestValidator.ValidateGuidFormat(Guid.NewGuid().ToString(), "id", out var ok));
        Assert.Null(ok);

        Assert.False(RequestValidator.ValidateGuidFormat("nope", "id", out var err));
        Assert.Equal("Invalid id format - expected GUID", err);
    }
}

public class RequestValidatorNumericTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(5.0)]
    [InlineData(10.0)]
    public void ValidateRange_Double_ReturnsTrue_WithinInclusiveBounds(double value)
    {
        Assert.True(RequestValidator.ValidateRange(value, 0.0, 10.0, "v", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.1)]
    public void ValidateRange_Double_ReturnsFalse_OutOfBounds(double value)
    {
        Assert.False(RequestValidator.ValidateRange(value, 0.0, 10.0, "v", out var error));
        Assert.Equal("v must be between 0 and 10", error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void ValidateRange_Int_ReturnsTrue_WithinInclusiveBounds(int value)
    {
        Assert.True(RequestValidator.ValidateRange(value, 1, 10, "v", out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void ValidateRange_Int_ReturnsFalse_OutOfBounds(int value)
    {
        Assert.False(RequestValidator.ValidateRange(value, 1, 10, "v", out var error));
        Assert.Equal("v must be between 1 and 10", error);
    }

    [Fact]
    public void ValidatePositive_Double_RejectsZeroAndNegative()
    {
        Assert.True(RequestValidator.ValidatePositive(0.001, "v", out var ok));
        Assert.Null(ok);

        Assert.False(RequestValidator.ValidatePositive(0.0, "v", out var zero));
        Assert.Equal("v must be positive", zero);

        Assert.False(RequestValidator.ValidatePositive(-1.0, "v", out var neg));
        Assert.Equal("v must be positive", neg);
    }

    [Fact]
    public void ValidatePositive_Int_RejectsZeroAndNegative()
    {
        Assert.True(RequestValidator.ValidatePositive(1, "v", out var ok));
        Assert.Null(ok);

        Assert.False(RequestValidator.ValidatePositive(0, "v", out var zero));
        Assert.Equal("v must be positive", zero);

        Assert.False(RequestValidator.ValidatePositive(-3, "v", out var neg));
        Assert.Equal("v must be positive", neg);
    }

    [Fact]
    public void ValidateNonNegative_Double_AcceptsZero_RejectsNegative()
    {
        Assert.True(RequestValidator.ValidateNonNegative(0.0, "v", out var zero));
        Assert.Null(zero);

        Assert.False(RequestValidator.ValidateNonNegative(-0.5, "v", out var neg));
        Assert.Equal("v cannot be negative", neg);
    }
}

public class RequestValidatorFileTests
{
    [Theory]
    [InlineData("model.gh")]
    [InlineData("MODEL.GH")]   // case-insensitive
    [InlineData("path/to/model.ghx")]
    public void ValidateFileExtension_ReturnsTrue_ForAllowed(string path)
    {
        Assert.True(RequestValidator.ValidateFileExtension(path, new[] { ".gh", ".ghx" }, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void ValidateFileExtension_ReturnsFalse_ForDisallowed()
    {
        Assert.False(RequestValidator.ValidateFileExtension("model.3dm", new[] { ".gh", ".ghx" }, out var error));
        Assert.Equal("Invalid file extension. Allowed: .gh, .ghx", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateFileExtension_ReturnsFalse_ForMissingPath(string path)
    {
        Assert.False(RequestValidator.ValidateFileExtension(path, new[] { ".gh" }, out var error));
        Assert.Equal("File path is required", error);
    }

    [Fact]
    public void ValidateFileExists_ReturnsTrue_ForExistingFile()
    {
        var temp = Path.GetTempFileName();
        try
        {
            Assert.True(RequestValidator.ValidateFileExists(temp, out var error));
            Assert.Null(error);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void ValidateFileExists_ReturnsFalse_ForMissingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "cordyceps-does-not-exist-" + Guid.NewGuid() + ".gh");
        Assert.False(RequestValidator.ValidateFileExists(path, out var error));
        Assert.Equal($"File not found: {path}", error);
    }

    [Fact]
    public void ValidateFileExists_ReturnsFalse_ForEmptyPath()
    {
        Assert.False(RequestValidator.ValidateFileExists("", out var error));
        Assert.Equal("File path is required", error);
    }

    [Fact]
    public void ValidateDirectoryExists_ReturnsTrue_ForExistingDir()
    {
        Assert.True(RequestValidator.ValidateDirectoryExists(Path.GetTempPath(), out var error));
        Assert.Null(error);
    }

    [Fact]
    public void ValidateDirectoryExists_ReturnsFalse_ForMissingDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "cordyceps-no-dir-" + Guid.NewGuid());
        Assert.False(RequestValidator.ValidateDirectoryExists(path, out var error));
        Assert.Equal($"Directory not found: {path}", error);
    }
}

public class RequestValidatorOneOfTests
{
    [Theory]
    [InlineData("python")]
    [InlineData("PYTHON")]   // case-insensitive
    [InlineData("CSharp")]
    public void ValidateOneOf_ReturnsTrue_ForAllowed(string value)
    {
        Assert.True(RequestValidator.ValidateOneOf(value, new[] { "python", "csharp" }, "lang", out var error));
        Assert.Null(error);
    }

    [Fact]
    public void ValidateOneOf_ReturnsFalse_ForDisallowed()
    {
        Assert.False(RequestValidator.ValidateOneOf("ruby", new[] { "python", "csharp" }, "lang", out var error));
        Assert.Equal("Invalid lang. Allowed values: python, csharp", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateOneOf_ReturnsFalse_ForMissing(string value)
    {
        Assert.False(RequestValidator.ValidateOneOf(value, new[] { "a" }, "lang", out var error));
        Assert.Equal("lang is required", error);
    }
}
