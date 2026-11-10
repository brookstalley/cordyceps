using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cordyceps.Core.Canvas
{
    /// <summary>How much of the document a <see cref="CanvasSnapshot"/> holds.</summary>
    public enum CanvasDetail
    {
        /// <summary>
        /// Ids, kinds, type names, nicknames, default-nickname flags, groups and infrastructure only:
        /// enough to resolve names, cheap enough to build on every call that takes an id.
        /// </summary>
        Names,

        /// <summary>Everything, including ports, wires, messages, values and solver state.</summary>
        Full
    }

    /// <summary>What kind of object a node is; decides how its value is shown.</summary>
    public enum NodeKind
    {
        Component,
        Parameter,
        Slider,
        Toggle,
        ValueList,
        Panel,

        /// <summary>Anything else on the canvas that is not a group (a scribble, a sketch).</summary>
        Other
    }

    /// <summary>
    /// Where an object stands in the solution. Grasshopper's intermediate phases (blank, collecting,
    /// computing) all read as <see cref="NotComputed"/>: the object's data is not from a finished solve.
    /// </summary>
    public enum NodePhase
    {
        Computed,
        NotComputed,
        Failed
    }

    /// <summary>The highest runtime message level an object carries. Remarks never change a status.</summary>
    public enum MessageLevel
    {
        None,
        Remark,
        Warning,
        Error
    }

    /// <summary>The document's solver switches, as the last read found them.</summary>
    public sealed class SolverFlags
    {
        /// <summary>This document's own enabled flag (<c>GH_Document.Enabled</c>).</summary>
        public bool DocumentEnabled { get; init; } = true;

        /// <summary>Grasshopper's global switch (<c>GH_Document.EnableSolutions</c>).</summary>
        public bool SolutionsEnabled { get; init; } = true;

        /// <summary>The document's last solution was aborted.</summary>
        public bool LastSolutionAborted { get; init; }

        /// <summary>Both switches are on, so counts can be current.</summary>
        public bool IsOn => DocumentEnabled && SolutionsEnabled;
    }

    /// <summary>
    /// One wire arriving at an input: the node it comes from and which of that node's outputs.
    /// </summary>
    public sealed class PortSource
    {
        public PortSource(Guid nodeId, int outputIndex)
        {
            NodeId = nodeId;
            OutputIndex = outputIndex;
        }

        public Guid NodeId { get; }

        /// <summary>
        /// Index into the source component's outputs; -1 when the source is a parameter object,
        /// whose one output is itself.
        /// </summary>
        public int OutputIndex { get; }
    }

    /// <summary>One input or output port. On a parameter object, the parameter is its own port.</summary>
    public sealed class CanvasPort
    {
        public string Name { get; init; } = string.Empty;
        public string NickName { get; init; } = string.Empty;
        public bool Optional { get; init; }
        public DataMappingChoice Mapping { get; init; }
        public bool Simplify { get; init; }
        public bool Reverse { get; init; }

        /// <summary>
        /// Grasshopper's own source count, infrastructure sources included. The DISCONNECTED rule
        /// reads this rather than <see cref="Sources"/> so it says exactly what <c>status</c> says.
        /// </summary>
        public int SourceCount { get; init; }

        /// <summary>Items in the port's volatile data (from the last solution; not forced).</summary>
        public int VolatileCount { get; init; }

        /// <summary>Wires into this port, in Grasshopper's order, with infrastructure sources removed.</summary>
        public IReadOnlyList<PortSource> Sources { get; init; } = Array.Empty<PortSource>();

        /// <summary>The modifiers in words, in a fixed order: graft, flatten, simplify, reverse.</summary>
        public IReadOnlyList<string> ModifierWords
        {
            get
            {
                var words = new List<string>();
                if (Mapping == DataMappingChoice.Graft) words.Add("graft");
                if (Mapping == DataMappingChoice.Flatten) words.Add("flatten");
                if (Simplify) words.Add("simplify");
                if (Reverse) words.Add("reverse");
                return words;
            }
        }
    }

    /// <summary>One canvas object that is not a group and not Cordyceps infrastructure.</summary>
    public sealed class CanvasNode
    {
        public Guid Id { get; init; }
        public NodeKind Kind { get; init; }

        /// <summary>The object's type as Grasshopper names it: "Addition", "Number Slider", "Panel".</summary>
        public string TypeName { get; init; } = string.Empty;

        public string NickName { get; init; } = string.Empty;

        /// <summary>The nickname is the type's default (its proxy nickname or its name).</summary>
        public bool IsDefaultNickName { get; init; }

        /// <summary>The object's pivot on the canvas.</summary>
        public float X { get; init; }
        public float Y { get; init; }

        /// <summary>Disabled (Grasshopper's "Enable" unticked).</summary>
        public bool Locked { get; init; }

        /// <summary>Preview turned off; null when the object has no preview at all.</summary>
        public bool? PreviewHidden { get; init; }

        public NodePhase Phase { get; init; } = NodePhase.NotComputed;
        public MessageLevel MessageLevel { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        /// <summary>
        /// A control's value in invariant text: a slider's number, a toggle's <c>true</c>/<c>false</c>,
        /// a value list's selected item name. Null for anything else, and for a list with nothing selected.
        /// </summary>
        public string ValueText { get; init; }

        /// <summary>A slider's minimum and maximum in invariant text; null for anything else.</summary>
        public string SliderMin { get; init; }
        public string SliderMax { get; init; }

        /// <summary>
        /// An unwired panel's own text, capped at <see cref="CanvasText.PanelTextCap"/> characters;
        /// null for a wired panel (its stored text is not what it shows) and for anything else.
        /// </summary>
        public string PanelText { get; init; }
        public bool PanelTextTruncated { get; init; }

        /// <summary>Inputs in port order. A parameter object that takes wires has itself as its one input.</summary>
        public IReadOnlyList<CanvasPort> Inputs { get; init; } = Array.Empty<CanvasPort>();

        /// <summary>Outputs in port order. A parameter object has itself as its one output.</summary>
        public IReadOnlyList<CanvasPort> Outputs { get; init; } = Array.Empty<CanvasPort>();

        public bool IsControl => Kind == NodeKind.Slider || Kind == NodeKind.Toggle || Kind == NodeKind.ValueList;

        /// <summary>A parameter object (a plain parameter, a control or a panel), not a component.</summary>
        public bool IsParameterObject => Kind == NodeKind.Parameter || Kind == NodeKind.Panel || IsControl;

        /// <summary>The output a <see cref="PortSource"/> on another node names; null when out of range.</summary>
        public CanvasPort OutputFor(int outputIndex)
        {
            int i = outputIndex < 0 ? 0 : outputIndex;
            return i < Outputs.Count ? Outputs[i] : null;
        }
    }

    /// <summary>A canvas group that is not Cordyceps infrastructure.</summary>
    public sealed class CanvasGroup
    {
        public Guid Id { get; init; }
        public string NickName { get; init; } = string.Empty;

        /// <summary>The top-left corner of the group's bounds (a group's own pivot is not meaningful).</summary>
        public float X { get; init; }
        public float Y { get; init; }

        /// <summary>Non-group objects directly in the group, in the group's own order.</summary>
        public IReadOnlyList<Guid> MemberIds { get; init; } = Array.Empty<Guid>();

        /// <summary>Groups directly in this group, in the group's own order.</summary>
        public IReadOnlyList<Guid> ChildGroupIds { get; init; } = Array.Empty<Guid>();
    }

    /// <summary>One wire between two nodes.</summary>
    public sealed class CanvasWire
    {
        public CanvasWire(Guid sourceId, int sourceOutput, Guid targetId, int targetInput)
        {
            SourceId = sourceId;
            SourceOutput = sourceOutput;
            TargetId = targetId;
            TargetInput = targetInput;
        }

        public Guid SourceId { get; }

        /// <summary>The source's output index; -1 for a parameter object.</summary>
        public int SourceOutput { get; }

        public Guid TargetId { get; }
        public int TargetInput { get; }
    }

    /// <summary>
    /// An immutable, host-free picture of one Grasshopper document, read once per call on the UI
    /// thread by <c>CanvasReader</c> and used afterwards from any thread. Wires are recorded once, on
    /// the receiving input; upstream and downstream are both derived from them. Groups may overlap
    /// and nest under several parents, so membership and nesting are multimaps, and every walk over
    /// them carries a seen-set.
    /// </summary>
    public sealed class CanvasSnapshot : IObjectIndex
    {
        private static readonly IReadOnlyList<Guid> NoIds = Array.Empty<Guid>();

        private readonly Dictionary<Guid, CanvasNode> _nodeById = new Dictionary<Guid, CanvasNode>();
        private readonly Dictionary<Guid, CanvasGroup> _groupById = new Dictionary<Guid, CanvasGroup>();
        private readonly Dictionary<Guid, ObjectEntry> _entryById = new Dictionary<Guid, ObjectEntry>();
        private readonly List<ObjectEntry> _entries = new List<ObjectEntry>();
        private readonly Dictionary<string, List<ObjectEntry>> _byNickname = new Dictionary<string, List<ObjectEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<ObjectEntry>> _byGroupName = new Dictionary<string, List<ObjectEntry>>(StringComparer.Ordinal);
        private readonly HashSet<Guid> _infrastructure;
        private readonly GuidPrefixIndex _prefixes;

        private readonly Dictionary<Guid, List<Guid>> _upstream = new Dictionary<Guid, List<Guid>>();
        private readonly Dictionary<Guid, List<Guid>> _downstream = new Dictionary<Guid, List<Guid>>();
        private readonly Dictionary<Guid, List<Guid>> _groupsOfNode = new Dictionary<Guid, List<Guid>>();
        private readonly Dictionary<Guid, List<Guid>> _parentsOfGroup = new Dictionary<Guid, List<Guid>>();
        private readonly Dictionary<Guid, IReadOnlyCollection<Guid>> _effective = new Dictionary<Guid, IReadOnlyCollection<Guid>>();
        private readonly Dictionary<Guid, List<Guid>> _topGroupsOfNode = new Dictionary<Guid, List<Guid>>();

        public CanvasSnapshot(
            string documentName,
            SolverFlags solver,
            CanvasDetail detail,
            IEnumerable<CanvasNode> nodes,
            IEnumerable<CanvasGroup> groups,
            IEnumerable<Guid> infrastructure)
        {
            DocumentName = documentName ?? string.Empty;
            Solver = solver ?? new SolverFlags();
            Detail = detail;
            _infrastructure = new HashSet<Guid>(infrastructure ?? Enumerable.Empty<Guid>());

            // Infrastructure never enters the snapshot, whoever built the lists.
            var nodeList = new List<CanvasNode>();
            foreach (var n in nodes ?? Enumerable.Empty<CanvasNode>())
            {
                if (n == null || _infrastructure.Contains(n.Id) || _nodeById.ContainsKey(n.Id)) continue;
                _nodeById[n.Id] = n;
                nodeList.Add(n);
            }
            var groupList = new List<CanvasGroup>();
            foreach (var g in groups ?? Enumerable.Empty<CanvasGroup>())
            {
                if (g == null || _infrastructure.Contains(g.Id) || _groupById.ContainsKey(g.Id) || _nodeById.ContainsKey(g.Id)) continue;
                _groupById[g.Id] = g;
                groupList.Add(g);
            }
            Nodes = nodeList;
            Groups = groupList;

            foreach (var n in nodeList)
            {
                var e = new ObjectEntry(n.Id, n.NickName, n.IsDefaultNickName, n.TypeName, isGroup: false);
                AddEntry(e);
                if (!string.IsNullOrEmpty(n.NickName)) Bucket(_byNickname, n.NickName).Add(e);
            }
            foreach (var g in groupList)
            {
                var e = new ObjectEntry(g.Id, g.NickName, false, ObjectEntry.GroupTypeName, isGroup: true);
                AddEntry(e);
                if (!string.IsNullOrEmpty(g.NickName)) Bucket(_byGroupName, g.NickName).Add(e);
            }
            _prefixes = new GuidPrefixIndex(_entries.Select(e => e.Id));

            Wires = BuildWires(nodeList);
            BuildGroupMaps(groupList);
            TopLevelGroups = FindTopLevelGroups(groupList);
            foreach (var top in TopLevelGroups)
                foreach (var m in EffectiveMembers(top.Id))
                    Bucket(_topGroupsOfNode, m).Add(top.Id);
        }

        public string DocumentName { get; }
        public SolverFlags Solver { get; }
        public CanvasDetail Detail { get; }

        /// <summary>Every node, in document order.</summary>
        public IReadOnlyList<CanvasNode> Nodes { get; }

        /// <summary>Every group, in document order.</summary>
        public IReadOnlyList<CanvasGroup> Groups { get; }

        /// <summary>Every wire between two nodes, in node order then input order.</summary>
        public IReadOnlyList<CanvasWire> Wires { get; }

        /// <summary>
        /// Groups that sit inside no other group, in document order. A group reachable only through a
        /// nesting cycle is promoted (the first such group in document order), so no group vanishes.
        /// </summary>
        public IReadOnlyList<CanvasGroup> TopLevelGroups { get; }

        public CanvasNode Node(Guid id) => _nodeById.TryGetValue(id, out var n) ? n : null;
        public CanvasGroup Group(Guid id) => _groupById.TryGetValue(id, out var g) ? g : null;

        /// <summary>Nodes wired into this one, each once, in wire order.</summary>
        public IReadOnlyList<Guid> Upstream(Guid nodeId) => _upstream.TryGetValue(nodeId, out var l) ? l : NoIds;

        /// <summary>Nodes this one feeds, each once, in wire order.</summary>
        public IReadOnlyList<Guid> Downstream(Guid nodeId) => _downstream.TryGetValue(nodeId, out var l) ? l : NoIds;

        /// <summary>Groups that directly contain this node, in document order.</summary>
        public IReadOnlyList<Guid> GroupsOf(Guid nodeId) => _groupsOfNode.TryGetValue(nodeId, out var l) ? l : NoIds;

        /// <summary>Groups that directly contain this group, in document order.</summary>
        public IReadOnlyList<Guid> ParentsOf(Guid groupId) => _parentsOfGroup.TryGetValue(groupId, out var l) ? l : NoIds;

        /// <summary>Top-level groups that contain this node directly or through nested groups.</summary>
        public IReadOnlyList<Guid> TopLevelGroupsOf(Guid nodeId) => _topGroupsOfNode.TryGetValue(nodeId, out var l) ? l : NoIds;

        /// <summary>Every node in a group, its nested groups' nodes included; safe on nesting cycles.</summary>
        public IReadOnlyCollection<Guid> EffectiveMembers(Guid groupId)
        {
            if (_effective.TryGetValue(groupId, out var cached)) return cached;

            var result = new HashSet<Guid>();
            var seen = new HashSet<Guid>();
            var stack = new Stack<Guid>();
            stack.Push(groupId);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!seen.Add(id) || !_groupById.TryGetValue(id, out var g)) continue;
                foreach (var m in g.MemberIds)
                    if (_nodeById.ContainsKey(m)) result.Add(m);
                foreach (var c in g.ChildGroupIds)
                    stack.Push(c);
            }
            _effective[groupId] = result;
            return result;
        }

        // ---- IObjectIndex ----

        public ObjectEntry Find(Guid id) => _entryById.TryGetValue(id, out var e) ? e : null;

        public IReadOnlyList<ObjectEntry> WithNickname(string nickname) =>
            nickname != null && _byNickname.TryGetValue(nickname, out var l) ? l : (IReadOnlyList<ObjectEntry>)Array.Empty<ObjectEntry>();

        public IReadOnlyList<ObjectEntry> WithGroupName(string name) =>
            name != null && _byGroupName.TryGetValue(name, out var l) ? l : (IReadOnlyList<ObjectEntry>)Array.Empty<ObjectEntry>();

        public IReadOnlyList<ObjectEntry> WithPrefix(string prefix) =>
            _prefixes.StartingWith(prefix).Select(id => _entryById[id]).ToList();

        public bool IsInfrastructure(Guid id) => _infrastructure.Contains(id);

        public IEnumerable<ObjectEntry> Entries => _entries;

        // ---- construction ----

        private void AddEntry(ObjectEntry e)
        {
            _entryById[e.Id] = e;
            _entries.Add(e);
        }

        private static List<V> Bucket<K, V>(Dictionary<K, List<V>> map, K key)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<V>();
            return list;
        }

        private static void AddOnce(Dictionary<Guid, List<Guid>> map, Guid key, Guid value)
        {
            var list = Bucket(map, key);
            if (!list.Contains(value)) list.Add(value);
        }

        private List<CanvasWire> BuildWires(List<CanvasNode> nodes)
        {
            var wires = new List<CanvasWire>();
            foreach (var n in nodes)
            {
                for (int i = 0; i < n.Inputs.Count; i++)
                {
                    foreach (var s in n.Inputs[i].Sources)
                    {
                        if (!_nodeById.ContainsKey(s.NodeId)) continue;
                        wires.Add(new CanvasWire(s.NodeId, s.OutputIndex, n.Id, i));
                        AddOnce(_upstream, n.Id, s.NodeId);
                        AddOnce(_downstream, s.NodeId, n.Id);
                    }
                }
            }
            return wires;
        }

        private void BuildGroupMaps(List<CanvasGroup> groups)
        {
            foreach (var g in groups)
            {
                foreach (var m in g.MemberIds)
                    if (_nodeById.ContainsKey(m)) AddOnce(_groupsOfNode, m, g.Id);
                foreach (var c in g.ChildGroupIds)
                    if (c != g.Id && _groupById.ContainsKey(c)) AddOnce(_parentsOfGroup, c, g.Id);
            }
        }

        private List<CanvasGroup> FindTopLevelGroups(List<CanvasGroup> groups)
        {
            var top = new List<CanvasGroup>();
            var reached = new HashSet<Guid>();

            void Reach(Guid root)
            {
                var stack = new Stack<Guid>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    var id = stack.Pop();
                    if (!reached.Add(id) || !_groupById.TryGetValue(id, out var g)) continue;
                    foreach (var c in g.ChildGroupIds)
                        if (c != id && _groupById.ContainsKey(c)) stack.Push(c);
                }
            }

            foreach (var g in groups)
            {
                if (ParentsOf(g.Id).Count > 0) continue;
                top.Add(g);
                Reach(g.Id);
            }
            // Groups only reachable through a nesting cycle: promote the first in document order.
            foreach (var g in groups)
            {
                if (reached.Contains(g.Id)) continue;
                top.Add(g);
                Reach(g.Id);
            }
            var order = new Dictionary<Guid, int>();
            for (int i = 0; i < groups.Count; i++) order[groups[i].Id] = i;
            return top.OrderBy(g => order[g.Id]).ToList();
        }
    }

    /// <summary>Host-free text helpers for values that end up in machine-facing output.</summary>
    public static class CanvasText
    {
        /// <summary>The longest panel text a snapshot keeps.</summary>
        public const int PanelTextCap = 200;

        /// <summary>A decimal in invariant culture, without trailing zeros (<c>0.5</c>, <c>10</c>, <c>-3.25</c>).</summary>
        public static string Number(decimal value) =>
            value.ToString("0.############################", CultureInfo.InvariantCulture);

        /// <summary>A double in invariant culture, round-trippable.</summary>
        public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary><paramref name="text"/> cut to <paramref name="max"/> characters; null stays null.</summary>
        public static string Cap(string text, int max, out bool truncated)
        {
            truncated = false;
            if (text == null) return null;
            if (text.Length <= max) return text;
            truncated = true;
            // Never split a surrogate pair.
            int cut = max;
            if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
            return text.Substring(0, cut);
        }
    }
}
