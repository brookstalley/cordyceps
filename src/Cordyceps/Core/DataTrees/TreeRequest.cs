using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core.DataTrees
{
    /// <summary>One thing to read: an object text and an optional port spec, with the label used in errors.</summary>
    public sealed class TreeTarget
    {
        public TreeTarget(string label, string id, string param)
        {
            Label = label;
            Id = id;
            Param = param;
        }

        /// <summary>"id" for a single target, "ids[i]" for the i-th of a list.</summary>
        public string Label { get; }
        public string Id { get; }
        public string Param { get; }
    }

    /// <summary>A fully validated gh_inspect(action='tree') request.</summary>
    public sealed class TreeRequest
    {
        public TreeRequest(IReadOnlyList<TreeTarget> targets, bool inputSide, int maxRuns)
        {
            Targets = targets;
            InputSide = inputSide;
            MaxRuns = maxRuns;
        }

        public IReadOnlyList<TreeTarget> Targets { get; }
        public bool InputSide { get; }
        public int MaxRuns { get; }
    }

    /// <summary>
    /// The shared meaning of gh_inspect's <c>limit</c>. The tool's default is 0, "this action's
    /// default", because a real default (100, once) collided with an explicit request for that same
    /// number. Out-of-range values are errors, never adjusted.
    /// </summary>
    public static class LimitRule
    {
        public const int LogDefault = 100;

        /// <summary>Entries the log action returns, or an error for a negative limit.</summary>
        public static bool TryResolveLog(int limit, out int count, out string error)
        {
            count = LogDefault;
            error = null;
            if (limit < 0)
            {
                error = $"limit must not be negative (got {limit.ToString(CultureInfo.InvariantCulture)}); omit it for the default of {LogDefault.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }
            if (limit > 0) count = limit;
            return true;
        }

        /// <summary>Path groups per tree: 0 means the default, otherwise exactly 1-500.</summary>
        public static bool TryResolveTree(int limit, out int maxRuns, out string error)
        {
            maxRuns = TreeSummary.DefaultMaxRuns;
            error = null;
            if (limit == 0) return true;
            if (limit < 1 || limit > TreeSummary.MaxRunsCeiling)
            {
                error = $"limit must be 1-{TreeSummary.MaxRunsCeiling.ToString(CultureInfo.InvariantCulture)} for tree (got {limit.ToString(CultureInfo.InvariantCulture)}); omit it for the default of {TreeSummary.DefaultMaxRuns.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }
            maxRuns = limit;
            return true;
        }
    }

    /// <summary>Validates every argument of gh_inspect(action='tree') before anything is read.</summary>
    public static class TreeRequestParser
    {
        public const int MaxTargets = 50;

        public static bool TryParse(string id, string ids, string param, string side, int limit,
            out TreeRequest request, out string error)
        {
            request = null;

            if (!TryParseSide(side, out bool inputSide, out error)) return false;
            if (!LimitRule.TryResolveTree(limit, out int maxRuns, out error)) return false;
            if (!TryParseTargets(id, ids, param, out var targets, out error)) return false;

            request = new TreeRequest(targets, inputSide, maxRuns);
            return true;
        }

        private static bool TryParseSide(string side, out bool inputSide, out string error)
        {
            inputSide = false;
            error = null;
            if (side == null) return true;
            var s = side.Trim();
            if (string.Equals(s, "output", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "input", StringComparison.OrdinalIgnoreCase)) { inputSide = true; return true; }
            error = $"side must be 'output' or 'input' (got '{side}')";
            return false;
        }

        private static bool TryParseTargets(string id, string ids, string param,
            out List<TreeTarget> targets, out string error)
        {
            targets = null;
            error = null;

            if (id != null && ids != null)
            {
                error = "give 'id' (one object) or 'ids' (a JSON list), not both";
                return false;
            }
            if (id == null && ids == null)
            {
                error = "tree needs 'id' (one object) or 'ids' (a JSON array of 1-" + MaxTargets.ToString(CultureInfo.InvariantCulture) +
                        " ids, each an id string or {\"id\": \"...\", \"param\": \"...\"})";
                return false;
            }

            if (id != null)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    error = "'id' is empty";
                    return false;
                }
                if (param != null && string.IsNullOrWhiteSpace(param))
                {
                    error = "'param' is empty; omit it to read every port on the side";
                    return false;
                }
                targets = new List<TreeTarget> { new TreeTarget("id", id, param) };
                return true;
            }

            if (param != null)
            {
                error = "'param' goes with 'id'; with 'ids', give a port per entry as {\"id\": \"...\", \"param\": \"...\"}";
                return false;
            }
            return TryParseIds(ids, out targets, out error);
        }

        private static bool TryParseIds(string ids, out List<TreeTarget> targets, out string error)
        {
            targets = null;
            error = null;

            JToken root;
            try
            {
                // Duplicate keys in one entry would silently keep the last; refuse them instead.
                // Strings stay strings (a nickname can look like "2026-01-01T00:00:00"), and
                // anything after the first value is a malformed call.
                root = ParseHelpers.ParseJsonKeepingText(ids, rejectDuplicateKeys: true);
            }
            catch (JsonException ex)
            {
                error = $"'ids' is not valid JSON: {ex.Message}";
                return false;
            }

            if (!(root is JArray array))
            {
                error = "'ids' must be a JSON array, e.g. [\"abc12345\", {\"id\": \"abc12345\", \"param\": \"Result\"}]";
                return false;
            }
            if (array.Count == 0 || array.Count > MaxTargets)
            {
                error = $"'ids' must hold 1-{MaxTargets.ToString(CultureInfo.InvariantCulture)} entries (got {array.Count.ToString(CultureInfo.InvariantCulture)})";
                return false;
            }

            var list = new List<TreeTarget>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                var label = $"ids[{i.ToString(CultureInfo.InvariantCulture)}]";
                if (!TryParseEntry(array[i], label, out var target, out error)) return false;
                list.Add(target);
            }
            targets = list;
            return true;
        }

        private static bool TryParseEntry(JToken entry, string label, out TreeTarget target, out string error)
        {
            target = null;
            error = null;

            if (entry.Type == JTokenType.String)
            {
                var text = entry.Value<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = $"{label}: the id is empty";
                    return false;
                }
                target = new TreeTarget(label, text, null);
                return true;
            }

            if (entry is JObject obj)
            {
                var unknown = obj.Properties().Select(p => p.Name).Where(n => n != "id" && n != "param").ToList();
                if (unknown.Count > 0)
                {
                    error = $"{label}: unknown key{(unknown.Count == 1 ? "" : "s")} {string.Join(", ", unknown.Select(u => "'" + u + "'"))}; an entry takes only 'id' and 'param'";
                    return false;
                }
                if (!(obj["id"] is JValue idValue) || idValue.Type != JTokenType.String || string.IsNullOrWhiteSpace(idValue.Value<string>()))
                {
                    error = $"{label}: 'id' must be a non-empty string";
                    return false;
                }

                string param = null;
                if (obj.TryGetValue("param", out var paramToken))
                {
                    // An integer too large for Int64 arrives as a BigInteger; it is no port's index.
                    if (paramToken.Type == JTokenType.Integer && !(((JValue)paramToken).Value is long))
                    {
                        error = $"{label}: 'param' index {paramToken.ToString(Formatting.None)} is out of range";
                        return false;
                    }
                    if (paramToken.Type == JTokenType.Integer)
                        param = paramToken.Value<long>().ToString(CultureInfo.InvariantCulture);
                    else if (paramToken.Type == JTokenType.String && !string.IsNullOrWhiteSpace(paramToken.Value<string>()))
                        param = paramToken.Value<string>();
                    else
                    {
                        error = $"{label}: 'param' must be a non-empty string or a port index";
                        return false;
                    }
                }
                target = new TreeTarget(label, idValue.Value<string>(), param);
                return true;
            }

            error = $"{label}: an entry must be an id string or {{\"id\": \"...\", \"param\": \"...\"}}";
            return false;
        }
    }
}
