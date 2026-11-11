using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free answer to "which existing ports would this call remove?", computed before the
    /// call changes anything.
    /// </summary>
    /// <remarks>
    /// <para>Removing a port destroys every wire on it, so a call that removes one is destructive,
    /// and a destructive call never selects its component by a default nickname (a name every new
    /// object of the type gets, unique today and ambiguous after the next one is added). The rule
    /// applies only to calls that actually remove a port: a call that adds or keeps ports still
    /// accepts a unique default nickname, because refusing harmless calls teaches that the rule is
    /// noise. The caller resolves its id ordinarily, asks this type, and when the set is not empty
    /// re-resolves with <c>ResolveIntent.Destructive</c> before the first mutation.</para>
    /// <para>Each factory mirrors one mutation path exactly, reading the same pure decisions that
    /// path executes (<see cref="ParamSyncPlan"/>, <see cref="ScriptSignature"/>), so the prediction
    /// and the mutation cannot disagree about Cordyceps' own changes. Port names are the ports'
    /// nicknames, the names the sync paths match on.</para>
    /// </remarks>
    public sealed class PortRemoval
    {
        /// <summary>A call that removes no port.</summary>
        public static readonly PortRemoval None = new PortRemoval(Array.Empty<string>(), Array.Empty<string>());

        private PortRemoval(IReadOnlyList<string> inputs, IReadOnlyList<string> outputs)
        {
            Inputs = inputs;
            Outputs = outputs;
        }

        /// <summary>Names of the input ports the call would remove, in port order.</summary>
        public IReadOnlyList<string> Inputs { get; }

        /// <summary>Names of the output ports the call would remove, in port order.</summary>
        public IReadOnlyList<string> Outputs { get; }

        /// <summary>True when the call removes no existing port, so ordinary resolution stands.</summary>
        public bool IsEmpty => Inputs.Count == 0 && Outputs.Count == 0;

        /// <summary>
        /// <c>gh_canvas(action='zoomable', operation='remove')</c>: the port at
        /// <paramref name="index"/>, or the last port when it is negative.
        /// </summary>
        /// <param name="sidePorts">The names of the ports on the side being changed, in order.</param>
        /// <param name="input">True for the input side.</param>
        /// <param name="index">The requested index; negative means the last port.</param>
        /// <param name="canRemove">
        /// The component's own answer to whether the port at an index may be removed. A call the
        /// component refuses removes nothing, so it is not destructive.
        /// </param>
        public static PortRemoval ForRemove(IReadOnlyList<string> sidePorts, bool input, int index, Func<int, bool> canRemove)
        {
            if (sidePorts == null) throw new ArgumentNullException(nameof(sidePorts));
            if (canRemove == null) throw new ArgumentNullException(nameof(canRemove));

            int removeIndex = index >= 0 ? index : sidePorts.Count - 1;
            if (removeIndex < 0 || removeIndex >= sidePorts.Count || !canRemove(removeIndex))
                return None;

            return OneSide(input, new[] { sidePorts[removeIndex] });
        }

        /// <summary>
        /// <c>gh_canvas(action='zoomable', operation='set_count')</c>: the ports past
        /// <paramref name="targetCount"/>, which set_count removes from the end. A target at or above
        /// the current count removes nothing.
        /// </summary>
        /// <param name="canRemove">
        /// The component's answer for an index. set_count removes the last port first and stops at
        /// the first refusal, so when the last port cannot be removed the call removes nothing.
        /// </param>
        public static PortRemoval ForSetCount(IReadOnlyList<string> sidePorts, bool input, int targetCount, Func<int, bool> canRemove)
        {
            if (sidePorts == null) throw new ArgumentNullException(nameof(sidePorts));
            if (canRemove == null) throw new ArgumentNullException(nameof(canRemove));

            if (targetCount < 0 || targetCount >= sidePorts.Count || !canRemove(sidePorts.Count - 1))
                return None;

            return OneSide(input, sidePorts.Skip(targetCount).ToList());
        }

        /// <summary>
        /// A name-matched sync of a script component's ports to target names — the
        /// <c>gh_script(action='configure')</c> parameter route, and the sync <c>set</c> derives from
        /// code. A port whose name is not kept is removed; a rename is a removal plus an insertion.
        /// </summary>
        /// <param name="existingInputs">Current input names.</param>
        /// <param name="targetInputs">Target input names, or <c>null</c> when the input side is left alone.</param>
        /// <param name="existingOutputs">
        /// Current output names past the built-in leading output the sync never touches.
        /// </param>
        /// <param name="targetOutputs">Target output names, or <c>null</c> when the output side is left alone.</param>
        public static PortRemoval ForSync(
            IReadOnlyList<string> existingInputs, IReadOnlyList<string> targetInputs,
            IReadOnlyList<string> existingOutputs, IReadOnlyList<string> targetOutputs)
        {
            var inputs = targetInputs == null ? Array.Empty<string>() : Removed(existingInputs, targetInputs);
            var outputs = targetOutputs == null ? Array.Empty<string>() : Removed(existingOutputs, targetOutputs);
            return inputs.Count == 0 && outputs.Count == 0 ? None : new PortRemoval(inputs, outputs);
        }

        /// <summary>
        /// The sync <c>gh_script</c> runs from source: the ports <paramref name="code"/> declares
        /// (<see cref="ScriptSignature"/>) replace the current ones by name. Code whose ports are
        /// not derived from it removes nothing.
        /// </summary>
        /// <param name="componentTypeName">The script component's .NET type name.</param>
        /// <param name="code">The source as the caller sent it, which is what the sync reads.</param>
        public static PortRemoval ForSourceSync(string componentTypeName, string code,
            IReadOnlyList<string> existingInputs, IReadOnlyList<string> existingOutputs)
        {
            var declared = ScriptSignature.Parse(componentTypeName, code);
            if (declared == null)
                return None;
            return ForSync(existingInputs, declared.Value.inputs, existingOutputs, declared.Value.outputs);
        }

        /// <summary>
        /// The ports in words, for an error: <c>input 'y'</c>, <c>inputs 'a', 'b' and output 'c'</c>.
        /// </summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (Inputs.Count > 0) parts.Add(Side("input", Inputs));
            if (Outputs.Count > 0) parts.Add(Side("output", Outputs));
            return string.Join(" and ", parts);
        }

        /// <summary>
        /// The refusal for a destructive call whose id did not pass destructive resolution: what the
        /// call would remove, then the resolver's own teaching error naming the id to pass instead.
        /// </summary>
        /// <param name="resolverError">The error destructive resolution returned.</param>
        public string Refusal(string resolverError)
        {
            int count = Inputs.Count + Outputs.Count;
            return $"This call removes {Describe()}, and every wire on {(count == 1 ? "it" : "them")}, "
                + $"so nothing was changed. {resolverError}";
        }

        private static PortRemoval OneSide(bool input, IReadOnlyList<string> names) =>
            input ? new PortRemoval(names, Array.Empty<string>()) : new PortRemoval(Array.Empty<string>(), names);

        private static IReadOnlyList<string> Removed(IReadOnlyList<string> existing, IReadOnlyList<string> target)
        {
            var plan = ParamSyncPlan.Compute(existing, target);
            return plan.RemoveExistingIndicesDescending
                .OrderBy(i => i)
                .Select(i => existing[i])
                .ToList();
        }

        private static string Side(string side, IReadOnlyList<string> names)
        {
            var quoted = names.Select(n => $"'{n}'").ToList();
            return $"{side}{(names.Count == 1 ? "" : "s")} {string.Join(", ", quoted)}";
        }
    }
}
