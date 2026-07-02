using System;
using System.Collections.Generic;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free bounded store behind gh_document's snapshots (GHD-6M2J). Snapshots are full
    /// document serializations that previously accumulated in an unbounded process-lifetime
    /// dictionary — and with undo/redo disabled, every documented mutation workflow funnels
    /// through this store. Bounds the count with oldest-first eviction (the
    /// <see cref="LogBuffer"/> pattern) and reports what was evicted so the caller can surface
    /// it to the agent.
    ///
    /// Semantics: re-saving an existing name replaces its payload and refreshes its age (it just
    /// became the newest snapshot) without evicting anything; saving a new name at capacity
    /// evicts the oldest entry first. Listing returns oldest-first insertion order.
    ///
    /// Host-independent (no Grasshopper/Rhino references, no <c>DebugLog</c>) so the
    /// bound/eviction semantics can be unit-tested — <c>Cordyceps.Tests</c> links this file
    /// directly. Thread-safe: written under the UI thread, listed from HTTP worker threads.
    /// </summary>
    public sealed class SnapshotStore
    {
        /// <summary>A snapshot's listing metadata.</summary>
        public sealed class SnapshotInfo
        {
            public string Name { get; set; }
            public int SizeBytes { get; set; }
            public DateTime CreatedAtUtc { get; set; }
        }

        private sealed class Entry
        {
            public byte[] Data;
            public DateTime CreatedAtUtc;
        }

        private readonly object _lock = new object();
        // Insertion/refresh order, oldest first; _entries carries the payloads. Both are
        // maintained together under _lock.
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly int _maxSnapshots;

        /// <param name="maxSnapshots">Maximum number of snapshots retained; saving a new name
        /// beyond this evicts the oldest entry.</param>
        public SnapshotStore(int maxSnapshots)
        {
            if (maxSnapshots < 1) throw new ArgumentOutOfRangeException(nameof(maxSnapshots));
            _maxSnapshots = maxSnapshots;
        }

        /// <summary>Number of snapshots currently stored.</summary>
        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        /// <summary>The configured capacity.</summary>
        public int MaxSnapshots => _maxSnapshots;

        /// <summary>
        /// Store (or replace) a snapshot. Returns the names evicted to stay within capacity —
        /// empty for a same-name refresh or when under capacity.
        /// </summary>
        public IReadOnlyList<string> Save(string name, byte[] data)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Snapshot name is required", nameof(name));
            if (data == null) throw new ArgumentNullException(nameof(data));

            lock (_lock)
            {
                var evicted = new List<string>();

                if (_entries.ContainsKey(name))
                {
                    // Refresh: newest content, newest age, no eviction.
                    _order.Remove(name);
                }
                else
                {
                    while (_entries.Count >= _maxSnapshots)
                    {
                        var oldest = _order[0];
                        _order.RemoveAt(0);
                        _entries.Remove(oldest);
                        evicted.Add(oldest);
                    }
                }

                _order.Add(name);
                _entries[name] = new Entry { Data = data, CreatedAtUtc = DateTime.UtcNow };
                return evicted;
            }
        }

        /// <summary>Retrieve a snapshot's payload by name.</summary>
        public bool TryGet(string name, out byte[] data)
        {
            lock (_lock)
            {
                if (name != null && _entries.TryGetValue(name, out var entry))
                {
                    data = entry.Data;
                    return true;
                }
                data = null;
                return false;
            }
        }

        /// <summary>Delete a snapshot by name. Returns false if the name is not stored.</summary>
        public bool Remove(string name)
        {
            lock (_lock)
            {
                if (name == null || !_entries.Remove(name)) return false;
                _order.Remove(name);
                return true;
            }
        }

        /// <summary>All snapshots, oldest first.</summary>
        public IReadOnlyList<SnapshotInfo> List()
        {
            lock (_lock)
            {
                var result = new List<SnapshotInfo>(_order.Count);
                foreach (var name in _order)
                {
                    var entry = _entries[name];
                    result.Add(new SnapshotInfo
                    {
                        Name = name,
                        SizeBytes = entry.Data.Length,
                        CreatedAtUtc = entry.CreatedAtUtc,
                    });
                }
                return result;
            }
        }
    }
}
