using System;
using System.Collections.Generic;
using Rhino;

namespace Cordyceps.Core
{
    /// <summary>
    /// Centralized debug logging that captures messages for retrieval via MCP.
    /// The buffer/gating logic lives in the host-free <see cref="LogBuffer"/> (unit-tested in
    /// Cordyceps.Tests); this static wrapper owns the host-coupled RhinoApp.WriteLine emission
    /// and keeps the public API the tools consume.
    /// </summary>
    public static class DebugLog
    {
        private const int MaxEntries = 500;
        private static readonly LogBuffer _buffer = new LogBuffer(MaxEntries);

        /// <summary>
        /// Current debug level. Messages with level > DebugLevel are not written to Rhino command history.
        /// Level 0: Server start/stop/URL only. Level 1+: Request/response details.
        /// Thread-safe across HTTP worker threads and UI thread (volatile inside LogBuffer).
        /// </summary>
        public static int DebugLevel
        {
            get => _buffer.DebugLevel;
            set => _buffer.DebugLevel = value;
        }

        /// <summary>
        /// Write a debug message with specified verbosity level.
        /// Only writes to Rhino command line if messageLevel &lt;= DebugLevel.
        /// </summary>
        public static void WriteLine(string message, string level = "INFO", int messageLevel = 1)
        {
            // Every message is buffered; Add returns whether it passes the level gate for
            // console emission.
            if (_buffer.Add(message, level, messageLevel))
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
        /// Write an error message. messageLevel 0 so errors always reach the Rhino command
        /// line regardless of the configured DebugLevel (warnings stay at level 1).
        /// </summary>
        public static void Error(string message) => WriteLine(message, "ERROR", 0);

        /// <summary>
        /// Write a debug message
        /// </summary>
        public static void Debug(string message) => WriteLine(message, "DEBUG");

        /// <summary>
        /// Get all log entries and optionally clear the buffer
        /// </summary>
        public static List<LogBuffer.LogEntry> GetEntries(bool clear = false)
        {
            return _buffer.GetEntries(clear);
        }

        /// <summary>
        /// Get entries since a specific time
        /// </summary>
        public static List<LogBuffer.LogEntry> GetEntriesSince(DateTime since)
        {
            return _buffer.GetEntriesSince(since);
        }

        /// <summary>
        /// Clear all entries
        /// </summary>
        public static void Clear()
        {
            _buffer.Clear();
        }

        /// <summary>
        /// Get the count of stored entries
        /// </summary>
        public static int Count => _buffer.Count;
    }
}
