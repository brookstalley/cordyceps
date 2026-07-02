using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Cordyceps.Core
{
    /// <summary>
    /// Metadata for an action within a unified tool
    /// </summary>
    public class ActionInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string[] Required { get; set; } = Array.Empty<string>();
        public string[] Optional { get; set; } = Array.Empty<string>();
        public string Example { get; set; }
        public string[] Tips { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Metadata for a unified tool
    /// </summary>
    public class UnifiedToolInfo
    {
        public string ToolName { get; set; }
        public string Description { get; set; }
        public Dictionary<string, ActionInfo> Actions { get; set; } = new Dictionary<string, ActionInfo>();
        public string[] Notes { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Helper methods for unified tool pattern
    /// </summary>
    public static class UnifiedToolHelpers
    {
        /// <summary>
        /// Generate help response for a unified tool
        /// </summary>
        public static string GenerateHelp(UnifiedToolInfo toolInfo)
        {
            var actions = new Dictionary<string, object>();
            foreach (var kvp in toolInfo.Actions)
            {
                var actionObj = new Dictionary<string, object>
                {
                    ["description"] = kvp.Value.Description
                };

                if (kvp.Value.Required.Length > 0)
                    actionObj["required"] = kvp.Value.Required;

                if (kvp.Value.Optional.Length > 0)
                    actionObj["optional"] = kvp.Value.Optional;

                if (!string.IsNullOrEmpty(kvp.Value.Example))
                    actionObj["example"] = kvp.Value.Example;

                if (kvp.Value.Tips.Length > 0)
                    actionObj["tips"] = kvp.Value.Tips;

                actions[kvp.Key] = actionObj;
            }

            var result = new Dictionary<string, object>
            {
                ["tool"] = toolInfo.ToolName,
                ["description"] = toolInfo.Description,
                ["actions"] = actions
            };

            if (toolInfo.Notes.Length > 0)
                result["notes"] = toolInfo.Notes;

            return JsonConvert.SerializeObject(result, Formatting.None);
        }

        /// <summary>
        /// Validate required parameters for an action.
        /// Returns null if valid, or an error response string if invalid.
        /// </summary>
        public static string ValidateAction(
            UnifiedToolInfo toolInfo,
            string action,
            Dictionary<string, object> providedParams)
        {
            if (string.IsNullOrEmpty(action))
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = "Missing required parameter: action",
                    availableActions = toolInfo.Actions.Keys.ToArray(),
                    hint = $"Use action='help' to see all {toolInfo.ToolName} actions"
                });
            }

            // Case-insensitive like the dispatch (which lowercases): try the exact key first,
            // then the lowercased form, so "Add"/"ADD" validate the same action they dispatch to.
            if (!toolInfo.Actions.TryGetValue(action, out var actionInfo) &&
                !toolInfo.Actions.TryGetValue(action.ToLowerInvariant(), out actionInfo))
            {
                return JsonConvert.SerializeObject(new
                {
                    success = false,
                    error = $"Unknown action: '{action}'",
                    availableActions = toolInfo.Actions.Keys.ToArray(),
                    hint = $"Use action='help' to see all {toolInfo.ToolName} actions"
                });
            }

            // Check required parameters
            var missing = actionInfo.Required
                .Where(p => !providedParams.ContainsKey(p) || providedParams[p] == null)
                .ToArray();

            if (missing.Length > 0)
            {
                var result = new Dictionary<string, object>
                {
                    ["success"] = false,
                    ["error"] = $"Missing required params for '{action}': {string.Join(", ", missing)}",
                    ["required"] = actionInfo.Required
                };

                if (actionInfo.Optional.Length > 0)
                    result["optional"] = actionInfo.Optional;

                if (!string.IsNullOrEmpty(actionInfo.Example))
                    result["example"] = actionInfo.Example;

                return JsonConvert.SerializeObject(result);
            }

            return null; // Valid
        }

        /// <summary>
        /// Parse params from the raw parameter dictionary, handling type conversions
        /// </summary>
        public static T GetParam<T>(Dictionary<string, object> parameters, string name, T defaultValue = default)
        {
            if (!parameters.TryGetValue(name, out var value) || value == null)
                return defaultValue;

            try
            {
                // Handle direct type match
                if (value is T typed)
                    return typed;

                // Handle numeric conversions
                if (typeof(T) == typeof(double) && value is IConvertible)
                    return (T)(object)Convert.ToDouble(value);

                if (typeof(T) == typeof(int) && value is IConvertible)
                    return (T)(object)Convert.ToInt32(value);

                if (typeof(T) == typeof(bool))
                {
                    if (value is bool b)
                        return (T)(object)b;
                    if (value is string s)
                        return (T)(object)(s.ToLower() == "true" || s == "1");
                }

                if (typeof(T) == typeof(string))
                    return (T)(object)value.ToString();

                // For complex types, try JSON conversion
                var json = JsonConvert.SerializeObject(value);
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or JsonException)
            {
                // Expected when the supplied value can't be coerced to T (bad string, wrong shape,
                // out-of-range); fall back to the default. Unexpected exceptions propagate.
                return defaultValue;
            }
        }

        /// <summary>
        /// Build a dictionary of parameters from named values (for easier action dispatch)
        /// </summary>
        public static Dictionary<string, object> BuildParams(params (string name, object value)[] pairs)
        {
            var dict = new Dictionary<string, object>();
            foreach (var (name, value) in pairs)
            {
                if (value != null)
                    dict[name] = value;
            }
            return dict;
        }
    }
}
