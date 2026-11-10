using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Cordyceps.Core.Naming;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free "validate and bind" step for an MCP <c>tools/call</c>: turns the JSON
    /// <c>arguments</c> object into the positional argument array for a tool method.
    /// Any argument name that is not a parameter of the method is rejected (never silently
    /// dropped), naming every offender with a "did you mean" and the valid parameter names.
    /// Failures are <see cref="InvalidOperationException"/>, the same type used for
    /// "Missing required parameter", so they surface as the standard tool-execution error.
    /// </summary>
    public static class ToolArgumentBinder
    {
        public static object[] Bind(MethodInfo method, JsonElement arguments)
        {
            var parameters = method.GetParameters();

            // Absent (default), null and {} are all valid "no arguments".
            if (arguments.ValueKind != JsonValueKind.Undefined &&
                arguments.ValueKind != JsonValueKind.Null &&
                arguments.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Invalid arguments: 'arguments' must be a JSON object of parameter names to values, got {arguments.ValueKind.ToString().ToLowerInvariant()}.");
            }

            if (arguments.ValueKind == JsonValueKind.Object)
                RejectUnknown(parameters, arguments);

            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (arguments.ValueKind == JsonValueKind.Object &&
                    arguments.TryGetProperty(param.Name, out var argVal))
                {
                    args[i] = JsonTypeConverter.ConvertJsonValue(argVal, param.ParameterType);
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = param.DefaultValue;
                }
                else
                {
                    throw new InvalidOperationException($"Missing required parameter: {param.Name}");
                }
            }
            return args;
        }

        private static void RejectUnknown(System.Reflection.ParameterInfo[] parameters, JsonElement arguments)
        {
            var names = parameters.Select(p => p.Name).ToList();
            var known = new HashSet<string>(names, StringComparer.Ordinal);
            var unknown = arguments.EnumerateObject()
                .Select(p => p.Name)
                .Where(n => !known.Contains(n))
                .ToList();
            if (unknown.Count == 0) return;

            var parts = unknown.Select(u =>
            {
                var s = Suggest(u, names);
                return s == null ? $"'{u}'" : $"'{u}' (did you mean '{s}'?)";
            });
            throw new InvalidOperationException(
                $"Unknown argument{(unknown.Count > 1 ? "s" : "")}: {string.Join(", ", parts)}. " +
                $"Valid parameters: {string.Join(", ", names)}.");
        }

        /// <summary>Same name ignoring case, else the nearest within a small edit distance.</summary>
        internal static string Suggest(string unknown, IReadOnlyList<string> candidates)
        {
            var ci = candidates.FirstOrDefault(c => string.Equals(c, unknown, StringComparison.OrdinalIgnoreCase));
            if (ci != null) return ci;

            int limit = unknown.Length >= 5 ? 2 : 1;
            string best = null;
            int bestDist = int.MaxValue;
            foreach (var c in candidates)
            {
                int d = NameSuggestions.EditDistance(unknown.ToLowerInvariant(), c.ToLowerInvariant());
                if (d <= limit && d < bestDist) { best = c; bestDist = d; }
            }
            return best;
        }
    }
}
