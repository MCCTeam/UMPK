namespace Umpk.Protocol.Java;

/// <summary>The inbound (wire id -> codec) and outbound (packet type -> wire id + codec) tables for one (phase, flow) pair in one protocol version. Inbound lookup is an array index by wire id; outbound lookup is by packet identity. Immutable once built.</summary>
/// <remarks>Construction refuses a duplicate wire id and a duplicate packet identity. Both are registration mistakes with no legal reading: one wire id names one packet in a (phase, flow), and one packet travels on one wire id.</remarks>
public sealed class PhaseRegistry
{
    private readonly BoundPacketCodec?[] _byWireId;

    private readonly Dictionary<PacketType, BoundPacketCodec> _byType;

    private readonly (int WireId, PacketType Type)[] _packets;

    internal PhaseRegistry(IReadOnlyList<BoundPacketCodec> entries)
    {
        int max = -1;
        foreach (BoundPacketCodec e in entries)
            if (e.WireId > max)
                max = e.WireId;

        _byWireId = new BoundPacketCodec?[max + 1];
        _byType = new Dictionary<PacketType, BoundPacketCodec>(entries.Count);
        var packets = new (int, PacketType)[entries.Count];
        // Both tables were last-write-wins. A second entry at an occupied slot displaced the first from the registry and from the registration pin at once, so the surviving table looked exactly like a table that had only ever been handed one entry.
        for (int i = 0; i < entries.Count; i++)
        {
            BoundPacketCodec e = entries[i];
            if (_byWireId[e.WireId] is { } clash)
                throw new InvalidOperationException(
                    $"Wire id 0x{e.WireId:X2} is claimed by both {clash.Type.Id} and {e.Type.Id}.");

            _byWireId[e.WireId] = e;
            if (!_byType.TryAdd(e.Type, e))
                throw new InvalidOperationException(
                    $"{e.Type.Id} is registered at wire ids 0x{_byType[e.Type].WireId:X2} and 0x{e.WireId:X2}.");

            packets[i] = (e.WireId, e.Type);
        }

        _packets = packets;
    }

    /// <summary>The (wire id, packet type) pairs in this registry, for introspection/debugging.</summary>
    public IReadOnlyList<(int WireId, PacketType Type)> Packets => _packets;

    /// <summary>Looks up the codec for an inbound wire id.</summary>
    public bool TryGetInbound(int wireId, out BoundPacketCodec entry)
    {
        if ((uint)wireId < (uint)_byWireId.Length && _byWireId[wireId] is { } found)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>Looks up the wire id and codec for an outbound packet type.</summary>
    public bool TryGetOutbound(PacketType type, out int wireId, out BoundPacketCodec entry)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (_byType.TryGetValue(type, out BoundPacketCodec? found))
        {
            wireId = found.WireId;
            entry = found;
            return true;
        }

        wireId = -1;
        entry = null!;
        return false;
    }
}
