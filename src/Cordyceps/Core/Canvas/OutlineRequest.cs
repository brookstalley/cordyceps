using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core.Canvas
{
    /// <summary>Which outline view a call asks for.</summary>
    public enum OutlineMode
    {
        Overview,
        Groups,
        Around
    }

    /// <summary>
    /// The arguments of gh_canvas(action='outline'), checked before the document is read. Every rule
    /// here is an error, never an adjustment: an agent that passes depth=50 learns the range instead
    /// of silently getting depth 10.
    /// </summary>
    public sealed class OutlineRequest
    {
        public const int DefaultDepth = 2;
        public const int MinDepth = 1;
        public const int MaxDepth = 10;

        private OutlineRequest(string id, IReadOnlyList<string> groups, int depth, bool depthGiven)
        {
            Id = id;
            GroupTexts = groups;
            Depth = depth;
            DepthGiven = depthGiven;
        }

        /// <summary>The caller's <c>id</c>, unchanged; null when not given.</summary>
        public string Id { get; }

        /// <summary>The caller's group names or ids, in order; null when <c>group</c> was not given.</summary>
        public IReadOnlyList<string> GroupTexts { get; }

        /// <summary>Wires each way for the around view (the default when not given).</summary>
        public int Depth { get; }

        /// <summary>Whether the caller passed <c>depth</c>, as opposed to getting the default.</summary>
        public bool DepthGiven { get; }

        public OutlineMode Mode => Id != null ? OutlineMode.Around : GroupTexts != null ? OutlineMode.Groups : OutlineMode.Overview;

        /// <summary>
        /// Check the arguments. <paramref name="depth"/> 0 means not given (the tool's default).
        /// An empty <paramref name="id"/> or <paramref name="group"/> counts as not given; a
        /// whitespace-only one is passed on, so the resolver reports it in its usual words.
        /// </summary>
        public static bool TryParse(string id, string group, int depth, out OutlineRequest request, out string error)
        {
            request = null;
            error = null;
            if (id == string.Empty) id = null;
            if (group == string.Empty) group = null;

            if (id != null && group != null)
            {
                error = "Give id or group, not both: id shows the wiring around one object, group shows one or more groups";
                return false;
            }
            if (depth != 0 && id == null)
            {
                error = "depth only applies with id (the wiring around one object)";
                return false;
            }
            if (depth != 0 && (depth < MinDepth || depth > MaxDepth))
            {
                error = $"depth must be {I(MinDepth)}-{I(MaxDepth)} (got {I(depth)}); omit it for the default {I(DefaultDepth)}";
                return false;
            }

            IReadOnlyList<string> groups = null;
            if (group != null)
            {
                if (!TryParseGroups(group, out groups, out error))
                    return false;
            }

            request = new OutlineRequest(id, groups, depth == 0 ? DefaultDepth : depth, depth != 0);
            return true;
        }

        /// <summary>
        /// One name, or a JSON array of names. Text that starts with <c>[</c> is read only as JSON, so
        /// a malformed list is an error and never retried as a group name.
        /// </summary>
        private static bool TryParseGroups(string group, out IReadOnlyList<string> groups, out string error)
        {
            groups = null;
            error = null;
            if (!group.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                groups = new[] { group };
                return true;
            }

            JArray array;
            try
            {
                array = JArray.Parse(group);
            }
            catch (JsonReaderException ex)
            {
                // A group can be named "[WIP] facade". The overview prints that name, so say how
                // to pass it: inside a JSON array, where the brackets are just text.
                error = $"group looks like a JSON array but does not parse: {ex.Message} "
                        + "To name one group whose name starts with '[', put it in a JSON array: [\"[NAME] ...\"]";
                return false;
            }

            if (array.Count == 0)
            {
                error = "group is an empty JSON array; give at least one group name or id";
                return false;
            }
            var list = new List<string>();
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.String)
                {
                    error = $"group[{I(i)}] must be a string (a group name or id), got {array[i].Type.ToString().ToLowerInvariant()}";
                    return false;
                }
                list.Add((string)array[i]);
            }
            groups = list;
            return true;
        }

        /// <summary>
        /// Resolve the request's names against <paramref name="index"/>: <c>id</c> in the Objects
        /// scope (a group's id selects the group view), <c>group</c> entries in the Groups scope.
        /// Every name is resolved before anything is rendered; one failure fails the call.
        /// </summary>
        public bool TryResolve(IObjectIndex index, out OutlineTarget target, out string error)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            target = null;
            error = null;

            switch (Mode)
            {
                case OutlineMode.Overview:
                    target = new OutlineTarget(OutlineMode.Overview, Guid.Empty, Array.Empty<Guid>(), Depth);
                    return true;

                case OutlineMode.Around:
                {
                    var r = ObjectResolver.Resolve(index, Id, ResolveScope.Objects);
                    if (!r.IsResolved)
                    {
                        error = r.Error;
                        return false;
                    }
                    // depth counts wires around one object; a group has no "around", so a depth
                    // passed with a group's id is refused rather than quietly dropped.
                    if (r.Entry.IsGroup && DepthGiven)
                    {
                        error = "depth only applies when id names an object; this id is a group, which is shown whole (omit depth)";
                        return false;
                    }
                    target = r.Entry.IsGroup
                        ? new OutlineTarget(OutlineMode.Groups, Guid.Empty, new[] { r.Entry.Id }, Depth)
                        : new OutlineTarget(OutlineMode.Around, r.Entry.Id, Array.Empty<Guid>(), Depth);
                    return true;
                }

                default:
                {
                    var resolved = ObjectResolver.ResolveMany(index, GroupTexts, ResolveScope.Groups);
                    var failures = resolved.Where(e => !e.Resolution.IsResolved).ToList();
                    if (failures.Count > 0)
                    {
                        error = GroupTexts.Count == 1
                            ? failures[0].Resolution.Error
                            : string.Join("; ", failures.Select(f => $"group[{I(f.Index)}]: {f.Resolution.Error}"));
                        return false;
                    }
                    var ids = resolved.Where(e => !e.DuplicateOf.HasValue).Select(e => e.Resolution.Entry.Id).ToList();
                    target = new OutlineTarget(OutlineMode.Groups, Guid.Empty, ids, Depth);
                    return true;
                }
            }
        }

        private static string I(int n) => n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>What a resolved outline request renders.</summary>
    public sealed class OutlineTarget
    {
        public OutlineTarget(OutlineMode mode, Guid centre, IReadOnlyList<Guid> groups, int depth)
        {
            Mode = mode;
            Centre = centre;
            Groups = groups;
            Depth = depth;
        }

        public OutlineMode Mode { get; }

        /// <summary>The around view's centre object.</summary>
        public Guid Centre { get; }

        /// <summary>The groups the group view shows, each once, in the caller's order.</summary>
        public IReadOnlyList<Guid> Groups { get; }

        public int Depth { get; }

        /// <summary>Render the view over <paramref name="snapshot"/>.</summary>
        public JObject Render(CanvasSnapshot snapshot)
        {
            switch (Mode)
            {
                case OutlineMode.Groups: return OutlineView.Groups(snapshot, Groups);
                case OutlineMode.Around: return OutlineView.Around(snapshot, Centre, Depth);
                default: return OutlineView.Overview(snapshot);
            }
        }
    }
}
