using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Cordyceps.Core.Canvas
{
    /// <summary>
    /// The one Grasshopper adapter for <see cref="CanvasSnapshot"/>: reads a document into the
    /// host-free model, and nothing else. Every rule that reads the model lives in host-free code
    /// beside it; keep this file to copying values out of Grasshopper.
    /// <para><b>Call on the UI thread only</b> (inside <c>GrasshopperContext.ExecuteOnUiThread</c>):
    /// it walks <c>doc.Objects</c> and reads volatile data, which Grasshopper does not guard.</para>
    /// </summary>
    public static class CanvasReader
    {
        /// <summary>
        /// Read <paramref name="doc"/>. <see cref="CanvasDetail.Names"/> fills ids, kinds, type names,
        /// nicknames, default-nickname flags, groups and infrastructure only, for per-call id lookups;
        /// <see cref="CanvasDetail.Full"/> fills everything. Must be called on the UI thread.
        /// </summary>
        public static CanvasSnapshot Read(GH_Document doc, CanvasDetail detail)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var infra = ToolHelpers.GetCordycepsInfrastructureIds(doc);
            var objects = ToolHelpers.GetActiveObjects(doc, infra).ToList();
            var defaults = DefaultNicknames();

            var nodeIds = new HashSet<Guid>(objects.Where(o => !(o is GH_Group)).Select(o => o.InstanceGuid));
            var groupIds = new HashSet<Guid>(objects.OfType<GH_Group>().Select(g => g.InstanceGuid));

            var infraSet = new HashSet<Guid>(infra);
            var dropped = new List<string>();
            var nodes = new List<CanvasNode>();
            var groups = new List<CanvasGroup>();
            foreach (var obj in objects)
            {
                if (obj is GH_Group g)
                    groups.Add(ReadGroup(g, nodeIds, groupIds));
                else
                    nodes.Add(detail == CanvasDetail.Full
                        ? ReadFullNodeNamed(obj, defaults, nodeIds, infraSet, dropped)
                        : ReadNameNode(obj, defaults));
            }

            // One line per read, not per wire, so a bad document cannot flood the log.
            if (dropped.Count > 0)
                DebugLog.Debug($"CanvasReader: skipped {dropped.Count} wire source(s) it could not place: {string.Join("; ", dropped.Take(10))}{(dropped.Count > 10 ? "; ..." : "")}");

            var solver = detail == CanvasDetail.Full
                ? new SolverFlags
                {
                    DocumentEnabled = doc.Enabled,
                    SolutionsEnabled = GH_Document.EnableSolutions,
                    // From the solve record, not doc.SolutionState: Grasshopper overwrites Aborted
                    // with PostProcess before the solve returns, so the state never shows it.
                    LastSolutionAborted = SolverState.Shared.LastSolutionAborted(doc.DocumentID)
                }
                : new SolverFlags();

            // The count travels with the snapshot so the views can tell the caller, not only the log.
            return new CanvasSnapshot(doc.DisplayName, solver, detail, nodes, groups, infra, unreadWires: dropped.Count);
        }

        /// <summary>
        /// The health of a live object by the shared rule, for callers that walk the document
        /// themselves (<c>status</c>). Must be called on the UI thread.
        /// </summary>
        public static HealthResult Health(IGH_ActiveObject obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            var level = ToLevel(obj.RuntimeMessageLevel);
            var comp = obj as IGH_Component;
            return ObjectHealth.Classify(
                level,
                level == MessageLevel.Error ? obj.RuntimeMessages(GH_RuntimeMessageLevel.Error) : null,
                level == MessageLevel.Warning ? obj.RuntimeMessages(GH_RuntimeMessageLevel.Warning) : null,
                comp == null ? null : HealthInputs(comp),
                comp != null);
        }

        /// <summary>
        /// What the shared health rule reads about each input of <paramref name="comp"/>, in input
        /// order (so entry i is <c>Params.Input[i]</c>). Must be called on the UI thread.
        /// </summary>
        public static IReadOnlyList<HealthInput> HealthInputs(IGH_Component comp)
        {
            if (comp == null) throw new ArgumentNullException(nameof(comp));
            return comp.Params.Input.Select(i => new HealthInput(i.Name, i.Optional, i.SourceCount, i.VolatileDataCount)).ToList();
        }

        /// <summary>Grasshopper's message level, mapped by name onto the host-free one.</summary>
        public static MessageLevel ToLevel(GH_RuntimeMessageLevel level)
        {
            switch (level)
            {
                case GH_RuntimeMessageLevel.Error: return MessageLevel.Error;
                case GH_RuntimeMessageLevel.Warning: return MessageLevel.Warning;
                case GH_RuntimeMessageLevel.Remark: return MessageLevel.Remark;
                default: return MessageLevel.None;
            }
        }

        private static NodePhase ToPhase(GH_SolutionPhase phase)
        {
            switch (phase)
            {
                case GH_SolutionPhase.Computed: return NodePhase.Computed;
                case GH_SolutionPhase.Failed: return NodePhase.Failed;
                default: return NodePhase.NotComputed;
            }
        }

        private static NodeKind KindOf(IGH_DocumentObject obj)
        {
            switch (obj)
            {
                case GH_NumberSlider _: return NodeKind.Slider;
                case GH_BooleanToggle _: return NodeKind.Toggle;
                case GH_ValueList _: return NodeKind.ValueList;
                case GH_Panel _: return NodeKind.Panel;
                case IGH_Component _: return NodeKind.Component;
                case IGH_Param _: return NodeKind.Parameter;
                default: return NodeKind.Other;
            }
        }

        private static bool IsDefaultNickName(IGH_DocumentObject obj, Dictionary<Guid, string> defaults)
        {
            var nick = obj.NickName ?? string.Empty;
            if (nick == (obj.Name ?? string.Empty)) return true;
            return defaults.TryGetValue(obj.ComponentGuid, out var standard) && nick == standard;
        }

        private static CanvasNode ReadNameNode(IGH_DocumentObject obj, Dictionary<Guid, string> defaults) =>
            new CanvasNode
            {
                Id = obj.InstanceGuid,
                Kind = KindOf(obj),
                TypeName = obj.Name ?? string.Empty,
                NickName = obj.NickName ?? string.Empty,
                IsDefaultNickName = IsDefaultNickName(obj, defaults),
                X = obj.Attributes?.Pivot.X ?? 0,
                Y = obj.Attributes?.Pivot.Y ?? 0,
                Detail = CanvasDetail.Names
            };

        // A failure while reading one object is rethrown naming that object; nothing is swallowed.
        private static CanvasNode ReadFullNodeNamed(IGH_DocumentObject obj, Dictionary<Guid, string> defaults,
            HashSet<Guid> nodeIds, HashSet<Guid> infra, List<string> dropped)
        {
            try
            {
                return ReadFullNode(obj, defaults, nodeIds, infra, dropped);
            }
            catch (Exception ex) // prawduct:allow prawduct/broad-except -- rethrown at once naming the object; nothing is swallowed, and third-party components can throw anything
            {
                throw new InvalidOperationException(
                    $"Could not read canvas object '{obj.NickName}' ({obj.InstanceGuid}): {ex.Message}", ex);
            }
        }

        private static CanvasNode ReadFullNode(IGH_DocumentObject obj, Dictionary<Guid, string> defaults,
            HashSet<Guid> nodeIds, HashSet<Guid> infra, List<string> dropped)
        {
            var kind = KindOf(obj);
            var active = obj as IGH_ActiveObject;
            var level = active != null ? ToLevel(active.RuntimeMessageLevel) : MessageLevel.None;

            string valueText = null, sliderMin = null, sliderMax = null, panelText = null;
            bool panelTruncated = false;
            switch (obj)
            {
                case GH_NumberSlider slider:
                    valueText = CanvasText.Number(slider.CurrentValue);
                    sliderMin = CanvasText.Number(slider.Slider.Minimum);
                    sliderMax = CanvasText.Number(slider.Slider.Maximum);
                    break;
                case GH_BooleanToggle toggle:
                    valueText = toggle.Value ? "true" : "false";
                    break;
                case GH_ValueList list:
                    valueText = list.SelectedItems.FirstOrDefault()?.Name;
                    break;
                case GH_Panel panel:
                    if (panel.SourceCount == 0)
                        panelText = CanvasText.Cap(panel.UserText ?? string.Empty, CanvasText.PanelTextCap, out panelTruncated);
                    break;
            }

            var inputs = new List<CanvasPort>();
            var outputs = new List<CanvasPort>();
            if (obj is IGH_Component comp)
            {
                foreach (var p in comp.Params.Input) inputs.Add(ReadPort(p, nodeIds, infra, dropped));
                foreach (var p in comp.Params.Output) outputs.Add(ReadPort(p, nodeIds, infra, dropped));
            }
            else if (obj is IGH_Param param)
            {
                // A parameter object is its own port. Controls draw no input grip and take no wires.
                if (param.Attributes == null || param.Attributes.HasInputGrip)
                    inputs.Add(ReadPort(param, nodeIds, infra, dropped));
                outputs.Add(ReadPort(param, nodeIds, infra, dropped));
            }

            return new CanvasNode
            {
                Id = obj.InstanceGuid,
                Kind = kind,
                TypeName = obj.Name ?? string.Empty,
                NickName = obj.NickName ?? string.Empty,
                IsDefaultNickName = IsDefaultNickName(obj, defaults),
                X = obj.Attributes?.Pivot.X ?? 0,
                Y = obj.Attributes?.Pivot.Y ?? 0,
                Locked = active?.Locked ?? false,
                PreviewHidden = obj is IGH_PreviewObject preview ? preview.Hidden : (bool?)null,
                HasSolveState = active != null,
                Phase = active != null ? ToPhase(active.Phase) : NodePhase.NotComputed,
                MessageLevel = level,
                Warnings = active?.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToList() ?? new List<string>(),
                Errors = active?.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToList() ?? new List<string>(),
                ValueText = valueText,
                SliderMin = sliderMin,
                SliderMax = sliderMax,
                PanelText = panelText,
                PanelTextTruncated = panelTruncated,
                Inputs = inputs,
                Outputs = outputs
            };
        }

        private static CanvasPort ReadPort(IGH_Param p, HashSet<Guid> nodeIds, HashSet<Guid> infra, List<string> dropped)
        {
            var sources = new List<PortSource>();
            foreach (var s in p.Sources)
            {
                var owner = s.Attributes?.GetTopLevel?.DocObject;
                if (owner == null)
                {
                    dropped.Add($"'{p.NickName}' source {s.InstanceGuid} has no attributes or owner");
                    continue;
                }
                // Infrastructure is not a node and drops out silently, by design.
                if (infra.Contains(owner.InstanceGuid)) continue;
                if (!nodeIds.Contains(owner.InstanceGuid))
                {
                    dropped.Add($"'{p.NickName}' source owner {owner.InstanceGuid} is not a canvas node");
                    continue;
                }
                int index = owner is IGH_Component sc ? sc.Params.Output.IndexOf(s) : -1;
                if (owner is IGH_Component && index < 0)
                {
                    dropped.Add($"'{p.NickName}' source {s.InstanceGuid} is not an output of its owner {owner.InstanceGuid}");
                    continue;
                }
                sources.Add(new PortSource(owner.InstanceGuid, index));
            }

            return new CanvasPort
            {
                Name = p.Name ?? string.Empty,
                NickName = p.NickName ?? string.Empty,
                Optional = p.Optional,
                Mapping = ToolHelpers.ToMappingChoice(p.DataMapping),
                Simplify = p.Simplify,
                Reverse = p.Reverse,
                SourceCount = p.SourceCount,
                VolatileCount = p.VolatileDataCount,
                Sources = sources
            };
        }

        private static CanvasGroup ReadGroup(GH_Group g, HashSet<Guid> nodeIds, HashSet<Guid> groupIds)
        {
            var members = new List<Guid>();
            var children = new List<Guid>();
            foreach (var id in g.ObjectIDs)
            {
                if (nodeIds.Contains(id)) { if (!members.Contains(id)) members.Add(id); }
                else if (groupIds.Contains(id) && id != g.InstanceGuid) { if (!children.Contains(id)) children.Add(id); }
            }

            var bounds = g.Attributes?.Bounds ?? default;
            return new CanvasGroup
            {
                Id = g.InstanceGuid,
                NickName = g.NickName ?? string.Empty,
                X = bounds.X,
                Y = bounds.Y,
                MemberIds = members,
                ChildGroupIds = children
            };
        }

        // Default nicknames by component id, so a renamed object can be told from an untouched one.
        // Built per read, never cached: a count-keyed cache goes stale when a plugin loads or swaps
        // proxies without changing the count (ComponentRegistry.BuildCatalog refuses it for that reason).
        private static Dictionary<Guid, string> DefaultNicknames()
        {
            var d = new Dictionary<Guid, string>();
            foreach (var p in Instances.ComponentServer.ObjectProxies)
            {
                // One bad proxy must not fail every read.
                if (p?.Desc == null) continue;
                if (!d.ContainsKey(p.Guid)) d[p.Guid] = p.Desc.NickName;
            }
            return d;
        }
    }
}
