using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="SliderConfig"/>, the host-free policy that both
    /// <c>gh_canvas(action='add')</c> and <c>gh_canvas(action='config')</c> use to decide which
    /// Number Slider settings to apply. The defining regression (GHC-7X4B) is that <c>add</c> used
    /// to drop min/max/value/decimals entirely — covered by <see cref="Plan_WithAllParams_AppliesAll"/>.
    /// </summary>
    public class SliderConfigTests
    {
        // Sentinels matching the gh_canvas tool's parameter defaults for "not provided".
        private const double NoMin = double.NaN;
        private const double NoMax = double.NaN;
        private const int NoDecimals = -1;
        private const string NoValue = null;

        [Fact]
        public void Plan_WithAllParams_AppliesAll()
        {
            // GHC-7X4B regression: add(slider, min=0, max=100, value=50, decimals=2) must carry all four.
            var plan = SliderConfig.Plan(min: 0, max: 100, value: "50", decimals: 2);

            Assert.True(plan.HasAny);
            Assert.True(plan.SetMinimum);
            Assert.Equal(0m, plan.Minimum);
            Assert.True(plan.SetMaximum);
            Assert.Equal(100m, plan.Maximum);
            Assert.True(plan.SetDecimals);
            Assert.Equal(2, plan.Decimals);
            Assert.True(plan.SetValue);
            Assert.Equal(50m, plan.Value);
        }

        [Fact]
        public void Plan_WithNoParams_AppliesNothing()
        {
            // A plain add (no slider params) must leave the slider at its component defaults.
            var plan = SliderConfig.Plan(NoMin, NoMax, NoValue, NoDecimals);

            Assert.False(plan.HasAny);
            Assert.False(plan.SetMinimum);
            Assert.False(plan.SetMaximum);
            Assert.False(plan.SetDecimals);
            Assert.False(plan.SetValue);
        }

        [Fact]
        public void Plan_OnlyMin_AppliesOnlyMin()
        {
            var plan = SliderConfig.Plan(min: 5, max: NoMax, value: NoValue, decimals: NoDecimals);

            Assert.True(plan.SetMinimum);
            Assert.Equal(5m, plan.Minimum);
            Assert.False(plan.SetMaximum);
            Assert.False(plan.SetDecimals);
            Assert.False(plan.SetValue);
        }

        [Fact]
        public void Plan_OnlyMax_AppliesOnlyMax()
        {
            var plan = SliderConfig.Plan(min: NoMin, max: 42, value: NoValue, decimals: NoDecimals);

            Assert.False(plan.SetMinimum);
            Assert.True(plan.SetMaximum);
            Assert.Equal(42m, plan.Maximum);
            Assert.False(plan.SetDecimals);
            Assert.False(plan.SetValue);
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(2, true)]
        [InlineData(8, true)]     // Grasshopper's most decimal places
        [InlineData(-1, false)]   // sentinel: not provided
        [InlineData(-5, false)]   // any negative is "not provided"
        public void Plan_Decimals_HonorsNonNegativeSentinel(int decimals, bool expectedSet)
        {
            var plan = SliderConfig.Plan(NoMin, NoMax, NoValue, decimals);

            Assert.Equal(expectedSet, plan.SetDecimals);
            if (expectedSet)
                Assert.Equal(decimals, plan.Decimals);
        }

        [Theory]
        [InlineData("50", true, 50)]
        [InlineData("3.14", true, 3.14)]      // invariant decimal point parses regardless of locale
        [InlineData("-7.5", true, -7.5)]
        [InlineData("0", true, 0)]
        [InlineData(null, false, 0)]
        [InlineData("", false, 0)]
        [InlineData("abc", false, 0)]         // non-numeric → left as-is, no throw
        [InlineData("   ", false, 0)]
        public void Plan_Value_ParsedOnlyWhenNumeric(string value, bool expectedSet, double expected)
        {
            var plan = SliderConfig.Plan(NoMin, NoMax, value, NoDecimals);

            Assert.Equal(expectedSet, plan.SetValue);
            if (expectedSet)
                Assert.Equal((decimal)expected, plan.Value);
        }

        [Theory]
        [InlineData("abc", true)]      // substantive but unparseable → caller error
        [InlineData("1,5x", true)]
        [InlineData(null, false)]      // not provided
        [InlineData("", false)]        // not provided
        [InlineData("   ", false)]     // whitespace counts as not provided, not invalid
        [InlineData("50", false)]      // parses → valid
        [InlineData("3.14", false)]
        public void Plan_ValueInvalid_FlagsOnlySubstantiveUnparseableValues(string value, bool expectedInvalid)
        {
            var plan = SliderConfig.Plan(NoMin, NoMax, value, NoDecimals);

            Assert.Equal(expectedInvalid, plan.ValueInvalid);
            if (expectedInvalid)
                Assert.False(plan.SetValue); // invalid implies not applied
        }

        [Fact]
        public void Plan_NegativeRange_IsApplied()
        {
            // Negative min/max are valid slider bounds — NaN is the only "not provided" signal.
            var plan = SliderConfig.Plan(min: -100, max: -10, value: NoValue, decimals: NoDecimals);

            Assert.True(plan.SetMinimum);
            Assert.Equal(-100m, plan.Minimum);
            Assert.True(plan.SetMaximum);
            Assert.Equal(-10m, plan.Maximum);
        }

        [Fact]
        public void Plan_FractionalBounds_PreservedAsDecimal()
        {
            var plan = SliderConfig.Plan(min: 0.25, max: 9.75, value: NoValue, decimals: NoDecimals);

            Assert.Equal(0.25m, plan.Minimum);
            Assert.Equal(9.75m, plan.Maximum);
        }

        [Fact]
        public void Plan_Accepted_HasNoError()
        {
            Assert.Null(SliderConfig.Plan(min: 0, max: 100, value: "50", decimals: 2, 0m, 1m).Error);
        }

        [Fact]
        public void Plan_InvalidValue_IsRefusedWithItsText()
        {
            var plan = SliderConfig.Plan(NoMin, NoMax, "abc", NoDecimals);

            Assert.Equal("Invalid number: abc", plan.Error);
            Assert.False(plan.HasAny);
        }

        // A slider holds decimals, and casting any of these to decimal throws OverflowException;
        // the plan refuses them instead, so neither caller needs a catch.
        [Theory]
        [InlineData(1e30, double.NaN, "min")]
        [InlineData(-1e30, double.NaN, "min")]
        [InlineData(double.PositiveInfinity, double.NaN, "min")]
        [InlineData(double.NegativeInfinity, double.NaN, "min")]
        [InlineData(double.NaN, 1e30, "max")]
        [InlineData(double.NaN, double.PositiveInfinity, "max")]
        [InlineData(double.NaN, double.MaxValue, "max")]
        public void Plan_BoundThatDoesNotFitADecimal_IsRefused(double min, double max, string named)
        {
            var plan = SliderConfig.Plan(min, max, NoValue, NoDecimals);

            Assert.NotNull(plan.Error);
            Assert.StartsWith($"{named} must be a finite number", plan.Error);
            Assert.False(plan.HasAny);
        }

        [Theory]
        [InlineData("1e30")]
        [InlineData("-1e30")]
        [InlineData("1e400")]       // parses as infinity
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        public void Plan_ValueThatDoesNotFitADecimal_IsRefused(string value)
        {
            var plan = SliderConfig.Plan(NoMin, NoMax, value, NoDecimals);

            Assert.NotNull(plan.Error);
            Assert.StartsWith("value must be a finite number", plan.Error);
            Assert.Contains(value, plan.Error);
            Assert.False(plan.HasAny);
            Assert.False(plan.ValueInvalid); // it is a number, just not one a slider holds
        }

        [Fact]
        public void Plan_LargestDecimalSizedBounds_AreAccepted()
        {
            var plan = SliderConfig.Plan(min: -7.9e28, max: 7.9e28, value: NoValue, decimals: NoDecimals);

            Assert.Null(plan.Error);
            Assert.True(plan.SetMinimum);
            Assert.True(plan.SetMaximum);
        }

        [Theory]
        [InlineData(9)]
        [InlineData(28)]
        [InlineData(int.MaxValue)]
        public void Plan_DecimalsAboveEight_AreRefused(int decimals)
        {
            // Grasshopper clamps decimal places to 8, so a larger request would silently become 8.
            var plan = SliderConfig.Plan(NoMin, NoMax, NoValue, decimals);

            Assert.NotNull(plan.Error);
            Assert.StartsWith("decimals must be 0 to 8", plan.Error);
            Assert.False(plan.HasAny);
        }

        [Fact]
        public void Plan_MinAboveMax_IsRefused()
        {
            var plan = SliderConfig.Plan(min: 10, max: 5, value: NoValue, decimals: NoDecimals);

            Assert.Equal("min (10) is greater than max (5); a slider's min must not exceed its max.", plan.Error);
            Assert.False(plan.HasAny);
        }

        [Fact]
        public void Plan_MinEqualToMax_IsAccepted()
        {
            Assert.Null(SliderConfig.Plan(min: 5, max: 5, value: NoValue, decimals: NoDecimals).Error);
        }

        [Fact]
        public void Plan_LoneMinAboveTheCurrentMax_IsRefused()
        {
            // A new slider is 0–1: min=5 alone would leave it with a min above its max.
            var plan = SliderConfig.Plan(min: 5, max: NoMax, value: NoValue, decimals: NoDecimals,
                currentMinimum: 0m, currentMaximum: 1m);

            Assert.NotNull(plan.Error);
            Assert.StartsWith("min (5) is greater than the slider's max (1)", plan.Error);
            Assert.Contains("pass max as well", plan.Error);
            Assert.False(plan.HasAny);
        }

        [Fact]
        public void Plan_LoneMaxBelowTheCurrentMin_IsRefused()
        {
            var plan = SliderConfig.Plan(min: NoMin, max: -5, value: NoValue, decimals: NoDecimals,
                currentMinimum: 0m, currentMaximum: 1m);

            Assert.NotNull(plan.Error);
            Assert.StartsWith("max (-5) is less than the slider's min (0)", plan.Error);
            Assert.Contains("pass min as well", plan.Error);
        }

        [Fact]
        public void Plan_BothBoundsReplacingTheCurrentRange_IsCheckedOnlyAgainstEachOther()
        {
            // min=5, max=10 on a 0–1 slider moves the whole range; the old max is irrelevant.
            var plan = SliderConfig.Plan(min: 5, max: 10, value: NoValue, decimals: NoDecimals,
                currentMinimum: 0m, currentMaximum: 1m);

            Assert.Null(plan.Error);
            Assert.Equal(5m, plan.Minimum);
            Assert.Equal(10m, plan.Maximum);
        }

        [Fact]
        public void Plan_NoBoundGiven_DoesNotJudgeAnAlreadyInvertedRange()
        {
            // The call does not move the range, so a value is still applied to such a slider.
            var plan = SliderConfig.Plan(NoMin, NoMax, "3", NoDecimals,
                currentMinimum: 10m, currentMaximum: 1m);

            Assert.Null(plan.Error);
            Assert.True(plan.SetValue);
        }

        [Fact]
        public void Plan_LoneMinWithinTheCurrentRange_IsAccepted()
        {
            var plan = SliderConfig.Plan(min: 0.5, max: NoMax, value: NoValue, decimals: NoDecimals,
                currentMinimum: 0m, currentMaximum: 1m);

            Assert.Null(plan.Error);
            Assert.Equal(0.5m, plan.Minimum);
        }
    }
}
