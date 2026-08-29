using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Cordyceps.Core
{
    /// <summary>
    /// A single script parameter definition as supplied to gh_script (configure) via the
    /// 'inputs'/'outputs' JSON arrays: [{name, type, access}].
    /// </summary>
    public class ScriptParamDef
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Access { get; set; }
    }

    /// <summary>
    /// Host-free parsing of the gh_script 'inputs'/'outputs' JSON arrays. Distinguishes the three
    /// cases the partial-update contract depends on: side omitted (null/whitespace), side provided
    /// as a genuinely empty array ("clear it"), and malformed JSON (a hard error — previously this
    /// was silently read as an empty array, which wiped all params and wires under success:true).
    /// </summary>
    public static class ScriptParamDefs
    {
        /// <summary>
        /// Parse a param-def JSON array.
        /// Returns true with <paramref name="defs"/> = null when <paramref name="json"/> is
        /// null/whitespace (side omitted — leave it untouched); true with an empty list for a
        /// valid "[]" (clear the side); false with <paramref name="error"/> set when the JSON is
        /// malformed or not an array of objects. Never mutate anything when this returns false.
        /// </summary>
        public static bool TryParse(string json, out List<ScriptParamDef> defs, out string error)
        {
            defs = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
                return true; // Side omitted — caller leaves it (and its wires) untouched.

            JToken token;
            try
            {
                token = JToken.Parse(json);
            }
            catch (JsonException ex)
            {
                error = $"not valid JSON: {ex.Message}";
                return false;
            }

            if (!(token is JArray array))
            {
                error = $"expected a JSON array of {{name, type, access}} objects, got {token.Type}";
                return false;
            }

            var result = new List<ScriptParamDef>();
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject obj))
                {
                    error = $"expected a JSON object at index {i}, got {array[i].Type}";
                    return false;
                }

                result.Add(new ScriptParamDef
                {
                    Name = obj["name"]?.ToString() ?? "param",
                    Type = obj["type"]?.ToString() ?? "object",
                    Access = obj["access"]?.ToString() ?? ""
                });
            }

            defs = result;
            return true;
        }
    }
}
