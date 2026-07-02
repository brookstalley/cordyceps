using System.IO;

namespace Cordyceps.Core
{
    /// <summary>
    /// Pure, host-free validation for <c>rhino_scene(action='place_image')</c>.
    ///
    /// Validates the value constraints of a PictureFrame placement request — the path must be
    /// absolute, the file must exist and the dimensions must be positive. Presence/required-ness of the numeric
    /// placement params (<c>x</c>/<c>y</c>/<c>z</c>/<c>width</c>/<c>height</c>) is enforced
    /// upstream by <see cref="UnifiedToolHelpers.ValidateAction"/> (which excludes NaN doubles
    /// from the provided-params set), so this only checks the values themselves.
    ///
    /// Intentionally free of Grasshopper/RhinoCommon types (plane construction lives in the
    /// host handler) so it can be linked into and unit-tested by <c>Cordyceps.Tests</c>.
    /// </summary>
    public static class PlaceImageValidation
    {
        /// <summary>
        /// Returns an error message describing the first failed constraint, or <c>null</c> if
        /// the request is valid. The <c>!(x &gt; 0)</c> form also rejects <c>NaN</c>.
        /// </summary>
        public static string Validate(string path, double width, double height)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "path is required";
            // A relative path can pass File.Exists against the plugin process's CWD yet break
            // later as a linked texture (resolved from a different directory) — require absolute.
            if (!Path.IsPathRooted(path))
                return $"path must be an absolute path (got relative path: {path})";
            if (!File.Exists(path))
                return $"Image file not found: {path}";
            if (!(width > 0))
                return "width must be greater than 0";
            if (!(height > 0))
                return "height must be greater than 0";
            return null;
        }
    }
}
