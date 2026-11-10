// Adapted from Hyperceps (https://github.com/yang-yibinn/hyperceps) by Yibin Yang, MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core.Naming
{
    /// <summary>One installed component or parameter type, as the type-name rule sees it.</summary>
    public sealed class TypeCandidate
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string SubCategory { get; set; }
        public Guid Guid { get; set; }

        /// <summary>Obsolete, listed in the deprecation registry, or an <c>OBSOLETE</c> type.</summary>
        public bool Deprecated { get; set; }

        /// <summary>The plugin (library) the type comes from; "Grasshopper" for the core library.</summary>
        public string Plugin { get; set; }

        public string Description { get; set; }

        /// <summary>The replacement's name when the type is deprecated and one is known.</summary>
        public string UpgradeTo { get; set; }

        public Guid? UpgradeToGuid { get; set; }

        /// <summary>"component", "parameter", or "input" (an interactive control).</summary>
        public string Role => RoleOf(Category, SubCategory);

        public string PathText => $"{Category}/{SubCategory}/{Name}";

        /// <summary>
        /// The role a Grasshopper category implies: the Params tab holds parameters, its Input
        /// subcategory holds interactive controls, and everything else is a component.
        /// </summary>
        public static string RoleOf(string category, string subCategory)
        {
            if (category == "Params")
                return subCategory == "Input" ? "input" : "parameter";
            return "component";
        }

        /// <summary><c>Curve/Primitive/Line (component, Grasshopper, 4c4e56eb-…)</c>, for messages.</summary>
        public string Describe() =>
            $"{PathText} ({Role}{(Deprecated ? ", deprecated" : "")}"
            + $"{(string.IsNullOrEmpty(Plugin) ? "" : ", " + Plugin)}, {Guid:D})";
    }

    /// <summary>Every installed type, indexed by name (ignoring case) and by GUID.</summary>
    public sealed class TypeCatalog
    {
        private readonly Dictionary<string, List<TypeCandidate>> _byName =
            new Dictionary<string, List<TypeCandidate>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, TypeCandidate> _byGuid = new Dictionary<Guid, TypeCandidate>();
        private readonly HashSet<string> _categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TypeCandidate> _all = new List<TypeCandidate>();

        public TypeCatalog(IEnumerable<TypeCandidate> candidates)
        {
            foreach (var c in candidates ?? Enumerable.Empty<TypeCandidate>())
            {
                if (c == null || string.IsNullOrEmpty(c.Name)) continue;
                // A GUID names one type. If a broken install registers it twice, the first wins
                // everywhere, so a name match and a GUID match can never disagree.
                if (_byGuid.ContainsKey(c.Guid)) continue;

                _all.Add(c);
                _byGuid[c.Guid] = c;
                if (!_byName.TryGetValue(c.Name, out var list))
                    _byName[c.Name] = list = new List<TypeCandidate>();
                list.Add(c);
                if (!string.IsNullOrEmpty(c.Category)) _categories.Add(c.Category);
            }
        }

        public IReadOnlyList<TypeCandidate> All => _all;

        public IReadOnlyList<TypeCandidate> ByName(string name) =>
            _byName.TryGetValue(name ?? string.Empty, out var list)
                ? list
                : (IReadOnlyList<TypeCandidate>)Array.Empty<TypeCandidate>();

        public TypeCandidate ByGuid(Guid id) => _byGuid.TryGetValue(id, out var c) ? c : null;

        public bool HasCategory(string category) => _categories.Contains(category ?? string.Empty);
    }

    public enum TypeResolutionKind
    {
        Resolved,
        Ambiguous,
        NotFound,
        CategoryMissing,
        GuidNotInstalled,
        Missing
    }

    /// <summary>A type offered to the caller, with a spelling of it that the rule resolves to it alone.</summary>
    public sealed class TypeChoice
    {
        public TypeChoice(TypeCandidate type, string use)
        {
            Type = type;
            Use = use;
        }

        public TypeCandidate Type { get; }

        /// <summary>Text that, passed as the type, resolves to exactly this type.</summary>
        public string Use { get; }
    }

    public sealed class TypeResolution
    {
        public TypeResolutionKind Kind { get; internal set; }

        /// <summary>The caller's text, trimmed.</summary>
        public string Text { get; internal set; }

        /// <summary>What the text was rewritten to by an alias, or null when no alias applied.</summary>
        public string AliasedTo { get; internal set; }

        public TypeCandidate Match { get; internal set; }

        /// <summary>The match is deprecated, used only because it was the only one.</summary>
        public bool UsedDeprecated { get; internal set; }

        /// <summary>The competing matches when <see cref="Kind"/> is Ambiguous.</summary>
        public List<TypeChoice> Candidates { get; internal set; } = new List<TypeChoice>();

        /// <summary>Close names when nothing matched: shown, never used.</summary>
        public List<TypeChoice> Suggestions { get; internal set; } = new List<TypeChoice>();

        /// <summary>The category the caller named when it is not installed.</summary>
        public string Category { get; internal set; }

        /// <summary>The GUID the caller passed when no installed type has it.</summary>
        public Guid? Guid { get; internal set; }

        public bool IsResolved => Kind == TypeResolutionKind.Resolved;
    }

    /// <summary>
    /// The one rule for turning a component-type name into the type to create. Never loose:
    /// <list type="number">
    /// <item>A GUID selects that type.</item>
    /// <item>An entry in <see cref="Aliases"/> rewrites the whole text, exactly and ignoring case
    /// (<c>python</c> → <c>Python 3 Script</c>).</item>
    /// <item>A name in <see cref="Pinned"/> always means that built-in type, even when a plugin
    /// ships a type with the same name.</item>
    /// <item>Otherwise the name must equal a type's name, ignoring case, optionally narrowed as
    /// <c>Category/Name</c>, <c>Category/Subcategory/Name</c> or <c>Name (Category)</c>. Exactly
    /// one current match is used; a deprecated type counts only when it is the only match.</item>
    /// </list>
    /// Anything else is an error that lists the candidates, or offers near names as suggestions.
    /// A near name is never used in place of the one asked for.
    /// </summary>
    public static class TypeNameRule
    {
        /// <summary>Curated shorthands; each is an exact, whole-text rewrite.</summary>
        public static readonly IReadOnlyDictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Script components
                { "c#", "C# Script" },
                { "csharp", "C# Script" },
                { "csharp script", "C# Script" },
                { "python", "Python 3 Script" },
                { "python script", "Python 3 Script" },
                { "ghpython", "Python 3 Script" },

                // Planes
                { "plane", "XY Plane" },
                { "xyplane", "XY Plane" },
                { "xy", "XY Plane" },
                { "xzplane", "XZ Plane" },
                { "xz", "XZ Plane" },
                { "yzplane", "YZ Plane" },
                { "yz", "YZ Plane" },

                // Geometry
                { "cube", "Box" },
                { "rect", "Rectangle" },
                { "circ", "Circle" },
                { "cyl", "Cylinder" },
                { "pt", "Point" },
                { "ln", "Line" },
                { "crv", "Curve" },

                // Parameters
                { "slider", "Number Slider" },
                { "numberslider", "Number Slider" },
                { "num", "Number" },
                { "int", "Integer" },
                { "bool", "Boolean" },
                { "str", "Text" },
                { "string", "Text" },

                // Stream filter
                { "streamfilter", "Stream Filter" },
                { "filter", "Stream Filter" },
            };

        /// <summary>
        /// Built-in names that always mean the core Grasshopper type, so a plugin type that shares
        /// the name cannot make them ambiguous. A pin whose GUID is not installed is skipped and
        /// the name goes through the ordinary rule.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, Guid> Pinned =
            new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                { "C# Script", new Guid("b6ba1144-02d6-4a2d-b53c-ec62e290eeb7") },
                { "Python 3 Script", new Guid("c9b2d725-6f87-4b07-af90-bd9aefef68eb") },
                { "Stream Filter", new Guid("fb97f83d-9f77-4c6c-b134-1ddfec17e8aa") },
                { "Panel", new Guid("59e0b89a-e487-49f8-bab8-b5bab16be14c") },
                { "Number Slider", new Guid("57da07bd-ecab-415d-9d86-af36d7073abc") },
            };

        private const int MaxSuggestions = 5;

        public static TypeResolution Resolve(string text, TypeCatalog catalog) =>
            Resolve(text, catalog, explain: true);

        private static TypeResolution Resolve(string text, TypeCatalog catalog, bool explain)
        {
            catalog ??= new TypeCatalog(null);
            var s = (text ?? string.Empty).Trim();
            var result = new TypeResolution { Text = s };

            if (s.Length == 0)
            {
                result.Kind = TypeResolutionKind.Missing;
                return result;
            }

            if (System.Guid.TryParse(s, out var guid))
            {
                var byGuid = catalog.ByGuid(guid);
                if (byGuid == null)
                {
                    result.Kind = TypeResolutionKind.GuidNotInstalled;
                    result.Guid = guid;
                    return result;
                }
                return Resolved(result, byGuid);
            }

            var name = s;
            if (Aliases.TryGetValue(s, out var aliased))
            {
                result.AliasedTo = aliased;
                name = aliased;
            }

            if (Pinned.TryGetValue(name, out var pinnedGuid))
            {
                var pinned = catalog.ByGuid(pinnedGuid);
                if (pinned != null)
                    return Resolved(result, pinned);
            }

            // The whole text always counts as a name, so a type whose own name contains '/' or
            // parentheses is still found by writing it out.
            var matches = new List<TypeCandidate>(catalog.ByName(name));
            var (category, subCategory, bareName) = SplitQualified(name);
            if (bareName != null)
            {
                foreach (var c in catalog.ByName(bareName))
                {
                    if (!string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
                    if (subCategory != null && !string.Equals(c.SubCategory, subCategory, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!matches.Contains(c)) matches.Add(c);
                }
            }

            var live = matches.Where(c => !c.Deprecated).ToList();
            var pool = live.Count > 0 ? live : matches;

            if (pool.Count == 1)
                return Resolved(result, pool[0]);

            if (pool.Count > 1)
            {
                result.Kind = TypeResolutionKind.Ambiguous;
                if (explain)
                    result.Candidates = pool
                        .OrderBy(c => c.Role == "component" ? 0 : 1)
                        .ThenBy(c => c.PathText, StringComparer.OrdinalIgnoreCase)
                        .Select(c => new TypeChoice(c, SpellingFor(c, catalog)))
                        .ToList();
                return result;
            }

            if (category != null && !catalog.HasCategory(category))
            {
                result.Kind = TypeResolutionKind.CategoryMissing;
                result.Category = category;
            }
            else
            {
                result.Kind = TypeResolutionKind.NotFound;
            }

            if (explain)
                result.Suggestions = Suggest(bareName ?? name, catalog)
                    .Select(c => new TypeChoice(c, SpellingFor(c, catalog)))
                    .ToList();
            return result;
        }

        private static TypeResolution Resolved(TypeResolution result, TypeCandidate match)
        {
            result.Kind = TypeResolutionKind.Resolved;
            result.Match = match;
            result.UsedDeprecated = match.Deprecated;
            return result;
        }

        /// <summary>
        /// Read <c>Category/Name</c>, <c>Category/Subcategory/Name</c> or <c>Name (Category)</c>.
        /// Returns a null name when the text is not written in one of those forms (including when
        /// any part of it is blank).
        /// </summary>
        private static (string category, string subCategory, string name) SplitQualified(string text)
        {
            if (text.IndexOf('/') >= 0)
            {
                var parts = text.Split('/').Select(p => p.Trim()).ToArray();
                if (parts.Any(p => p.Length == 0)) return (null, null, null);
                if (parts.Length == 2) return (parts[0], null, parts[1]);
                return (parts[0], parts[1], string.Join("/", parts.Skip(2)));
            }

            if (text.EndsWith(")", StringComparison.Ordinal))
            {
                int open = text.LastIndexOf('(');
                if (open > 0)
                {
                    var name = text.Substring(0, open).Trim();
                    var category = text.Substring(open + 1, text.Length - open - 2).Trim();
                    if (name.Length > 0 && category.Length > 0) return (category, null, name);
                }
            }

            return (null, null, null);
        }

        /// <summary>
        /// The shortest spelling that resolves to <paramref name="c"/> alone: its name, then
        /// <c>Category/Name</c>, then <c>Category/Subcategory/Name</c>, else its GUID. Each is
        /// checked by running the rule itself, so a spelling offered is a spelling that works.
        /// </summary>
        private static string SpellingFor(TypeCandidate c, TypeCatalog catalog)
        {
            foreach (var spelling in new[] { c.Name, $"{c.Category}/{c.Name}", $"{c.Category}/{c.SubCategory}/{c.Name}" })
            {
                var r = Resolve(spelling, catalog, explain: false);
                if (r.IsResolved && ReferenceEquals(r.Match, c))
                    return spelling;
            }
            return c.Guid.ToString("D");
        }

        /// <summary>
        /// Up to five close names: types whose name contains the text (or is contained in it),
        /// nearest in length first; failing those, near misses by edit distance. Current types come
        /// before deprecated ones, and a deprecated type is dropped when a current type has the
        /// same name.
        /// </summary>
        private static List<TypeCandidate> Suggest(string name, TypeCatalog catalog)
        {
            if (name.Length < 2) return new List<TypeCandidate>();

            var containing = catalog.All
                .Where(c => c.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                         || (c.Name.Length >= 4 && name.IndexOf(c.Name, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
            var ranked = containing.Count > 0
                ? Rank(containing, c => Math.Abs(c.Name.Length - name.Length))
                : NearMisses(name, catalog);

            return ranked.Take(MaxSuggestions).ToList();
        }

        private static IEnumerable<TypeCandidate> NearMisses(string name, TypeCatalog catalog)
        {
            int limit = Math.Max(2, name.Length / 4);
            var lower = name.ToLowerInvariant();
            var near = catalog.All
                .Where(c => Math.Abs(c.Name.Length - name.Length) <= limit)
                .Select(c => (c, d: NameSuggestions.EditDistance(lower, c.Name.ToLowerInvariant())))
                .Where(x => x.d <= limit)
                .ToList();
            var distance = near.ToDictionary(x => x.c, x => x.d);
            return Rank(near.Select(x => x.c).ToList(), c => distance[c]);
        }

        private static IEnumerable<TypeCandidate> Rank(List<TypeCandidate> pool, Func<TypeCandidate, int> closeness)
        {
            var liveNames = new HashSet<string>(pool.Where(c => !c.Deprecated).Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            return pool
                .Where(c => !c.Deprecated || !liveNames.Contains(c.Name))
                .OrderBy(closeness)
                .ThenBy(c => c.Deprecated ? 1 : 0)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.PathText, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The caller-facing reason an unresolved <paramref name="r"/> did not resolve, written so
        /// the next call can be fixed without another lookup. Null for a resolved result.
        /// </summary>
        /// <param name="r">The resolution to explain.</param>
        /// <param name="argName">The tool argument the name arrived in.</param>
        /// <param name="consequence">What the call therefore did not do, as a sentence.</param>
        public static string Explain(TypeResolution r, string argName = "type", string consequence = "Nothing was created.")
        {
            const string forms = "a component name (exact, case ignored), 'Category/Name', 'Category/Subcategory/Name', or a GUID";
            switch (r.Kind)
            {
                case TypeResolutionKind.Resolved:
                    return null;

                case TypeResolutionKind.Missing:
                    return $"'{argName}' is required: pass {forms}.";

                case TypeResolutionKind.GuidNotInstalled:
                    return $"No installed component type has the GUID {r.Guid:D}. {consequence} " +
                           "The plugin that provides it may not be loaded; find installed types with gh_canvas(action='search', query='...').";

                case TypeResolutionKind.Ambiguous:
                    return $"'{r.Text}'{AliasNote(r)} matches {r.Candidates.Count} component types. {consequence} " +
                           "Candidates: " +
                           string.Join("; ", r.Candidates.Select(c => $"{c.Type.Describe()}, write '{c.Use}'")) +
                           $". Pass one of those spellings (or the GUID) as '{argName}'.";

                case TypeResolutionKind.CategoryMissing:
                    return $"No installed category is named '{r.Category}' (from '{r.Text}'). {consequence} " +
                           "The category is the Grasshopper ribbon tab; the plugin may not be installed." +
                           DidYouMean(r) + $" Pass {forms}.";

                default:
                    return $"No component type is named '{r.Text}'{AliasNote(r)}; names are matched exactly, never partially. {consequence}" +
                           DidYouMean(r) +
                           $" Pass {forms}; browse with gh_canvas(action='search', query='...').";
            }
        }

        /// <summary>
        /// The structured error a tool returns for an unresolved <paramref name="r"/>:
        /// <c>success</c>, <c>error</c> (the <paramref name="message"/>, or <see cref="Explain"/>),
        /// a machine-readable <c>code</c>, and the candidates or suggestions, each carrying a
        /// <c>use</c> spelling that resolves to it alone.
        /// </summary>
        public static Dictionary<string, object> ErrorPayload(TypeResolution r, string message = null,
            string argName = "type", string consequence = "Nothing was created.")
        {
            var payload = new Dictionary<string, object>
            {
                ["success"] = false,
                ["error"] = message ?? Explain(r, argName, consequence),
                ["code"] = CodeFor(r.Kind)
            };

            if (r.Kind == TypeResolutionKind.Ambiguous)
            {
                payload["matchCount"] = r.Candidates.Count;
                payload["matches"] = r.Candidates.Select(Describe).ToList();
            }
            else if (r.Suggestions.Count > 0)
            {
                payload["suggestions"] = r.Suggestions.Select(Describe).ToList();
            }

            return payload;
        }

        private static string CodeFor(TypeResolutionKind kind)
        {
            switch (kind)
            {
                case TypeResolutionKind.Ambiguous: return "ambiguous_name";
                case TypeResolutionKind.CategoryMissing: return "unknown_category";
                case TypeResolutionKind.GuidNotInstalled: return "guid_not_installed";
                case TypeResolutionKind.Missing: return "type_required";
                case TypeResolutionKind.Resolved: return "not_instantiated";
                default: return "unknown_type";
            }
        }

        private static Dictionary<string, object> Describe(TypeChoice c)
        {
            var d = new Dictionary<string, object>
            {
                ["name"] = c.Type.Name,
                ["use"] = c.Use,
                ["category"] = c.Type.Category,
                ["subcategory"] = c.Type.SubCategory,
                ["role"] = c.Type.Role,
                ["plugin"] = c.Type.Plugin,
                ["guid"] = c.Type.Guid.ToString("D"),
                ["deprecated"] = c.Type.Deprecated
            };
            if (c.Type.UpgradeTo != null)
            {
                d["upgradeTo"] = c.Type.UpgradeTo;
                d["upgradeToGuid"] = c.Type.UpgradeToGuid?.ToString("D");
            }
            return d;
        }

        private static string AliasNote(TypeResolution r) =>
            r.AliasedTo == null ? string.Empty : $" (an alias for '{r.AliasedTo}')";

        private static string DidYouMean(TypeResolution r) =>
            r.Suggestions.Count == 0
                ? string.Empty
                : " Did you mean " + string.Join(", ", r.Suggestions.Select(s => $"'{s.Use}' ({s.Type.Describe()})")) + "?";
    }
}
