// Adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core;
using Cordyceps.Core.Canvas;
using Cordyceps.Core.DataTrees;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Tools.Unified
{
    /// <summary>
    /// gh_inspect(action='tree'): branch paths and item counts for outputs or inputs, read from the
    /// canvas without pulling the data. Thin glue: argument rules, notes and wording live in
    /// <c>Core/DataTrees</c>, where they are unit-tested.
    /// </summary>
    public partial class GhInspectTool
    {
        /// <summary>One port to read, resolved before any read happens.</summary>
        private sealed class TreeRead
        {
            public TreeTarget Target;
            public IGH_DocumentObject Object;
            public IGH_ActiveObject Owner;
            public bool IsParameterObject;
            public List<IGH_Param> Ports;
        }

        private string ActionTree(string id, string ids, string param, string side, int limit)
        {
            if (!TreeRequestParser.TryParse(id, ids, param, side, limit, out var request, out var parseError))
                return ToolHelpers.ErrorResponse(parseError);

            // Reading volatile data needs the UI thread; answer at once if it is blocked.
            var liveness = SolverState.Shared.Derive();
            if (liveness.Ui == UiLiveness.Blocked)
                return StatusEnvelope.BusyResult(liveness);

            return _context.ExecuteOnUiThread(() =>
            {
                if (!ToolHelpers.TryGetActiveDocument(_context, out var doc, out var docError))
                    return ToolHelpers.ErrorResponse(docError);

                // Resolve every target first; nothing is read until all of them have been looked at.
                // One lookup for the call, so names cost one snapshot however many targets there are.
                var errors = new List<string>();
                var reads = new List<TreeRead>();
                var lookup = new ObjectLookup(doc);
                foreach (var target in request.Targets)
                {
                    if (!ToolHelpers.TryGetUnprotectedComponent(lookup, target.Id, out var obj, out var error,
                            worksOn: "tree reads components and parameters"))
                    {
                        errors.Add($"{target.Label}: {error}");
                        continue;
                    }
                    var read = Resolve(target, obj, request.InputSide, errors);
                    if (read != null) reads.Add(read);
                }

                var solverOff = TreeStatusText.SolverOff(
                    doc.Enabled, GH_Document.EnableSolutions, doc.SolutionState == GH_ProcessStep.Aborted);

                var trees = new JArray();
                foreach (var read in reads)
                    foreach (var tree in DescribeTrees(read, request, solverOff?.Reason))
                        trees.Add(tree);

                var result = new JObject { ["success"] = errors.Count == 0 };
                if (solverOff != null) result["solver"] = solverOff.Value.Text;
                result["trees"] = trees;
                if (errors.Count > 0) result["errors"] = new JArray(errors);
                return result.ToString(Formatting.None);
            });
        }

        private static TreeRead Resolve(TreeTarget target, IGH_DocumentObject obj, bool inputSide, List<string> errors)
        {
            // A group never gets here: the lookup refuses it in tree's own words.

            // A parameter object has one tree, so side and param do not apply to it.
            if (obj is IGH_Param standalone && !(obj is IGH_Component))
                return new TreeRead
                {
                    Target = target, Object = obj, Owner = standalone,
                    IsParameterObject = true, Ports = new List<IGH_Param> { standalone }
                };

            if (obj is IGH_Component comp)
            {
                var sideName = inputSide ? "input" : "output";
                var all = (inputSide ? comp.Params.Input : comp.Params.Output).ToList();
                if (all.Count == 0)
                {
                    errors.Add($"{target.Label}: {comp.NickName} has no {sideName} parameters");
                    return null;
                }

                var ports = all;
                if (target.Param != null)
                {
                    var identities = all.Select(p => new ParamIdentity(p.Name, p.NickName)).ToList();
                    var found = ParamSpecLookup.Resolve(target.Param, identities, inputSide, comp.NickName);
                    if (!found.IsResolved)
                    {
                        errors.Add($"{target.Label}: {found.Error}");
                        return null;
                    }
                    ports = new List<IGH_Param> { all[found.Index] };
                }
                return new TreeRead { Target = target, Object = obj, Owner = comp, Ports = ports };
            }

            errors.Add($"{target.Label}: {obj.NickName} is neither a component nor a parameter");
            return null;
        }

        private static IEnumerable<JObject> DescribeTrees(TreeRead read, TreeRequest request, string solverOffReason)
        {
            var described = new List<(JObject Tree, LabeledTree Labeled)>();
            foreach (var port in read.Ports)
            {
                var branches = DataTreeReader.ReadBranches(port.VolatileData);
                var label = PortLabel(port);

                var tree = new JObject
                {
                    ["target"] = read.Target.Id,
                    ["object"] = read.Object.NickName,
                    ["id"] = read.Object.InstanceGuid.ToString(),
                    ["port"] = label,
                    ["side"] = read.IsParameterObject ? "parameter" : request.InputSide ? "input" : "output",
                    ["type"] = port.TypeName
                };

                var state = OwnerStateOf(read.Owner);
                if (state != OwnerState.Computed)
                    tree["solved"] = false;

                foreach (var kv in TreeSummary.Describe(branches, request.MaxRuns))
                    tree[kv.Key] = kv.Value;

                // A warning or error on the owner usually explains an empty or odd tree. The level
                // and its messages come from the shared health rule, as status and outline read them.
                var health = CanvasReader.Health(read.Owner);
                if (health.Status == HealthStatus.Error || health.Status == HealthStatus.Warning)
                {
                    var levelName = health.Status == HealthStatus.Error ? "Error" : "Warning";
                    var text = TreeStatusText.FormatMessage(levelName, health.Messages.ToList());
                    if (text != null) tree["message"] = text;
                }

                var note = TreeStatusText.OwnerNote(state, solverOffReason);
                if (note != null) tree["note"] = note;

                described.Add((tree, new LabeledTree(label, branches)));
            }

            // Comparing inputs only means something for several inputs of one component, read together.
            if (request.InputSide && !read.IsParameterObject && read.Target.Param == null && described.Count > 1)
            {
                var alignment = TreeAlignment.Check(described.Select(d => d.Labeled).ToList());
                if (alignment != null)
                {
                    var firstMulti = described.First(d => d.Labeled.Branches.Count > 1);
                    TreeSummary.AddNote(firstMulti.Tree, alignment);
                }
            }

            return described.Select(d => d.Tree);
        }

        private static string PortLabel(IGH_Param port)
            => port.NickName == port.Name ? port.Name : $"{port.NickName} ({port.Name})";

        private static OwnerState OwnerStateOf(IGH_ActiveObject owner)
        {
            if (owner.Locked) return OwnerState.Locked;
            switch (owner.Phase)
            {
                case GH_SolutionPhase.Computed: return OwnerState.Computed;
                case GH_SolutionPhase.Failed: return OwnerState.Failed;
                case GH_SolutionPhase.Computing: return OwnerState.Computing;
                default: return OwnerState.NotComputed;
            }
        }
    }
}
