using System;
using System.Collections.Generic;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free ring buffer behind <see cref="DebugLog"/>: bounded entry storage with
    /// oldest-first trimming, a level gate for console emission, and time-filtered retrieval.
    /// Extracted so the buffer/gating logic can be unit-tested — this file is linked directly
    /// into Cordyceps.Tests, so it must stay free of Grasshopper/Rhino dependencies (BCL only).
    /// <see cref="DebugLog"/> wraps a static instance and owns the host-coupled
    /// RhinoApp.WriteLine emission.
    /// </summary>
    public sealed class LogBuffer
    {
        private readonly object _lock = new object();
        private readonly Queue<LogEntry> _entries = new Queue<LogEntry>();
        private readonly int _maxEntries;

        /// <summary>
        /// Current debug level backing field. Messages with messageLevel > DebugLevel are stored
        /// but not emitted to the console sink. Volatile for thread-safe reads/writes across
        /// HTTP worker threads and the UI thread.
        /// </summary>
        private volatile int _debugLevel;

        /// <summary>A single captured log entry.</summary>
        public class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Level { get; set; }
            public string Message { get; set; }
        }

        /// <param name="maxEntries">Maximum number of entries retained; the oldest entry is
        /// dropped once the buffer exceeds this size.</param>
        public LogBuffer(int maxEntries)
        {
            if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            _maxEntries = maxEntries;
        }

        /// <summary>
        /// Current debug level. Messages with messageLevel > DebugLevel are stored in the
        /// buffer but should not be emitted to the console (see <see cref="Add"/>).
        /// </summary>
        public int DebugLevel
        {
            get => _debugLevel;
            set => _debugLevel = value;
        }

        /// <summary>
        /// Append an entry (timestamped now, UTC), trimming the oldest entries beyond the
        /// buffer's capacity. Every message is stored regardless of level; the return value
        /// tells the caller whether the message passes the level gate for console emission.
        /// </summary>
        /// <param name="message">The log message</param>
        /// <param name="level">Severity label (INFO/WARN/ERROR/DEBUG)</param>
        /// <param name="messageLevel">Verbosity level; emit to console only if &lt;= <see cref="DebugLevel"/></param>
        /// <returns>True if the message should also be written to the console sink</returns>
        public bool Add(string message, string level, int messageLevel)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Message = message
            };

            lock (_lock)
            {
                _entries.Enqueue(entry);
                while (_entries.Count > _maxEntries)
                {
                    _entries.Dequeue();
                }
            }

            return messageLevel <= DebugLevel;
        }

        /// <summary>
        /// Get all entries (oldest first) and optionally clear the buffer.
        /// </summary>
        public List<LogEntry> GetEntries(bool clear = false)
        {
            lock (_lock)
            {
                var result = new List<LogEntry>(_entries);
                if (clear)
                {
                    _entries.Clear();
                }
                return result;
            }
        }

        /// <summary>
        /// Get entries with a timestamp at or after a specific time.
        /// </summary>
        public List<LogEntry> GetEntriesSince(DateTime since)
        {
            lock (_lock)
            {
                var result = new List<LogEntry>();
                foreach (var entry in _entries)
                {
                    if (entry.Timestamp >= since)
                    {
                        result.Add(entry);
                    }
                }
                return result;
            }
        }

        /// <summary>
        /// Clear all entries.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        /// <summary>
        /// Get the count of stored entries.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
        }
    }
}
