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

    [Theory]
    [InlineData("art.png")]
    [InlineData("textures/art.png")]
    public void Error_WhenPathRelative(string relative)
    {
        // Normalize separators per-platform so the case is meaningful on Windows and macOS.
        relative = relative.Replace('/', Path.DirectorySeparatorChar);
        var result = PlaceImageValidation.Validate(relative, 200, 150);
        Assert.StartsWith("path must be an absolute path", result);
        Assert.Contains(relative, result);
    }

    [Fact]
    public void RelativePath_RejectedEvenIfFileExistsInCwd()
    {
        // A relative path that File.Exists would accept against the process CWD must still be
        // rejected — the absolute-path rule fires before the existence check.
        var fileName = $"cordyceps-relative-{Guid.NewGuid():N}.png";
        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        File.WriteAllBytes(cwdPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        try
        {
            Assert.True(File.Exists(fileName)); // relative form resolves against CWD
            var result = PlaceImageValidation.Validate(fileName, 200, 150);
            Assert.StartsWith("path must be an absolute path", result);
        }
        finally
        {
            File.Delete(cwdPath);
        }
    }

    [Fact]
    public void AbsolutePath_PassesTheRootedCheck()
    {
        // A rooted-but-missing path must get past the absolute-path rule and fail on existence
        // instead (proves rooted paths are accepted by the new check).
        var rooted = Path.Combine(Path.GetTempPath(), $"cordyceps-rooted-missing-{Guid.NewGuid():N}.png");
        Assert.True(Path.IsPathRooted(rooted));
        Assert.StartsWith("Image file not found:", PlaceImageValidation.Validate(rooted, 200, 150));
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
