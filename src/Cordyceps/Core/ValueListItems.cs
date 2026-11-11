using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core
{
    /// <summary>One value-list item as <c>gh_canvas(action='config')</c> was given it: a display name and the expression it outputs.</summary>
    public sealed class ValueListItemSpec
    {
        public ValueListItemSpec(string name, string expression)
        {
            Name = name;
            Expression = expression;
        }

        public string Name { get; }
        public string Expression { get; }
    }

    /// <summary>
    /// Host-free reading of the <c>items</c> argument of <c>gh_canvas(action='config')</c> on a
    /// value list, and the selection the list keeps once its items are replaced.
    ///
    /// <para>Every item is checked before the list is touched, so a bad item refuses the whole call
    /// and leaves the list as it was: replacing items is clear-then-add, and a failure between the
    /// two would leave an empty list.</para>
    /// </summary>
    public static class ValueListItems
    {
        /// <summary>The name an item gets when it gives none.</summary>
        public const string DefaultName = "Item";

        /// <summary>The expression an item gets when it gives none.</summary>
        public const string DefaultExpression = "0";

        private const string Shape = "'items' must be a JSON array of {name, value} objects, e.g. [{\"name\":\"A\",\"value\":\"1\"}]";

        /// <summary>
        /// Read <paramref name="json"/> as a non-empty array of <c>{name, value}</c> objects. A
        /// missing <c>name</c> is <see cref="DefaultName"/> and a missing <c>value</c> is
        /// <see cref="DefaultExpression"/>; a number or boolean is read as its invariant text.
        /// Refused, naming the first offending item: text that is not JSON, anything but an array,
        /// an empty array (it would leave the list with nothing to output), an entry that is not an
        /// object, a key other than <c>name</c> and <c>value</c> (a misspelt key would otherwise
        /// silently become a default), a key given twice, and a <c>name</c> or <c>value</c> that is
        /// null, an object or an array.
        /// </summary>
        public static bool TryParse(string json, out List<ValueListItemSpec> items, out string error)
        {
            items = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = Shape;
                return false;
            }

            JToken token;
            try
            {
                // Strings stay strings: an item named "2026-01-01" keeps that name exactly.
                token = ParseHelpers.ParseJsonKeepingText(json, rejectDuplicateKeys: true);
            }
            catch (JsonException ex)
            {
                error = $"{Shape}. It is not valid JSON: {ex.Message}";
                return false;
            }

            if (!(token is JArray array))
            {
                error = $"{Shape}; got {token.Type}.";
                return false;
            }

            if (array.Count == 0)
            {
                error = "'items' is empty; a value list needs at least one item.";
                return false;
            }

            var result = new List<ValueListItemSpec>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject obj))
                {
                    error = $"{Shape}; item {i} is {array[i].Type}.";
                    return false;
                }

                foreach (var property in obj.Properties())
                {
                    if (property.Name != "name" && property.Name != "value")
                    {
                        error = $"Item {i} has an unknown key '{property.Name}'; an item has 'name' and 'value'.";
                        return false;
                    }
                }

                if (!TryField(obj, "name", DefaultName, i, out var name, out error)) return false;
                if (!TryField(obj, "value", DefaultExpression, i, out var expression, out error)) return false;
                result.Add(new ValueListItemSpec(name, expression));
            }

            items = result;
            return true;
        }

        /// <summary>
        /// The indices to select once a list's items are replaced by <paramref name="newNames"/>.
        /// The previous selection is kept by name (exact, case-sensitive) where its item still
        /// exists. In a single-selection list that is the first item named like the previously
        /// first selected item; in a check list, every item named like any previously selected one.
        /// When nothing carries over, item 0 is selected. Empty only when
        /// <paramref name="newNames"/> is.
        /// </summary>
        /// <param name="previouslySelected">The names of the items selected before, in list order.</param>
        /// <param name="newNames">The names of the new items, in list order.</param>
        /// <param name="multiSelect">True for a check list, where several items can be selected.</param>
        public static IReadOnlyList<int> SelectionAfterReplace(
            IEnumerable<string> previouslySelected, IReadOnlyList<string> newNames, bool multiSelect)
        {
            if (newNames == null) throw new ArgumentNullException(nameof(newNames));
            if (newNames.Count == 0) return Array.Empty<int>();

            var previous = (previouslySelected ?? Enumerable.Empty<string>()).Where(n => n != null).ToList();
            var kept = new List<int>();

            if (multiSelect)
            {
                var names = new HashSet<string>(previous, StringComparer.Ordinal);
                for (int i = 0; i < newNames.Count; i++)
                    if (newNames[i] != null && names.Contains(newNames[i]))
                        kept.Add(i);
            }
            else if (previous.Count > 0)
            {
                for (int i = 0; i < newNames.Count; i++)
                {
                    if (string.Equals(newNames[i], previous[0], StringComparison.Ordinal))
                    {
                        kept.Add(i);
                        break;
                    }
                }
            }

            if (kept.Count == 0) kept.Add(0);
            return kept;
        }

        private static bool TryField(JObject obj, string key, string fallback, int index, out string text, out string error)
        {
            text = null;
            error = null;

            if (!obj.TryGetValue(key, StringComparison.Ordinal, out var token))
            {
                text = fallback;
                return true;
            }

            if (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined
                || token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            {
                error = $"Item {index} has a '{key}' that is {token.Type}; it must be text, a number or a boolean.";
                return false;
            }

            text = ParseHelpers.TextOf(token);
            return true;
        }
    }
}
