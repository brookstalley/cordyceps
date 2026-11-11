using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free parsing helpers shared by MCP tool implementations. Extracted from
    /// <see cref="ToolHelpers"/> so the wire-format parsing logic can be unit-tested —
    /// this file is linked directly into Cordyceps.Tests, so it must stay free of
    /// Grasshopper/Rhino/DebugLog dependencies (BCL + Newtonsoft only).
    /// <see cref="ToolHelpers"/> keeps thin forwarders so existing call sites are unchanged.
    /// </summary>
    public static class ParseHelpers
    {
        #region GUID Parsing

        /// <summary>
        /// Try to parse a string as a GUID.
        /// </summary>
        /// <param name="id">The string to parse</param>
        /// <param name="guid">Output: the parsed GUID if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseGuid(string id, out Guid guid, out string error)
        {
            if (string.IsNullOrEmpty(id))
            {
                guid = Guid.Empty;
                error = "Component ID is required";
                return false;
            }

            if (!Guid.TryParse(id, out guid))
            {
                error = "Invalid component ID format";
                return false;
            }
            error = null;
            return true;
        }

        #endregion

        #region JSON Deserialization Helpers

        /// <summary>
        /// Parse one JSON value from an argument that can carry names (object nicknames, group
        /// names, parameter names), keeping every string a string. By default Json.NET turns
        /// date-shaped text such as <c>"2026-01-01T00:00:00"</c> into a Date token, which a caller
        /// then reads back as a current-culture date rendering: a nickname of that shape would
        /// resolve to nothing. Text after the first value is an error, not something to ignore.
        /// Throws <see cref="JsonReaderException"/> on malformed JSON (and on a repeated key when
        /// <paramref name="rejectDuplicateKeys"/> is set); each caller words its own error.
        /// </summary>
        public static JToken ParseJsonKeepingText(string json, bool rejectDuplicateKeys = false)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None })
            {
                var settings = rejectDuplicateKeys
                    ? new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }
                    : null;
                var token = JToken.Load(reader, settings);
                if (reader.Read())
                    throw new JsonReaderException("unexpected text after the JSON value");
                return token;
            }
        }

        /// <summary>
        /// The text of one field read from <see cref="ParseJsonKeepingText"/>: a string as it is; a
        /// missing field or JSON null as null; a number or boolean in the invariant culture; an
        /// object or array as compact JSON.
        /// </summary>
        public static string TextOf(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined) return null;
            if (token.Type == JTokenType.String) return (string)token;
            if (token is JValue v) return Convert.ToString(v.Value, CultureInfo.InvariantCulture);
            return token.ToString(Formatting.None);
        }

        /// <summary>
        /// Try to deserialize a JSON string to a list with null checking.
        /// </summary>
        /// <typeparam name="T">Element type</typeparam>
        /// <param name="json">JSON string to parse</param>
        /// <param name="result">Output: the parsed list if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if deserialization succeeded and result is not null, false otherwise</returns>
        public static bool TryDeserializeList<T>(string json, out List<T> result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "JSON input is required";
                return false;
            }

            try
            {
                result = JsonConvert.DeserializeObject<List<T>>(json);
                if (result == null)
                {
                    error = "Failed to parse JSON array";
                    return false;
                }
                return true;
            }
            catch (JsonException ex)
            {
                error = $"Invalid JSON format: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Try to deserialize a JSON string to an array with null checking.
        /// </summary>
        /// <typeparam name="T">Element type</typeparam>
        /// <param name="json">JSON string to parse</param>
        /// <param name="result">Output: the parsed array if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if deserialization succeeded and result is not null, false otherwise</returns>
        public static bool TryDeserializeArray<T>(string json, out T[] result, out string error)
        {
            result = null;
            error = null;

            if (string.IsNullOrEmpty(json))
            {
                error = "JSON input is required";
                return false;
            }

            try
            {
                result = JsonConvert.DeserializeObject<T[]>(json);
                if (result == null)
                {
                    error = "Failed to parse JSON array";
                    return false;
                }
                return true;
            }
            catch (JsonException ex)
            {
                error = $"Invalid JSON format: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Try to parse a JSON array of GUIDs.
        /// </summary>
        /// <param name="json">JSON array of GUID strings</param>
        /// <param name="guids">Output: list of parsed GUIDs if successful</param>
        /// <param name="error">Output: error message if failed</param>
        /// <returns>True if all GUIDs parsed successfully, false otherwise</returns>
        public static bool TryParseGuidArray(string json, out List<Guid> guids, out string error)
        {
            guids = null;

            if (!TryDeserializeArray<string>(json, out var idStrings, out error))
                return false;

            guids = new List<Guid>(idStrings.Length);
            foreach (var idStr in idStrings)
            {
                if (!Guid.TryParse(idStr, out var guid))
                {
                    error = $"Invalid GUID in array: {idStr}";
                    guids = null;
                    return false;
                }
                guids.Add(guid);
            }

            return true;
        }

        #endregion

        #region Color Utilities

        /// <summary>
        /// Convert a System.Drawing.Color to hex string (#RRGGBB format).
        /// </summary>
        public static string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        /// <summary>
        /// Parse color from hex (#RRGGBB or RRGGBB), RGB (R,G,B), or named color (Red, Blue, etc.) format.
        /// </summary>
        /// <param name="colorStr">Color string to parse</param>
        /// <param name="color">Output: parsed color if successful</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseColor(string colorStr, out Color color)
        {
            color = Color.Black;

            if (string.IsNullOrEmpty(colorStr))
                return false;

            // Try hex format: #RRGGBB or RRGGBB
            var hexStr = colorStr;
            if (hexStr.StartsWith("#"))
                hexStr = hexStr.Substring(1);

            if (hexStr.Length == 6)
            {
                try
                {
                    int r = Convert.ToInt32(hexStr.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hexStr.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hexStr.Substring(4, 2), 16);
                    color = Color.FromArgb(r, g, b);
                    return true;
                }
                catch (FormatException)
                {
                    // Not valid hex digits; fall through to try RGB / named formats.
                }
            }

            // Try RGB format: R,G,B
            var parts = colorStr.Split(',');
            if (parts.Length == 3)
            {
                if (int.TryParse(parts[0].Trim(), out int r) &&
                    int.TryParse(parts[1].Trim(), out int g) &&
                    int.TryParse(parts[2].Trim(), out int b))
                {
                    r = Math.Max(0, Math.Min(255, r));
                    g = Math.Max(0, Math.Min(255, g));
                    b = Math.Max(0, Math.Min(255, b));
                    color = Color.FromArgb(r, g, b);
                    return true;
                }
            }

            // Try named color format (Red, Blue, Green, etc.).
            // Color.FromName never throws for a non-null string — an unrecognized name yields
            // an empty color with IsKnownColor == false, so no try/catch is needed here.
            var namedColor = Color.FromName(colorStr);
            if (namedColor.IsKnownColor)
            {
                color = namedColor;
                return true;
            }

            return false;
        }

        /// <summary>The refusal for a colour that does not parse, the same wherever a colour is set.</summary>
        public static string InvalidColorError(string colorStr) => $"Invalid color: {colorStr}";

        /// <summary>
        /// Parse a colour argument that may be left out. Null or empty is "not given": true, with
        /// <paramref name="color"/> null. A colour that parses (<see cref="TryParseColor"/>) is true
        /// with the colour. Anything else is false with <paramref name="error"/> set, never read as
        /// "not given", so a call refuses it before it changes anything.
        /// </summary>
        public static bool TryParseOptionalColor(string colorStr, out Color? color, out string error)
        {
            color = null;
            error = null;
            if (string.IsNullOrEmpty(colorStr))
                return true;
            if (TryParseColor(colorStr, out var parsed))
            {
                color = parsed;
                return true;
            }
            error = InvalidColorError(colorStr);
            return false;
        }

        #endregion

        #region Boolean Parsing

        /// <summary>
        /// Parse a boolean string value with a default fallback.
        /// Accepts: "true", "false", "1", "0" (case-insensitive)
        /// </summary>
        /// <param name="value">String value to parse</param>
        /// <param name="defaultValue">Default value if parsing fails</param>
        /// <returns>Parsed boolean or default value</returns>
        public static bool ParseBool(string value, bool defaultValue = false)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            if (bool.TryParse(value, out bool result))
                return result;

            // Also accept "1"/"0"
            if (value == "1") return true;
            if (value == "0") return false;

            return defaultValue;
        }

        /// <summary>
        /// Strictly parse a boolean string. Accepts true/false, 1/0, yes/no (case-insensitive,
        /// trimmed); anything else fails. Use this where an unrecognized value must surface as a
        /// validation error; use <see cref="ParseBool"/> where a default is genuinely wanted.
        /// </summary>
        /// <param name="value">String value to parse</param>
        /// <param name="result">Output: parsed boolean if successful</param>
        /// <returns>True if the value was recognized, false otherwise</returns>
        public static bool TryParseBool(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                    result = true;
                    return true;
                case "false":
                case "0":
                case "no":
                    result = false;
                    return true;
                default:
                    return false;
            }
        }

        #endregion

        #region Coordinate Parsing

        /// <summary>
        /// Try to parse three coordinates from a comma-separated string "x,y,z".
        /// Components are parsed with the invariant culture ('.' decimal separator) so the
        /// wire format is stable regardless of the host machine's locale — current-culture
        /// parsing on comma-decimal locales would mis-split "10.5,0,3.2" catastrophically.
        /// This is the host-free core of <see cref="ToolHelpers.TryParsePoint3d"/>.
        /// </summary>
        /// <param name="value">String in "x,y,z" format</param>
        /// <param name="x">Output: first coordinate if successful</param>
        /// <param name="y">Output: second coordinate if successful</param>
        /// <param name="z">Output: third coordinate if successful</param>
        /// <returns>True if parsing succeeded, false otherwise</returns>
        public static bool TryParseXyz(string value, out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (string.IsNullOrEmpty(value)) return false;

            var parts = value.Split(',');
            if (parts.Length != 3) return false;

            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
            if (!double.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z)) return false;

            return true;
        }

        #endregion
    }
}
