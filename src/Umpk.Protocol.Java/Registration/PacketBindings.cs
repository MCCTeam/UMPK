namespace Umpk.Protocol.Java;

/// <summary>The master registration table: a lookup from <c>(phase, flow, identifier)</c> to the timeline that resolves the packet's era codec for a protocol number. It is assembled once from the family binding files and cached. A duplicate <c>(phase, flow, identifier)</c> key throws at build time, so a copy-paste that would silently shadow a packet fails loudly instead.</summary>
internal sealed class PacketBindings
{
    private readonly Dictionary<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id), TimelineBinding> _table = [];

    private readonly Dictionary<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id), PacketTimeline> _absences = [];

    private static readonly Lazy<PacketBindings> Cached = new(BuildTable);

    /// <summary>The assembled, cached binding table.</summary>
    public static IReadOnlyDictionary<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id), TimelineBinding> Table => Cached.Value._table;

    /// <summary>The declared absences: identifiers with no codec timeline at all, whose marker is a decision somebody wrote down rather than a registration line nobody wrote.</summary>
    /// <remarks>Deliberately a second table rather than marker-only entries in <see cref="Table"/>. That table means "this identifier has a codec", which is what lets the reachability gate call any entry resolving no implemented codec anywhere a dead binding; an absence resolves none by construction and would have to be excused there, weakening the gate for every real binding beside it.</remarks>
    public static IReadOnlyDictionary<(ProtocolPhase Phase, PacketFlow Flow, Identifier Id), PacketTimeline> Absences => Cached.Value._absences;

    /// <summary>Starts a timeline for a packet, keyed by its canonical identity across all protocols.</summary>
    public PacketTimelineBuilder<TPacket> Packet<TPacket>(PacketType<TPacket> type)
        where TPacket : class, IPacket
    {
        ArgumentNullException.ThrowIfNull(type);
        var timeline = new PacketTimeline();
        AddKey(type.Phase, type.Flow, type.Id, timeline, int.MinValue, int.MaxValue, isAlias: false);
        return new PacketTimelineBuilder<TPacket>(type, timeline, this);
    }

    /// <summary>Starts a marker-only timeline for an identifier this library deliberately leaves unbound.</summary>
    public MarkerTimelineBuilder Absent(ProtocolPhase phase, PacketFlow flow, Identifier id)
    {
        var timeline = new PacketTimeline();
        if (_table.ContainsKey((phase, flow, id)) || !_absences.TryAdd((phase, flow, id), timeline))
            throw new InvalidOperationException(
                $"Duplicate registration for ({phase}, {flow}, {id}).");

        return new MarkerTimelineBuilder(timeline);
    }

    internal void AddAlias(ProtocolPhase phase, PacketFlow flow, Identifier alias, PacketTimeline timeline, int min, int max) =>
        AddKey(phase, flow, alias, timeline, min, max, isAlias: true);

    private void AddKey(ProtocolPhase phase, PacketFlow flow, Identifier id, PacketTimeline timeline, int min, int max, bool isAlias)
    {
        if (_absences.ContainsKey((phase, flow, id))
            || !_table.TryAdd((phase, flow, id), new TimelineBinding(timeline, min, max, isAlias)))
            throw new InvalidOperationException(
                $"Duplicate registration for ({phase}, {flow}, {id}).");

    }

    private static PacketBindings BuildTable()
    {
        var bindings = new PacketBindings();
        HandshakeStatusBindings.Register(bindings);
        LoginBindings.Register(bindings);
        ConfigurationBindings.Register(bindings);
        PlayCoreBindings.Register(bindings);
        WorldBindings.Register(bindings);
        EntityBindings.Register(bindings);
        ItemBindings.Register(bindings);
        UiBindings.Register(bindings);
        CommandsBindings.Register(bindings);
        DeclaredAbsences.Register(bindings);
        foreach (((ProtocolPhase phase, PacketFlow flow, Identifier id), PacketTimeline timeline) in bindings._absences)
        {
            // An absence with no band declares nothing, which is the state the declaration exists to end.
            if (!timeline.HasSteps)
                throw new InvalidOperationException(
                    $"Declared absence ({phase}, {flow}, {id}) states no protocol and no reason.");

        }

        return bindings;
    }
}

/// <summary>A timeline together with the protocol range over which a given identifier resolves it.</summary>
internal readonly struct TimelineBinding(PacketTimeline timeline, int minProtocol, int maxProtocol, bool isAlias)
{
    /// <summary>The timeline this identifier resolves.</summary>
    public PacketTimeline Timeline { get; } = timeline;

    /// <summary>True when this key is a curated legacy alias rather than the packet's canonical identity. The descriptor renders the canonical name for either, so an alias is not observable in a built descriptor; the reachability check keys off this to avoid claiming an alias is dead when it fired.</summary>
    public bool IsAlias { get; } = isAlias;

    /// <summary>Whether the identifier resolves the timeline at <paramref name="protocol"/>.</summary>
    public bool Covers(int protocol) => protocol >= minProtocol && protocol <= maxProtocol;
}
