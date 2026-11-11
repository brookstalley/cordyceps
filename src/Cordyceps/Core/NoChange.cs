using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free answers to "would this call change anything?", asked before its edit opens. A call
    /// that changes nothing opens no edit, so it pushes no undo step and returns no <c>undo</c>: an
    /// agent reads <c>undo</c> as "the canvas changed", and a human's Ctrl+Z spent on an empty step
    /// reverts nothing.
    /// </summary>
    /// <remarks>
    /// Each answer compares the values its action's edit would write, taken from the same functions
    /// the edit writes them with (<see cref="SignatureSync"/>, <see cref="ParamSyncPlan"/>,
    /// <see cref="ScriptParamDefs.AccessToWrite"/>), with what the document holds now. Where a value
    /// cannot be compared, the answer is "changes something": an empty undo step costs a keystroke,
    /// while a change answered as "nothing" would go unrecorded and unapplied.
    /// </remarks>
    public static class NoChange
    {
        /// <summary>
        /// <c>gh_canvas(action='group_add')</c> on an existing group: true when every id is already a
        /// member and no new colour is asked for. Grasshopper's own <c>AddObject</c> skips a member
        /// it already holds, so such a call changes neither the membership nor the colour.
        /// </summary>
        /// <param name="members">The group's current member ids.</param>
        /// <param name="adding">The ids the call adds.</param>
        /// <param name="currentArgb">The group's colour as ARGB.</param>
        /// <param name="requestedArgb">The colour the call sets as ARGB, or null when it sets none.</param>
        public static bool GroupAdd(IEnumerable<Guid> members, IEnumerable<Guid> adding, int currentArgb, int? requestedArgb)
        {
            if (adding == null) throw new ArgumentNullException(nameof(adding));
            if (requestedArgb.HasValue && requestedArgb.Value != currentArgb) return false;
            var held = new HashSet<Guid>(members ?? Enumerable.Empty<Guid>());
            return adding.All(held.Contains);
        }

        /// <summary>
        /// True when <paramref name="requested"/> is exactly the source the component stores. A
        /// component whose source cannot be read is never assumed to hold it.
        /// </summary>
        public static bool SameSource(string stored, string requested)
            => stored != null && requested != null && string.Equals(stored, requested, StringComparison.Ordinal);

        /// <summary>
        /// <c>gh_script(action='set')</c>, and <c>configure</c> with only <c>code</c>: true when the
        /// component already stores <paramref name="finalSource"/> and the port sync that follows the
        /// write would leave its ports as they are, so the call would write the same text and move
        /// nothing.
        /// </summary>
        /// <param name="storedSource">The source the component stores now, or null when it cannot be read.</param>
        /// <param name="finalSource">The source the call would store, with the language line kept.</param>
        /// <param name="sync">
        /// The port sync the write would run, the same value the sync itself executes, or null when it
        /// would run none (code that declares no ports, or a component that cannot vary them).
        /// </param>
        public static bool ScriptSourceSync(string storedSource, string finalSource, SignatureSync sync)
            => SameSource(storedSource, finalSource) && (sync == null || !sync.ChangesPorts);

        /// <summary>
        /// <c>gh_script(action='configure')</c> with <c>inputs</c> and/or <c>outputs</c> on a component
        /// that can vary its parameters: true when the sync of every side given would move no port
        /// (<see cref="ParamSyncPlan"/>, the plan the sync executes), every input <c>access</c> it would
        /// write (<see cref="ScriptParamDefs.AccessToWrite"/>) is already the input's, no <c>type</c>
        /// is given, and <paramref name="finalSource"/> (when code is passed) is already stored.
        /// </summary>
        /// <remarks>
        /// A <c>type</c> always counts as a change: the script parameter's current type hint is held
        /// by a converter that cannot be compared with a type name without setting it, so a call that
        /// gives one is recorded even when the hint was already that type.
        /// </remarks>
        /// <param name="existingInputs">Current inputs as name and access, the access being the name of its <c>GH_ParamAccess</c> member.</param>
        /// <param name="existingOutputs">Current output names past the built-in leading output.</param>
        /// <param name="inputDefs">The requested inputs, or null when the side is left alone.</param>
        /// <param name="outputDefs">The requested outputs, or null when the side is left alone.</param>
        /// <param name="storedSource">The source the component stores now, or null when it cannot be read.</param>
        /// <param name="finalSource">The source the call would store, or null when it passes no code.</param>
        public static bool ScriptParameters(
            IReadOnlyList<(string Name, string Access)> existingInputs, IReadOnlyList<string> existingOutputs,
            IReadOnlyList<ScriptParamDef> inputDefs, IReadOnlyList<ScriptParamDef> outputDefs,
            string storedSource, string finalSource)
        {
            if (existingInputs == null) throw new ArgumentNullException(nameof(existingInputs));
            if (existingOutputs == null) throw new ArgumentNullException(nameof(existingOutputs));

            if (finalSource != null && !SameSource(storedSource, finalSource)) return false;

            if (inputDefs != null)
            {
                if (!ParamSyncPlan.Compute(existingInputs.Select(p => p.Name).ToList(), inputDefs.Select(d => d.Name).ToList()).IsNoOp)
                    return false;
                for (int i = 0; i < inputDefs.Count; i++)
                {
                    var def = inputDefs[i];
                    if (!string.IsNullOrEmpty(def.Type)) return false;
                    var access = ScriptParamDefs.AccessToWrite(def.Access);
                    if (access != null && !string.Equals(access, existingInputs[i].Access, StringComparison.Ordinal))
                        return false;
                }
            }

            if (outputDefs != null)
            {
                if (!ParamSyncPlan.Compute(existingOutputs, outputDefs.Select(d => d.Name).ToList()).IsNoOp)
                    return false;
                if (outputDefs.Any(d => !string.IsNullOrEmpty(d.Type))) return false;
            }

            return true;
        }
    }
}
