using System;
using System.Threading;

namespace Cordyceps.Core
{
    /// <summary>
    /// Serializes Rhino/Grasshopper document operations with a <em>bounded</em> acquire.
    /// When one operation holds the lock longer than the configured timeout, other callers
    /// fail fast with <see cref="DocumentBusyException"/> instead of blocking indefinitely —
    /// so a single long-running or wedged operation can no longer make every subsequent
    /// request hang forever.
    ///
    /// Host-independent (no Grasshopper/Rhino references) so the timeout/release semantics
    /// can be unit-tested — see <c>Cordyceps.Tests</c>, which links this file directly. The
    /// actual UI-thread marshaling lives in <see cref="GrasshopperContext"/>.
    /// </summary>
    public sealed class DocumentLock
    {
        private readonly SemaphoreSlim _mutex = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _timeout;

        public DocumentLock(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        /// <summary>
        /// Run <paramref name="body"/> while holding the lock and return its result. The lock
        /// is always released, even if <paramref name="body"/> throws.
        /// </summary>
        /// <exception cref="DocumentBusyException">
        /// The lock could not be acquired within the configured timeout.
        /// </exception>
        public T Run<T>(Func<T> body)
        {
            if (!_mutex.Wait(_timeout))
                throw new DocumentBusyException(_timeout);
            try
            {
                return body();
            }
            finally
            {
                _mutex.Release();
            }
        }

        /// <summary>
        /// Run <paramref name="body"/> while holding the lock. The lock is always released,
        /// even if <paramref name="body"/> throws.
        /// </summary>
        /// <exception cref="DocumentBusyException">
        /// The lock could not be acquired within the configured timeout.
        /// </exception>
        public void Run(Action body) => Run<object>(() => { body(); return null; });
    }

    /// <summary>
    /// Thrown when a document operation cannot acquire the document lock within its timeout —
    /// another operation has held it too long. Surfaces to the MCP client as a structured
    /// <c>{ success: false, error }</c> result via the tool-call boundary.
    /// </summary>
    public sealed class DocumentBusyException : Exception
    {
        public DocumentBusyException(TimeSpan timeout)
            : base($"Document is busy: another operation held the Rhino/Grasshopper document " +
                   $"lock for more than {timeout.TotalSeconds:0} seconds. Rhino may be running a " +
                   $"long operation or be wedged (e.g. an infinite-loop script component). If this " +
                   $"persists, restart Rhino.")
        {
        }
    }
}
