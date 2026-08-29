using System.Collections.Generic;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests;

/// <summary>
/// Tests for <see cref="PromptTemplate"/> — the placeholder substitution behind
/// PromptRegistry.GetPrompt. Pins the fix for the unfilled-placeholder bug: an unfilled
/// {goal} must render as an explicit [goal] slot, not the bare word "goal" mid-sentence.
/// </summary>
public class PromptTemplateTests
{
    private static readonly string[] GoalAndCount = { "goal", "count" };

    [Fact]
    public void AllArgumentsProvided_SubstitutesValues()
    {
        var result = PromptTemplate.Render(
            "Build {goal} with {count} sliders.",
            new Dictionary<string, string> { ["goal"] = "a tower", ["count"] = "3" },
            GoalAndCount);

        Assert.Equal("Build a tower with 3 sliders.", result);
    }

    [Fact]
    public void PartiallyProvided_UnfilledDeclaredPlaceholderRendersAsBracketedSlot()
    {
        var result = PromptTemplate.Render(
            "Build {goal} with {count} sliders.",
            new Dictionary<string, string> { ["goal"] = "a tower" },
            GoalAndCount);

        Assert.Equal("Build a tower with [count] sliders.", result);
    }

    [Fact]
    public void NoneProvided_AllDeclaredPlaceholdersRenderAsBracketedSlots()
    {
        var result = PromptTemplate.Render(
            "Build {goal} with {count} sliders.",
            new Dictionary<string, string>(),
            GoalAndCount);

        Assert.Equal("Build [goal] with [count] sliders.", result);
    }

    [Fact]
    public void NullArgumentDictionary_TreatedAsNoneProvided()
    {
        var result = PromptTemplate.Render("Build {goal}.", null, new[] { "goal" });

        Assert.Equal("Build [goal].", result);
    }

    [Fact]
    public void UnfilledPlaceholder_DoesNotRenderAsBareArgumentName()
    {
        // Regression guard for the original bug: {goal} used to render as the literal word
        // "goal", indistinguishable from prose. It must be a visibly intentional slot.
        var result = PromptTemplate.Render("Accomplish {goal} now.", null, new[] { "goal" });

        Assert.DoesNotContain("Accomplish goal now.", result);
        Assert.Equal("Accomplish [goal] now.", result);
    }

    [Fact]
    public void TemplateWithNoPlaceholders_IsUnchanged()
    {
        const string template = "No slots here at all.";

        Assert.Equal(template, PromptTemplate.Render(
            template,
            new Dictionary<string, string> { ["goal"] = "x" },
            new[] { "goal" }));
    }

    [Fact]
    public void ArgumentValueContainingBraces_IsKeptLiteral()
    {
        // A substituted value that happens to contain brace syntax must not be re-processed
        // into a slot marker (as long as it doesn't collide with a declared argument name).
        var result = PromptTemplate.Render(
            "Value: {goal}",
            new Dictionary<string, string> { ["goal"] = "{not_an_arg}" },
            new[] { "goal" });

        Assert.Equal("Value: {not_an_arg}", result);
    }

    [Fact]
    public void ProvidedButUndeclaredArgument_StillSubstitutes()
    {
        var result = PromptTemplate.Render(
            "Extra: {extra}",
            new Dictionary<string, string> { ["extra"] = "value" },
            new string[0]);

        Assert.Equal("Extra: value", result);
    }

    [Fact]
    public void UndeclaredUnfilledPlaceholder_IsLeftAsIs()
    {
        // Only declared arguments get the [slot] treatment; unknown brace text is untouched.
        var result = PromptTemplate.Render("Keep {mystery} literal.", null, new[] { "goal" });

        Assert.Equal("Keep {mystery} literal.", result);
    }

    [Fact]
    public void NullDeclaredArgNames_LeavesPlaceholdersAlone()
    {
        Assert.Equal("Keep {goal}.", PromptTemplate.Render("Keep {goal}.", null, null));
    }

    [Fact]
    public void NullTemplate_ReturnsNull()
    {
        Assert.Null(PromptTemplate.Render(null, null, new[] { "goal" }));
    }
}
