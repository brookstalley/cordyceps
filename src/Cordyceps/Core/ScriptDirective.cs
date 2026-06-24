using System;

namespace Cordyceps.Core
{
    /// <summary>
    /// Pure helpers for the Rhino 8 script "language specifier" directive that must occupy
    /// the first line of a script component's source so Rhino can infer the language:
    ///   Python:  <c>#! python 3</c> / <c>#! python 2</c>
    ///   C#:      <c>// #! csharp</c>   (comment-prefixed — <c>#</c> is not a C# comment)
    ///
    /// Replacing the whole body via <c>SetSource()</c> without this line makes the component
    /// fail at solve time with "Can not determine input code language" and emit no geometry.
    /// These helpers are intentionally free of Grasshopper types so they can be unit-tested.
    /// </summary>
    public static class ScriptDirective
    {
        // Unicode BOM — may lead the first line of a source loaded from disk.
        private const char Bom = '\uFEFF';

        /// <summary>
        /// Return the code to hand to <c>SetSource()</c>, preserving the script component's
        /// language directive when the new body omits it.
        /// <list type="bullet">
        /// <item>If <paramref name="newCode"/> already carries a directive on its first line,
        /// it is returned unchanged — the caller's explicit choice wins.</item>
        /// <item>Otherwise, if <paramref name="existingSource"/> has a directive on its first
        /// line, that exact line is prepended to <paramref name="newCode"/>.</item>
        /// <item>If there is nothing to preserve (no existing directive, or empty/null inputs),
        /// <paramref name="newCode"/> is returned unchanged.</item>
        /// </list>
        /// </summary>
        public static string Preserve(string existingSource, string newCode)
        {
            if (string.IsNullOrEmpty(newCode))
                return newCode;
            if (HasDirective(newCode))
                return newCode;

            var directive = ExtractDirective(existingSource);
            if (directive == null)
                return newCode;

            return directive + "\n" + newCode;
        }

        /// <summary>True when the first line of <paramref name="code"/> is a language directive.</summary>
        public static bool HasDirective(string code) => ExtractDirective(code) != null;

        /// <summary>The component type name of Rhino 8's unified "Script" component.</summary>
        private const string UnifiedScriptComponentTypeName = "ScriptComponent";

        /// <summary>
        /// Returns a caller-facing warning when a <c>gh_script(set)</c> would leave a component
        /// unable to determine its language, or <c>null</c> when the source is fine.
        ///
        /// <para>Rhino 8's unified <b>Script</b> component (<c>RhinoCodePluginGH.Components.ScriptComponent</c>,
        /// type name <c>ScriptComponent</c>) infers its language from a first-line directive. A body
        /// with no directive leaves it with no language, and it fails at solve time with
        /// "Can not determine input code language" — but <c>SetSource</c> reports no error, so the
        /// caller is left with a silently-broken component (GitHub issue #15). The dedicated
        /// <c>C# Script</c>/<c>CSharpComponent</c> and friends carry a concrete language and don't
        /// need a directive, so they never trigger this.</para>
        ///
        /// <para><paramref name="finalSource"/> is the body actually handed to <c>SetSource</c>
        /// (i.e. after <see cref="Preserve"/> has re-applied any existing directive), so a warning
        /// fires only when there was no directive to preserve and the caller supplied none.</para>
        /// </summary>
        public static string LanguageWarning(string componentTypeName, string finalSource)
        {
            if (componentTypeName != UnifiedScriptComponentTypeName)
                return null;
            if (HasDirective(finalSource))
                return null;

            return "Source was stored, but this unified Script component has no language set, so it "
                + "will fail at solve time with \"Can not determine input code language\" and emit no "
                + "geometry. Start your code with a language directive on line 1 — \"#! python 3\" "
                + "(or \"#! python 2\") for Python, \"// #! csharp\" for C# — then call gh_script(set) "
                + "again. (The dedicated \"C# Script\" / \"Python 3 Script\" components don't need "
                + "this.)";
        }

        /// <summary>
        /// Return the first-line language directive of <paramref name="source"/> verbatim
        /// (including any original leading BOM/whitespace), or null when the first line is not
        /// a directive. Detection is a heuristic: the trimmed first line starts with <c>#!</c>
        /// (Python) or starts with <c>//</c> and contains <c>#!</c> (C#).
        /// </summary>
        public static string ExtractDirective(string source)
        {
            var first = FirstLine(source);
            if (first == null)
                return null;

            var trimmed = first.TrimStart(Bom, ' ', '\t');
            bool isDirective =
                trimmed.StartsWith("#!", StringComparison.Ordinal) ||
                (trimmed.StartsWith("//", StringComparison.Ordinal) && trimmed.Contains("#!", StringComparison.Ordinal));

            return isDirective ? first : null;
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;
            int idx = s.IndexOfAny(new[] { '\r', '\n' });
            return idx < 0 ? s : s.Substring(0, idx);
        }
    }
}
