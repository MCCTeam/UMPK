using System.Collections.Frozen;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Connection-owned tag membership. Each network update replaces one immutable snapshot; reconfiguration and respawn retain it until another update or connection teardown.</summary>
internal sealed class ClientRegistryTags
{
    private sealed record Snapshot(bool IsKnown, FrozenDictionary<(Identifier Registry, Identifier Tag), FrozenSet<int>> Memberships);

    private static readonly Snapshot Unknown = new(false, FrozenDictionary<(Identifier, Identifier), FrozenSet<int>>.Empty);
    private Snapshot _snapshot = Unknown;

    public bool? Contains(Identifier registry, Identifier tag, int networkId)
    {
        Snapshot snapshot = _snapshot;
        return snapshot.IsKnown
            ? snapshot.Memberships.TryGetValue((registry, tag), out FrozenSet<int>? members) && members.Contains(networkId)
            : null;
    }

    public void Install(IReadOnlyList<TagRegistry> registries)
    {
        Dictionary<(Identifier, Identifier), FrozenSet<int>> memberships = [];
        HashSet<Identifier> seenRegistries = [];
        foreach (TagRegistry registry in registries)
        {
            if (!seenRegistries.Add(registry.Registry))
                throw new ProtocolViolationException("Duplicate registry in tag synchronization.");
            foreach (TagEntry tag in registry.Tags)
            {
                foreach (int member in tag.Ids)
                    if (member < 0)
                        throw new ProtocolViolationException("A synchronized tag contains a negative registry ID.");
                if (!memberships.TryAdd((registry.Registry, tag.TagId), tag.Ids.ToFrozenSet()))
                    throw new ProtocolViolationException("Duplicate tag in registry synchronization.");
            }
        }
        _snapshot = new Snapshot(true, memberships.ToFrozenDictionary());
    }

    public void Clear() => _snapshot = Unknown;
}
