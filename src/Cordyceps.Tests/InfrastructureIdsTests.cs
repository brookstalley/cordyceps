using System;
using System.Linq;
using Cordyceps.Core;
using Xunit;

namespace Cordyceps.Tests
{
    public class InfrastructureIdsTests
    {
        private static Guid G() => Guid.NewGuid();

        [Fact]
        public void NoBridges_IsEmpty_EvenWithGroups()
        {
            var ids = InfrastructureIds.Compute(new BridgeLinks[0], new[] { new GroupMembers(G(), new[] { G() }) });
            Assert.Empty(ids);
        }

        [Fact]
        public void OneBridge_CollectsSelfNeighboursAndGroup_NotUnrelated()
        {
            Guid b = G(), src = G(), rcp = G(), grp = G(), other = G(), otherGrp = G();
            var ids = InfrastructureIds.Compute(
                new[] { new BridgeLinks(b, new[] { src }, new[] { rcp }) },
                new[] { new GroupMembers(grp, new[] { src, other }), new GroupMembers(otherGrp, new[] { other }) });
            Assert.Equal(new[] { b, src, rcp, grp }.OrderBy(x => x), ids.OrderBy(x => x));
        }

        [Fact]
        public void TwoBridges_BothAndTheirNeighboursAndGroupsAreInfrastructure()
        {
            Guid b1 = G(), s1 = G(), r1 = G(), g1 = G();
            Guid b2 = G(), s2 = G(), r2 = G(), g2 = G();
            Guid user = G(), userGrp = G();
            var ids = InfrastructureIds.Compute(
                new[]
                {
                    new BridgeLinks(b1, new[] { s1 }, new[] { r1 }),
                    new BridgeLinks(b2, new[] { s2 }, new[] { r2 }),
                },
                new[]
                {
                    new GroupMembers(g1, new[] { b1 }),
                    new GroupMembers(g2, new[] { r2 }),
                    new GroupMembers(userGrp, new[] { user }),
                });

            // Failed before the fix: only the first bridge's set was collected.
            Assert.Contains(b2, ids);
            Assert.Contains(s2, ids);
            Assert.Contains(r2, ids);
            Assert.Contains(g2, ids);
            Assert.Contains(b1, ids);
            Assert.Contains(g1, ids);
            Assert.DoesNotContain(user, ids);
            Assert.DoesNotContain(userGrp, ids);
            Assert.Equal(8, ids.Count);
        }

        [Fact]
        public void SharedNeighbour_IsCountedOnce()
        {
            Guid b1 = G(), b2 = G(), shared = G();
            var ids = InfrastructureIds.Compute(
                new[]
                {
                    new BridgeLinks(b1, new[] { shared }, new Guid[0]),
                    new BridgeLinks(b2, new Guid[0], new[] { shared }),
                }, new GroupMembers[0]);
            Assert.Equal(3, ids.Count);
            Assert.Contains(shared, ids);
        }
    }
}
