using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Internal;

/// <summary>Connection-owned effective 26.3 default tool holders. Configuration entries append in frame order and become visible only at the existing before-play commit.</summary>
internal sealed class ClientToolTransformers
{
    private readonly record struct Holder(int Id, bool Verified)
    {
        public static Holder Unknown => new(-1, false);
    }

    private readonly record struct Snapshot(Holder Axe, Holder Hoe, Holder Shovel)
    {
        public static Snapshot Unknown => new(Holder.Unknown, Holder.Unknown, Holder.Unknown);
    }

    private static readonly Identifier Registry = Identifier.Minecraft("block_transformer");
    private Snapshot _snapshot = Snapshot.Unknown;
    private Snapshot _pending = Snapshot.Unknown;
    private HashSet<Identifier> _pendingKeys = [];
    private int _nextHolderId;
    private bool _enabled;
    private bool _contentsReceived;

    public void BeginConfiguration(int protocol)
    {
        _enabled = protocol == 777;
        _pending = Snapshot.Unknown;
        _pendingKeys = [];
        _nextHolderId = 0;
        _contentsReceived = false;
    }

    public void RecordContents(ClientboundConfigRegistryDataPacket packet)
    {
        if (!_enabled)
            return;
        // Any received registry contents select a rebuilt dynamic registry access. An omitted tool registry cannot retain facts from the previous access.
        if (packet.Registry != Registry)
        {
            _contentsReceived = true;
            return;
        }
        Snapshot pending = _pending;
        HashSet<Identifier> keys = new(_pendingKeys);
        int next = _nextHolderId;
        foreach (PackedRegistryEntry entry in packet.Entries)
        {
            if (!keys.Add(entry.Id))
                throw new ProtocolViolationException("Duplicate block-transformer registry entry.");
            var holder = new Holder(next, VanillaToolTransformerDefinitions.Matches(entry.Id, entry.Data));
            next = checked(next + 1);
            if (entry.Id == Identifier.Minecraft("axe"))
                pending = pending with { Axe = holder };
            else if (entry.Id == Identifier.Minecraft("hoe"))
                pending = pending with { Hoe = holder };
            else if (entry.Id == Identifier.Minecraft("shovel"))
                pending = pending with { Shovel = holder };
        }
        // Publish no pending prefix from an invalid frame; committed holders remain unchanged throughout configuration.
        _pending = pending;
        _pendingKeys = keys;
        _nextHolderId = next;
        _contentsReceived = true;
    }

    public void CommitConfiguration()
    {
        if (_enabled && _contentsReceived)
            _snapshot = _pending;
        _pending = Snapshot.Unknown;
        _pendingKeys = [];
        _nextHolderId = 0;
        _contentsReceived = false;
    }

    public bool TryGetVanillaDefault(Identifier key, out int holderId)
    {
        Holder holder = !_enabled || !key.IsMinecraft ? Holder.Unknown : key.Path switch
        {
            "axe" => _snapshot.Axe,
            "hoe" => _snapshot.Hoe,
            "shovel" => _snapshot.Shovel,
            _ => Holder.Unknown,
        };
        holderId = holder.Id;
        return holder.Id >= 0 && holder.Verified;
    }

    public void Clear()
    {
        _snapshot = Snapshot.Unknown;
        BeginConfiguration(0);
    }
}
