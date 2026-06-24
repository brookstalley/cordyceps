using System.IO;

namespace Cordyceps.Core
{
    /// <summary>
    /// Pure, host-free policy for how <c>gh_document(action='save')</c> writes a Grasshopper
    /// definition to disk via GH_IO's
    /// <c>GH_Archive.WriteToFile(path, overwrite, rememberPath)</c>.
    ///
    /// <para>Both supported formats — <c>.gh</c> (binary) and <c>.ghx</c> (XML) — MUST be written
    /// with <c>overwrite=true</c> so a repeated Save replaces the existing file. The historical bug
    /// (GitHub issue #14) passed <c>overwrite=false</c> on the <c>.gh</c> branch only, so every save
    /// over an existing <c>.gh</c> returned a bare "Failed to write file" — the format determined
    /// the overwrite flag, which is exactly the defect this helper removes.</para>
    ///
    /// <para><c>rememberPath=true</c> mirrors Grasshopper's File→Save: a successful save adopts the
    /// path as the document's current path, so a subsequent in-app save targets the same file.</para>
    ///
    /// Intentionally free of Grasshopper/GH_IO types (the <c>GH_Archive</c> call lives in the host
    /// tool) so it can be linked into and unit-tested by <c>Cordyceps.Tests</c>.
    /// </summary>
    public static class GhArchiveSave
    {
        /// <summary>
        /// Maps a save path's extension to its human-facing format label. Returns <c>true</c> and
        /// sets <paramref name="format"/> to <c>"binary"</c> for <c>.gh</c> or <c>"XML"</c> for
        /// <c>.ghx</c> (case-insensitive); returns <c>false</c> with a null <paramref name="format"/>
        /// for any other or missing extension.
        /// </summary>
        public static bool TryGetFormat(string path, out string format)
        {
            format = null;
            if (string.IsNullOrEmpty(path))
                return false;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".gh":
                    format = "binary";
                    return true;
                case ".ghx":
                    format = "XML";
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Flags to pass to <c>GH_Archive.WriteToFile</c> for any valid save, independent of format:
        /// <c>overwrite=true</c> (Save must replace an existing file — guards issue #14) and
        /// <c>rememberPath=true</c> (the saved path becomes the document's current path, matching
        /// File→Save).
        /// </summary>
        public static (bool overwrite, bool rememberPath) WriteFlags => (true, true);
    }
}
