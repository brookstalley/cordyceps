using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
    /// of a component.
    ///
    /// <para>The rule that matters: <b>a numeric spec means an index and nothing else.</b> An index
    /// past the end of the list is refused with the valid range, never retried as a name. The wire
    /// tool used to fold the range check into the same condition as the numeric parse, so
    /// <c>targetParam='2'</c> against a two-input component fell through to the substring matcher
    /// and connected to a parameter merely called something like <c>Plane 2</c> — reporting success
    /// on a port the caller never asked for. Grasshopper auto-numbers duplicated nicknames on
    /// exactly the variable-parameter components these tools target, so digit-bearing names are
    /// ordinary there rather than exotic.</para>
    ///
    /// <para>A spec that is genuinely a name is matched exactly (against name or nickname) first,
    /// and only then by substring against the name — unchanged, and deliberately so: an agent that
    /// asks for <c>Pl</c> is guessing at a name, whereas one that asks for <c>2</c> is not.</para>
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

            var available = string.Join(", ", parameters.Select(p => p.Name));

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

            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].Name.Equals(spec, StringComparison.OrdinalIgnoreCase) ||
                    parameters[i].NickName.Equals(spec, StringComparison.OrdinalIgnoreCase))
                    return ParamLookupResult.Resolved(i);
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].Name.IndexOf(spec, StringComparison.OrdinalIgnoreCase) >= 0)
                    return ParamLookupResult.Resolved(i);
            }

            return ParamLookupResult.Failed(
                $"Parameter '{spec}' not found on the {sideName} side of '{ownerName}'. " +
                $"Available: {available}");
        }
    }
}
