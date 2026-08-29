using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="DataModifiers"/>, the host-free policy behind
    /// <c>gh_canvas(action='modifier')</c>. The two contracts that matter most: an omitted
    /// argument leaves that modifier untouched (partial update), and valid arguments that set
    /// nothing mean "read the current state" rather than "clear everything".
    /// </summary>
    public class DataModifiersTests
    {
        [Fact]
        public void Plan_WithNothingProvided_IsRead()
        {
            var plan = DataModifiers.Plan(mapping: null, simplify: null, reverse: null);

            Assert.True(plan.IsValid);
            Assert.False(plan.HasAny);
            Assert.True(plan.IsRead);
            Assert.False(plan.SetMapping);
            Assert.False(plan.SetSimplify);
            Assert.False(plan.SetReverse);
        }

        [Fact]
        public void Plan_WithEmptyAndWhitespaceArgs_IsRead()
        {
            // Empty strings reach the tool from clients that send "" for "unset"; they must not
            // be read as a request to clear the modifiers.
            var plan = DataModifiers.Plan(mapping: "", simplify: "   ", reverse: "");

            Assert.True(plan.IsValid);
            Assert.True(plan.IsRead);
        }

        [Fact]
        public void Plan_WithAllProvided_SetsAll()
        {
            var plan = DataModifiers.Plan(mapping: "graft", simplify: "true", reverse: "false");

            Assert.True(plan.IsValid);
            Assert.True(plan.HasAny);
            Assert.False(plan.IsRead);
            Assert.True(plan.SetMapping);
            Assert.Equal(DataMappingChoice.Graft, plan.Mapping);
            Assert.True(plan.SetSimplify);
            Assert.True(plan.Simplify);
            Assert.True(plan.SetReverse);
            Assert.False(plan.Reverse);
        }

        [Fact]
        public void Plan_OnlyMapping_LeavesFlagsUntouched()
        {
            var plan = DataModifiers.Plan(mapping: "flatten", simplify: null, reverse: null);

            Assert.True(plan.SetMapping);
            Assert.Equal(DataMappingChoice.Flatten, plan.Mapping);
            Assert.False(plan.SetSimplify);
            Assert.False(plan.SetReverse);
        }

        [Fact]
        public void Plan_OnlySimplify_LeavesMappingAndReverseUntouched()
        {
            var plan = DataModifiers.Plan(mapping: null, simplify: "yes", reverse: null);

            Assert.False(plan.SetMapping);
            Assert.True(plan.SetSimplify);
            Assert.True(plan.Simplify);
            Assert.False(plan.SetReverse);
        }

        [Fact]
        public void Plan_OnlyReverse_LeavesMappingAndSimplifyUntouched()
        {
            var plan = DataModifiers.Plan(mapping: null, simplify: null, reverse: "0");

            Assert.False(plan.SetMapping);
            Assert.False(plan.SetSimplify);
            Assert.True(plan.SetReverse);
            Assert.False(plan.Reverse);
        }

        [Fact]
        public void Plan_TurningModifiersOff_IsAWrite_NotARead()
        {
            // mapping='none' + simplify=false + reverse=false is the "clear the modifiers"
            // request; it must not collapse into read mode just because the values are falsey.
            var plan = DataModifiers.Plan(mapping: "none", simplify: "false", reverse: "false");

            Assert.True(plan.HasAny);
            Assert.False(plan.IsRead);
            Assert.True(plan.SetMapping);
            Assert.Equal(DataMappingChoice.None, plan.Mapping);
            Assert.True(plan.SetSimplify);
            Assert.False(plan.Simplify);
            Assert.True(plan.SetReverse);
            Assert.False(plan.Reverse);
        }

        [Theory]
        [InlineData("GRAFT", DataMappingChoice.Graft)]
        [InlineData("Flatten", DataMappingChoice.Flatten)]
        [InlineData("  none  ", DataMappingChoice.None)]
        public void Plan_MappingIsCaseAndWhitespaceInsensitive(string raw, DataMappingChoice expected)
        {
            var plan = DataModifiers.Plan(raw, null, null);

            Assert.True(plan.IsValid);
            Assert.True(plan.SetMapping);
            Assert.Equal(expected, plan.Mapping);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("TRUE", true)]
        [InlineData("1", true)]
        [InlineData("yes", true)]
        [InlineData("false", false)]
        [InlineData("0", false)]
        [InlineData("no", false)]
        public void Plan_FlagsAcceptTheSameBooleanSpellingsAsTheRestOfTheApi(string raw, bool expected)
        {
            var plan = DataModifiers.Plan(null, raw, raw);

            Assert.True(plan.IsValid);
            Assert.True(plan.SetSimplify);
            Assert.Equal(expected, plan.Simplify);
            Assert.True(plan.SetReverse);
            Assert.Equal(expected, plan.Reverse);
        }

        [Fact]
        public void Plan_UnknownMapping_IsInvalidAndSetsNothing()
        {
            // "reparameterize" is a real port option Grasshopper offers but this action does not
            // carry — it must fail loudly rather than being ignored.
            var plan = DataModifiers.Plan(mapping: "reparameterize", simplify: null, reverse: null);

            Assert.False(plan.IsValid);
            Assert.False(plan.SetMapping);
            Assert.Contains("mapping", plan.Error);
            Assert.Contains("reparameterize", plan.Error);
            Assert.Contains("graft", plan.Error);
        }

        [Fact]
        public void Plan_UnparseableFlag_IsInvalidAndSetsNothing()
        {
            var plan = DataModifiers.Plan(mapping: null, simplify: "maybe", reverse: null);

            Assert.False(plan.IsValid);
            Assert.False(plan.SetSimplify);
            Assert.Contains("simplify", plan.Error);
            Assert.Contains("maybe", plan.Error);
        }

        [Fact]
        public void Plan_ReportsEveryBadArgumentAtOnce()
        {
            // One round trip should tell the caller about all of its mistakes.
            var plan = DataModifiers.Plan(mapping: "grafted", simplify: "maybe", reverse: "sometimes");

            Assert.False(plan.IsValid);
            Assert.Contains("mapping", plan.Error);
            Assert.Contains("simplify", plan.Error);
            Assert.Contains("reverse", plan.Error);
        }

        [Fact]
        public void Plan_InvalidArgument_DoesNotMakeAnInvalidPlanLookLikeARead()
        {
            // IsRead gates the mutate-vs-report branch; an invalid plan must not take it.
            var plan = DataModifiers.Plan(mapping: "sideways", simplify: null, reverse: null);

            Assert.False(plan.IsRead);
            Assert.False(plan.HasAny);
        }

        [Fact]
        public void Plan_ValidAndInvalidMixed_StillAppliesNothingUntilTheCallerRejects()
        {
            // Validate-then-mutate: a partially bad request carries the good value but is invalid,
            // so the caller rejects the whole thing instead of half-applying it.
            var plan = DataModifiers.Plan(mapping: "graft", simplify: "maybe", reverse: null);

            Assert.False(plan.IsValid);
            Assert.True(plan.SetMapping);
            Assert.False(plan.SetSimplify);
        }

        [Theory]
        [InlineData(DataMappingChoice.None, "none")]
        [InlineData(DataMappingChoice.Flatten, "flatten")]
        [InlineData(DataMappingChoice.Graft, "graft")]
        public void MappingName_MatchesTheNamesTheParserAccepts(DataMappingChoice mapping, string expected)
        {
            var name = DataModifiers.MappingName(mapping);

            Assert.Equal(expected, name);

            // Round trip: what info reports must be feedable straight back in as an argument.
            Assert.True(DataModifiers.TryParseMapping(name, out var reparsed));
            Assert.Equal(mapping, reparsed);
        }

        [Fact]
        public void TryParseMapping_WithNoValue_ReturnsFalse()
        {
            Assert.False(DataModifiers.TryParseMapping(null, out _));
            Assert.False(DataModifiers.TryParseMapping("", out _));
            Assert.False(DataModifiers.TryParseMapping("   ", out _));
        }
    }
}
