// The resolution design is adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps)
// by Yibin Yang, MIT License (BatchCanvas.Resolve).

using System;
using System.Collections.Generic;
using System.Linq;
using Cordyceps.Core.Naming;

namespace Cordyceps.Core.Canvas
{
    /// <summary>One nameable canvas object (a node or a group), as the resolver sees it.</summary>
    public sealed class ObjectEntry
    {
        /// <summary>The type name candidate lists print for a group.</summary>
        public const string GroupTypeName = "Group";

        /// <summary>
        /// The name every new group carries until someone names it: <c>new GH_Group()</c> is
        /// constructed as "Group", and <c>group_add</c> without a name uses the same text.
        /// </summary>
        public const string DefaultGroupName = "Group";

        /// <summary>True when a group's name is the one every new group starts with.</summary>
        public static bool IsDefaultGroupName(string name) => string.Equals(name, DefaultGroupName, StringComparison.Ordinal);

        public ObjectEntry(Guid id, string nickName, bool isDefaultNickName, string typeName, bool isGroup)
        {
            Id = id;
            NickName = nickName ?? string.Empty;
            IsDefaultNickName = isDefaultNickName;
            TypeName = typeName ?? string.Empty;
            IsGroup = isGroup;
        }

        public Guid Id { get; }

        /// <summary>The object's nickname, or a group's name.</summary>
        public string NickName { get; }

        /// <summary>
        /// True when the nickname is the one every new object of the type gets (for a group,
        /// <see cref="DefaultGroupName"/>): a name nobody chose, so it never picks what a
        /// <see cref="ResolveIntent.Destructive"/> call removes.
        /// </summary>
        public bool IsDefaultNickName { get; }
        public string TypeName { get; }
        public bool IsGroup { get; }
    }

    /// <summary>
    /// The narrow view of a canvas the resolver reads. <see cref="CanvasSnapshot"/> implements it;
    /// anything that needs pending changes seen as if applied (a batch's creates, deletes and
    /// renames) decorates it, and the resolver's rules stay as they are. Implementations exclude
    /// Cordyceps infrastructure from every lookup; the resolver checks
    /// <see cref="IsInfrastructure"/> as well, so a decorator that forgets cannot leak it.
    /// </summary>
    public interface IObjectIndex
    {
        /// <summary>The entry with this id, node or group; null when there is none.</summary>
        ObjectEntry Find(Guid id);

        /// <summary>Non-group entries whose nickname equals <paramref name="nickname"/> exactly (ordinal).</summary>
        IReadOnlyList<ObjectEntry> WithNickname(string nickname);

        /// <summary>Group entries whose name equals <paramref name="name"/> exactly (ordinal).</summary>
        IReadOnlyList<ObjectEntry> WithGroupName(string name);

        /// <summary>Entries, nodes and groups, whose lowercase D-form id starts with the prefix, ignoring case.</summary>
        IReadOnlyList<ObjectEntry> WithPrefix(string prefix);

        bool IsInfrastructure(Guid id);

        /// <summary>Every entry, in document order (nodes first, then groups); used for suggestions.</summary>
        IEnumerable<ObjectEntry> Entries { get; }
    }

    /// <summary>Which kind of thing a call names.</summary>
    public enum ResolveScope
    {
        /// <summary>
        /// Object actions. Names match non-group nicknames only; a GUID or prefix reaches any
        /// object, groups included.
        /// </summary>
        Objects,

        /// <summary>Group actions. Names match group names only; a GUID or prefix must land on a group.</summary>
        Groups
    }

    /// <summary>
    /// What the caller will do with the object it names. Where many ids are resolved at once the
    /// intent is a required argument; the single lookups have an explicit-intent overload, and the
    /// overloads without it mean <see cref="Ordinary"/>.
    /// </summary>
    public enum ResolveIntent
    {
        /// <summary>Reads, edits, moves, wiring: every reading resolves, a default nickname included.</summary>
        Ordinary,

        /// <summary>
        /// The call removes something (an object, a group, wires). A nickname reading that lands on
        /// a default nickname is refused even when it is unique: it names whichever object of that
        /// type happens to be alone right now. A GUID, a GUID prefix and a chosen nickname resolve.
        /// </summary>
        Destructive
    }

    /// <summary>Which reading of the text named the object. Later readings (a batch ref) are added here.</summary>
    public enum MatchedBy
    {
        Guid,
        GuidPrefix,
        Nickname,
        GroupName
    }

    /// <summary>The outcome of resolving one name: either an entry or a caller-facing message, never both.</summary>
    public sealed class Resolution
    {
        private Resolution(ObjectEntry entry, MatchedBy matchedBy, string error)
        {
            Entry = entry;
            MatchedBy = matchedBy;
            Error = error;
        }

        public ObjectEntry Entry { get; }
        public MatchedBy MatchedBy { get; }
        public string Error { get; }
        public bool IsResolved => Error == null;

        public static Resolution Resolved(ObjectEntry entry, MatchedBy matchedBy) => new Resolution(entry, matchedBy, null);
        public static Resolution Failed(string error) => new Resolution(null, default, error);
    }

    /// <summary>One entry of a many-id call, resolved against the same index as its siblings.</summary>
    public sealed class ResolvedEntry
    {
        public ResolvedEntry(int index, string text, Resolution resolution, int? duplicateOf)
        {
            Index = index;
            Text = text;
            Resolution = resolution;
            DuplicateOf = duplicateOf;
        }

        /// <summary>The entry's 0-based position in the caller's list.</summary>
        public int Index { get; }

        /// <summary>The caller's text, unchanged.</summary>
        public string Text { get; }

        public Resolution Resolution { get; }

        /// <summary>The 0-based position of an earlier entry that resolved to the same object; null otherwise.</summary>
        public int? DuplicateOf { get; }

        /// <summary><c>same object as entry i</c> when this entry repeats an earlier one; null otherwise.</summary>
        public string Note => DuplicateOf.HasValue ? $"same object as entry {DuplicateOf.Value}" : null;
    }

    /// <summary>
    /// The one rule that turns the text a caller passes as an object or group id into one canvas
    /// object. Strict: nothing is ever guessed. A name that fits two objects, or two readings that
    /// fit different objects, is an error listing the candidates by an id that works, because a
    /// wrong pick here becomes a wrong delete on someone's canvas.
    /// <list type="number">
    /// <item>Empty or all-whitespace text: <c>Component ID is required</c>.</item>
    /// <item>Text <see cref="Guid.TryParse(string, out Guid)"/> accepts is read only as a GUID.</item>
    /// <item>Otherwise the name reading (nickname, or group name in the Groups scope; exact and
    /// ordinal, never trimmed) and the GUID-prefix reading (8 to 35 hex digits and hyphens, at least
    /// 8 hex digits) are both collected.</item>
    /// <item>Two candidates in one reading, or two readings naming different objects, is an error.</item>
    /// <item>Cordyceps infrastructure is absent from every reading, with the same "not found" text
    /// as an object that does not exist.</item>
    /// <item>Under <see cref="ResolveIntent.Destructive"/>, a name reading that lands on a default
    /// nickname (or the default group name) is refused even when it is unique, with an error that
    /// prints the id to pass instead. When the prefix reading names the same object, it resolves
    /// by prefix.</item>
    /// </list>
    /// </summary>
    public static class ObjectResolver
    {
        /// <summary>How many candidates an ambiguity message lists before <c>and K more</c>.</summary>
        public const int MaxCandidatesListed = 10;

        /// <summary>The longest text read as a GUID prefix; 36 characters is a whole D-form GUID.</summary>
        public const int MaxPrefixLength = 35;

        /// <summary>Resolve with <see cref="ResolveIntent.Ordinary"/> intent: for actions that remove nothing.</summary>
        public static Resolution Resolve(IObjectIndex index, string text, ResolveScope scope) =>
            Resolve(index, text, scope, ResolveIntent.Ordinary);

        public static Resolution Resolve(IObjectIndex index, string text, ResolveScope scope, ResolveIntent intent)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            var outcome = Decide(index, text, scope, intent);
            return outcome.Entry != null
                ? Resolution.Resolved(outcome.Entry, outcome.MatchedBy)
                : Resolution.Failed(Describe(index, text, scope, intent, outcome));
        }

        /// <summary>
        /// Resolve every text against one index, before the caller changes anything. An entry that
        /// resolves to the same object as an earlier one carries <see cref="ResolvedEntry.DuplicateOf"/>,
        /// so the caller acts on the object once.
        /// </summary>
        public static IReadOnlyList<ResolvedEntry> ResolveMany(IObjectIndex index, IReadOnlyList<string> texts, ResolveScope scope) =>
            ResolveMany(index, texts, scope, ResolveIntent.Ordinary);

        /// <summary><see cref="ResolveMany(IObjectIndex, IReadOnlyList{string}, ResolveScope)"/> with an explicit intent.</summary>
        public static IReadOnlyList<ResolvedEntry> ResolveMany(IObjectIndex index, IReadOnlyList<string> texts, ResolveScope scope, ResolveIntent intent)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            var results = new List<ResolvedEntry>();
            var firstIndex = new Dictionary<Guid, int>();
            for (int i = 0; i < (texts?.Count ?? 0); i++)
            {
                var r = Resolve(index, texts[i], scope, intent);
                int? dup = null;
                if (r.IsResolved)
                {
                    if (firstIndex.TryGetValue(r.Entry.Id, out var first)) dup = first;
                    else firstIndex[r.Entry.Id] = i;
                }
                results.Add(new ResolvedEntry(i, texts[i], r, dup));
            }
            return results;
        }

        /// <summary>
        /// Text the prefix reading applies to: 8 to 35 characters, each an ASCII hex digit or a
        /// hyphen, with at least 8 hex digits. Hyphens are not checked against D-form positions; a
        /// misplaced one simply matches nothing.
        /// </summary>
        public static bool IsGuidPrefixShaped(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < GuidPrefixIndex.MinimumLength || text.Length > MaxPrefixLength)
                return false;
            int hex = 0;
            foreach (var c in text)
            {
                if (c == '-') continue;
                if (!IsHex(c)) return false;
                hex++;
            }
            return hex >= GuidPrefixIndex.MinimumLength;
        }

        private static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        // ---- the rule ----

        private enum Failure
        {
            None,
            Required,
            GuidNotFound,
            GroupNotFound,
            NotAGroup,
            AmbiguousName,
            DefaultName,
            AmbiguousPrefix,
            TwoReadings,
            NotFound
        }

        private sealed class Outcome
        {
            public ObjectEntry Entry;
            public MatchedBy MatchedBy;
            public Failure Failure;
            public Guid Guid;
            public List<ObjectEntry> Names = new List<ObjectEntry>();
            public List<ObjectEntry> Prefixed = new List<ObjectEntry>();
        }

        /// <summary>
        /// The decision alone, with no message text. Kept apart from <see cref="Describe"/> because a
        /// message lists candidates by their display id, and computing a display id resolves again.
        /// </summary>
        private static Outcome Decide(IObjectIndex index, string text, ResolveScope scope, ResolveIntent intent)
        {
            var o = new Outcome();
            if (string.IsNullOrWhiteSpace(text))
            {
                o.Failure = Failure.Required;
                return o;
            }

            if (Guid.TryParse(text, out var guid))
            {
                o.Guid = guid;
                var hit = index.IsInfrastructure(guid) ? null : index.Find(guid);
                if (scope == ResolveScope.Objects)
                {
                    if (hit != null) Resolve(o, hit, MatchedBy.Guid);
                    else o.Failure = Failure.GuidNotFound;
                }
                else
                {
                    if (hit == null) o.Failure = Failure.GroupNotFound;
                    else if (!hit.IsGroup) o.Failure = Failure.NotAGroup;
                    else Resolve(o, hit, MatchedBy.Guid);
                }
                return o;
            }

            var names = scope == ResolveScope.Objects ? index.WithNickname(text) : index.WithGroupName(text);
            o.Names = names
                .Where(e => !index.IsInfrastructure(e.Id) && e.IsGroup == (scope == ResolveScope.Groups) && e.NickName.Length > 0)
                .ToList();
            if (IsGuidPrefixShaped(text))
                o.Prefixed = index.WithPrefix(text).Where(e => !index.IsInfrastructure(e.Id)).ToList();

            if (o.Names.Count > 1) { o.Failure = Failure.AmbiguousName; return o; }
            if (o.Prefixed.Count > 1) { o.Failure = Failure.AmbiguousPrefix; return o; }

            var byName = o.Names.FirstOrDefault();
            var byPrefix = o.Prefixed.FirstOrDefault();
            if (byName != null && byPrefix != null && byName.Id != byPrefix.Id) { o.Failure = Failure.TwoReadings; return o; }

            if (byName != null)
            {
                bool refused = intent == ResolveIntent.Destructive && byName.IsDefaultNickName;
                if (refused && byPrefix == null)
                {
                    o.Failure = Failure.DefaultName;
                    return o;
                }
                // A default name that is also a prefix of the same object's id resolves by the prefix.
                Resolve(o, byName, refused ? MatchedBy.GuidPrefix
                    : scope == ResolveScope.Objects ? MatchedBy.Nickname : MatchedBy.GroupName);
                return o;
            }
            if (byPrefix != null)
            {
                if (scope == ResolveScope.Groups && !byPrefix.IsGroup) o.Failure = Failure.NotAGroup;
                else Resolve(o, byPrefix, MatchedBy.GuidPrefix);
                return o;
            }

            o.Failure = Failure.NotFound;
            return o;
        }

        private static void Resolve(Outcome o, ObjectEntry entry, MatchedBy by)
        {
            o.Entry = entry;
            o.MatchedBy = by;
            o.Failure = Failure.None;
        }

        // ---- messages ----

        private static string Describe(IObjectIndex index, string text, ResolveScope scope, ResolveIntent intent, Outcome o)
        {
            var display = new DisplayNames(index);
            switch (o.Failure)
            {
                case Failure.DefaultName:
                    return DefaultNameRefusal(display, text, o.Names[0]);
                case Failure.AmbiguousName when intent == ResolveIntent.Destructive && o.Names.All(e => e.IsDefaultNickName):
                    return scope == ResolveScope.Objects
                        ? $"'{text}' is the default nickname of {o.Names.Count} objects: {Candidates(display, o.Names)}. "
                          + "A default nickname never selects what this action removes; pass one of these ids"
                        : $"'{text}' is the default name of {o.Names.Count} groups: {Candidates(display, o.Names)}. "
                          + "A default group name never selects what this action removes; pass one of these ids";
                case Failure.Required:
                    return "Component ID is required";
                case Failure.GuidNotFound:
                    return $"Component not found: {o.Guid:D}";
                case Failure.GroupNotFound:
                    return $"Group not found: {text}";
                case Failure.NotAGroup:
                    return $"Object is not a group: {text}";
                case Failure.AmbiguousName:
                    return scope == ResolveScope.Objects
                        ? $"'{text}' is the nickname of {o.Names.Count} objects: {Candidates(display, o.Names)}; name one by its id"
                        : $"'{text}' is the name of {o.Names.Count} groups: {Candidates(display, o.Names)}; name one by its id";
                case Failure.AmbiguousPrefix:
                    return $"GUID prefix '{text}' matches {o.Prefixed.Count} objects: {Candidates(display, o.Prefixed)}; give more characters";
                case Failure.TwoReadings:
                    var nameWord = scope == ResolveScope.Objects ? "nickname" : "group name";
                    return $"'{text}' names two objects: {nameWord} → {Candidate(display, o.Names[0])}; "
                           + $"GUID prefix → {Candidate(display, o.Prefixed[0])}. Use a longer GUID prefix";
                default:
                    return NotFound(index, display, text, scope, intent);
            }
        }

        /// <summary>
        /// The refusal of a unique default name under destructive intent. It prints the id that
        /// works and the rename call that makes a name work, so the next call can be the right one.
        /// </summary>
        private static string DefaultNameRefusal(DisplayNames display, string text, ObjectEntry e)
        {
            var id = display.DisplayId(e.Id);
            return e.IsGroup
                ? $"'{text}' is the default name of group {id}, which every new group gets, so it never selects what this action removes. "
                  + $"Pass id '{id}', or name the group first with gh_canvas(action='group_rename', id='{id}', name='NEW') and pass that name"
                : $"'{text}' is the default nickname of {id} {e.TypeName}, which every new {e.TypeName} gets, so it never selects what this action removes. "
                  + $"Pass id '{id}', or name it first with gh_canvas(action='rename', id='{id}', nickname='NEW') and pass that name";
        }

        private static string NotFound(IObjectIndex index, DisplayNames display, string text, ResolveScope scope, ResolveIntent intent)
        {
            var visible = index.Entries.Where(e => !index.IsInfrastructure(e.Id)).ToList();
            var msg = scope == ResolveScope.Objects ? $"No object named '{text}'." : $"No group named '{text}'.";

            // Under destructive intent a default name would only be refused, so it is never suggested.
            var scopeNames = visible
                .Where(e => e.IsGroup == (scope == ResolveScope.Groups) && e.NickName.Length > 0
                            && !(intent == ResolveIntent.Destructive && e.IsDefaultNickName))
                .Select(e => e.NickName);
            var closest = NameSuggestions.Closest(text, scopeNames);
            if (closest != null) msg += $" Did you mean '{closest}'?";

            if (scope == ResolveScope.Objects)
            {
                var groups = visible.Where(e => e.IsGroup && string.Equals(e.NickName, text, StringComparison.Ordinal)).ToList();
                if (groups.Count > 0)
                    msg += $" '{text}' is a group; group actions take its name, and delete, move, rename, info, bounds and zoom take its id ({string.Join(", ", groups.Select(g => display.DisplayId(g.Id)))}).";
            }
            else if (visible.Any(e => !e.IsGroup && string.Equals(e.NickName, text, StringComparison.Ordinal)))
            {
                msg += $" '{text}' is an object's nickname, not a group.";
            }

            // Needs a decimal digit as well: "Add" and "beef" are all hex letters, and telling
            // someone who mistyped a nickname about GUID prefixes sends them the wrong way.
            if (text.Length >= 1 && text.Length < GuidPrefixIndex.MinimumLength && text.All(IsHex)
                && text.Any(c => c >= '0' && c <= '9'))
                msg += $" A GUID prefix needs at least {GuidPrefixIndex.MinimumLength} hex digits.";

            return msg;
        }

        private static string Candidate(DisplayNames display, ObjectEntry e) => $"{display.DisplayId(e.Id)} {e.TypeName}";

        private static string Candidates(DisplayNames display, IReadOnlyList<ObjectEntry> entries)
        {
            var shown = entries.Take(MaxCandidatesListed).Select(e => Candidate(display, e));
            var list = string.Join(", ", shown);
            int more = entries.Count - MaxCandidatesListed;
            return more > 0 ? $"{list}, and {more} more" : list;
        }

        /// <summary>True when <paramref name="text"/> resolves to <paramref name="id"/> in the scope; builds no message.</summary>
        internal static bool ResolvesTo(IObjectIndex index, string text, ResolveScope scope, Guid id)
        {
            var o = Decide(index, text, scope, ResolveIntent.Ordinary);
            return o.Entry != null && o.Entry.Id == id;
        }
    }

    /// <summary>
    /// The names outline and error messages print for objects. Each one is chosen only after
    /// checking that it resolves back to the same object, so every printed name works as an id.
    /// <list type="bullet">
    /// <item><b>ShortId</b>: the shortest prefix of the D form, at least 8 characters, that no other
    /// object in the index shares.</item>
    /// <item><b>DisplayId</b>: the short id when it resolves back (an object: in the Objects scope; a
    /// group: in both scopes, since group lines are passed to object and group actions alike), else
    /// the full GUID.</item>
    /// <item><b>DisplayName</b>: an object's nickname when it is not the type default and resolves
    /// back in the Objects scope; a group's name when it resolves back in the Groups scope; else
    /// the DisplayId. Either is used only when <see cref="OutlineGrammar.IsPrintable"/> says it can
    /// be cut back out of an outline line (no edge whitespace, no control character, no separator).</item>
    /// </list>
    /// Instances cache, so build one per index and reuse it for a whole view.
    /// </summary>
    public sealed class DisplayNames
    {
        private readonly IObjectIndex _index;
        private GuidPrefixIndex _prefixes;
        private readonly Dictionary<Guid, string> _shortIds = new Dictionary<Guid, string>();
        private readonly Dictionary<Guid, string> _displayIds = new Dictionary<Guid, string>();
        private readonly Dictionary<Guid, string> _displayNames = new Dictionary<Guid, string>();

        public DisplayNames(IObjectIndex index)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
        }

        public string ShortId(Guid id)
        {
            if (_shortIds.TryGetValue(id, out var cached)) return cached;
            // Built lazily, once, over the same ids WithPrefix searches (infrastructure left out).
            _prefixes ??= new GuidPrefixIndex(_index.Entries.Where(e => !_index.IsInfrastructure(e.Id)).Select(e => e.Id));
            string result = _prefixes.Shortest(id);
            return _shortIds[id] = result;
        }

        public string DisplayId(Guid id)
        {
            if (_displayIds.TryGetValue(id, out var cached)) return cached;
            var shortId = ShortId(id);
            var entry = _index.Find(id);
            bool ok = ObjectResolver.ResolvesTo(_index, shortId, ResolveScope.Objects, id)
                      && (entry == null || !entry.IsGroup || ObjectResolver.ResolvesTo(_index, shortId, ResolveScope.Groups, id));
            return _displayIds[id] = ok ? shortId : id.ToString("D");
        }

        public string DisplayName(Guid id)
        {
            if (_displayNames.TryGetValue(id, out var cached)) return cached;
            var entry = _index.Find(id);
            string result;
            if (entry == null)
                result = DisplayId(id);
            else if (entry.IsGroup)
                // A default group name is refused by group_delete, like a default nickname by
                // delete, so it is never printed as a name: every printed name must work everywhere.
                result = !entry.IsDefaultNickName && OutlineGrammar.IsPrintable(entry.NickName, isGroup: true)
                         && ObjectResolver.ResolvesTo(_index, entry.NickName, ResolveScope.Groups, id)
                    ? entry.NickName : DisplayId(id);
            else
                result = !entry.IsDefaultNickName && OutlineGrammar.IsPrintable(entry.NickName, isGroup: false)
                         && ObjectResolver.ResolvesTo(_index, entry.NickName, ResolveScope.Objects, id)
                    ? entry.NickName : DisplayId(id);
            return _displayNames[id] = result;
        }
    }
}
