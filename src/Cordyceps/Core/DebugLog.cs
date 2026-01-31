using System;
using System.Collections.Generic;
using Rhino;

namespace Cordyceps.Core
{
    /// <summary>
    /// Centralized debug logging that captures messages for retrieval via MCP
    /// </summary>
    public static class DebugLog
    {
        private static readonly object _lock = new object();
        private static readonly Queue<LogEntry> _entries = new Queue<LogEntry>();
        private const int MaxEntries = 500;

        /// <summary>
        /// Current debug level. Messages with level > DebugLevel are not written to Rhino command history.
        /// Level 0: Server start/stop/URL only. Level 1+: Request/response details.
        /// </summary>
        public static int DebugLevel { get; set; } = 0;

        public class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Level { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Write a debug message with specified verbosity level.
        /// Only writes to Rhino command line if messageLevel &lt;= DebugLevel.
        /// </summary>
        public static void WriteLine(string message, string level = "INFO", int messageLevel = 1)
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
                while (_entries.Count > MaxEntries)
                {
                    _entries.Dequeue();
                }
            }

            // Only write to Rhino command line if message level is within configured debug level
            if (messageLevel <= DebugLevel)
            {
                RhinoApp.WriteLine($"Cordyceps [{level}]: {message}");
            }
        }

        /// <summary>
        /// Write an info message
        /// </summary>
        public static void Info(string message) => WriteLine(message, "INFO");

        /// <summary>
        /// Write a warning message
        /// </summary>
        public static void Warn(string message) => WriteLine(message, "WARN");

        /// <summary>
        /// Write an error message
        /// </summary>
        public static void Error(string message) => WriteLine(message, "ERROR");

        /// <summary>
        /// Write a debug message
        /// </summary>
        public static void Debug(string message) => WriteLine(message, "DEBUG");

        /// <summary>
        /// Get all log entries and optionally clear the buffer
        /// </summary>
        public static List<LogEntry> GetEntries(bool clear = false)
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
        /// Get entries since a specific time
        /// </summary>
        public static List<LogEntry> GetEntriesSince(DateTime since)
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
        /// Clear all entries
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }
        }

        /// <summary>
        /// Get the count of stored entries
        /// </summary>
        public static int Count
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
