using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core
{
    /// <summary>One entry of a many-id call after resolution: an object id or a caller-facing error, never both.</summary>
    public sealed class IdOutcome
    {
        public IdOutcome(int index, string text, Guid? id, string error, int? duplicateOf)
        {
            Index = index;
            Text = text;
            Id = id;
            Error = error;
            DuplicateOf = duplicateOf;
        }

        /// <summary>The entry's 0-based position in the caller's list.</summary>
        public int Index { get; }

        /// <summary>The caller's text, unchanged; failure entries echo this.</summary>
        public string Text { get; }

        /// <summary>The resolved object; null when the entry failed.</summary>
        public Guid? Id { get; }

        public string Error { get; }
        public bool IsResolved => Error == null;

        /// <summary>The 0-based position of an earlier entry that resolved to the same object; null otherwise.</summary>
        public int? DuplicateOf { get; }

        /// <summary><c>same object as entry i</c> when this entry repeats an earlier one; null otherwise.</summary>
        public string Note => DuplicateOf.HasValue ? $"same object as entry {DuplicateOf.Value}" : null;

        /// <summary>True for an entry the caller's action should apply to: resolved, and not a repeat.</summary>
        public bool ActOn => IsResolved && !DuplicateOf.HasValue;
    }

    /// <summary>
    /// The host-free half of every call that names several canvas objects at once (<c>ids</c>,
    /// <c>moves</c>, member arrays). Every entry is resolved before the caller changes anything, so
    /// a typo in entry five can never leave entries one to four half-applied; a repeat of an earlier
    /// entry is marked so the object is acted on once. The per-text resolution itself (GUID, prefix,
    /// nickname) is passed in, so this file never sees Grasshopper.
    /// </summary>
    public static class IdBatch
    {
        /// <summary>
        /// Resolve every text with <paramref name="resolve"/>, which returns either an id or an error.
        /// Call this before the first mutation.
        /// </summary>
        public static IReadOnlyList<IdOutcome> Collect(IReadOnlyList<string> texts, Func<string, (Guid? Id, string Error)> resolve)
        {
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            var outcomes = new List<IdOutcome>();
            var first = new Dictionary<Guid, int>();
            for (int i = 0; i < (texts?.Count ?? 0); i++)
            {
                var (id, error) = resolve(texts[i]);
                if (error == null && !id.HasValue)
                    error = "Component not found";
                if (error != null)
                {
                    outcomes.Add(new IdOutcome(i, texts[i], null, error, null));
                    continue;
                }

                int? dup = null;
                if (first.TryGetValue(id.Value, out var earlier)) dup = earlier;
                else first[id.Value] = i;
                outcomes.Add(new IdOutcome(i, texts[i], id, null, dup));
            }
            return outcomes;
        }

        /// <summary>
        /// For an all-or-nothing action: null when every entry resolved, else one message naming every
        /// entry that did not, by position and text, and saying that nothing was changed.
        /// </summary>
        public static string AllOrNothingError(IReadOnlyList<IdOutcome> outcomes, string field)
        {
            var failed = (outcomes ?? Array.Empty<IdOutcome>()).Where(o => !o.IsResolved).ToList();
            if (failed.Count == 0) return null;
            var parts = failed.Select(o => $"{field}[{o.Index}] '{o.Text}': {o.Error}");
            return $"Nothing was changed: {failed.Count} of {outcomes.Count} {field} did not resolve. {string.Join("; ", parts)}";
        }

        /// <summary>The ids to act on, in the caller's order, each once.</summary>
        public static IReadOnlyList<Guid> DistinctIds(IReadOnlyList<IdOutcome> outcomes) =>
            (outcomes ?? Array.Empty<IdOutcome>()).Where(o => o.ActOn).Select(o => o.Id.Value).ToList();

        /// <summary><c>field[i]: same object as entry j</c> for every repeated entry; empty when there are none.</summary>
        public static IReadOnlyList<string> DuplicateNotes(IReadOnlyList<IdOutcome> outcomes, string field) =>
            (outcomes ?? Array.Empty<IdOutcome>()).Where(o => o.DuplicateOf.HasValue).Select(o => $"{field}[{o.Index}]: {o.Note}").ToList();
    }
}
