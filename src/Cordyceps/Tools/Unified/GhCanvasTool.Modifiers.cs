using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cordyceps.Core;
using Grasshopper.Kernel;
using Newtonsoft.Json;

namespace Cordyceps.Tools.Unified
{
    public partial class GhCanvasTool
    {
        #region Data Modifier Actions

        /// <summary>
        /// Read or write a parameter's data modifiers — the Flatten/Graft (<c>DataMapping</c>),
        /// Simplify and Reverse options Grasshopper puts on a port's right-click menu.
        ///
        /// <para>Partial update: an omitted modifier is left unchanged. Supplying none of them
        /// reports the parameter's current state instead of mutating it, so the same call shape
        /// serves reading and writing.</para>
        /// </summary>
        private string ActionModifier(string id, string side, string param,
            string mapping, string simplify, string reverse)
        {
            // Validate every argument before touching the document: a half-applied modifier set
            // is worse than a rejected call, and the plan is host-free so it costs no UI thread.
            var plan = DataModifiers.Plan(mapping, simplify, reverse);
            if (!plan.IsValid)
                return ToolHelpers.ErrorResponse(plan.Error);

            bool isInput = true;
            if (!string.IsNullOrWhiteSpace(side))
            {
                if (side.Trim().Equals("output", StringComparison.OrdinalIgnoreCase))
                    isInput = false;
                else if (!side.Trim().Equals("input", StringComparison.OrdinalIgnoreCase))
                    return ToolHelpers.ErrorResponse($"Invalid side: {side}. Use 'input' or 'output'");
            }

            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetUnprotectedComponentWithDoc(_context, id, out _, out var obj, out var error))
                    return ToolHelpers.ErrorResponse(error);

                var target = ResolveModifierParam(obj, param, isInput, out var resolveError);
                if (target == null)
                    return ToolHelpers.ErrorResponse(resolveError);

                var before = ToolHelpers.BuildModifierInfo(target);

                var response = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["id"] = obj.InstanceGuid.ToString(),
                    ["param"] = target.Name
                };

                // A free-floating param IS the target, so no side applies to it; only a
                // component port belongs to an input or output list.
                if (obj is IGH_Component)
                    response["side"] = isInput ? "input" : "output";

                if (plan.IsRead)
                {
                    response["mode"] = "read";
                    response["modifiers"] = before;
                    return JsonConvert.SerializeObject(response);
                }

                var changed = new List<string>();

                if (plan.SetMapping)
                {
                    var requested = ToolHelpers.ToGhDataMapping(plan.Mapping);
                    if (target.DataMapping != requested)
                    {
                        target.DataMapping = requested;
                        changed.Add("mapping");
                    }
                }

                if (plan.SetSimplify && target.Simplify != plan.Simplify)
                {
                    target.Simplify = plan.Simplify;
                    changed.Add("simplify");
                }

                if (plan.SetReverse && target.Reverse != plan.Reverse)
                {
                    target.Reverse = plan.Reverse;
                    changed.Add("reverse");
                }

                if (changed.Count > 0)
                {
                    // ExpireSolution(false) — never (true), which breaks cluster editing. The
                    // owning object is expired so downstream recomputes with the new structure.
                    var owner = (obj as IGH_ActiveObject) ?? (target as IGH_ActiveObject);
                    owner?.ExpireSolution(false);
                }

                response["mode"] = "write";
                response["changed"] = changed;
                response["previous"] = before;
                response["modifiers"] = ToolHelpers.BuildModifierInfo(target);
                return JsonConvert.SerializeObject(response);
            });
        }

        /// <summary>
        /// Resolve the parameter a modifier call targets: a free-floating param is itself the
        /// target, while a component port is named or given as a 0-based index.
        /// </summary>
        /// <remarks>
        /// Mirrors <c>GhWireTool.GetParameter</c> but tolerates a missing spec — the wire tool
        /// always defaults its spec before calling, and this action's <c>param</c> is genuinely
        /// optional (a floating param needs none), so an absent spec must produce a message
        /// rather than an exception.
        /// </remarks>
        private static IGH_Param ResolveModifierParam(IGH_DocumentObject obj, string paramSpec,
            bool isInput, out string error)
        {
            error = null;

            if (obj is IGH_Param floating)
                return floating;

            if (!(obj is IGH_Component comp))
            {
                error = $"Object '{obj.NickName}' has no parameters to modify";
                return null;
            }

            string sideName = isInput ? "input" : "output";
            var list = isInput ? comp.Params.Input : comp.Params.Output;
            if (list.Count == 0)
            {
                error = $"Component '{comp.NickName}' has no {sideName} parameters";
                return null;
            }

            var available = string.Join(", ", list.Select(p => p.Name));

            if (string.IsNullOrWhiteSpace(paramSpec))
            {
                error = $"'param' is required for a component — pass a name or 0-based index. " +
                        $"Available {sideName} params: {available}";
                return null;
            }

            var spec = paramSpec.Trim();

            if (int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                if (index >= 0 && index < list.Count)
                    return list[index];

                error = $"Parameter index {index} is out of range for the {sideName} side " +
                        $"(0-{list.Count - 1}). Available: {available}";
                return null;
            }

            var match = list.FirstOrDefault(p =>
                    p.Name.Equals(spec, StringComparison.OrdinalIgnoreCase) ||
                    p.NickName.Equals(spec, StringComparison.OrdinalIgnoreCase))
                ?? list.FirstOrDefault(p => p.Name.IndexOf(spec, StringComparison.OrdinalIgnoreCase) >= 0);

            if (match == null)
                error = $"Parameter '{paramSpec}' not found on the {sideName} side of " +
                        $"'{comp.NickName}'. Available: {available}";

            return match;
        }

        #endregion
    }
}
