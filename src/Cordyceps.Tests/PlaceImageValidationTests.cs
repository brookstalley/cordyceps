using System;
using System.IO;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

public class PlaceImageValidationTests
{
    // A real, existing file so the path/exists checks pass and dimension checks are exercised.
    private static string TempImage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cordyceps-place-image-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG magic, content irrelevant
        return path;
    }

    [Fact]
    public void Valid_WhenFileExistsAndDimensionsPositive()
    {
        var path = TempImage();
        try
        {
            Assert.Null(PlaceImageValidation.Validate(path, 200, 150));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Error_WhenPathMissing(string path)
    {
        Assert.Equal("path is required", PlaceImageValidation.Validate(path, 200, 150));
    }

    [Fact]
    public void Error_WhenFileDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"cordyceps-does-not-exist-{Guid.NewGuid():N}.png");
        var result = PlaceImageValidation.Validate(missing, 200, 150);
        Assert.StartsWith("Image file not found:", result);
        Assert.Contains(missing, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Error_WhenWidthNotPositive(double width)
    {
        var path = TempImage();
        try
        {
            Assert.Equal("width must be greater than 0", PlaceImageValidation.Validate(path, width, 150));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Error_WhenHeightNotPositive(double height)
    {
        var path = TempImage();
        try
        {
            Assert.Equal("height must be greater than 0", PlaceImageValidation.Validate(path, 200, height));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PathMissing_TakesPrecedenceOverDimensions()
    {
        // Missing path is reported before non-positive dimensions (checked first).
        Assert.Equal("path is required", PlaceImageValidation.Validate(null, -5, -5));
    }
}
