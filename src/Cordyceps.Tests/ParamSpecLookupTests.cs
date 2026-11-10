using System;
using System.Collections.Generic;
using System.Globalization;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ParamSpecLookup"/>, the host-free rule behind the <c>param</c>,
    /// <c>sourceParam</c> and <c>targetParam</c> arguments. The contract: a numeric spec means an
    /// index and nothing else, so an index past the end of the list is refused with the valid range
    /// instead of quietly becoming a name search; a name must match a port's name or nickname
    /// exactly, or ignoring case when that leaves one match; anything partial or ambiguous is an
    /// error that names the candidates, and nothing is ever resolved by a guess.
    /// </summary>
    public class ParamSpecLookupTests
    {
        /// <summary>
        /// A two-input side whose second parameter's name contains a digit — the shape Grasshopper
        /// produces on its own when a variable-parameter component's nicknames are auto-numbered,
        /// and the shape that used to swallow an out-of-range index.
        /// </summary>
        private static IReadOnlyList<ParamIdentity> DigitBearingSide() => new[]
        {
            new ParamIdentity("Plane 1", "P1"),
            new ParamIdentity("Plane 2", "P2")
        };

        [Fact]
        public void Resolve_WithIndexPastEnd_ReportsRangeAndDoesNotMatchDigitBearingName()
        {
            var result = ParamSpecLookup.Resolve("2", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Equal(-1, result.Index);
            Assert.Contains("index 2 is out of range", result.Error);
            Assert.Contains("(0-1)", result.Error);
            Assert.Contains("input", result.Error);
            Assert.Contains("Plane 1, Plane 2", result.Error);
        }

        [Fact]
        public void Resolve_WithNegativeIndex_ReportsRange()
        {
            var result = ParamSpecLookup.Resolve("-1", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Contains("index -1 is out of range", result.Error);
        }

        [Fact]
        public void Resolve_WithInRangeIndex_ResolvesByPositionNotName()
        {
            // "1" is a substring of "Plane 1" at index 0, so a resolver that searched by name
            // would answer 0 here. An index is an index.
            var result = ParamSpecLookup.Resolve("1", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
            Assert.Null(result.Error);
        }

        [Fact]
        public void Resolve_TrimsSurroundingWhitespaceOnAnIndex()
        {
            var result = ParamSpecLookup.Resolve(" 1 ", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
        }

        [Theory]
        [InlineData("99999999999999999999")]
        [InlineData("-99999999999999999999")]
        public void Resolve_WithAnIndexTooLargeForAnInt_StillRefusesRatherThanSearchingNames(string spec)
        {
            // The defect this rule exists to close, at the far end of the range: a spec written as
            // a number is a number even when it does not fit in an int, so it must not reach the
            // name matchers.
            var result = ParamSpecLookup.Resolve(spec, DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Contains("out of range", result.Error);
            Assert.Contains("(0-1)", result.Error);
        }

        [Fact]
        public void Resolve_WithASignedInRangeIndex_ResolvesIt()
        {
            var result = ParamSpecLookup.Resolve("+1", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
        }

        [Fact]
        public void Resolve_WithASignAndNoDigits_IsTreatedAsAName()
        {
            var side = new[] { new ParamIdentity("Plane"), new ParamIdentity("-") };

            var result = ParamSpecLookup.Resolve("-", side, isInput: true, ownerName: "Frame");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
        }

        [Fact]
        public void Resolve_MatchesNameCaseInsensitively()
        {
            var result = ParamSpecLookup.Resolve("pLaNe 2", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
        }

        [Fact]
        public void Resolve_MatchesNickName()
        {
            var result = ParamSpecLookup.Resolve("P2", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
        }

        [Fact]
        public void Resolve_PrefersAnExactMatchOverAnEarlierSubstringMatch()
        {
            var side = new[]
            {
                new ParamIdentity("Radius Factor"),
                new ParamIdentity("Radius")
            };

            var result = ParamSpecLookup.Resolve("Radius", side, isInput: true, ownerName: "Circle");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
        }

        [Fact]
        public void Resolve_APartialName_IsRefusedAndSuggestsThePortItIsPartOf()
        {
            // A substring used to be accepted here and wired the port it was part of. Partial names
            // are guesses; the caller is now told the port and how to name it instead.
            var result = ParamSpecLookup.Resolve("lane 2", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Equal(-1, result.Index);
            Assert.Contains("Parameter 'lane 2' not found on the input side of 'Frame'", result.Error);
            Assert.Contains("partial names are not matched", result.Error);
            Assert.Contains("Did you mean 'P2' (1 P2 (Plane 2))?", result.Error);
            Assert.Contains("Available: Plane 1, Plane 2 (nicknames: P1, P2)", result.Error);
        }

        [Fact]
        public void Resolve_APartialNameOfSeveralPorts_IsRefusedAndSuggestsEach()
        {
            // "Plane" is part of both names. The lowest-indexed port used to win silently, landing
            // on a port nobody asked for; now neither is used.
            var result = ParamSpecLookup.Resolve("Plane", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Contains("'P1' (0 P1 (Plane 1)) or 'P2' (1 P2 (Plane 2))", result.Error);
        }

        [Fact]
        public void Resolve_ThePartialNameFromCullPattern_SuggestsItsNickname()
        {
            // The case that motivated the strict rule: "Pattern" used to land on "Cull Pattern".
            var side = new[] { new ParamIdentity("List", "L"), new ParamIdentity("Cull Pattern", "P") };

            var result = ParamSpecLookup.Resolve("Pattern", side, isInput: true, ownerName: "Cull");

            Assert.False(result.IsResolved);
            Assert.Contains("Did you mean 'P' (1 P (Cull Pattern))?", result.Error);
            Assert.Contains("Available: List, Cull Pattern (nicknames: L, P)", result.Error);
        }

        [Fact]
        public void Resolve_ANearMiss_IsSuggestedByEditDistance()
        {
            var side = new[] { new ParamIdentity("Radius", "R"), new ParamIdentity("Plane", "P") };

            var result = ParamSpecLookup.Resolve("Radus", side, isInput: true, ownerName: "Circle");

            Assert.False(result.IsResolved);
            Assert.Contains("Did you mean 'R' (0 R (Radius))?", result.Error);
        }

        [Fact]
        public void Resolve_ExactCaseWinsOverACaselessTwin()
        {
            var side = new[] { new ParamIdentity("Total", "T"), new ParamIdentity("Twist", "t") };

            Assert.Equal(0, ParamSpecLookup.Resolve("T", side, true, "x").Index);
            Assert.Equal(1, ParamSpecLookup.Resolve("t", side, true, "x").Index);
        }

        [Fact]
        public void Resolve_TwoExactMatches_IsAmbiguousAndAsksForTheIndex()
        {
            var side = new[] { new ParamIdentity("Data", "D"), new ParamIdentity("Data", "D") };

            var result = ParamSpecLookup.Resolve("D", side, isInput: true, ownerName: "Merge");

            Assert.False(result.IsResolved);
            Assert.Contains("'D' is ambiguous on the input side of 'Merge'", result.Error);
            Assert.Contains("(0 D (Data), 1 D (Data))", result.Error);
            Assert.Contains("pass the 0-based index instead", result.Error);
        }

        [Fact]
        public void Resolve_ANameAndAnotherPortsNickname_IsAmbiguous()
        {
            var side = new[] { new ParamIdentity("Point", "P"), new ParamIdentity("P", "X") };

            var result = ParamSpecLookup.Resolve("P", side, isInput: true, ownerName: "Odd");

            Assert.False(result.IsResolved);
            Assert.Contains("matches 2 parameters", result.Error);
        }

        [Fact]
        public void Resolve_SeveralCaselessMatches_AsksForTheExactCase()
        {
            var side = new[] { new ParamIdentity("alpha", "x"), new ParamIdentity("ALPHA", "y") };

            var result = ParamSpecLookup.Resolve("Alpha", side, isInput: true, ownerName: "x");

            Assert.False(result.IsResolved);
            Assert.Contains("when case is ignored", result.Error);
            Assert.Contains("write the exact case, or pass the 0-based index", result.Error);
        }

        [Fact]
        public void Resolve_NeverLooksOnTheOtherSide()
        {
            // Only the outputs are passed in, so a name that is an input elsewhere is not found.
            var outputs = new[] { new ParamIdentity("Circle", "C") };

            var result = ParamSpecLookup.Resolve("Radius", outputs, isInput: false, ownerName: "Circle");

            Assert.False(result.IsResolved);
            Assert.Contains("output side of 'Circle'", result.Error);
        }

        [Fact]
        public void Resolve_ASuggestionForAPortWithASharedNickname_OffersItsName()
        {
            var side = new[] { new ParamIdentity("Start", "S"), new ParamIdentity("Shift", "S") };

            var result = ParamSpecLookup.Resolve("Shif", side, isInput: true, ownerName: "x");

            Assert.Contains("Did you mean 'Shift' (1 S (Shift))?", result.Error);
        }

        [Fact]
        public void Resolve_WithUnknownName_ListsTheAvailableNames()
        {
            var result = ParamSpecLookup.Resolve("Curve", DigitBearingSide(), isInput: false, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Contains("'Curve' not found", result.Error);
            Assert.Contains("output side of 'Frame'", result.Error);
            Assert.Contains("Plane 1, Plane 2", result.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Resolve_WithNoSpec_NamesTheArgumentItWanted(string spec)
        {
            var result = ParamSpecLookup.Resolve(spec, DigitBearingSide(), isInput: true,
                ownerName: "Frame", specArgName: "targetParam");

            Assert.False(result.IsResolved);
            Assert.Contains("'targetParam' is required", result.Error);
            Assert.Contains("Plane 1, Plane 2", result.Error);
        }

        [Fact]
        public void Resolve_WithNoParameters_SaysSoRatherThanSearching()
        {
            var result = ParamSpecLookup.Resolve("0", Array.Empty<ParamIdentity>(), isInput: false, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Equal("Component 'Frame' has no output parameters", result.Error);
        }

        [Fact]
        public void Resolve_WithNullParameterList_SaysSoRatherThanThrowing()
        {
            var result = ParamSpecLookup.Resolve("0", null, isInput: true, ownerName: "Frame");

            Assert.False(result.IsResolved);
            Assert.Equal("Component 'Frame' has no input parameters", result.Error);
        }

        [Fact]
        public void Resolve_ReadsAnIndexAndMatchesNamesTheSameWayUnderAnyCulture()
        {
            // Turkish is the classic trap on both counts: its dotted/dotless I breaks
            // culture-sensitive case folding, and its digit and sign formatting differ from the
            // invariant culture. The tool contract is machine-facing, so neither may move.
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

                var byIndex = ParamSpecLookup.Resolve("1", DigitBearingSide(), isInput: true, ownerName: "Frame");
                Assert.True(byIndex.IsResolved);
                Assert.Equal(1, byIndex.Index);

                var side = new[] { new ParamIdentity("Interval"), new ParamIdentity("Plane") };
                var byName = ParamSpecLookup.Resolve("INTERVAL", side, isInput: true, ownerName: "Frame");
                Assert.True(byName.IsResolved);
                Assert.Equal(0, byName.Index);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
