using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cordyceps.Core.DataTrees
{
    /// <summary>Where a tree's owner is in the solution, reduced to what the tree notes need.</summary>
    public enum OwnerState
    {
        Computed,

        /// <summary>Blank, collecting or collected: it has not produced data in the current solution.</summary>
        NotComputed,
        Computing,
        Failed,
        Locked
    }

    /// <summary>
    /// The words gh_inspect(action='tree') uses for solver and owner state. Each one states only what
    /// Grasshopper's flags show; none guesses at a cause the flags do not name.
    /// </summary>
    public static class TreeStatusText
    {
        public const int MessageClip = 160;

        /// <summary>
        /// Why the solver is off, as <c>(Text, Reason)</c>, or null when it is on. Text is the
        /// <c>solver</c> field ("off: ..."); Reason is the same without the "off: " prefix, for
        /// sentences. A last-aborted solution is reported only alongside an off solver.
        /// </summary>
        public static (string Text, string Reason)? SolverOff(bool documentEnabled, bool globalSolutionsEnabled, bool lastSolutionAborted)
        {
            if (documentEnabled && globalSolutionsEnabled) return null;

            string reason;
            if (!documentEnabled && !globalSolutionsEnabled)
                reason = "this document's solver is disabled and Grasshopper's solver is disabled";
            else if (!documentEnabled)
                reason = "this document's solver is disabled";
            else
                reason = "Grasshopper's solver is disabled";

            if (lastSolutionAborted) reason += "; the last solution was aborted";
            return ("off: " + reason, reason);
        }

        /// <summary>
        /// The note for a tree whose owner has not finished computing, or null when it has.
        /// <paramref name="solverOffReason"/> is <see cref="SolverOff"/>'s Reason, or null when the
        /// solver is on.
        /// </summary>
        public static string OwnerNote(OwnerState state, string solverOffReason)
        {
            switch (state)
            {
                case OwnerState.Computed:
                    return null;
                case OwnerState.Locked:
                    return "the owner is locked (disabled) and the solver skips it, so this tree is not being updated";
                case OwnerState.Failed:
                    return "the owner failed in the last solution, so the tree may be empty or incomplete";
                case OwnerState.Computing:
                    return "the owner is computing right now, so the tree is not final";
                default:
                    return solverOffReason != null
                        ? $"not solved: {solverOffReason}, so the tree may be empty or out of date"
                        : "not computed in the current solution yet, so the tree may be empty or out of date";
            }
        }

        /// <summary>
        /// <c>"Warning: first message (+K more)"</c> for the owner's messages at
        /// <paramref name="level"/>, the first clipped to <see cref="MessageClip"/> characters
        /// (ellipsis included); null when there is no message.
        /// </summary>
        public static string FormatMessage(string level, IReadOnlyList<string> messages)
        {
            var list = (messages ?? new List<string>()).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (list.Count == 0) return null;

            var first = list[0];
            if (first.Length > MessageClip) first = first.Substring(0, MessageClip - 1) + "…";
            var more = list.Count - 1;
            return $"{level}: {first}" + (more > 0 ? $" (+{more.ToString(CultureInfo.InvariantCulture)} more)" : "");
        }
    }
}
