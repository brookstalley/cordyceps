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

            var nodes = new List<CanvasNode>();
            var groups = new List<CanvasGroup>();
            foreach (var obj in objects)
            {
                if (obj is GH_Group g)
                    groups.Add(ReadGroup(g, nodeIds, groupIds));
                else
                    nodes.Add(detail == CanvasDetail.Full
                        ? ReadFullNode(obj, defaults, nodeIds)
                        : ReadNameNode(obj, defaults));
            }

            var solver = detail == CanvasDetail.Full
                ? new SolverFlags
                {
                    DocumentEnabled = doc.Enabled,
                    SolutionsEnabled = GH_Document.EnableSolutions,
                    LastSolutionAborted = doc.SolutionState == GH_ProcessStep.Aborted
                }
                : new SolverFlags();

            return new CanvasSnapshot(doc.DisplayName, solver, detail, nodes, groups, infra);
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
                comp?.Params.Input.Select(i => new HealthInput(i.Name, i.Optional, i.SourceCount, i.VolatileDataCount)),
                comp != null);
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
                Y = obj.Attributes?.Pivot.Y ?? 0
            };

        private static CanvasNode ReadFullNode(IGH_DocumentObject obj, Dictionary<Guid, string> defaults, HashSet<Guid> nodeIds)
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
                foreach (var p in comp.Params.Input) inputs.Add(ReadPort(p, nodeIds));
                foreach (var p in comp.Params.Output) outputs.Add(ReadPort(p, nodeIds));
            }
            else if (obj is IGH_Param param)
            {
                // A parameter object is its own port. Controls draw no input grip and take no wires.
                if (param.Attributes == null || param.Attributes.HasInputGrip)
                    inputs.Add(ReadPort(param, nodeIds));
                outputs.Add(ReadPort(param, nodeIds));
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

        private static CanvasPort ReadPort(IGH_Param p, HashSet<Guid> nodeIds)
        {
            var sources = new List<PortSource>();
            foreach (var s in p.Sources)
            {
                var owner = s.Attributes?.GetTopLevel?.DocObject;
                // Infrastructure (and anything not on the active canvas) is not a node, so it drops out here.
                if (owner == null || !nodeIds.Contains(owner.InstanceGuid)) continue;
                int index = owner is IGH_Component sc ? sc.Params.Output.IndexOf(s) : -1;
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
        // Rebuilt when the installed proxy count changes; UI-thread only, like the reader.
        private static Dictionary<Guid, string> _defaults;
        private static int _defaultsCount = -1;

        private static Dictionary<Guid, string> DefaultNicknames()
        {
            var proxies = Instances.ComponentServer.ObjectProxies;
            if (_defaults != null && _defaultsCount == proxies.Count)
                return _defaults;

            var d = new Dictionary<Guid, string>();
            foreach (var p in proxies)
            {
                if (!d.ContainsKey(p.Guid)) d[p.Guid] = p.Desc.NickName;
            }
            _defaults = d;
            _defaultsCount = proxies.Count;
            return d;
        }
    }
}
