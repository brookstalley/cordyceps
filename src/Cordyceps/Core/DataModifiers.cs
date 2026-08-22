using System.Collections.Generic;

namespace Cordyceps.Core
{
    /// <summary>
    /// The data-mapping modifier a parameter can carry, mirroring Grasshopper's
    /// <c>GH_DataMapping</c> without referencing it. Kept host-free so the parse/plan policy
    /// behind <c>gh_canvas(action='modifier')</c> can be unit-tested in <c>Cordyceps.Tests</c>,
    /// which cannot load Grasshopper types.
    ///
    /// <para>The host maps to and from <c>GH_DataMapping</c> by name, never by numeric value —
    /// a cast would silently bind this enum's ordering to Grasshopper's.</para>
    /// </summary>
    public enum DataMappingChoice
    {
        None,
        Flatten,
        Graft
    }

    /// <summary>
    /// Which of a parameter's data modifiers a caller's raw arguments ask to change, and to what.
    ///
    /// <para>Partial-update semantics, matching <c>gh_script(action='configure')</c>: an omitted
    /// argument leaves that modifier untouched. When nothing is set and the arguments are valid,
    /// <see cref="IsRead"/> is true and the caller reports current state instead of mutating —
    /// that is how the same action serves both reading and writing.</para>
    /// </summary>
    public sealed class DataModifierPlan
    {
        internal DataModifierPlan(
            bool setMapping, DataMappingChoice mapping,
            bool setSimplify, bool simplify,
            bool setReverse, bool reverse,
            string error)
        {
            SetMapping = setMapping;
            Mapping = mapping;
            SetSimplify = setSimplify;
            Simplify = simplify;
            SetReverse = setReverse;
            Reverse = reverse;
            Error = error;
        }

        /// <summary>True when <c>mapping</c> was supplied and should be applied.</summary>
        public bool SetMapping { get; }

        /// <summary>The requested mapping; meaningful only when <see cref="SetMapping"/> is true.</summary>
        public DataMappingChoice Mapping { get; }

        /// <summary>True when <c>simplify</c> was supplied and should be applied.</summary>
        public bool SetSimplify { get; }

        /// <summary>The requested simplify state; meaningful only when <see cref="SetSimplify"/> is true.</summary>
        public bool Simplify { get; }

        /// <summary>True when <c>reverse</c> was supplied and should be applied.</summary>
        public bool SetReverse { get; }

        /// <summary>The requested reverse state; meaningful only when <see cref="SetReverse"/> is true.</summary>
        public bool Reverse { get; }

        /// <summary>Caller-facing message describing every unparseable argument, or null when valid.</summary>
        public string Error { get; }

        /// <summary>False when any supplied argument failed to parse — reject before mutating.</summary>
        public bool IsValid => Error == null;

        /// <summary>True when at least one modifier should be written.</summary>
        public bool HasAny => SetMapping || SetSimplify || SetReverse;

        /// <summary>
        /// True when the arguments are valid but ask for no change, i.e. the caller wants to read
        /// the parameter's current modifier state.
        /// </summary>
        public bool IsRead => IsValid && !HasAny;
    }

    /// <summary>
    /// Pure, host-free policy for the per-parameter data modifiers Grasshopper exposes on a port's
    /// right-click menu — Flatten/Graft (<c>DataMapping</c>), Simplify, and Reverse.
    /// </summary>
    public static class DataModifiers
    {
        /// <summary>
        /// Compute which modifiers to write from raw tool arguments. Null, empty, or
        /// whitespace-only arguments mean "not provided" and leave that modifier alone; a
        /// substantive but unparseable argument is an error, never a silent no-op.
        /// </summary>
        /// <param name="mapping">Requested mapping: <c>none</c>, <c>flatten</c>, or <c>graft</c>.</param>
        /// <param name="simplify">Requested simplify state as a boolean string (true/false, 1/0, yes/no).</param>
        /// <param name="reverse">Requested reverse state as a boolean string (true/false, 1/0, yes/no).</param>
        public static DataModifierPlan Plan(string mapping, string simplify, string reverse)
        {
            var errors = new List<string>();

            bool setMapping = false;
            var mappingChoice = DataMappingChoice.None;
            if (!string.IsNullOrWhiteSpace(mapping))
            {
                if (TryParseMapping(mapping, out mappingChoice))
                    setMapping = true;
                else
                    errors.Add($"Invalid 'mapping' value: '{mapping}'. Use 'none', 'flatten', or 'graft'.");
            }

            bool setSimplify = TryParseFlag("simplify", simplify, errors, out bool simplifyValue);
            bool setReverse = TryParseFlag("reverse", reverse, errors, out bool reverseValue);

            return new DataModifierPlan(
                setMapping, mappingChoice,
                setSimplify, simplifyValue,
                setReverse, reverseValue,
                errors.Count == 0 ? null : string.Join(" ", errors));
        }

        /// <summary>
        /// Parse a mapping name case-insensitively. Accepts exactly the three names the tool
        /// documents and reports, so an <c>info</c> response round-trips back in as an argument.
        /// </summary>
        public static bool TryParseMapping(string value, out DataMappingChoice mapping)
        {
            mapping = DataMappingChoice.None;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "none":
                    mapping = DataMappingChoice.None;
                    return true;
                case "flatten":
                    mapping = DataMappingChoice.Flatten;
                    return true;
                case "graft":
                    mapping = DataMappingChoice.Graft;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The wire name for a mapping — the single source of truth for what both
        /// <c>action='modifier'</c> and <c>action='info'</c> emit, and what <see cref="TryParseMapping"/>
        /// accepts back.
        /// </summary>
        public static string MappingName(DataMappingChoice mapping)
        {
            switch (mapping)
            {
                case DataMappingChoice.Flatten: return "flatten";
                case DataMappingChoice.Graft: return "graft";
                case DataMappingChoice.None: return "none";
                default: return "none";
            }
        }

        private static bool TryParseFlag(string argName, string raw, List<string> errors, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (ParseHelpers.TryParseBool(raw, out value))
                return true;

            errors.Add($"Invalid '{argName}' value: '{raw}'. Use true/false, 1/0, or yes/no.");
            value = false;
            return false;
        }
    }
}
