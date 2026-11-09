using System;
using System.Collections.Generic;

namespace Cordyceps.Core
{
    /// <summary>One Cordyceps bridge component and the ids wired to it (host-free description).</summary>
    public sealed class BridgeLinks
    {
        public Guid BridgeId { get; }
        public IReadOnlyCollection<Guid> InputSources { get; }
        public IReadOnlyCollection<Guid> OutputRecipients { get; }

        public BridgeLinks(Guid bridgeId, IEnumerable<Guid> inputSources, IEnumerable<Guid> outputRecipients)
        {
            BridgeId = bridgeId;
            InputSources = new List<Guid>(inputSources ?? Array.Empty<Guid>());
            OutputRecipients = new List<Guid>(outputRecipients ?? Array.Empty<Guid>());
        }
    }

    /// <summary>A canvas group and the ids of its members (host-free description).</summary>
    public sealed class GroupMembers
    {
        public Guid GroupId { get; }
        public IReadOnlyCollection<Guid> MemberIds { get; }

        public GroupMembers(Guid groupId, IEnumerable<Guid> memberIds)
        {
            GroupId = groupId;
            MemberIds = new List<Guid>(memberIds ?? Array.Empty<Guid>());
        }
    }

    /// <summary>
    /// Host-free decision of which object ids are Cordyceps infrastructure. EVERY bridge component
    /// counts (a second bridge on another port must be protected too); for each, its input sources,
    /// output recipients and any group containing an infrastructure member are infrastructure.
    /// </summary>
    public static class InfrastructureIds
    {
        public static HashSet<Guid> Compute(IEnumerable<BridgeLinks> bridges, IEnumerable<GroupMembers> groups)
        {
            var ids = new HashSet<Guid>();
            if (bridges != null)
            {
                foreach (var b in bridges)
                {
                    ids.Add(b.BridgeId);
                    foreach (var s in b.InputSources) ids.Add(s);
                    foreach (var r in b.OutputRecipients) ids.Add(r);
                }
            }
            if (groups != null)
            {
                foreach (var g in groups)
                {
                    foreach (var m in g.MemberIds)
                    {
                        if (ids.Contains(m)) { ids.Add(g.GroupId); break; }
                    }
                }
            }
            return ids;
        }
    }
}
