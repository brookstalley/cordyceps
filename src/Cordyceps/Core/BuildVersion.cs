using System;

namespace Cordyceps.Core
{
    /// <summary>
    /// Which version string identifies this build to a client.
    ///
    /// <para>The assembly version cannot: it has four numeric fields, so a pre-release built with
    /// <c>-p:Version=1.5.0-rc.2</c> reports the same <c>1.5.0.0</c> as <c>rc.1</c> did, and a tester
    /// asked to verify a fix has no way to confirm they are running the build that contains it. The
    /// informational version keeps the whole string — pre-release tag and, because the project
    /// stamps a <c>SourceRevisionId</c>, the build it came from — which is what distinguishes two
    /// builds of the same release.</para>
    /// </summary>
    public static class BuildVersion
    {
        /// <summary>Reported when the assembly carries no version information at all.</summary>
        public const string Unknown = "1.0.0";

        /// <summary>
        /// The most identifying version available: the informational version when the build has
        /// one, else the assembly version, else <see cref="Unknown"/>.
        /// </summary>
        /// <param name="informationalVersion">
        /// The assembly's informational version (<c>AssemblyInformationalVersionAttribute</c>), or
        /// null when the assembly has none.
        /// </param>
        /// <param name="assemblyVersion">The assembly version, or null when unavailable.</param>
        public static string Describe(string informationalVersion, Version assemblyVersion)
        {
            if (!string.IsNullOrWhiteSpace(informationalVersion))
                return informationalVersion.Trim();

            return assemblyVersion?.ToString() ?? Unknown;
        }
    }
}
