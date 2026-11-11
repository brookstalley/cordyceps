using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free reader of the port names a script's source declares: the parameters of a C#
    /// <c>RunScript</c> signature, inputs plain and outputs marked <c>ref</c>/<c>out</c>.
    /// </summary>
    /// <remarks>
    /// <c>gh_script(action='set')</c> syncs a component's ports to these names, and the same names
    /// decide, before anything is written, whether that sync would remove a port (see
    /// <see cref="PortRemoval"/>). One reader for both keeps the prediction and the sync from
    /// disagreeing about what the code declares. Pure (BCL only) so it is linked into the test project.
    /// </remarks>
    public static class ScriptSignature
    {
        private static readonly Regex RunScriptSignature = new Regex(@"RunScript\s*\(([^)]*)\)", RegexOptions.CultureInvariant);

        /// <summary>
        /// The input and output names <paramref name="code"/> declares, or <c>null</c> when its
        /// ports are not derived from the code: empty code, a Python component (its ports are
        /// managed by zooming, not by the source), or source with no <c>RunScript</c> signature.
        /// </summary>
        /// <param name="componentTypeName">The script component's .NET type name.</param>
        /// <param name="code">The source as the caller sent it.</param>
        public static (List<string> inputs, List<string> outputs)? Parse(string componentTypeName, string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;

            if (componentTypeName != null && componentTypeName.Contains("Python"))
                return null;

            // The unified Script component's type name names no language, so the source has to:
            // Python there declares `def RunScript(self, x)`, whose untyped parameters would read
            // as no inputs at all, and a sync to that would remove every port.
            if (IsPythonSource(code))
                return null;

            var match = RunScriptSignature.Match(code);
            if (!match.Success)
                return null;

            var inputs = new List<string>();
            var outputs = new List<string>();

            foreach (var paramStr in SplitParameters(match.Groups[1].Value))
            {
                var trimmed = paramStr.Trim();
                if (trimmed.Length == 0)
                    continue;

                var tokens = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2)
                    continue;

                if (tokens[0] == "ref" || tokens[0] == "out")
                    outputs.Add(tokens[tokens.Length - 1]);
                else
                    inputs.Add(tokens[tokens.Length - 1]);
            }

            return (inputs, outputs);
        }

        private static readonly Regex PythonRunScript = new Regex(@"\bdef\s+RunScript\s*\(", RegexOptions.CultureInvariant);

        /// <summary>
        /// True when <paramref name="code"/> is Python: a Python directive on its first line
        /// (<c>#!</c> with no leading <c>//</c>, which is C#'s form), or a <c>def RunScript</c>.
        /// </summary>
        public static bool IsPythonSource(string code)
        {
            if (string.IsNullOrEmpty(code))
                return false;
            return ScriptDirective.HasPythonDirective(code) || PythonRunScript.IsMatch(code);
        }

        /// <summary>
        /// Split a parameter list on its top-level commas, so a generic type such as
        /// <c>Dictionary&lt;string, int&gt;</c> stays one parameter.
        /// </summary>
        public static List<string> SplitParameters(string signature)
        {
            var result = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < signature.Length; i++)
            {
                char c = signature[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(signature.Substring(start, i - start));
                    start = i + 1;
                }
            }

            if (start < signature.Length)
                result.Add(signature.Substring(start));

            return result;
        }
    }
}
