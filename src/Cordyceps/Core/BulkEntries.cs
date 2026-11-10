using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core
{
    /// <summary>One entry of <c>gh_canvas(action='move', moves=...)</c>: the caller's id text and a position, or why the position is unusable.</summary>
    public sealed class MoveEntry
    {
        public MoveEntry(string id, double x, double y, string error)
        {
            Id = id;
            X = x;
            Y = y;
            Error = error;
        }

        /// <summary>The caller's id text, unchanged; null when the entry has none.</summary>
        public string Id { get; }
        public double X { get; }
        public double Y { get; }

        /// <summary>Why the entry's x/y (or the entry itself) cannot be used; null when they can.</summary>
        public string Error { get; }
    }

    /// <summary>One entry of <c>gh_wire(action='connect', connections=...)</c>, read as text, or why it cannot be read.</summary>
    public sealed class ConnectionEntry
    {
        public ConnectionEntry(string sourceId, string sourceParam, string targetId, string targetParam, string error)
        {
            SourceId = sourceId;
            SourceParam = sourceParam;
            TargetId = targetId;
            TargetParam = targetParam;
            Error = error;
        }

        public string SourceId { get; }

        /// <summary>The source port spec; <c>"0"</c> when the entry gives none.</summary>
        public string SourceParam { get; }

        public string TargetId { get; }

        /// <summary>The target port spec; <c>"0"</c> when the entry gives none.</summary>
        public string TargetParam { get; }

        /// <summary>Why the entry cannot be used as it stands; null when it can.</summary>
        public string Error { get; }
    }

    /// <summary>
    /// Host-free readers for the JSON arrays of the bulk actions (<c>moves</c>, <c>connections</c>).
    /// Their entries carry object names, so every string is read as a string
    /// (<see cref="ParseHelpers.ParseJsonKeepingText"/>): a nickname shaped like a date stays that
    /// nickname. A malformed array fails the call; an entry that cannot be used is carried with its
    /// error so the action can report it per entry.
    /// </summary>
    public static class BulkEntries
    {
        public const string MovesShape = "[{\"id\": \"...\", \"x\": 0, \"y\": 0}, ...]";
        public const string ConnectionsShape = "[{sourceId,sourceParam,targetId,targetParam},...]";

        public static bool TryParseMoves(string json, out List<MoveEntry> entries, out string error)
        {
            entries = null;
            if (!TryParseArray(json, out var array, out error, "Invalid JSON format",
                    $"'moves' must be a JSON array: {MovesShape}"))
                return false;

            var list = new List<MoveEntry>();
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject o))
                {
                    list.Add(new MoveEntry(null, 0, 0, $"Invalid entry: moves[{I(i)}] must be an object {{\"id\", \"x\", \"y\"}}"));
                    continue;
                }

                var id = ParseHelpers.TextOf(o["id"]);
                string coordError = null;
                if (!TryNumber(o["x"], out var x)) coordError = $"Invalid x/y: moves[{I(i)}].x must be a number";
                else if (!TryNumber(o["y"], out var yValue)) coordError = $"Invalid x/y: moves[{I(i)}].y must be a number";
                else
                {
                    list.Add(new MoveEntry(id, x, yValue, null));
                    continue;
                }
                list.Add(new MoveEntry(id, 0, 0, coordError));
            }
            entries = list;
            return true;
        }

        public static bool TryParseConnections(string json, out List<ConnectionEntry> entries, out string error)
        {
            entries = null;
            if (!TryParseArray(json, out var array, out error, "Failed to parse connections JSON",
                    $"'connections' must be a JSON array: {ConnectionsShape}"))
                return false;

            var list = new List<ConnectionEntry>();
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject o))
                {
                    list.Add(new ConnectionEntry(null, "0", null, "0",
                        $"connections[{I(i)}] must be an object {{sourceId, sourceParam, targetId, targetParam}}"));
                    continue;
                }

                var src = ParseHelpers.TextOf(o["sourceId"]);
                var tgt = ParseHelpers.TextOf(o["targetId"]);
                list.Add(new ConnectionEntry(
                    src,
                    ParseHelpers.TextOf(o["sourceParam"]) ?? "0",
                    tgt,
                    ParseHelpers.TextOf(o["targetParam"]) ?? "0",
                    string.IsNullOrEmpty(src) || string.IsNullOrEmpty(tgt) ? "Missing sourceId or targetId" : null));
            }
            entries = list;
            return true;
        }

        private static bool TryParseArray(string json, out JArray array, out string error, string prefix, string notArray)
        {
            array = null;
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "JSON input is required";
                return false;
            }

            JToken token;
            try
            {
                token = ParseHelpers.ParseJsonKeepingText(json);
            }
            catch (JsonReaderException ex)
            {
                error = $"{prefix}: {ex.Message}";
                return false;
            }

            array = token as JArray;
            if (array == null)
            {
                // The JSON literal null, an object or a bare value is a malformed call, not zero entries.
                error = notArray;
                return false;
            }
            return true;
        }

        /// <summary>A JSON number, or a string holding one in the invariant culture.</summary>
        private static bool TryNumber(JToken token, out double value)
        {
            value = 0;
            if (token == null) return false;
            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    value = (double)token;
                    return !double.IsNaN(value) && !double.IsInfinity(value);
                case JTokenType.String:
                    return double.TryParse((string)token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        && !double.IsNaN(value) && !double.IsInfinity(value);
                default:
                    return false;
            }
        }

        private static string I(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
