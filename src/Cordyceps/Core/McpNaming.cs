using System.Text;

namespace Cordyceps.Core
{
    /// <summary>
    /// Host-free naming conversion for the MCP tool surface. The MCP tool name exposed to
    /// clients is derived from the C# method name (e.g. <c>GhCanvas</c> → <c>gh_canvas</c>),
    /// so any change here silently renames every tool — the mapping for the real tool names
    /// is pinned as a contract in Cordyceps.Tests, which links this file directly (it must
    /// stay free of Grasshopper/Rhino/DebugLog dependencies).
    /// </summary>
    public static class McpNaming
    {
        /// <summary>
        /// Convert a PascalCase method name to the snake_case MCP tool name.
        /// An underscore is inserted before every uppercase letter after the first
        /// character, and all letters are lowercased.
        /// </summary>
        public static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    sb.Append('_');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }
    }
}
