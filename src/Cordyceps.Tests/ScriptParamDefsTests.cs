using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Contract tests for ScriptParamDefs.TryParse — the gh_script configure partial-update
    /// contract hinges on distinguishing "side omitted" (null defs), "clear the side" (empty
    /// list), and "malformed JSON" (hard error, must NOT be read as an empty array: that
    /// previously wiped all params and wires under success:true).
    /// </summary>
    public class ScriptParamDefsTests
    {
        [Fact]
        public void TryParse_ValidArray_ReturnsDefs()
        {
            var ok = ScriptParamDefs.TryParse(
                "[{\"name\":\"x\",\"type\":\"double\",\"access\":\"list\"},{\"name\":\"y\"}]",
                out var defs, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotNull(defs);
            Assert.Equal(2, defs.Count);
            Assert.Equal("x", defs[0].Name);
            Assert.Equal("double", defs[0].Type);
            Assert.Equal("list", defs[0].Access);
            // Missing fields fall back to the historical defaults.
            Assert.Equal("y", defs[1].Name);
            Assert.Equal("object", defs[1].Type);
            Assert.Equal("", defs[1].Access);
        }

        [Fact]
        public void TryParse_EmptyArray_IsValidAndMeansClear()
        {
            var ok = ScriptParamDefs.TryParse("[]", out var defs, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotNull(defs); // non-null empty list = "clear this side"
            Assert.Empty(defs);
        }

        [Theory]
        [InlineData("[{\"name\":\"x\"")]            // truncated JSON
        [InlineData("not json at all")]              // garbage
        [InlineData("{\"name\":\"x\"}")]             // object, not array
        [InlineData("42")]                           // scalar, not array
        [InlineData("[\"x\",\"y\"]")]                // array of strings, not objects
        [InlineData("[{\"name\":\"x\"}, 7]")]        // mixed: non-object item
        public void TryParse_MalformedJson_FailsAndIsNotReadAsEmpty(string json)
        {
            var ok = ScriptParamDefs.TryParse(json, out var defs, out var error);

            Assert.False(ok);
            Assert.Null(defs); // must NOT come back as an empty (or partial) list
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        public void TryParse_NullOrWhitespace_MeansSideOmitted(string json)
        {
            var ok = ScriptParamDefs.TryParse(json, out var defs, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Null(defs); // null = side omitted, leave params and wires untouched
        }
    }
}
