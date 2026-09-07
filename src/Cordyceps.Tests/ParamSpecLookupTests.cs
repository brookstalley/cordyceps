using System;
using System.Collections.Generic;
using System.Globalization;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="ParamSpecLookup"/>, the host-free rule behind the <c>param</c>,
    /// <c>sourceParam</c> and <c>targetParam</c> arguments. The contract that matters most: a
    /// numeric spec means an index and nothing else, so an index past the end of the list is
    /// refused with the valid range instead of quietly becoming a name search.
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
        public void Resolve_FallsBackToSubstringWhenNoNameMatchesExactly()
        {
            var result = ParamSpecLookup.Resolve("lane 2", DigitBearingSide(), isInput: true, ownerName: "Frame");

            Assert.True(result.IsResolved);
            Assert.Equal(1, result.Index);
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
