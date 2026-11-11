using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Contract tests for <see cref="ScriptSignature"/>, the reader <c>gh_script(action='set')</c>
    /// syncs ports from and <see cref="PortRemoval"/> predicts that sync with. One reader for both
    /// is what keeps the prediction honest.
    /// </summary>
    public class ScriptSignatureTests
    {
        [Fact]
        public void Parse_SplitsInputsFromRefAndOutOutputs()
        {
            var parsed = ScriptSignature.Parse("CSharpComponent",
                "private void RunScript(double x, int n, ref object a, out object b) { }");

            Assert.NotNull(parsed);
            Assert.Equal(new[] { "x", "n" }, parsed.Value.inputs);
            Assert.Equal(new[] { "a", "b" }, parsed.Value.outputs);
        }

        [Fact]
        public void Parse_GenericTypeWithACommaStaysOneParameter()
        {
            var parsed = ScriptSignature.Parse("CSharpComponent",
                "private void RunScript(Dictionary<string, int> map, List<Point3d> pts, ref object a) { }");

            Assert.Equal(new[] { "map", "pts" }, parsed.Value.inputs);
            Assert.Equal(new[] { "a" }, parsed.Value.outputs);
        }

        [Fact]
        public void Parse_EmptySignature_DeclaresNoPorts()
        {
            var parsed = ScriptSignature.Parse("CSharpComponent", "private void RunScript() { }");

            Assert.NotNull(parsed);
            Assert.Empty(parsed.Value.inputs);
            Assert.Empty(parsed.Value.outputs);
        }

        [Theory]
        [InlineData("Python3Component", "def RunScript(self, x):\n    return x")]
        [InlineData("CSharpComponent", "a = x + 1;")]
        [InlineData("CSharpComponent", "")]
        [InlineData("CSharpComponent", null)]
        public void Parse_PortsNotDerivedFromCode_ReturnsNull(string typeName, string code)
        {
            Assert.Null(ScriptSignature.Parse(typeName, code));
        }

        [Fact]
        public void SplitParameters_SplitsOnlyTopLevelCommas()
        {
            var parts = ScriptSignature.SplitParameters("A<B, C> x, int y");

            Assert.Equal(new[] { "A<B, C> x", " int y" }, parts);
        }
    }
}
