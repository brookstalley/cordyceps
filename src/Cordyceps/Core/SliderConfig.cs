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
    /// <c>min</c>/<c>max</c> = <c>double.NaN</c>, <c>decimals</c> &lt; 0, and a null/empty or
    /// non-numeric <c>value</c> string. Semantics mirror the original inline <c>ActionConfig</c> code
    /// so routing both actions through this helper is behavior-preserving for <c>config</c>.</para>
    ///
    /// <para>The caller applies the plan in field order — minimum, maximum, decimals, then value —
    /// because setting the value clamps it to the slider's current range, so the range must be set
    /// first for an explicit value to survive on a freshly-added (default 0–1) slider.</para>
    /// </summary>
    public static class SliderConfig
    {
        /// <summary>
        /// Compute which slider settings to apply from raw tool arguments.
        /// </summary>
        /// <param name="min">Requested minimum, or <c>double.NaN</c> if not provided.</param>
        /// <param name="max">Requested maximum, or <c>double.NaN</c> if not provided.</param>
        /// <param name="value">Requested value as a string; null/empty or non-numeric means "leave as-is".</param>
        /// <param name="decimals">Requested decimal places, or a negative value if not provided.</param>
        public static SliderConfigPlan Plan(double min, double max, string value, int decimals)
        {
            bool setMin = !double.IsNaN(min);
            bool setMax = !double.IsNaN(max);
            bool setDecimals = decimals >= 0;

            // Mirrors ActionConfig's value handling: apply only when the string parses as a number.
            // InvariantCulture keeps parsing deterministic regardless of the Rhino host's locale —
            // MCP/JSON-sourced numeric strings ("3.14") are always invariant, so the original
            // current-culture parse was a latent locale bug this shared helper removes.
            double parsedValue = 0;
            bool setValue = !string.IsNullOrEmpty(value)
                && double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out parsedValue);

            // A substantive-but-unparseable value ("abc") is a caller error, distinct from
            // "not provided" (null/empty/whitespace) — callers surface it instead of silently
            // succeeding with the value ignored.
            bool valueInvalid = !string.IsNullOrWhiteSpace(value) && !setValue;

            return new SliderConfigPlan(
                setMin, setMin ? (decimal)min : 0m,
                setMax, setMax ? (decimal)max : 0m,
                setDecimals, setDecimals ? decimals : 0,
                setValue, setValue ? (decimal)parsedValue : 0m,
                valueInvalid);
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
            bool setValue, decimal value,
            bool valueInvalid = false)
        {
            SetMinimum = setMinimum;
            Minimum = minimum;
            SetMaximum = setMaximum;
            Maximum = maximum;
            SetDecimals = setDecimals;
            Decimals = decimals;
            SetValue = setValue;
            Value = value;
            ValueInvalid = valueInvalid;
        }

        public bool SetMinimum { get; }
        public decimal Minimum { get; }
        public bool SetMaximum { get; }
        public decimal Maximum { get; }
        public bool SetDecimals { get; }
        public int Decimals { get; }
        public bool SetValue { get; }
        public decimal Value { get; }

        /// <summary>
        /// True when the caller provided a substantive <c>value</c> string that did not parse as a
        /// number (e.g. "abc"). Null/empty/whitespace count as "not provided", not invalid.
        /// </summary>
        public bool ValueInvalid { get; }

        /// <summary>True if the plan applies at least one setting (i.e. the caller passed something).</summary>
        public bool HasAny => SetMinimum || SetMaximum || SetDecimals || SetValue;
    }
}
