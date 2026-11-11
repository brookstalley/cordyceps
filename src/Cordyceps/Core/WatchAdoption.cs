using System;
using System.Collections.Generic;
using System.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// Which "adopted" documents the solution watcher lets go. A document is adopted when the
    /// solve coordinator asks for it to be watched but Grasshopper's document server does not know
    /// it (snapshot revert puts a document on the canvas directly), so no <c>DocumentRemoved</c>
    /// will ever release it. Without a rule, every revert would keep a whole document alive and
    /// subscribed for the rest of the session.
    ///
    /// <para>The rule: each time a document is adopted or re-requested, every other adopted
    /// document that is not the active canvas document is released. That bounds the adopted set
    /// to the document being scheduled plus the one on the canvas, and needs no canvas event.</para>
    ///
    /// <para>Host-free (generic over the document type) and linked into <c>Cordyceps.Tests</c>.</para>
    /// </summary>
    public static class WatchAdoption
    {
        /// <summary>
        /// The adopted documents to release: every one that is not in <paramref name="keep"/> (the
        /// document being requested and the active canvas document; nulls ignored).
        /// </summary>
        public static List<T> ToRelease<T>(IEnumerable<T> adopted, IEnumerable<T> keep, IEqualityComparer<T> comparer = null)
            where T : class
        {
            comparer ??= EqualityComparer<T>.Default;
            var kept = new HashSet<T>((keep ?? Enumerable.Empty<T>()).Where(k => k != null), comparer);
            return (adopted ?? Enumerable.Empty<T>()).Where(a => a != null && !kept.Contains(a)).ToList();
        }

        /// <summary>
        /// Whether releasing a document should also drop its cached solve state. Not when another
        /// watched document carries the same id: solve state is keyed by document id, and a
        /// reverted document can share its id with the one it replaced, whose state must survive.
        /// </summary>
        public static bool ShouldForget(Guid releasedId, IEnumerable<Guid> otherWatchedIds)
            => !(otherWatchedIds ?? Enumerable.Empty<Guid>()).Contains(releasedId);
    }
}
