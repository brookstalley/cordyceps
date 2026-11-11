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
        // The unified Script component's type name names no language; the source does.
        [InlineData("ScriptComponent", "def RunScript(self, x, y):\n    return x")]
        [InlineData("ScriptComponent", "#! python 3\nclass MyComponent:\n    def RunScript(self, x):\n        return x")]
        [InlineData("ScriptComponent", "#! python 3\nimport RunScript_helpers\nRunScript(a b)")]
        [InlineData("CSharpComponent", "a = x + 1;")]
        [InlineData("CSharpComponent", "")]
        [InlineData("CSharpComponent", null)]
        public void Parse_PortsNotDerivedFromCode_ReturnsNull(string typeName, string code)
        {
            Assert.Null(ScriptSignature.Parse(typeName, code));
        }

        [Fact]
        public void Parse_UnifiedScriptWithCSharpDirective_ReadsTheSignature()
        {
            var parsed = ScriptSignature.Parse("ScriptComponent",
                "// #! csharp\nprivate void RunScript(double x, ref object a) { }");

            Assert.NotNull(parsed);
            Assert.Equal(new[] { "x" }, parsed.Value.inputs);
            Assert.Equal(new[] { "a" }, parsed.Value.outputs);
        }

        [Fact]
        public void SplitParameters_SplitsOnlyTopLevelCommas()
        {
            var parts = ScriptSignature.SplitParameters("A<B, C> x, int y");

            Assert.Equal(new[] { "A<B, C> x", " int y" }, parts);
        }

        // ---- SignatureSync: the one reading of the sync a source write runs ----

        [Fact]
        public void SignatureSync_ReadsTheDeclaredNamesAndWhichSidesChange()
        {
            var sync = SignatureSync.For("CSharpComponent",
                "private void RunScript(double x, double z, ref object a) { }", new[] { "x", "y" }, new[] { "a" });

            Assert.NotNull(sync);
            Assert.Equal(new[] { "x", "y" }, sync.ExistingInputs);
            Assert.Equal(new[] { "a" }, sync.ExistingOutputs);
            Assert.Equal(new[] { "x", "z" }, sync.Inputs);
            Assert.Equal(new[] { "a" }, sync.Outputs);
            Assert.True(sync.InputsChange);
            Assert.False(sync.OutputsChange);
            Assert.True(sync.ChangesPorts);
        }

        [Theory]
        // Names are compared exactly: case and order both count.
        [InlineData(new[] { "X" }, new[] { "a" }, true, false)]
        [InlineData(new[] { "x" }, new[] { "b" }, false, true)]
        [InlineData(new[] { "x" }, new[] { "a" }, false, false)]
        public void SignatureSync_ComparesNamesOrdinally(string[] inputs, string[] outputs, bool inputsChange, bool outputsChange)
        {
            var sync = SignatureSync.For("CSharpComponent", "private void RunScript(double x, ref object a) { }", inputs, outputs);

            Assert.Equal(inputsChange, sync.InputsChange);
            Assert.Equal(outputsChange, sync.OutputsChange);
            Assert.Equal(inputsChange || outputsChange, sync.ChangesPorts);
        }

        [Theory]
        [InlineData("Python3Component", "def RunScript(x):\n    return x")]
        [InlineData("ScriptComponent", "#! python 3\na = x")]
        [InlineData("CSharpComponent", "a = x + 1;")]
        [InlineData("CSharpComponent", null)]
        public void SignatureSync_CodeThatDeclaresNoPorts_RunsNoSync(string typeName, string code)
        {
            Assert.Null(SignatureSync.For(typeName, code, new[] { "x" }, new[] { "a" }));
        }
    }
}
