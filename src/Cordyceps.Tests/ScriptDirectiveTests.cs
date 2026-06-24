using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

public class ScriptDirectivePreserveTests
{
    [Fact]
    public void PreservesPythonDirective_WhenNewCodeLacksOne()
    {
        var existing = "#! python 3\nimport old";
        var newCode = "import Rhino.Geometry as rg\na = 1";

        var result = ScriptDirective.Preserve(existing, newCode);

        Assert.Equal("#! python 3\nimport Rhino.Geometry as rg\na = 1", result);
    }

    [Fact]
    public void PreservesCSharpDirective_WhenNewCodeLacksOne()
    {
        var existing = "// #! csharp\n// old body";
        var newCode = "A = 1;";

        var result = ScriptDirective.Preserve(existing, newCode);

        Assert.Equal("// #! csharp\nA = 1;", result);
    }

    [Fact]
    public void PreservesExactPythonVersion()
    {
        // Python 2 must not be "upgraded" to 3 — preserve verbatim.
        var existing = "#! python 2\nprint x";
        var newCode = "print y";

        var result = ScriptDirective.Preserve(existing, newCode);

        Assert.Equal("#! python 2\nprint y", result);
    }

    [Fact]
    public void RespectsDirectiveAlreadyInNewCode_Python()
    {
        // Caller's explicit directive wins, even if it differs from the existing one.
        var existing = "#! python 3\nimport old";
        var newCode = "#! python 2\nprint x";

        var result = ScriptDirective.Preserve(existing, newCode);

        Assert.Equal(newCode, result);
    }

    [Fact]
    public void RespectsDirectiveAlreadyInNewCode_CSharp()
    {
        var existing = "#! python 3\nimport old";
        var newCode = "// #! csharp\nA = 1;";

        var result = ScriptDirective.Preserve(existing, newCode);

        Assert.Equal(newCode, result);
    }

    [Fact]
    public void NoOp_WhenExistingSourceHasNoDirective()
    {
        var existing = "import Rhino.Geometry as rg\na = 1";
        var newCode = "b = 2";

        var result = ScriptDirective.Preserve(existing, newCode);

        Assert.Equal("b = 2", result);
    }

    [Fact]
    public void NoOp_WhenExistingSourceNull()
    {
        Assert.Equal("b = 2", ScriptDirective.Preserve(null, "b = 2"));
    }

    [Fact]
    public void NoOp_WhenExistingSourceEmpty()
    {
        Assert.Equal("b = 2", ScriptDirective.Preserve("", "b = 2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReturnsNewCodeUnchanged_WhenNewCodeNullOrEmpty(string newCode)
    {
        Assert.Equal(newCode, ScriptDirective.Preserve("#! python 3\nx", newCode));
    }

    [Fact]
    public void OnlyFirstLineCountsAsDirective()
    {
        // A "#!" on a non-first line is not a directive — there is nothing to preserve.
        var existing = "import os\n#! python 3";
        var newCode = "a = 1";

        Assert.Equal("a = 1", ScriptDirective.Preserve(existing, newCode));
    }

    [Fact]
    public void ToleratesCrlfInExistingSource()
    {
        var existing = "#! python 3\r\nimport old";
        var newCode = "a = 1";

        var result = ScriptDirective.Preserve(existing, newCode);

        // Directive extracted without the trailing \r, then prepended with \n.
        Assert.Equal("#! python 3\na = 1", result);
    }

    [Fact]
    public void ToleratesBomBeforeDirective()
    {
        var existing = "\uFEFF#! python 3\nimport old";
        var newCode = "a = 1";

        var result = ScriptDirective.Preserve(existing, newCode);

        // The first line is preserved verbatim (BOM included) and prepended.
        Assert.Equal("\uFEFF#! python 3\na = 1", result);
    }
}

public class ScriptDirectiveExtractTests
{
    [Theory]
    [InlineData("#! python 3\nx", "#! python 3")]
    [InlineData("#! python 2", "#! python 2")]
    [InlineData("// #! csharp\nA=1;", "// #! csharp")]
    public void ExtractsKnownDirectives(string source, string expected)
    {
        Assert.Equal(expected, ScriptDirective.ExtractDirective(source));
    }

    [Theory]
    [InlineData("import os\nx = 1")]
    [InlineData("A = 1;")]
    [InlineData("")]
    [InlineData(null)]
    public void ReturnsNull_WhenNoDirective(string source)
    {
        Assert.Null(ScriptDirective.ExtractDirective(source));
    }

    [Theory]
    [InlineData("#! python 3", true)]
    [InlineData("// #! csharp", true)]
    [InlineData("import os", false)]
    [InlineData("A = 1;", false)]
    [InlineData(null, false)]
    public void HasDirective_Detects(string code, bool expected)
    {
        Assert.Equal(expected, ScriptDirective.HasDirective(code));
    }
}

public class ScriptDirectiveLanguageWarningTests
{
    // Issue #15: a unified "Script" component (type name ScriptComponent) with a directive-less
    // body silently compiles to nothing — warn so the caller isn't left with a broken component.
    [Theory]
    [InlineData("a = 42")]
    [InlineData("private void RunScript(object x, ref object A) { A = x; }")]
    [InlineData("")]            // empty body still leaves no language
    [InlineData(null)]
    public void Warns_ForUnifiedScript_WithoutDirective(string finalSource)
    {
        var warning = ScriptDirective.LanguageWarning("ScriptComponent", finalSource);
        Assert.NotNull(warning);
        Assert.Contains("Can not determine input code language", warning);
        Assert.Contains("#! python 3", warning);
        Assert.Contains("// #! csharp", warning);
    }

    [Theory]
    [InlineData("#! python 3\na = 42")]
    [InlineData("#! python 2\na = 42")]
    [InlineData("// #! csharp\nA = 42;")]
    public void NoWarning_ForUnifiedScript_WithDirective(string finalSource)
    {
        Assert.Null(ScriptDirective.LanguageWarning("ScriptComponent", finalSource));
    }

    // The dedicated components carry a concrete language and never need a directive.
    [Theory]
    [InlineData("CSharpComponent", "A = 42;")]
    [InlineData("Python3Component", "a = 42")]
    [InlineData("IronPython2Component", "a = 42")]
    [InlineData("Component", "anything")]
    public void NoWarning_ForOtherComponentTypes(string typeName, string finalSource)
    {
        Assert.Null(ScriptDirective.LanguageWarning(typeName, finalSource));
    }
}
