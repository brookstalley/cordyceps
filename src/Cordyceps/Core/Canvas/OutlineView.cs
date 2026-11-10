// Adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License
// (Core/Outline/CanvasOutline.cs).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cordyceps.Core.DataTrees;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core.Canvas
{
    /// <summary>
    /// gh_canvas(action='outline'): the canvas as a few compact lines, for orientation in one call.
    /// Three views over one <see cref="CanvasSnapshot"/>: the overview, a group's detail, and the
    /// wiring around one object. Host-free, so the grammar is pinned by unit tests.
    /// <para>The rules every line keeps:</para>
    /// <list type="bullet">
    /// <item>The first field of an object line, control line, problem line or group line is the
    /// object's or group's <see cref="DisplayNames.DisplayName"/>, printed whole: an agent pastes it
    /// back into other tools, so it must resolve to the same thing. An object line's head is
    /// <c>NAME · Type</c>, with <see cref="OutlineGrammar.Field"/> between them, so a multi-word name
    /// is never confused with a multi-word type; a name is printed only when
    /// <see cref="OutlineGrammar.IsPrintable"/> says it can be cut back out. Wherever a name that is
    /// only an id appears inside a list, its type follows it after a space (an id has no spaces), so
    /// the reader learns what it is.</item>
    /// <item>Port tokens are the port's nickname when <see cref="ParamSpecLookup"/> resolves it to
    /// the same port, else its name, else its 0-based index.</item>
    /// <item>A count prints bare only when the solver is on and its owner computed; otherwise it says
    /// <c>stale</c>, or <c>disabled</c> for a locked owner. Agents act on these numbers.</item>
    /// <item>Group membership is a multimap: an object in two groups is tagged with both, and every
    /// group it sits in counts it.</item>
    /// <item>Problems use <see cref="ObjectHealth"/>, the rule <c>status</c> uses, over the same
    /// objects (<see cref="ObjectHealth.IsSubject(CanvasNode)"/>), and are capped; outline orients.
    /// <c>status</c> lists every one of them.</item>
    /// <item>Wires the reader could not place are counted in <c>unread</c>, never left silent.</item>
    /// <item>Absent things print nothing.</item>
    /// </list>
    /// </summary>
    public static class OutlineView
    {
        public const int MaxGroupLines = 60;
        public const int MaxObjectLines = 150;
        public const int MaxAroundObjects = 60;
        public const int MaxControls = 20;
        public const int MaxProblems = 10;
        public const int MaxDisabled = 20;
        public const int MaxNetworks = 10;
        public const int MaxNames = 8;
        public const int MaxOutEntries = 6;
        public const int MaxDestinations = 4;
        public const int MaxSourcesPerInput = 8;
        public const int MaxDetailEdges = 16;

        public const int ValueClip = 30;
        public const int MessageClip = 100;
        public const int PanelClip = 30;

        public const string StaleNote = "counts are from the last solution and may be out of date";

        /// <summary>The <c>unread</c> statement for <paramref name="count"/> wires the reader could not place; null for none.</summary>
        public static string UnreadText(int count) => count <= 0
            ? null
            : $"{I(count)} wire(s) could not be read, so some inputs may show as unfed; gh_inspect(action='log') names them";

        // ---- overview -------------------------------------------------------------------------------

        public static JObject Overview(CanvasSnapshot snapshot)
        {
            var v = new View(snapshot);
            var s = snapshot;
            var o = new JObject { ["success"] = true };
            if (!string.IsNullOrEmpty(s.DocumentName)) o["document"] = s.DocumentName;
            o["summary"] = v.Summary();
            v.AddSolver(o, always: true);
            v.AddUnread(o);
            var more = new JObject();

            var controls = v.ByPosition(s.Nodes.Where(n => n.IsControl)).ToList();
            if (controls.Count > 0)
                o["controls"] = Capped(controls.Select(n => v.Head(n, Guid.Empty)), MaxControls, more, "controls");

            if (s.Groups.Count > 0)
            {
                var order = v.GroupOrder();
                o["groups"] = Capped(order.Select(g => v.GroupLine(g, top: true)), MaxGroupLines, more, "groups");
                var loose = s.Nodes.Where(n => s.GroupsOf(n.Id).Count == 0).Select(n => n.Id).ToList();
                if (loose.Count > 0)
                    o["ungrouped"] = v.UngroupedText(loose);
            }
            else
            {
                v.AddNetworks(o, more);
            }

            var problems = v.Problems();
            if (problems.Count > 0) o["problems"] = Capped(problems, MaxProblems, more, "problems");

            var disabled = v.ByPosition(s.Nodes.Where(n => n.Locked)).Select(n => v.Ref(n.Id)).ToList();
            if (disabled.Count > 0) o["disabled"] = Capped(disabled, MaxDisabled, more, "disabled");

            if (more.Count > 0) o["more"] = more;

            var next = s.Groups.Count > 0
                ? "group='NAME' shows how one group is wired inside (a JSON list for several); id='NAME' shows what feeds one object and what it feeds (depth=N wires each way, default 2)"
                : "id='NAME' shows what feeds one object and what it feeds (depth=N wires each way, default 2)";
            if (more["problems"] != null)
                next += "; problems are capped: gh_inspect(action='status') lists every object's status, parameter objects included";
            o["next"] = next;
            return o;
        }

        // ---- group detail ---------------------------------------------------------------------------

        /// <summary>
        /// The detail of each group in <paramref name="groupIds"/>, in that order, sharing one budget of
        /// <see cref="MaxObjectLines"/> lines.
        /// </summary>
        public static JObject Groups(CanvasSnapshot snapshot, IReadOnlyList<Guid> groupIds)
        {
            var v = new View(snapshot);
            int budget = MaxObjectLines;
            var more = new JObject();
            var list = new JArray();
            foreach (var id in groupIds ?? Array.Empty<Guid>())
            {
                var g = snapshot.Group(id);
                if (g != null) list.Add(v.GroupDetail(g, ref budget, more));
            }
            var o = new JObject { ["success"] = true };
            v.AddSolver(o, always: false);
            v.AddUnread(o);
            o["groups"] = list;
            if (more.Count > 0) o["more"] = more;
            return o;
        }

        // ---- around ---------------------------------------------------------------------------------

        /// <summary>What feeds one object and what it feeds, up to <paramref name="depth"/> wires each way.</summary>
        public static JObject Around(CanvasSnapshot snapshot, Guid centreId, int depth)
        {
            var s = snapshot;
            var v = new View(s);
            var centre = s.Node(centreId) ?? throw new ArgumentException("not a node in this snapshot", nameof(centreId));

            var distance = new Dictionary<Guid, int> { [centre.Id] = 0 };
            Walk(centre.Id, depth, s.Upstream, distance);
            Walk(centre.Id, depth, s.Downstream, distance);

            var taken = distance
                .OrderBy(kv => kv.Value)
                .ThenBy(kv => s.Node(kv.Key).X).ThenBy(kv => s.Node(kv.Key).Y).ThenBy(kv => v.DocIndex(kv.Key))
                .Take(MaxAroundObjects).Select(kv => kv.Key).ToList();
            var set = new HashSet<Guid>(taken);

            var edges = s.Wires.Where(w => set.Contains(w.SourceId) && set.Contains(w.TargetId))
                .Select(w => (w.SourceId, w.TargetId));
            var order = FlowOrder(taken, edges, v.NodePosition);

            var o = new JObject { ["success"] = true };
            v.AddSolver(o, always: false);
            v.AddUnread(o);
            o["around"] = v.Name(centre.Id);
            o["depth"] = depth;
            o["objects"] = new JArray(order.Select(id => v.ObjectLine(s.Node(id), set, Guid.Empty)));

            var more = new JObject();
            var fedFrom = taken.SelectMany(id => s.Upstream(id)).Where(u => !set.Contains(u)).Distinct()
                .Select(u => v.Ref(u) + v.GroupTag(u, Guid.Empty)).ToList();
            if (fedFrom.Count > 0) o["fedFrom"] = Capped(fedFrom, MaxNames, more, "fedFrom");
            var feeds = taken.SelectMany(id => s.Downstream(id)).Where(d => !set.Contains(d)).Distinct()
                .Select(d => v.Ref(d) + v.GroupTag(d, Guid.Empty)).ToList();
            if (feeds.Count > 0) o["feeds"] = Capped(feeds, MaxNames, more, "feeds");
            if (distance.Count > taken.Count) more["objects"] = distance.Count - taken.Count;
            if (more.Count > 0) o["more"] = more;
            return o;
        }

        private static void Walk(Guid start, int depth, Func<Guid, IReadOnlyList<Guid>> next, Dictionary<Guid, int> distance)
        {
            var frontier = new List<Guid> { start };
            var seen = new HashSet<Guid> { start };
            for (int d = 1; d <= depth && frontier.Count > 0; d++)
            {
                var nextFrontier = new List<Guid>();
                foreach (var id in frontier)
                {
                    foreach (var n in next(id))
                    {
                        if (!seen.Add(n)) continue;
                        nextFrontier.Add(n);
                        if (!distance.TryGetValue(n, out var old) || d < old) distance[n] = d;
                    }
                }
                frontier = nextFrontier;
            }
        }

        // ---- ordering -------------------------------------------------------------------------------

        /// <summary>Counts the comparisons <see cref="FlowOrder"/> makes, so its cost can be asserted without a clock.</summary>
        public sealed class OrderCounter
        {
            public long Comparisons { get; internal set; }
        }

        /// <summary>
        /// Upstream first: an id comes after every id with an edge into it. Ids on a cycle (a
        /// strongly connected set) are kept together as one unit, ordered among themselves by
        /// position, and the unit is placed like any other id, so whatever the cycle feeds still
        /// comes after it. Among units that are ready, the one whose leftmost, then topmost, member
        /// comes first goes first; ties go to the earliest in <paramref name="ids"/>. Tarjan's
        /// algorithm, then Kahn's over a priority queue: O((V + E) log V).
        /// </summary>
        public static List<Guid> FlowOrder(
            IReadOnlyList<Guid> ids,
            IEnumerable<(Guid From, Guid To)> edges,
            Func<Guid, (float X, float Y)> position,
            OrderCounter counter = null)
        {
            var index = new Dictionary<Guid, int>();
            var list = new List<Guid>();
            foreach (var id in ids)
            {
                if (index.ContainsKey(id)) continue;
                index[id] = list.Count;
                list.Add(id);
            }
            int n = list.Count;
            var pos = list.Select(position).ToArray();
            var successors = new List<int>[n];
            var seenEdges = new HashSet<(int, int)>();
            var edgeList = new List<(int, int)>();
            foreach (var (from, to) in edges ?? Enumerable.Empty<(Guid, Guid)>())
            {
                if (!index.TryGetValue(from, out var a) || !index.TryGetValue(to, out var b) || a == b) continue;
                if (!seenEdges.Add((a, b))) continue;
                (successors[a] ??= new List<int>()).Add(b);
                edgeList.Add((a, b));
            }

            var comparer = Comparer<int>.Create((x, y) =>
            {
                if (counter != null) counter.Comparisons++;
                int c = pos[x].X.CompareTo(pos[y].X);
                if (c != 0) return c;
                c = pos[x].Y.CompareTo(pos[y].Y);
                return c != 0 ? c : x.CompareTo(y);
            });

            // Strongly connected components, iteratively (a long chain must not overflow the stack).
            var component = StronglyConnected(n, successors, out int componentCount);
            var members = new List<int>[componentCount];
            for (int i = 0; i < n; i++) (members[component[i]] ??= new List<int>()).Add(i);
            foreach (var m in members) if (m.Count > 1) m.Sort(comparer);

            var indegree = new int[componentCount];
            var next = new List<int>[componentCount];
            var seenComponentEdges = new HashSet<(int, int)>();
            foreach (var (a, b) in edgeList)
            {
                int ca = component[a], cb = component[b];
                if (ca == cb || !seenComponentEdges.Add((ca, cb))) continue;
                (next[ca] ??= new List<int>()).Add(cb);
                indegree[cb]++;
            }

            // Each component is queued under its first member, so one comparer orders both.
            var ready = new SortedSet<int>(comparer);
            for (int c = 0; c < componentCount; c++)
                if (indegree[c] == 0) ready.Add(members[c][0]);

            var result = new List<Guid>(n);
            while (ready.Count > 0)
            {
                var first = ready.Min;
                ready.Remove(first);
                int c = component[first];
                foreach (var m in members[c]) result.Add(list[m]);
                if (next[c] == null) continue;
                foreach (var d in next[c])
                    if (--indegree[d] == 0) ready.Add(members[d][0]);
            }
            return result;
        }

        /// <summary>Tarjan's strongly connected components; returns each node's component number.</summary>
        private static int[] StronglyConnected(int n, List<int>[] successors, out int count)
        {
            var component = new int[n];
            var order = new int[n];
            var low = new int[n];
            var onStack = new bool[n];
            for (int i = 0; i < n; i++) order[i] = -1;
            var stack = new Stack<int>();
            var work = new Stack<(int Node, int Edge)>();
            int counter = 0;
            count = 0;

            for (int start = 0; start < n; start++)
            {
                if (order[start] != -1) continue;
                order[start] = low[start] = counter++;
                stack.Push(start);
                onStack[start] = true;
                work.Push((start, 0));
                while (work.Count > 0)
                {
                    var (v, e) = work.Pop();
                    var succ = successors[v];
                    if (succ != null && e < succ.Count)
                    {
                        work.Push((v, e + 1));
                        int w = succ[e];
                        if (order[w] == -1)
                        {
                            order[w] = low[w] = counter++;
                            stack.Push(w);
                            onStack[w] = true;
                            work.Push((w, 0));
                        }
                        else if (onStack[w])
                        {
                            low[v] = Math.Min(low[v], order[w]);
                        }
                        continue;
                    }

                    if (low[v] == order[v])
                    {
                        int w;
                        do
                        {
                            w = stack.Pop();
                            onStack[w] = false;
                            component[w] = count;
                        } while (w != v);
                        count++;
                    }
                    if (work.Count > 0)
                    {
                        int parent = work.Peek().Node;
                        low[parent] = Math.Min(low[parent], low[v]);
                    }
                }
            }
            return component;
        }

        // ---- text helpers ---------------------------------------------------------------------------

        /// <summary><paramref name="lines"/> up to <paramref name="max"/>, then <c>+N more</c> as a last line; N is also recorded in <paramref name="more"/>.</summary>
        private static JArray Capped(IEnumerable<string> lines, int max, JObject more, string key)
        {
            var list = lines.ToList();
            var array = new JArray(list.Take(max));
            if (list.Count > max)
            {
                int left = list.Count - max;
                array.Add($"+{I(left)} more");
                more[key] = (more[key]?.Value<int>() ?? 0) + left;
            }
            return array;
        }

        /// <summary>Items joined by <paramref name="separator"/>, ending <c>+N more</c> when there are more than <paramref name="max"/>.</summary>
        internal static string Join(IEnumerable<string> items, int max, string separator = OutlineGrammar.Item)
        {
            var list = items.ToList();
            var text = string.Join(separator, list.Take(max));
            return list.Count > max ? $"{text}{separator}+{I(list.Count - max)} more" : text;
        }

        internal static string Count(int n, string noun) => $"{I(n)} {noun}{(n == 1 ? "" : "s")}";

        internal static string I(int n) => n.ToString(CultureInfo.InvariantCulture);

        /// <summary>At most <paramref name="max"/> characters, the last an ellipsis when clipped; never splits a surrogate pair.</summary>
        internal static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            int cut = max - 1;
            if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut).TrimEnd() + "…";
        }

        /// <summary>Lines joined with <c> / </c>, each trimmed, blanks dropped.</summary>
        internal static string OneLine(string s) =>
            string.Join(" / ", (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

        /// <summary>
        /// A port's token: its nickname when the parameter-spec rule reads that back as this port, else
        /// its name, else its 0-based index (two ports can share both).
        /// </summary>
        public static string PortToken(IReadOnlyList<CanvasPort> ports, int index, bool isInput)
        {
            var identities = ports.Select(p => new ParamIdentity(p.Name, p.NickName)).ToList();
            var p0 = ports[index];
            foreach (var spelling in new[] { p0.NickName, p0.Name })
            {
                if (string.IsNullOrWhiteSpace(spelling) || spelling != spelling.Trim()) continue;
                var r = ParamSpecLookup.Resolve(spelling, identities, isInput, string.Empty);
                if (r.IsResolved && r.Index == index) return spelling;
            }
            return I(index);
        }

        // ---- the per-call view ----------------------------------------------------------------------

        /// <summary>One render over one snapshot: caches names, health and wire maps.</summary>
        private sealed class View
        {
            private readonly CanvasSnapshot _s;
            private readonly DisplayNames _names;
            private readonly Dictionary<Guid, HealthResult> _health = new Dictionary<Guid, HealthResult>();
            private readonly Dictionary<Guid, int> _docIndex = new Dictionary<Guid, int>();
            private readonly Dictionary<(Guid, int), List<Guid>> _recipients = new Dictionary<(Guid, int), List<Guid>>();

            public View(CanvasSnapshot s)
            {
                _s = s;
                _names = new DisplayNames(s);
                for (int i = 0; i < s.Nodes.Count; i++) _docIndex[s.Nodes[i].Id] = i;
                for (int i = 0; i < s.Groups.Count; i++) _docIndex[s.Groups[i].Id] = s.Nodes.Count + i;
                foreach (var w in s.Wires)
                {
                    var key = (w.SourceId, w.SourceOutput < 0 ? 0 : w.SourceOutput);
                    if (!_recipients.TryGetValue(key, out var l)) _recipients[key] = l = new List<Guid>();
                    if (!l.Contains(w.TargetId)) l.Add(w.TargetId);
                }
            }

            public int DocIndex(Guid id) => _docIndex.TryGetValue(id, out var i) ? i : int.MaxValue;

            public (float X, float Y) NodePosition(Guid id)
            {
                var n = _s.Node(id);
                if (n != null) return (n.X, n.Y);
                var g = _s.Group(id);
                return g != null ? (g.X, g.Y) : (0f, 0f);
            }

            public IEnumerable<CanvasNode> ByPosition(IEnumerable<CanvasNode> nodes) =>
                nodes.OrderBy(n => n.X).ThenBy(n => n.Y).ThenBy(n => DocIndex(n.Id));

            public HealthResult Health(CanvasNode n)
            {
                if (!_health.TryGetValue(n.Id, out var h)) _health[n.Id] = h = ObjectHealth.Classify(n);
                return h;
            }

            // ---- names ----

            /// <summary>The DisplayName: what the first token of a line is, and what resolves back.</summary>
            public string Name(Guid id) => _names.DisplayName(id);

            /// <summary>A name inside a list: the DisplayName, followed by the type when the name is only an id.</summary>
            public string Ref(Guid id)
            {
                var name = Name(id);
                var n = _s.Node(id);
                return n != null && name == _names.DisplayId(id) ? $"{name} {n.TypeName}" : name;
            }

            /// <summary><c> [A, B]</c>: every group the object sits in directly, in document order, except <paramref name="except"/>.</summary>
            public string GroupTag(Guid nodeId, Guid except)
            {
                var groups = _s.GroupsOf(nodeId).Where(g => g != except).Select(Name).ToList();
                return groups.Count == 0 ? "" : OutlineGrammar.Tags + string.Join(OutlineGrammar.Item, groups) + OutlineGrammar.TagsClose;
            }

            // ---- counts ----

            public bool CountsCurrent(CanvasNode n) => _s.Solver.IsOn && n.Phase == NodePhase.Computed;

            public string CountText(CanvasNode n, CanvasPort p)
            {
                if (n.Locked) return "disabled";
                return CountsCurrent(n) ? I(p.VolatileCount) : $"{I(p.VolatileCount)} stale";
            }

            /// <summary><c>NAME (PORT: COUNT)</c>, or <c>NAME (COUNT)</c> for an object with one output.</summary>
            public string OutputRef(CanvasNode n, int output)
            {
                var count = CountText(n, n.Outputs[output]);
                return n.Outputs.Count > 1
                    ? $"{Ref(n.Id)} ({PortToken(n.Outputs, output, false)}: {count})"
                    : $"{Ref(n.Id)} ({count})";
            }

            /// <summary><c>NAME (A: 3, B: 4)</c>: every output's count, or <c>NAME (3)</c> for one output.</summary>
            public string EndRef(CanvasNode n)
            {
                if (n.Outputs.Count == 1) return $"{Ref(n.Id)} ({CountText(n, n.Outputs[0])})";
                var parts = Enumerable.Range(0, n.Outputs.Count).Select(i => $"{PortToken(n.Outputs, i, false)}: {CountText(n, n.Outputs[i])}");
                return $"{Ref(n.Id)} ({string.Join(OutlineGrammar.Item, parts)})";
            }

            // ---- summary and solver ----

            public string Summary()
            {
                var nodes = _s.Nodes;
                var subjects = nodes.Where(ObjectHealth.IsSubject).ToList();
                int errors = subjects.Count(n => Health(n).Status == HealthStatus.Error);
                int warnings = subjects.Count(n => Health(n).Status == HealthStatus.Warning);
                int disconnected = subjects.Count(n => Health(n).Status == HealthStatus.Disconnected);
                int disabled = nodes.Count(n => n.Locked);
                int notSolved = nodes.Count(n => IsSolvable(n) && !n.Locked && n.Phase != NodePhase.Computed);
                int previewOff = nodes.Count(n => n.PreviewHidden == true);
                return string.Join(OutlineGrammar.Field,
                    Count(nodes.Count, "object"),
                    Count(_s.Groups.Count, "group"),
                    Count(errors, "error"),
                    Count(warnings, "warning"),
                    $"{I(disconnected)} disconnected",
                    $"{I(disabled)} disabled",
                    $"{I(notSolved)} not solved",
                    $"{I(previewOff)} preview off");
            }

            /// <summary>Scribbles and sketches never compute, so they are never "not solved".</summary>
            private static bool IsSolvable(CanvasNode n) => n.Kind != NodeKind.Other;

            /// <summary>
            /// <c>solver</c>, and the stale-counts <c>note</c> when the solver is off or the last solve
            /// was aborted. The overview always prints <c>solver</c>; the other views only when it is not plainly on.
            /// </summary>
            public void AddSolver(JObject o, bool always)
            {
                var f = _s.Solver;
                var off = TreeStatusText.SolverOff(f.DocumentEnabled, f.SolutionsEnabled, f.LastSolutionAborted);
                string text = off?.Text ?? (f.LastSolutionAborted ? "on; the last solution was aborted" : "on");
                bool plain = off == null && !f.LastSolutionAborted;
                if (always || !plain) o["solver"] = text;
                if (!plain) o["note"] = StaleNote;
            }

            /// <summary>
            /// <c>unread</c>, only when the reader could not place some wires: those inputs would
            /// otherwise read as unfed under a plain success.
            /// </summary>
            public void AddUnread(JObject o)
            {
                var text = UnreadText(_s.UnreadWires);
                if (text != null) o["unread"] = text;
            }

            // ---- lines ----

            /// <summary><c>NAME · Type[ = VALUE][ [GROUPS]]</c>: the name is the first field.</summary>
            public string Head(CanvasNode n, Guid exceptGroup)
            {
                var head = Name(n.Id) + OutlineGrammar.Field + n.TypeName;
                var value = Value(n);
                if (value != null) head += OutlineGrammar.Value + value;
                return head + GroupTag(n.Id, exceptGroup);
            }

            /// <summary>A control's value (a slider's with its range) or an unwired panel's text; null otherwise.</summary>
            public static string Value(CanvasNode n)
            {
                if (n.Kind == NodeKind.Panel)
                {
                    if (n.PanelText == null) return null;
                    var text = OneLine(n.PanelText);
                    var clipped = Clip(text, PanelClip);
                    if (n.PanelTextTruncated && clipped == text) clipped = Clip(text + "…", PanelClip);
                    return $"\"{clipped}\"";
                }
                if (!n.IsControl || n.ValueText == null) return null;
                var v = Clip(OneLine(n.ValueText), ValueClip);
                if (n.Kind == NodeKind.Slider && n.SliderMin != null && n.SliderMax != null)
                    v += $" ({n.SliderMin}..{n.SliderMax})";
                return v;
            }

            /// <summary>
            /// <c>NAME · Type[ = VALUE][ [GROUPS]][ · disabled][ · preview off][ · PORT[ (mods)] ← SOURCE[ (PORT)][ [GROUPS]], …][ · health]</c>.
            /// Sources outside <paramref name="shown"/> carry their groups.
            /// </summary>
            public string ObjectLine(CanvasNode n, IReadOnlyCollection<Guid> shown, Guid exceptGroup)
            {
                var parts = new List<string> { Head(n, exceptGroup) };
                if (n.Locked) parts.Add("disabled");
                if (n.PreviewHidden == true) parts.Add("preview off");

                bool own = n.IsParameterObject;
                for (int i = 0; i < n.Inputs.Count; i++)
                {
                    var input = n.Inputs[i];
                    var mods = input.ModifierWords;
                    var label = own ? "" : PortToken(n.Inputs, i, true);
                    if (mods.Count > 0) label = (label + $" ({string.Join(OutlineGrammar.Item, mods)})").TrimStart();
                    if (input.Sources.Count > 0)
                    {
                        var refs = input.Sources.Select(src => SourceRef(src, shown)).Distinct();
                        var arrow = "← " + Join(refs, MaxSourcesPerInput);
                        parts.Add(label.Length > 0 ? $"{label} {arrow}" : arrow);
                    }
                    else if (mods.Count > 0)
                    {
                        parts.Add(label);
                    }
                }

                var health = HealthText(n);
                if (health != null) parts.Add(health);
                return string.Join(OutlineGrammar.Field, parts);
            }

            /// <summary><c>SOURCE[ (PORT)][ [GROUPS]]</c>: the port only when the source has several outputs.</summary>
            public string SourceRef(PortSource src, IReadOnlyCollection<Guid> shown)
            {
                var n = _s.Node(src.NodeId);
                if (n == null) return "?";
                var text = Ref(n.Id);
                if (n.Outputs.Count > 1 && src.OutputIndex >= 0 && src.OutputIndex < n.Outputs.Count)
                    text += $" ({PortToken(n.Outputs, src.OutputIndex, false)})";
                if (shown == null || !shown.Contains(n.Id)) text += GroupTag(n.Id, Guid.Empty);
                return text;
            }

            /// <summary><c>error: MSG[ (+K more)]</c>, <c>warning: …</c>, <c>disconnected: missing PORT, …</c>; null when OK.</summary>
            public string HealthText(CanvasNode n)
            {
                var h = Health(n);
                switch (h.Status)
                {
                    case HealthStatus.Error: return Messages("error", h.Messages);
                    case HealthStatus.Warning: return Messages("warning", h.Messages);
                    case HealthStatus.Disconnected: return "disconnected: missing " + string.Join(OutlineGrammar.Item, MissingTokens(n, h));
                    default: return null;
                }
            }

            private static string Messages(string level, IReadOnlyList<string> messages)
            {
                var list = messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
                if (list.Count == 0) return level;
                var text = $"{level}: {Clip(OneLine(list[0]), MessageClip)}";
                return list.Count > 1 ? $"{text} (+{I(list.Count - 1)} more)" : text;
            }

            /// <summary>The missing inputs as port tokens, matching each name the health rule gave to the next input carrying it.</summary>
            private static IEnumerable<string> MissingTokens(CanvasNode n, HealthResult h)
            {
                var used = new HashSet<int>();
                foreach (var name in h.MissingInputs)
                {
                    int i = Enumerable.Range(0, n.Inputs.Count).FirstOrDefault(k => !used.Contains(k) && n.Inputs[k].Name == name, -1);
                    if (i < 0) { yield return name; continue; }
                    used.Add(i);
                    yield return PortToken(n.Inputs, i, true);
                }
            }

            /// <summary>
            /// <c>NAME · Type[ [GROUPS]] · HEALTH</c> for every object with a problem, errors then
            /// warnings then disconnected, each left to right.
            /// </summary>
            public List<string> Problems()
            {
                return _s.Nodes
                    .Where(ObjectHealth.IsSubject)
                    .Select(n => (Node: n, Health: Health(n)))
                    .Where(x => x.Health.Status != HealthStatus.Ok)
                    .OrderBy(x => Rank(x.Health.Status))
                    .ThenBy(x => x.Node.X).ThenBy(x => x.Node.Y).ThenBy(x => DocIndex(x.Node.Id))
                    .Select(x => Name(x.Node.Id) + OutlineGrammar.Field + x.Node.TypeName + GroupTag(x.Node.Id, Guid.Empty)
                                 + OutlineGrammar.Field + HealthText(x.Node))
                    .ToList();
            }

            private static int Rank(HealthStatus s) => s == HealthStatus.Error ? 0 : s == HealthStatus.Warning ? 1 : 2;

            // ---- groups ----

            /// <summary>
            /// Top-level groups in data-flow order. Each wire u→v gives an edge from every top-level
            /// group of u to every other top-level group of v, so overlapping groups keep all their edges.
            /// </summary>
            public List<CanvasGroup> GroupOrder()
            {
                var top = _s.TopLevelGroups.Select(g => g.Id).ToList();
                var edges = new List<(Guid, Guid)>();
                foreach (var w in _s.Wires)
                    foreach (var a in _s.TopLevelGroupsOf(w.SourceId))
                        foreach (var b in _s.TopLevelGroupsOf(w.TargetId))
                            if (a != b) edges.Add((a, b));
                return FlowOrder(top, edges, NodePosition).Select(id => _s.Group(id)).ToList();
            }

            private void AddProblemCounts(IEnumerable<Guid> members, List<string> parts, bool withState)
            {
                var nodes = members.Select(_s.Node).Where(n => n != null).ToList();
                int errors = nodes.Count(n => Health(n).Status == HealthStatus.Error);
                int warnings = nodes.Count(n => Health(n).Status == HealthStatus.Warning);
                int disconnected = nodes.Count(n => Health(n).Status == HealthStatus.Disconnected);
                if (errors > 0) parts.Add(Count(errors, "error"));
                if (warnings > 0) parts.Add(Count(warnings, "warning"));
                if (disconnected > 0) parts.Add($"{I(disconnected)} disconnected");
                if (!withState) return;
                int disabled = nodes.Count(n => n.Locked);
                if (disabled > 0) parts.Add($"{I(disabled)} disabled");
            }

            /// <summary>
            /// <c>GROUP · N objects[ · problems][ · contains: …][ · shares K objects with …][ · in ← …][ · out: …]</c>
            /// (overview), or <c>GROUP · group · N objects[ · in ← …][ · out: …]</c> for a child line
            /// (the name first, then what it is, as on an object line).
            /// </summary>
            public string GroupLine(CanvasGroup g, bool top)
            {
                var members = _s.EffectiveMembers(g.Id);
                var parts = new List<string> { Name(g.Id) };
                if (!top) parts.Add("group");
                parts.Add(Count(members.Count, "object"));
                if (top)
                {
                    AddProblemCounts(members, parts, withState: false);

                    var children = g.ChildGroupIds.Select(_s.Group).Where(c => c != null).ToList();
                    if (children.Count > 0)
                        parts.Add("contains: " + Join(children.Select(c => $"{Name(c.Id)} ({Count(_s.EffectiveMembers(c.Id).Count, "object")})"), MaxNames));

                    var shares = new List<string>();
                    foreach (var other in _s.TopLevelGroups)
                    {
                        if (other.Id == g.Id) continue;
                        var otherMembers = _s.EffectiveMembers(other.Id);
                        int k = members.Count(otherMembers.Contains);
                        if (k > 0) shares.Add($"{Count(k, "object")} with {Name(other.Id)}");
                    }
                    if (shares.Count > 0) parts.Add("shares " + Join(shares, MaxNames));
                }

                var inbound = Inbound(members, withGroups: false);
                if (inbound.Count > 0) parts.Add("in ← " + Join(inbound, MaxNames));
                var outbound = Outbound(members);
                if (outbound.Count > 0) parts.Add("out: " + Join(outbound, MaxOutEntries, OutlineGrammar.Entry));
                return string.Join(OutlineGrammar.Field, parts);
            }

            /// <summary>The ungrouped objects: <c>N objects · inputs: … · ends: … · feeds: GROUP, …</c>.</summary>
            public string UngroupedText(IReadOnlyCollection<Guid> ids)
            {
                var set = new HashSet<Guid>(ids);
                var nodes = ByPosition(ids.Select(_s.Node)).ToList();
                var parts = new List<string> { Count(nodes.Count, "object") };
                var inputs = nodes.Where(IsInput).ToList();
                var ends = nodes.Where(n => !IsInput(n) && IsEnd(n)).ToList();
                if (inputs.Count > 0) parts.Add("inputs: " + Join(inputs.Select(InputRef), MaxNames));
                if (ends.Count > 0) parts.Add("ends: " + Join(ends.Select(EndRef), MaxNames));
                var feeds = nodes.SelectMany(n => _s.Downstream(n.Id)).Where(r => !set.Contains(r))
                    .SelectMany(r => _s.TopLevelGroupsOf(r)).Distinct().Select(Name).ToList();
                if (feeds.Count > 0) parts.Add("feeds: " + Join(feeds, MaxNames));
                return string.Join(OutlineGrammar.Field, parts);
            }

            /// <summary>Where data enters: a control, or an unwired panel with text, that nothing feeds.</summary>
            private bool IsInput(CanvasNode n) =>
                _s.Upstream(n.Id).Count == 0 && (n.IsControl || (n.Kind == NodeKind.Panel && n.PanelText != null));

            /// <summary>Where data leaves: an object with outputs that feeds nothing.</summary>
            private bool IsEnd(CanvasNode n) => n.Outputs.Count > 0 && _s.Downstream(n.Id).Count == 0;

            private string InputRef(CanvasNode n)
            {
                var value = Value(n);
                return value == null ? Ref(n.Id) : $"{Ref(n.Id)} = {value}";
            }

            /// <summary>Weakly connected sets of wired objects, largest first, and the objects with no wire at all.</summary>
            public void AddNetworks(JObject o, JObject more)
            {
                var wired = _s.Nodes.Where(n => _s.Upstream(n.Id).Count > 0 || _s.Downstream(n.Id).Count > 0).Select(n => n.Id).ToList();
                var alone = _s.Nodes.Where(n => _s.Upstream(n.Id).Count == 0 && _s.Downstream(n.Id).Count == 0).ToList();

                var networks = Components(wired).OrderByDescending(c => c.Count).ToList();
                if (networks.Count > 0)
                {
                    var lines = networks.Select((c, i) => (networks.Count == 1 ? "network · " : $"network {I(i + 1)} · ") + NetworkText(c));
                    o["networks"] = Capped(lines, MaxNetworks, more, "networks");
                }
                if (alone.Count > 0)
                    o["unconnected"] = Count(alone.Count, "object") + ": " + Join(ByPosition(alone).Select(n => Ref(n.Id)), MaxNames);
            }

            private string NetworkText(IReadOnlyCollection<Guid> ids)
            {
                var nodes = ByPosition(ids.Select(_s.Node)).ToList();
                var parts = new List<string> { Count(nodes.Count, "object") };
                var inputs = nodes.Where(IsInput).ToList();
                var ends = nodes.Where(n => !IsInput(n) && IsEnd(n)).ToList();
                var shown = new HashSet<Guid>(inputs.Concat(ends).Select(n => n.Id));
                var named = nodes.Where(n => !shown.Contains(n.Id) && IsNamed(n)).ToList();
                if (inputs.Count > 0) parts.Add("inputs: " + Join(inputs.Select(InputRef), MaxNames));
                if (named.Count > 0) parts.Add("named: " + Join(named.Select(n => Name(n.Id)), MaxNames));
                if (ends.Count > 0) parts.Add("ends: " + Join(ends.Select(EndRef), MaxNames));
                return string.Join(OutlineGrammar.Field, parts);
            }

            /// <summary>The user gave it a nickname, and that nickname is what prints.</summary>
            private bool IsNamed(CanvasNode n) => !n.IsDefaultNickName && Name(n.Id) == n.NickName;

            private List<List<Guid>> Components(List<Guid> ids)
            {
                var set = new HashSet<Guid>(ids);
                var seen = new HashSet<Guid>();
                var result = new List<List<Guid>>();
                foreach (var start in ids)
                {
                    if (!seen.Add(start)) continue;
                    var part = new List<Guid>();
                    var stack = new Stack<Guid>();
                    stack.Push(start);
                    while (stack.Count > 0)
                    {
                        var id = stack.Pop();
                        part.Add(id);
                        foreach (var next in _s.Upstream(id).Concat(_s.Downstream(id)))
                            if (set.Contains(next) && seen.Add(next)) stack.Push(next);
                    }
                    result.Add(part);
                }
                return result;
            }

            /// <summary>Distinct sources outside <paramref name="members"/> wired into them, members left to right.</summary>
            private List<string> Inbound(IReadOnlyCollection<Guid> members, bool withGroups)
            {
                var result = new List<string>();
                foreach (var n in ByPosition(members.Select(_s.Node).Where(x => x != null)))
                {
                    foreach (var input in n.Inputs)
                    {
                        foreach (var src in input.Sources)
                        {
                            if (members.Contains(src.NodeId)) continue;
                            var text = withGroups ? SourceRef(src, null) : Ref(src.NodeId);
                            if (!result.Contains(text)) result.Add(text);
                        }
                    }
                }
                return result;
            }

            /// <summary>
            /// <c>NAME (PORT: COUNT) → DEST, …</c> for each output of a member that feeds something
            /// outside. Each destination is every top-level group of a recipient, or the recipient's
            /// name when it sits in no group.
            /// </summary>
            private List<string> Outbound(IReadOnlyCollection<Guid> members)
            {
                var result = new List<string>();
                foreach (var n in ByPosition(members.Select(_s.Node).Where(x => x != null)))
                {
                    for (int i = 0; i < n.Outputs.Count; i++)
                    {
                        if (!_recipients.TryGetValue((n.Id, i), out var recipients)) continue;
                        var outside = recipients.Where(r => !members.Contains(r)).ToList();
                        if (outside.Count == 0) continue;
                        var dests = new List<string>();
                        foreach (var r in outside)
                        {
                            var tops = _s.TopLevelGroupsOf(r);
                            foreach (var d in tops.Count > 0 ? tops.Select(Name) : new[] { Ref(r) })
                                if (!dests.Contains(d)) dests.Add(d);
                        }
                        result.Add($"{OutputRef(n, i)} → {Join(dests, MaxDestinations)}");
                    }
                }
                return result;
            }

            /// <summary>
            /// One group's detail. Lines are units: each direct member, and each child group as one
            /// line. A direct member that is also in a child keeps its own line and is counted in the
            /// child's. Units run upstream first.
            /// </summary>
            public JObject GroupDetail(CanvasGroup g, ref int budget, JObject more)
            {
                var members = _s.EffectiveMembers(g.Id);
                var o = new JObject { ["group"] = Name(g.Id), ["id"] = _names.DisplayId(g.Id) };

                var summary = new List<string> { Count(members.Count, "object") };
                var parents = _s.ParentsOf(g.Id);
                if (parents.Count > 0) summary.Add("inside " + string.Join(OutlineGrammar.Item, parents.Select(Name)));
                AddProblemCounts(members, summary, withState: true);
                o["summary"] = string.Join(OutlineGrammar.Field, summary);

                var inbound = Inbound(members, withGroups: true);
                if (inbound.Count > 0) o["in"] = Capped(inbound, MaxDetailEdges, more, "in");

                var children = g.ChildGroupIds.Select(_s.Group).Where(c => c != null && c.Id != g.Id).ToList();
                var direct = g.MemberIds.Where(id => _s.Node(id) != null).Distinct().ToList();
                var units = new List<Guid>(direct);
                units.AddRange(children.Select(c => c.Id));

                // A node's units: itself when it is a direct member, and every child that contains it.
                var unitsOf = new Dictionary<Guid, List<Guid>>();
                foreach (var id in direct) unitsOf[id] = new List<Guid> { id };
                foreach (var c in children)
                    foreach (var id in _s.EffectiveMembers(c.Id))
                    {
                        if (!unitsOf.TryGetValue(id, out var l)) unitsOf[id] = l = new List<Guid>();
                        if (!l.Contains(c.Id)) l.Add(c.Id);
                    }

                var edges = new List<(Guid, Guid)>();
                foreach (var w in _s.Wires)
                {
                    if (!unitsOf.TryGetValue(w.SourceId, out var from) || !unitsOf.TryGetValue(w.TargetId, out var to)) continue;
                    foreach (var a in from)
                        foreach (var b in to)
                            if (a != b) edges.Add((a, b));
                }

                var order = FlowOrder(units, edges, NodePosition);
                var lines = new JArray();
                int shown = 0;
                foreach (var u in order)
                {
                    if (budget <= 0) break;
                    var child = _s.Group(u);
                    lines.Add(child != null ? GroupLine(child, top: false) : ObjectLine(_s.Node(u), members, g.Id));
                    budget--;
                    shown++;
                }
                o["objects"] = lines;
                if (shown < order.Count)
                    more["objects"] = (more["objects"]?.Value<int>() ?? 0) + (order.Count - shown);

                var outbound = Outbound(members);
                if (outbound.Count > 0) o["out"] = Capped(outbound, MaxDetailEdges, more, "out");
                return o;
            }
        }
    }
}
