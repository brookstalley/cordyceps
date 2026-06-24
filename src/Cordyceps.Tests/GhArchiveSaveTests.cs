using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

public class GhArchiveSaveTests
{
    [Theory]
    [InlineData("/tmp/work.gh")]
    [InlineData("/tmp/work.GH")]
    [InlineData("relative.gh")]
    [InlineData("/path/with spaces/def.gh")]
    public void TryGetFormat_BinaryForGh(string path)
    {
        Assert.True(GhArchiveSave.TryGetFormat(path, out var format));
        Assert.Equal("binary", format);
    }

    [Theory]
    [InlineData("/tmp/work.ghx")]
    [InlineData("/tmp/work.GHX")]
    [InlineData("relative.ghx")]
    public void TryGetFormat_XmlForGhx(string path)
    {
        Assert.True(GhArchiveSave.TryGetFormat(path, out var format));
        Assert.Equal("XML", format);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/tmp/no-extension")]
    [InlineData("/tmp/work.txt")]
    [InlineData("/tmp/work.3dm")]
    [InlineData("/tmp/work.ghcluster")]
    public void TryGetFormat_FalseForUnsupported(string path)
    {
        Assert.False(GhArchiveSave.TryGetFormat(path, out var format));
        Assert.Null(format);
    }

    // Regression guard for GitHub issue #14: the .gh branch used to pass overwrite=false, so every
    // save over an existing .gh returned "Failed to write file". The flags must not depend on
    // format — overwrite is always true.
    [Fact]
    public void WriteFlags_AlwaysOverwrites()
    {
        Assert.True(GhArchiveSave.WriteFlags.overwrite);
    }

    // A successful save adopts the path as the document's current path (File→Save semantics).
    [Fact]
    public void WriteFlags_RemembersPath()
    {
        Assert.True(GhArchiveSave.WriteFlags.rememberPath);
    }
}
