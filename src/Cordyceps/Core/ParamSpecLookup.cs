using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cordyceps.Core.Naming;

// The strict name steps and their suggestions are adapted from Hyperceps
// (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

namespace Cordyceps.Core
{
    /// <summary>
    /// The name and nickname of one parameter, which is everything the lookup rule needs to know
    /// about it. Keeping the rule to these two strings is what makes it host-free and therefore
    /// unit-testable — <c>Cordyceps.Tests</c> cannot load <c>IGH_Param</c>.
    /// </summary>
    public readonly struct ParamIdentity
    {
        /// <param name="name">The parameter's <c>Name</c>.</param>
        /// <param name="nickName">The parameter's <c>NickName</c>; defaults to the name when null.</param>
        public ParamIdentity(string name, string nickName = null)
        {
            Name = name ?? string.Empty;
            NickName = nickName ?? name ?? string.Empty;
        }

        public string Name { get; }
        public string NickName { get; }
    }

    /// <summary>
    /// The outcome of reading a parameter spec: either the 0-based index of the parameter it names,
    /// or a caller-facing reason it could not be resolved. Never both.
    /// </summary>
    public sealed class ParamLookupResult
    {
        private ParamLookupResult(int index, string error)
        {
            Index = index;
            Error = error;
        }

        /// <summary>0-based index into the side's parameter list; -1 when unresolved.</summary>
        public int Index { get; }

        /// <summary>Caller-facing reason the spec did not resolve, or null when it did.</summary>
        public string Error { get; }

        /// <summary>True when <see cref="Index"/> names a real parameter.</summary>
        public bool IsResolved => Error == null;

        internal static ParamLookupResult Resolved(int index) => new ParamLookupResult(index, null);

        internal static ParamLookupResult Failed(string error) => new ParamLookupResult(-1, error);
    }

    /// <summary>
    /// Pure, host-free rule for reading the parameter spec every tool accepts — the <c>param</c>,
    /// <c>sourceParam</c> and <c>targetParam</c> arguments — against one side (inputs or outputs)
    /// of a component. Strict, so a call never "succeeds" on a port nobody asked for:
    /// <list type="number">
    /// <item><b>A numeric spec means a 0-based index and nothing else.</b> An index past the end of
    /// the list is refused with the valid range, never retried as a name. Grasshopper auto-numbers
    /// duplicated nicknames on exactly the variable-parameter components these tools target, so
    /// digit-bearing names (<c>Plane 2</c>) are ordinary and must not catch a stray index.</item>
    /// <item>Otherwise a port whose name or nickname equals the spec exactly is used.</item>
    /// <item>Failing that, a match that ignores case is used only when it is the only one.</item>
    /// <item>More than one match at either step is an error listing them. A partial name is never
    /// used: it is an error that suggests the near ports and lists every port on the side.</item>
    /// </list>
    /// The side is the caller's: a name that exists only on the other side is still not found.
    /// </summary>
    public static class ParamSpecLookup
    {
        /// <summary>
        /// Resolve <paramref name="paramSpec"/> against <paramref name="parameters"/>, returning
        /// either the index it names or the reason it does not.
        /// </summary>
        /// <param name="paramSpec">The caller's spec: a 0-based index, a name, or a nickname.</param>
        /// <param name="parameters">The side's parameters, in port order.</param>
        /// <param name="isInput">Which side is being searched; names the side in every message.</param>
        /// <param name="ownerName">The component's nickname, for the messages.</param>
        /// <param name="specArgName">
        /// The tool argument the spec arrived in (<c>param</c>, <c>sourceParam</c>, …), so a missing
        /// spec is reported against the name the caller actually passed.
        /// </param>
        public static ParamLookupResult Resolve(
            string paramSpec,
            IReadOnlyList<ParamIdentity> parameters,
            bool isInput,
            string ownerName,
            string specArgName = "param")
        {
            parameters ??= Array.Empty<ParamIdentity>();
            string sideName = isInput ? "input" : "output";

            if (parameters.Count == 0)
                return ParamLookupResult.Failed($"Component '{ownerName}' has no {sideName} parameters");

            var available = Available(parameters);

            if (string.IsNullOrWhiteSpace(paramSpec))
                return ParamLookupResult.Failed(
                    $"'{specArgName}' is required for a component — pass a name or 0-based index. " +
                    $"Available {sideName} params: {available}");

            var spec = paramSpec.Trim();

            // Invariant culture, so a spec means the same index whatever locale Rhino is running
            // under; the tool contract is machine-facing, not localized.
            if (int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                if (index >= 0 && index < parameters.Count)
                    return ParamLookupResult.Resolved(index);

                return ParamLookupResult.Failed(
                    $"Parameter index {index} is out of range for the {sideName} side " +
                    $"(0-{parameters.Count - 1}). Available: {available}");
            }

            // A spec written as a number but too large to fit in an int is still a number. Letting
            // it fall through to the name matchers would let it land on a digit-bearing name, so it
            // is refused the same way an in-range parse that misses is.
            if (IsAsciiInteger(spec))
                return ParamLookupResult.Failed(
                    $"Parameter index '{spec}' is out of range for the {sideName} side " +
                    $"(0-{parameters.Count - 1}). Available: {available}");

            var byName = MatchName(spec, parameters);
            if (byName.Index >= 0)
                return ParamLookupResult.Resolved(byName.Index);

            if (byName.Matches != null)
            {
                var listed = string.Join(", ", byName.Matches.Select(i => Describe(parameters, i)));
                var fix = byName.CaseOnly
                    ? "write the exact case, or pass the 0-based index"
                    : "pass the 0-based index instead";
                return ParamLookupResult.Failed(
                    $"Parameter '{spec}' is ambiguous on the {sideName} side of '{ownerName}': it matches " +
                    $"{byName.Matches.Count} parameters{(byName.CaseOnly ? " when case is ignored" : "")} " +
                    $"({listed}) — {fix}. Available: {available}");
            }

            return ParamLookupResult.Failed(
                $"Parameter '{spec}' not found on the {sideName} side of '{ownerName}'. " +
                "A name must equal a parameter's name or nickname (case is ignored only when that " +
                "leaves one match); partial names are not matched." +
                DidYouMean(spec, parameters) +
                $" Available: {available}");
        }

        /// <summary>
        /// The name steps of the rule: an exact match, else a unique case-insensitive one. Returns
        /// the index, or the competing indices when a step matched more than one, or neither.
        /// </summary>
        private static (int Index, List<int> Matches, bool CaseOnly) MatchName(
            string spec, IReadOnlyList<ParamIdentity> parameters)
        {
            var exact = Indices(parameters, p =>
                string.Equals(p.Name, spec, StringComparison.Ordinal) ||
                string.Equals(p.NickName, spec, StringComparison.Ordinal));
            if (exact.Count == 1) return (exact[0], null, false);
            if (exact.Count > 1) return (-1, exact, false);

            var caseless = Indices(parameters, p =>
                p.Name.Equals(spec, StringComparison.OrdinalIgnoreCase) ||
                p.NickName.Equals(spec, StringComparison.OrdinalIgnoreCase));
            if (caseless.Count == 1) return (caseless[0], null, true);
            if (caseless.Count > 1) return (-1, caseless, true);

            return (-1, null, false);
        }

        /// <summary>
        /// Up to three ports whose name or nickname overlaps the spec, else the one nearest by edit
        /// distance, each offered in a spelling that resolves to it (nickname, name, else index).
        /// </summary>
        private static string DidYouMean(string spec, IReadOnlyList<ParamIdentity> parameters)
        {
            var near = Indices(parameters, p =>
                NameSuggestions.Overlaps(p.Name, spec) || NameSuggestions.Overlaps(p.NickName, spec));

            if (near.Count == 0)
            {
                var closest = NameSuggestions.Closest(spec, parameters.SelectMany(p => new[] { p.NickName, p.Name }));
                if (closest != null)
                    near = Indices(parameters, p => p.NickName == closest || p.Name == closest);
            }

            if (near.Count == 0) return string.Empty;

            return " Did you mean " +
                   string.Join(" or ", near.Take(3).Select(i => $"'{SpellingFor(parameters, i)}' ({Describe(parameters, i)})")) +
                   "?";
        }

        /// <summary>
        /// A spelling that resolves to port <paramref name="index"/> alone: its nickname, else its
        /// name, else the index. Checked by running the rule, so what is offered works.
        /// </summary>
        private static string SpellingFor(IReadOnlyList<ParamIdentity> parameters, int index)
        {
            var p = parameters[index];
            foreach (var spelling in new[] { p.NickName, p.Name })
            {
                if (string.IsNullOrWhiteSpace(spelling) || spelling != spelling.Trim() || IsAsciiInteger(spelling)) continue;
                if (MatchName(spelling, parameters).Index == index) return spelling;
            }
            return index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary><c>1 P (Cull Pattern)</c>: the index, the nickname, and the name when it differs.</summary>
        private static string Describe(IReadOnlyList<ParamIdentity> parameters, int index)
        {
            var p = parameters[index];
            return p.NickName == p.Name ? $"{index} {p.Name}" : $"{index} {p.NickName} ({p.Name})";
        }

        /// <summary>
        /// The side's names in port order, then their nicknames when any differs, so the caller can
        /// pick either spelling.
        /// </summary>
        private static string Available(IReadOnlyList<ParamIdentity> parameters)
        {
            var names = string.Join(", ", parameters.Select(p => p.Name));
            if (parameters.All(p => p.NickName == p.Name)) return names;
            return $"{names} (nicknames: {string.Join(", ", parameters.Select(p => p.NickName))})";
        }

        private static List<int> Indices(IReadOnlyList<ParamIdentity> parameters, Func<ParamIdentity, bool> test)
        {
            var result = new List<int>();
            for (int i = 0; i < parameters.Count; i++)
                if (test(parameters[i])) result.Add(i);
            return result;
        }

        /// <summary>
        /// Whether the spec is written as a plain base-10 integer, with an optional sign. Deliberately
        /// ASCII-only, matching what the invariant-culture parse above accepts, so exactly one of the
        /// two ever calls a spec numeric.
        /// </summary>
        private static bool IsAsciiInteger(string spec)
        {
            int start = spec[0] == '+' || spec[0] == '-' ? 1 : 0;
            if (start >= spec.Length)
                return false;

            for (int i = start; i < spec.Length; i++)
            {
                if (spec[i] < '0' || spec[i] > '9')
                    return false;
            }

            return true;
        }
    }
}
