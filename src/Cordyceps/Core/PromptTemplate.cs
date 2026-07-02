using System.Collections.Generic;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free placeholder substitution for MCP prompt templates. Extracted from
    /// <see cref="Cordyceps.Prompts.PromptRegistry"/> so the substitution logic can be
    /// unit-tested — this file is linked directly into Cordyceps.Tests, so it must stay
    /// free of Grasshopper/Rhino/DebugLog dependencies (BCL only).
    /// </summary>
    public static class PromptTemplate
    {
        /// <summary>
        /// Fill a prompt template's <c>{placeholder}</c> slots. Provided argument values are
        /// substituted first; any declared argument whose placeholder remains unfilled is
        /// rendered as an explicit <c>[argname]</c> marker (e.g. <c>{goal}</c> → <c>[goal]</c>).
        /// The bracketed marker was chosen so the reading agent sees an intentional slot to
        /// fill in, rather than the bare argument name masquerading as prose mid-sentence
        /// (the old behavior rendered an unfilled <c>{goal}</c> as the literal word "goal").
        /// </summary>
        /// <param name="template">The template text containing <c>{name}</c> placeholders</param>
        /// <param name="argValues">Provided argument values keyed by name (may be null)</param>
        /// <param name="declaredArgNames">The prompt's declared argument names (may be null)</param>
        /// <returns>The rendered template, or null if the template is null</returns>
        public static string Render(
            string template,
            IReadOnlyDictionary<string, string> argValues,
            IEnumerable<string> declaredArgNames)
        {
            if (template == null)
                return null;

            var result = template;

            // Substitute provided arguments
            if (argValues != null)
            {
                foreach (var arg in argValues)
                {
                    result = result.Replace($"{{{arg.Key}}}", arg.Value);
                }
            }

            // Render any remaining declared placeholders as explicit [argname] slots
            if (declaredArgNames != null)
            {
                foreach (var name in declaredArgNames)
                {
                    result = result.Replace($"{{{name}}}", $"[{name}]");
                }
            }

            return result;
        }
    }
}
