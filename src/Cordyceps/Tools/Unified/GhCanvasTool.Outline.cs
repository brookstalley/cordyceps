// Adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using Cordyceps.Core;
using Cordyceps.Core.Canvas;
using Newtonsoft.Json;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// gh_canvas(action='outline'): thin glue. The argument rules live in
    /// <c>Core/Canvas/OutlineRequest.cs</c> and the views in <c>Core/Canvas/OutlineView.cs</c>, where
    /// they are unit-tested; this file only reads the document and hands it over.
    /// </summary>
    public partial class GhCanvasTool
    {
        #region Outline Action

        private static ActionInfo OutlineActionInfo()
        {
            return new ActionInfo
            {
                Name = "outline",
                Description = "Orient on a definition in one call: compact lines for the groups and how data flows between them, controls and their values, item counts and problems. A compact alternative to list + gh_wire list + group_list",
                Optional = new[] { "id", "group", "depth" },
                Example = "action='outline' OR action='outline', group='INPUTS' OR action='outline', group='[\"INPUTS\", \"MATHS\"]' OR action='outline', id='COUNT', depth=3",
                Tips = new[]
                {
                    "No arguments: the overview (summary, solver, controls, one line per top-level group in data-flow order, ungrouped objects, problems, disabled objects). A canvas without groups shows its separate networks instead.",
                    "group: one group name or id, or a JSON array of them, shows each group's objects upstream first with what feeds each input. Group names match exactly (case-sensitive).",
                    "id: an object's name or id shows what feeds it and what it feeds, depth wires each way (depth 1-10, default 2; only with id). A group's id shows that group. id and group together is an error.",
                    "Every object line starts with a name you can pass back as an id to any tool that takes an object; a group line starts with the group's name, which group actions and outline group= take (a group name starting with '[' goes inside a JSON array). Port names before ← work as param specs. A name that is only an id is followed by its type in lists.",
                    "Counts read 'N stale' when the solver is off or the object has not computed in the current solution, and 'disabled' when the object is locked: don't trust a stale 0. The solver field says why.",
                    "problems uses the same rule as gh_inspect(action='status') (ERROR, WARNING, DISCONNECTED), capped at 10. When more.problems is set, run status for the full list. Outline is for orientation; it does not replace status.",
                    "Caps: 20 controls, 60 group lines, 10 problems, 20 disabled, 10 networks, 150 object lines per group call, 60 objects around an id; a capped list ends '+N more' and more gives the counts left out.",
                    "Not shown: stored values on unwired inputs, group colours, and plugin 'wireless' connections (invisible to Grasshopper's wires)."
                }
            };
        }

        private string ActionOutline(string id, string group, int depth)
        {
            // Every argument rule is checked before the document is touched.
            if (!OutlineRequest.TryParse(id, group, depth, out var request, out var parseError))
                return ToolHelpers.ErrorResponse(parseError);

            // Reading the canvas needs the UI thread; answer at once if it is blocked.
            var liveness = SolverState.Shared.Derive();
            if (liveness.Ui == UiLiveness.Blocked)
                return StatusEnvelope.BusyResult(liveness);

            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var docError))
                    return ToolHelpers.ErrorResponse(docError);

                var snapshot = CanvasReader.Read(doc, CanvasDetail.Full);
                if (!request.TryResolve(snapshot, out var target, out var resolveError))
                    return ToolHelpers.ErrorResponse(resolveError);

                return target.Render(snapshot).ToString(Formatting.None);
            });
        }

        #endregion
    }
}
