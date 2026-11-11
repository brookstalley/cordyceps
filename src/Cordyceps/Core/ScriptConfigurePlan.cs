namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free decision for <c>gh_script(action='configure')</c>: given what the call actually
    /// changed on a script component, must its program be rebuilt — and when it must not, why not.
    ///
    /// <para>Storing source does not recompile, and a rebuild reads the component's parameters to
    /// shape the program's inputs and outputs (see <see cref="ScriptProgram"/>). So <em>either</em>
    /// kind of change invalidates the running program: writing new source, and reshaping the
    /// parameter surface the old source was compiled against. Whether source happened to be
    /// written is a side effect of one branch, not a statement about what changed, so it cannot
    /// be the gate: a call that only moves the parameters would pass it unrebuilt and, because
    /// the gate also produces the response's rebuild fields, unreported. Asking this type keeps
    /// the two apart: a new way to change a component adds a fact here rather than a local that
    /// every route must remember to re-check.</para>
    ///
    /// <para>Pure and host-free (BCL only) so it is unit-testable with no Grasshopper types in the
    /// room; the caller owns the rebuild itself and the reporting.</para>
    /// </summary>
    public sealed class ScriptConfigurePlan
    {
        /// <summary>
        /// Said when a call reached the component but asked it to change nothing — no parameter
        /// side was applied and no source was written — so there is no stale program to replace.
        /// </summary>
        public const string NothingChangedReason =
            "This call changed neither the component's parameters nor its source, so its program "
            + "was left as it is.";

        private ScriptConfigurePlan(bool rebuildRequired, string skipReason)
        {
            RebuildRequired = rebuildRequired;
            SkipReason = skipReason;
        }

        /// <summary>True when the call invalidated the running program and it must be rebuilt.</summary>
        public bool RebuildRequired { get; }

        /// <summary>
        /// Why no rebuild is needed, for the caller to report. Non-<c>null</c> exactly when
        /// <see cref="RebuildRequired"/> is false — a caller must never have to explain the
        /// absence of a rebuild by omitting a field.
        /// </summary>
        public string SkipReason { get; }

        /// <summary>
        /// Decide from what the call changed.
        /// </summary>
        /// <param name="parametersChanged">
        /// True when the component's parameter surface was actually reshaped — not merely that
        /// <c>inputs</c>/<c>outputs</c> were supplied. A component that cannot vary its parameters
        /// is unchanged by a request to vary them, and reporting a rebuild for it would be a claim
        /// about work that never happened.
        /// </param>
        /// <param name="sourceWritten">
        /// True when new source actually landed on the component. A write that threw changed
        /// nothing, so it is false here — the failure is reported separately, by the caller.
        /// </param>
        public static ScriptConfigurePlan For(bool parametersChanged, bool sourceWritten)
        {
            if (parametersChanged || sourceWritten)
                return new ScriptConfigurePlan(true, null);

            return new ScriptConfigurePlan(false, NothingChangedReason);
        }
    }
}
