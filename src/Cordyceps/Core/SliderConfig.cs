using System;
using System.Globalization;

namespace Cordyceps.Core
{
    /// <summary>
    /// Pure, host-free policy for which Number Slider settings a caller's raw
    /// <c>min</c>/<c>max</c>/<c>value</c>/<c>decimals</c> arguments should apply, decoupled from the
    /// Grasshopper <c>GH_NumberSlider</c> host type so it can be unit-tested in <c>Cordyceps.Tests</c>.
    ///
    /// <para>Single source of truth shared by <c>gh_canvas(action='add')</c> and
    /// <c>gh_canvas(action='config')</c>: <c>add</c> historically dropped these four params entirely
    /// (GHC-7X4B), so a slider always landed at the default 0–1 range / 0.5 value. Both actions now
    /// compute the same <see cref="SliderConfigPlan"/> and apply it identically, giving <c>add</c>
    /// parity with <c>config</c>.</para>
    ///
    /// <para>"Not provided" sentinels match the tool's parameter defaults exactly:
    /// <c>min</c>/<c>max</c> = <c>double.NaN</c>, <c>decimals</c> &lt; 0, and a null, empty or
    /// whitespace <c>value</c> string. Anything else a slider cannot hold is refused by the plan
    /// itself (<see cref="SliderConfigPlan.Error"/>), before either action touches the document.</para>
    ///
    /// <para>The caller applies the plan in field order — minimum, maximum, decimals, then value —
    /// because setting the value clamps it to the slider's current range, so the range must be set
    /// first for an explicit value to survive on a freshly-added (default 0–1) slider.</para>
    /// </summary>
    public static class SliderConfig
    {
        /// <summary>
        /// The most decimal places a Grasshopper slider keeps: <c>GH_SliderBase.DecimalPlaces</c>
        /// clamps anything above it to this, so a larger request would silently become 8.
        /// </summary>
        public const int MaxDecimals = 8;

        /// <summary>
        /// Compute which slider settings to apply from raw tool arguments, or refuse them. A refused
        /// plan has <see cref="SliderConfigPlan.Error"/> set and applies nothing, so a caller checks
        /// that one property before any change and needs no <c>catch</c>.
        ///
        /// <para>Refused: a <c>value</c> that is not a number; a <c>min</c>, <c>max</c> or
        /// <c>value</c> that is infinite, NaN (for <c>value</c>) or beyond <see cref="decimal"/>'s
        /// range, because a slider holds decimals and the conversion would throw; <c>decimals</c>
        /// above <see cref="MaxDecimals"/>; and a range whose minimum exceeds its maximum. The range
        /// is checked on what the slider would hold: a bound not given is the slider's current one
        /// when <paramref name="currentMinimum"/>/<paramref name="currentMaximum"/> are passed, and
        /// unchecked when they are not.</para>
        /// </summary>
        /// <param name="min">Requested minimum, or <c>double.NaN</c> if not provided.</param>
        /// <param name="max">Requested maximum, or <c>double.NaN</c> if not provided.</param>
        /// <param name="value">Requested value as a string; null, empty or whitespace means "leave as-is".</param>
        /// <param name="decimals">Requested decimal places, or a negative value if not provided.</param>
        /// <param name="currentMinimum">The slider's minimum now, to check a lone <paramref name="max"/> against.</param>
        /// <param name="currentMaximum">The slider's maximum now, to check a lone <paramref name="min"/> against.</param>
        public static SliderConfigPlan Plan(double min, double max, string value, int decimals,
            decimal? currentMinimum = null, decimal? currentMaximum = null)
        {
            bool setMin = !double.IsNaN(min);
            bool setMax = !double.IsNaN(max);
            bool setDecimals = decimals >= 0;

            // InvariantCulture keeps parsing deterministic regardless of the Rhino host's locale:
            // MCP/JSON-sourced numeric strings ("3.14") are always invariant.
            double parsedValue = 0;
            bool setValue = !string.IsNullOrEmpty(value)
                && double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out parsedValue);

            // A substantive-but-unparseable value ("abc") is a caller error, distinct from
            // "not provided" (null/empty/whitespace): it is refused, never ignored.
            if (!string.IsNullOrWhiteSpace(value) && !setValue)
                return SliderConfigPlan.Refused($"Invalid number: {value}");

            decimal minimum = 0m, maximum = 0m, target = 0m;
            if (setMin && !TryToDecimal(min, out minimum))
                return SliderConfigPlan.Refused(NotADecimal("min", min.ToString("R", CultureInfo.InvariantCulture)));
            if (setMax && !TryToDecimal(max, out maximum))
                return SliderConfigPlan.Refused(NotADecimal("max", max.ToString("R", CultureInfo.InvariantCulture)));
            if (setValue && !TryToDecimal(parsedValue, out target))
                return SliderConfigPlan.Refused(NotADecimal("value", value));

            if (decimals > MaxDecimals)
                return SliderConfigPlan.Refused(
                    $"decimals must be 0 to {MaxDecimals}; got {decimals}. Grasshopper keeps at most {MaxDecimals} decimal places on a slider.");

            // Only a call that moves a bound is answerable for the range; a slider whose range is
            // already inverted can still take a value or decimals.
            decimal? lower = setMin ? minimum : currentMinimum;
            decimal? upper = setMax ? maximum : currentMaximum;
            if ((setMin || setMax) && lower.HasValue && upper.HasValue && lower.Value > upper.Value)
                return SliderConfigPlan.Refused(RangeError(setMin, setMax, lower.Value, upper.Value));

            return new SliderConfigPlan(
                setMin, minimum,
                setMax, maximum,
                setDecimals, setDecimals ? decimals : 0,
                setValue, target);
        }

        /// <summary>
        /// Convert to the decimal a slider holds: false for NaN, infinity and anything beyond
        /// <see cref="decimal"/>'s range, where the cast throws <see cref="OverflowException"/>.
        /// </summary>
        private static bool TryToDecimal(double number, out decimal result)
        {
            result = 0m;
            if (double.IsNaN(number) || double.IsInfinity(number)) return false;
            try
            {
                result = (decimal)number;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static string NotADecimal(string name, string given) =>
            $"{name} must be a finite number within ±{decimal.MaxValue.ToString("E2", CultureInfo.InvariantCulture)} " +
            $"(a slider holds a decimal); got {given}.";

        private static string RangeError(bool setMin, bool setMax, decimal lower, decimal upper)
        {
            string l = lower.ToString(CultureInfo.InvariantCulture);
            string u = upper.ToString(CultureInfo.InvariantCulture);
            if (setMin && setMax)
                return $"min ({l}) is greater than max ({u}); a slider's min must not exceed its max.";
            if (setMin)
                return $"min ({l}) is greater than the slider's max ({u}); pass max as well to move the whole range, e.g. min={l}, max=<at least {l}>.";
            return $"max ({u}) is less than the slider's min ({l}); pass min as well to move the whole range, e.g. min=<at most {u}>, max={u}.";
        }
    }

    /// <summary>
    /// The settings <see cref="SliderConfig.Plan"/> determined should be applied to a slider. Each
    /// <c>Set*</c> flag gates whether the corresponding value is applied; values are pre-converted to
    /// the <see cref="decimal"/>/<see cref="int"/> types the host slider expects.
    /// </summary>
    public sealed class SliderConfigPlan
    {
        public SliderConfigPlan(
            bool setMinimum, decimal minimum,
            bool setMaximum, decimal maximum,
            bool setDecimals, int decimals,
            bool setValue, decimal value)
        {
            SetMinimum = setMinimum;
            Minimum = minimum;
            SetMaximum = setMaximum;
            Maximum = maximum;
            SetDecimals = setDecimals;
            Decimals = decimals;
            SetValue = setValue;
            Value = value;
        }

        /// <summary>A plan that applies nothing and refuses the call with <paramref name="error"/>.</summary>
        internal static SliderConfigPlan Refused(string error) =>
            new SliderConfigPlan(false, 0m, false, 0m, false, 0, false, 0m)
            {
                Error = error
            };

        public bool SetMinimum { get; }
        public decimal Minimum { get; }
        public bool SetMaximum { get; }
        public decimal Maximum { get; }
        public bool SetDecimals { get; }
        public int Decimals { get; }
        public bool SetValue { get; }
        public decimal Value { get; }

        /// <summary>
        /// Why the arguments are refused, written for the caller, or null when the plan can be
        /// applied. A refused plan applies nothing.
        /// </summary>
        public string Error { get; private set; }

        /// <summary>True if the plan applies at least one setting (i.e. the caller passed something).</summary>
        public bool HasAny => SetMinimum || SetMaximum || SetDecimals || SetValue;
    }
}
