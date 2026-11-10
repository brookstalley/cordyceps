using System;
using System.Linq;
using Cordyceps.Core.Canvas;
using Xunit;

namespace Cordyceps.Tests
{
    /// <summary>
    /// <see cref="GuidPrefixIndex"/>: the shortest prefix no other id shares (never under 8
    /// characters), found from both sorted neighbours, and case-insensitive, untrimmed prefix lookup.
    /// </summary>
    public class GuidPrefixIndexTests
    {
        private static Guid G(string s) => Guid.Parse(s);

        [Fact]
        public void Shortest_IsEightCharacters_WhenNothingSharesThem()
        {
            var c = G("11111111-2222-3333-4444-555555555555");
            var index = new GuidPrefixIndex(new[] { c, G("22222222-0000-0000-0000-000000000000") });
            Assert.Equal("11111111", index.Shortest(c));
        }

        [Fact]
        public void Shortest_NeverGoesUnderTheMinimum_EvenForALoneId()
        {
            var only = G("abcdef01-0000-0000-0000-000000000000");
            Assert.Equal("abcdef01", new GuidPrefixIndex(new[] { only }).Shortest(only));
            Assert.Equal(8, GuidPrefixIndex.MinimumLength);
        }

        [Fact]
        public void Shortest_GrowsPastTheSharedPart_IncludingTheHyphen()
        {
            var a = G("9ac1d04b-1000-0000-0000-000000000000");
            var b = G("9ac1d04b-2000-0000-0000-000000000000");
            var index = new GuidPrefixIndex(new[] { a, b });
            Assert.Equal("9ac1d04b-1", index.Shortest(a));
            Assert.Equal("9ac1d04b-2", index.Shortest(b));
        }

        [Fact]
        public void Shortest_IsTheWholeGuid_WhenOnlyTheLastCharacterDiffers()
        {
            var a = G("9ac1d04b-0000-0000-0000-000000000001");
            var b = G("9ac1d04b-0000-0000-0000-000000000002");
            Assert.Equal(a.ToString("D"), new GuidPrefixIndex(new[] { a, b }).Shortest(a));
        }

        [Fact]
        public void Shortest_LooksAtTheNeighbourBelowAndTheNeighbourAbove()
        {
            var below = G("55555555-aaaa-0000-0000-000000000000");
            var mid = G("55555555-aaab-0000-0000-000000000000");
            var above = G("55555555-ab00-0000-0000-000000000000");
            var index = new GuidPrefixIndex(new[] { above, mid, below });

            // mid shares 12 characters with the one below and 10 with the one above: the longer wins.
            Assert.Equal("55555555-aaab", index.Shortest(mid));
            Assert.Equal("55555555-aaaa", index.Shortest(below));
            Assert.Equal("55555555-ab", index.Shortest(above));

            // The other way round: the longer share is with the neighbour above.
            var low = G("77777777-a000-0000-0000-000000000000");
            var centre = G("77777777-b000-0000-0000-000000000000");
            var high = G("77777777-b100-0000-0000-000000000000");
            var index2 = new GuidPrefixIndex(new[] { high, low, centre });
            Assert.Equal("77777777-b0", index2.Shortest(centre));
        }

        [Fact]
        public void Shortest_WorksForAnIdNotInTheIndex()
        {
            var index = new GuidPrefixIndex(new[] { G("9ac1d04b-0000-0000-0000-000000000001") });
            Assert.Equal("9ac1d04c", index.Shortest(G("9ac1d04c-0000-0000-0000-000000000001")));
            Assert.Equal("ffffffff", index.Shortest(G("ffffffff-0000-0000-0000-000000000001")));
            Assert.Equal("9ac1d04b-1", index.Shortest(G("9ac1d04b-1000-0000-0000-000000000001")));
        }

        [Fact]
        public void StartingWith_IgnoresCase_AndReturnsEveryMatch()
        {
            var a = G("9ac1d04b-0000-0000-0000-000000000001");
            var b = G("9ac1d04b-0000-0000-0000-000000000002");
            var c = G("11111111-2222-3333-4444-555555555555");
            var index = new GuidPrefixIndex(new[] { a, b, c });

            Assert.Equal(new[] { a, b }, index.StartingWith("9AC1D04B"));
            Assert.Equal(new[] { a, b }, index.StartingWith("9ac1d04b-0000"));
            Assert.Equal(new[] { c }, index.StartingWith("1111"));
            Assert.Empty(index.StartingWith("9ac1d04c"));
        }

        [Fact]
        public void StartingWith_DoesNotTrim_AndEmptyMatchesNothing()
        {
            var a = G("9ac1d04b-0000-0000-0000-000000000001");
            var index = new GuidPrefixIndex(new[] { a });

            Assert.Empty(index.StartingWith(" 9ac1d04b"));
            Assert.Empty(index.StartingWith("9ac1d04b "));
            Assert.Empty(index.StartingWith(""));
            Assert.Empty(index.StartingWith(null));
            Assert.Empty(index.StartingWith("9ac1d04b0000")); // no hyphen where the D form has one
        }

        [Fact]
        public void Index_DropsDuplicates()
        {
            var a = G("9ac1d04b-0000-0000-0000-000000000001");
            var index = new GuidPrefixIndex(new[] { a, a });
            Assert.Equal(1, index.Count);
            Assert.Equal("9ac1d04b", index.Shortest(a));
            Assert.Single(index.StartingWith("9ac1"));
        }

        [Fact]
        public void Shortest_AgreesWithDisplayNamesShortId_OnTheSameIds()
        {
            var rng = new Random(5);
            var ids = Enumerable.Range(0, 200).Select(i =>
            {
                var bytes = new byte[16];
                rng.NextBytes(bytes);
                // The D form starts with bytes 3..0 (little-endian), so these force shared leading characters.
                bytes[3] = (byte)(i % 3);
                bytes[2] = 0;
                return new Guid(bytes);
            }).Distinct().ToList();

            var index = new GuidPrefixIndex(ids);
            var snap = TestCanvas.Snap(ids.Select(id => TestCanvas.Node(id, "")));
            var names = new DisplayNames(snap);
            foreach (var id in ids)
                Assert.Equal(index.Shortest(id), names.ShortId(id));
        }
    }
}
