using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java;

/// <summary>Builds an immutable <see cref="ProtocolDescriptor"/> from plain, reviewable registration calls. Generated per-version code drives this: one <see cref="Register{TPacket}"/> per implemented packet, one <see cref="RegisterMarker(int, PacketType)"/> per registered-but-unimplemented packet, and <see cref="MarkTerminal"/> per phase-ending packet. Registration order per (phase, flow) equals the wire id, but the id is passed explicitly so the generated diffs are self-describing and gaps (unused ids) are legal.</summary>
public sealed class ProtocolDescriptorBuilder
{
    private readonly GameVersion _version;

    private readonly ProtocolFeatures _features;

    private readonly Dictionary<(ProtocolPhase, PacketFlow), List<BoundPacketCodec>> _entries = new();

    private readonly Dictionary<(ProtocolPhase, PacketFlow, int), ProtocolPhase> _terminals = new();

    private readonly HashSet<(ProtocolPhase, PacketFlow, int)> _compressionEnablePoints = new();

    private readonly HashSet<(ProtocolPhase, PacketFlow, int)> _encryptionEnablePoints = new();

    private bool _built;

    /// <summary>Creates a builder for a version with its feature flags.</summary>
    public ProtocolDescriptorBuilder(GameVersion version, ProtocolFeatures features)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(features);
        _version = version;
        _features = features;
    }

    /// <summary>The feature flags this descriptor is being built with (era-selection input for codecs).</summary>
    public ProtocolFeatures Features => _features;

    /// <summary>The entry the last registration added, so the registrar can check the slot it just filled.</summary>
    internal BoundPacketCodec? LastRegistered { get; private set; }

    /// <summary>The wire protocol number this descriptor is being built for. Registrar hooks use it to pick the correct per-protocol codec form when a shared codec key spans a wire change (e.g. the pre-1.19 dig send without the 1.19 sequence VarInt, or the 1.10-&gt;1.11 use_item_on i8-&gt;f32 cursor flip).</summary>
    public int Protocol => _version.Protocol;

    /// <summary>Registers an implemented packet with its wire id and codec.</summary>
    /// <param name="wireId">The numeric wire id in the packet's phase/flow.</param>
    /// <param name="type">The version-independent packet identity.</param>
    /// <param name="codec">The era codec to bind.</param>
    /// <param name="codecIdentity">A stable name for <paramref name="codec"/>, surfaced as <see cref="BoundPacketCodec.CodecIdentity"/> and frozen by the codec-identity pin so a rebinding is a visible diff rather than a silent one.</param>
    /// <param name="datasetIdentifier">The identifier the version dataset spelled this packet with, surfaced as <see cref="BoundPacketCodec.DatasetIdentifier"/>. Null means it matched the packet's own identity; pass it where a curated legacy alias resolved the binding, so an alias that fired is distinguishable from one that never did.</param>
    public ProtocolDescriptorBuilder Register<TPacket>(
        int wireId,
        PacketType<TPacket> type,
        PacketCodec<TPacket> codec,
        string? codecIdentity = null,
        Identifier? datasetIdentifier = null)
        where TPacket : class, IPacket
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(codec);
        Add(type, BoundPacketCodec.Create(wireId, type, codec, codecIdentity, datasetIdentifier));
        return this;
    }

    /// <summary>Registers a not-yet-implemented packet as a marker. It resolves to a codec so DataGen completeness accounting stays honest; decoding one applies the unknown-packet policy.</summary>
    public ProtocolDescriptorBuilder RegisterMarker(int wireId, PacketType type) => RegisterMarker(wireId, type, null);

    /// <summary>Registers a not-yet-implemented packet as a marker, carrying the declaration that says the marker is a decision. Null is the ordinary case: no binding file mentions the identifier at all.</summary>
    internal ProtocolDescriptorBuilder RegisterMarker(int wireId, PacketType type, MarkerDeclaration? declaration)
    {
        ArgumentNullException.ThrowIfNull(type);
        Add(type, BoundPacketCodec.Marker(wireId, type, declaration));
        return this;
    }

    /// <summary>Marks a wire id in a (phase, flow) as terminal, transitioning into <paramref name="nextPhase"/>.</summary>
    public ProtocolDescriptorBuilder MarkTerminal(ProtocolPhase phase, PacketFlow flow, int wireId, ProtocolPhase nextPhase)
    {
        _terminals[(phase, flow, wireId)] = nextPhase;
        if (Entry(phase, flow, wireId) is { } entry)
        {
            entry.Role |= FrameRole.Terminal;
            entry.NextPhase = nextPhase;
        }

        return this;
    }

    /// <summary>Marks a wire id in a (phase, flow) as the set-compression frame. The read loop pauses at this frame boundary until the consumer enables compression, so the next frame is read with the compressed reader (the transform-enable point).</summary>
    public ProtocolDescriptorBuilder MarkCompressionEnablePoint(ProtocolPhase phase, PacketFlow flow, int wireId)
    {
        _compressionEnablePoints.Add((phase, flow, wireId));
        if (Entry(phase, flow, wireId) is { } entry)
            entry.Role |= FrameRole.CompressionPoint;

        return this;
    }

    /// <summary>Marks a wire id in a (phase, flow) as the encryption-request frame. The read loop pauses at this frame boundary until the consumer enables encryption, so the peer's subsequent (encrypted) bytes are not appended to the read buffer undecrypted (mirror of the set-compression pause).</summary>
    public ProtocolDescriptorBuilder MarkEncryptionEnablePoint(ProtocolPhase phase, PacketFlow flow, int wireId)
    {
        _encryptionEnablePoints.Add((phase, flow, wireId));
        if (Entry(phase, flow, wireId) is { } entry)
            entry.Role |= FrameRole.EncryptionPoint;

        return this;
    }

    // Mark* keeps the side tables for descriptor-level queries and eagerly updates an existing entry. Add materializes the same tables into a newly registered entry, so either registration order produces the same resolved role bits. Entry searches backwards and stops on the first step in the overwhelming majority of calls. A gate whose wire id is not registered on this version leaves the side table alone to carry it, exactly as before.
    private BoundPacketCodec? Entry(ProtocolPhase phase, PacketFlow flow, int wireId)
    {
        if (!_entries.TryGetValue((phase, flow), out List<BoundPacketCodec>? list))
            return null;

        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].WireId == wireId)
                return list[i];

        return null;
    }

    private void Add(PacketType type, BoundPacketCodec bound)
    {
        if (_built)
            throw new InvalidOperationException("This descriptor builder has already produced a descriptor.");

        (ProtocolPhase, PacketFlow) key = (type.Phase, type.Flow);
        if (!_entries.TryGetValue(key, out List<BoundPacketCodec>? list))
        {
            list = [];
            _entries[key] = list;
        }

        list.Add(bound);
        ApplyRegisteredGateRoles(type, bound);
        LastRegistered = bound;
    }

    private void ApplyRegisteredGateRoles(PacketType type, BoundPacketCodec entry)
    {
        (ProtocolPhase, PacketFlow, int) key = (type.Phase, type.Flow, entry.WireId);
        if (_terminals.TryGetValue(key, out ProtocolPhase nextPhase))
        {
            entry.Role |= FrameRole.Terminal;
            entry.NextPhase = nextPhase;
        }

        if (_compressionEnablePoints.Contains(key))
            entry.Role |= FrameRole.CompressionPoint;

        if (_encryptionEnablePoints.Contains(key))
            entry.Role |= FrameRole.EncryptionPoint;

    }

    /// <summary>Produces the immutable descriptor and seals the builder.</summary>
    public ProtocolDescriptor Build()
    {
        if (_built)
            throw new InvalidOperationException("This descriptor builder has already produced a descriptor.");

        _built = true;
        var registries = new Dictionary<(ProtocolPhase, PacketFlow), PhaseRegistry>(_entries.Count);
        foreach (((ProtocolPhase, PacketFlow) key, List<BoundPacketCodec> list) in _entries)
            registries[key] = new PhaseRegistry(list);

        return new ProtocolDescriptor(_version, _features, registries, _terminals, _compressionEnablePoints, _encryptionEnablePoints);
    }
}
