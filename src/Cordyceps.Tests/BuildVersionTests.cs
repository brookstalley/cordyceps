using System;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// Unit tests for <see cref="BuildVersion"/>, which decides what the MCP <c>initialize</c>
    /// response calls this build.
    /// </summary>
    public class BuildVersionTests
    {
        [Fact]
        public void Describe_PrefersTheInformationalVersion()
        {
            // The point of the preference: two pre-releases of the same version are otherwise
            // indistinguishable, because both compile to the same four-field assembly version.
            var version = BuildVersion.Describe("1.5.0-rc.2", new Version(1, 5, 0, 0));

            Assert.Equal("1.5.0-rc.2", version);
        }

        [Fact]
        public void Describe_KeepsTheBuildStampTheProjectEmbeds()
        {
            var version = BuildVersion.Describe("1.5.0-rc.2+build20260827120000", new Version(1, 5, 0, 0));

            Assert.Equal("1.5.0-rc.2+build20260827120000", version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Describe_NoInformationalVersion_FallsBackToTheAssemblyVersion(string informational)
        {
            var version = BuildVersion.Describe(informational, new Version(1, 4, 12, 0));

            Assert.Equal("1.4.12.0", version);
        }

        [Fact]
        public void Describe_NeitherAvailable_ReportsTheUnknownPlaceholder()
        {
            Assert.Equal(BuildVersion.Unknown, BuildVersion.Describe(null, null));
        }

        [Fact]
        public void Describe_TrimsSurroundingWhitespace()
        {
            Assert.Equal("1.5.0-rc.2", BuildVersion.Describe("  1.5.0-rc.2  ", new Version(1, 5, 0, 0)));
        }
    }
}
