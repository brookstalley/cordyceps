using System;
using System.Globalization;
using System.Text.Json;

namespace Cordyceps.Core
{
    /// <summary>
    /// Converts between JSON Schema types and CLR types, with cross-type coercion
    /// for MCP clients that may send string-encoded numbers or vice versa.
    /// </summary>
    public static class JsonTypeConverter
    {
        /// <summary>
        /// Maps a CLR type to its JSON Schema type string.
        /// </summary>
        public static string GetJsonType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(double) || type == typeof(float)) return "number";
            if (type == typeof(bool)) return "boolean";
            return "string";
        }

        /// <summary>
        /// Converts a JsonElement to the specified CLR type, with cross-type coercion.
        /// Handles cases where MCP clients send string-encoded numbers ("300" instead of 300).
        /// </summary>
        public static object ConvertJsonValue(JsonElement element, Type targetType)
        {
            if (targetType == typeof(string))
                return element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : element.GetRawText();

            if (targetType == typeof(int))
            {
                if (element.ValueKind == JsonValueKind.Number)
                {
                    if (element.TryGetInt32(out var i))
                        return i;
                    // Accept whole-valued doubles (clients often send 300.0 for 300);
                    // reject true fractions with a clear error instead of a FormatException.
                    if (element.TryGetDouble(out var d) && !double.IsNaN(d) && !double.IsInfinity(d)
                        && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
                        return (int)d;
                    throw new InvalidOperationException(
                        $"Cannot convert number '{element}' to int: not a whole number in the int range");
                }
                if (element.ValueKind == JsonValueKind.String &&
                    int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                    return intVal;
                throw new InvalidOperationException(
                    $"Cannot convert {element.ValueKind} '{element}' to int");
            }

            if (targetType == typeof(long))
            {
                if (element.ValueKind == JsonValueKind.Number)
                {
                    if (element.TryGetInt64(out var l))
                        return l;
                    // Accept whole-valued doubles (300.0 → 300); reject true fractions.
                    // Upper bound is exclusive: long.MaxValue rounds UP to 2^63 as a double,
                    // so d <= long.MaxValue would admit 2^63 and overflow on the cast.
                    if (element.TryGetDouble(out var d) && !double.IsNaN(d) && !double.IsInfinity(d)
                        && d == Math.Floor(d) && d >= long.MinValue && d < 9223372036854775808.0)
                        return (long)d;
                    throw new InvalidOperationException(
                        $"Cannot convert number '{element}' to long: not a whole number in the long range");
                }
                if (element.ValueKind == JsonValueKind.String &&
                    long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var longVal))
                    return longVal;
                throw new InvalidOperationException(
                    $"Cannot convert {element.ValueKind} '{element}' to long");
            }

            if (targetType == typeof(double))
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetDouble();
                if (element.ValueKind == JsonValueKind.String &&
                    double.TryParse(element.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var dblVal))
                    return dblVal;
                throw new InvalidOperationException(
                    $"Cannot convert {element.ValueKind} '{element}' to double");
            }

            if (targetType == typeof(float))
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return (float)element.GetDouble();
                if (element.ValueKind == JsonValueKind.String &&
                    float.TryParse(element.GetString(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var fltVal))
                    return fltVal;
                throw new InvalidOperationException(
                    $"Cannot convert {element.ValueKind} '{element}' to float");
            }

            if (targetType == typeof(bool))
            {
                if (element.ValueKind == JsonValueKind.True) return true;
                if (element.ValueKind == JsonValueKind.False) return false;
                if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var boolVal))
                    return boolVal;
                // Number → bool: nonzero is true, zero is false, for ANY numeric value.
                // (GetInt32 here used to throw FormatException on non-int32 numbers like 1.5
                // or 2^40 — number→bool never fails now, matching C-style truthiness.)
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetDouble() != 0;
                throw new InvalidOperationException(
                    $"Cannot convert {element.ValueKind} '{element}' to bool");
            }

            return element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();
        }
    }
}
