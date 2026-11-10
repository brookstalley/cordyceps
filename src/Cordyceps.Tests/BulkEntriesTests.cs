using System.Globalization;
using System.Linq;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// The JSON arrays of gh_canvas move and gh_wire connect. Their entries carry object names, so
    /// every name must reach the resolver as the caller wrote it (a nickname can look like a date);
    /// a malformed array fails the call, and an unusable entry is carried with its own error.
    /// </summary>
    public class BulkEntriesTests
    {
        // ---- moves ---------------------------------------------------------------------------------

        [Fact]
        public void Moves_DateShapedId_StaysTheCallersText()
        {
            Assert.True(BulkEntries.TryParseMoves(
                "[{\"id\": \"2026-01-01T00:00:00\", \"x\": 10, \"y\": 20}, {\"id\": \"2026-01-01T00:00:00Z\", \"x\": 1.5, \"y\": -2}]",
                out var entries, out var error), error);

            Assert.Equal(new[] { "2026-01-01T00:00:00", "2026-01-01T00:00:00Z" }, entries.Select(e => e.Id));
            Assert.Equal(10, entries[0].X);
            Assert.Equal(20, entries[0].Y);
            Assert.Equal(1.5, entries[1].X);
            Assert.Equal(-2, entries[1].Y);
            Assert.All(entries, e => Assert.Null(e.Error));
        }

        [Fact]
        public void Moves_NumericStrings_AreReadInTheInvariantCulture()
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.True(BulkEntries.TryParseMoves("[{\"id\": \"A\", \"x\": \"1.5\", \"y\": \"2\"}]", out var entries, out _));
                Assert.Equal(1.5, entries[0].X);
                Assert.Equal(2, entries[0].Y);
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Theory]
        [InlineData("[{\"id\": \"A\", \"y\": 2}]", "Invalid x/y: moves[0].x must be a number")]
        [InlineData("[{\"id\": \"A\", \"x\": 1, \"y\": \"up\"}]", "Invalid x/y: moves[0].y must be a number")]
        [InlineData("[{\"id\": \"A\", \"x\": null, \"y\": 2}]", "Invalid x/y: moves[0].x must be a number")]
        [InlineData("[\"A\"]", "Invalid entry: moves[0] must be an object")]
        public void Moves_AnUnusableEntry_IsCarriedWithItsError(string json, string error)
        {
            Assert.True(BulkEntries.TryParseMoves(json, out var entries, out var callError), callError);
            Assert.Single(entries);
            Assert.StartsWith(error, entries[0].Error);
        }

        [Fact]
        public void Moves_EntryWithoutAnId_HasANullId()
        {
            // The resolver then answers "Component ID is required" for that entry.
            Assert.True(BulkEntries.TryParseMoves("[{\"x\": 1, \"y\": 2}]", out var entries, out _));
            Assert.Null(entries[0].Id);
            Assert.Null(entries[0].Error);
        }

        [Theory]
        [InlineData("null", "'moves' must be a JSON array")]
        [InlineData("{\"id\": \"A\", \"x\": 1, \"y\": 2}", "'moves' must be a JSON array")]
        [InlineData("[{\"id\": \"A\"", "Invalid JSON format:")]
        [InlineData("[] []", "Invalid JSON format:")]
        [InlineData("", "JSON input is required")]
        public void Moves_AMalformedArray_FailsTheCall(string json, string error)
        {
            Assert.False(BulkEntries.TryParseMoves(json, out var entries, out var e));
            Assert.Null(entries);
            Assert.StartsWith(error, e);
        }

        // ---- connections ---------------------------------------------------------------------------

        [Fact]
        public void Connections_DateShapedIds_StayTheCallersText()
        {
            Assert.True(BulkEntries.TryParseConnections(
                "[{\"sourceId\": \"2026-01-01T00:00:00\", \"sourceParam\": \"2026-02-02T00:00:00\", \"targetId\": \"2026-03-03T00:00:00Z\", \"targetParam\": 1}]",
                out var entries, out var error), error);

            var c = Assert.Single(entries);
            Assert.Equal("2026-01-01T00:00:00", c.SourceId);
            Assert.Equal("2026-02-02T00:00:00", c.SourceParam);
            Assert.Equal("2026-03-03T00:00:00Z", c.TargetId);
            Assert.Equal("1", c.TargetParam);
            Assert.Null(c.Error);
        }

        [Fact]
        public void Connections_MissingPorts_DefaultToTheFirst()
        {
            Assert.True(BulkEntries.TryParseConnections("[{\"sourceId\": \"A\", \"targetId\": \"B\", \"sourceParam\": null}]", out var entries, out _));
            Assert.Equal("0", entries[0].SourceParam);
            Assert.Equal("0", entries[0].TargetParam);
        }

        [Theory]
        [InlineData("[{\"sourceId\": \"A\"}]", "Missing sourceId or targetId")]
        [InlineData("[{\"targetId\": \"B\", \"sourceId\": \"\"}]", "Missing sourceId or targetId")]
        [InlineData("[null]", "connections[0] must be an object")]
        [InlineData("[\"A\"]", "connections[0] must be an object")]
        public void Connections_AnUnusableEntry_IsCarriedWithItsError(string json, string error)
        {
            Assert.True(BulkEntries.TryParseConnections(json, out var entries, out var callError), callError);
            Assert.StartsWith(error, Assert.Single(entries).Error);
        }

        [Theory]
        [InlineData("null", "'connections' must be a JSON array: [{sourceId,sourceParam,targetId,targetParam},...]")]
        [InlineData("{}", "'connections' must be a JSON array")]
        [InlineData("[{", "Failed to parse connections JSON:")]
        public void Connections_AMalformedArray_FailsTheCall(string json, string error)
        {
            Assert.False(BulkEntries.TryParseConnections(json, out var entries, out var e));
            Assert.Null(entries);
            Assert.StartsWith(error, e);
        }
    }
}
