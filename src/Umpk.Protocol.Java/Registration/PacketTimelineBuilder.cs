using System.Runtime.CompilerServices;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java;

/// <summary>Fluent surface for declaring one packet's timeline. Each <see cref="From"/> binds the era codec that takes effect from a protocol onward; <see cref="MarkerFrom(int, MarkerReason, string)"/> declares an honest not-yet-implemented span; <see cref="AliasedAs(Umpk.Identifier)"/> registers an extra dataset identifier that resolves to the same timeline (for the curated legacy names that differ from the canonical packet identity).</summary>
/// <typeparam name="TPacket">The packet record type this timeline binds.</typeparam>
internal sealed class PacketTimelineBuilder<TPacket>
    where TPacket : class, IPacket
{
    private readonly PacketType<TPacket> _type;

    private readonly PacketTimeline _timeline;

    private readonly PacketBindings _bindings;

    internal PacketTimelineBuilder(PacketType<TPacket> type, PacketTimeline timeline, PacketBindings bindings)
    {
        _type = type;
        _timeline = timeline;
        _bindings = bindings;
    }

    /// <summary>Binds <paramref name="codec"/> as the wire form used from <paramref name="fromProtocol"/> onward.</summary>
    /// <param name="fromProtocol">The first protocol this era codec governs.</param>
    /// <param name="codec">The era codec.</param>
    /// <param name="codecIdentity">The name recorded in the codec-identity pin. It defaults to the caller's own source expression for <paramref name="codec"/>, which is what makes the pin free at the call site; pass it explicitly only where that expression does not distinguish the eras (a loop over an era table, say), because a token that reads the same on every protocol is a token that cannot see a misbinding.</param>
    public PacketTimelineBuilder<TPacket> From(
        int fromProtocol,
        PacketCodec<TPacket> codec,
        [CallerArgumentExpression(nameof(codec))] string? codecIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(codec);
        PacketType<TPacket> type = _type;
        _timeline.Add(
            fromProtocol,
            (ProtocolDescriptorBuilder b, int wireId, Identifier datasetId) =>
                b.Register(wireId, type, codec, codecIdentity, datasetId));
        return this;
    }

    /// <summary>Binds an era whose wire form is carried by a different packet record than the timeline's primary type. Used for the few identifiers that a later era re-typed while keeping the same wire identity (for example serverbound <c>chat</c>, which changes from the unsigned legacy record to the signed record, and configuration <c>registry_data</c>, whose single-blob and packed-entry forms are distinct records). Both records answer to this timeline's identifier, in its phase and flow.</summary>
    /// <param name="fromProtocol">The first protocol this era codec governs.</param>
    /// <param name="type">The packet record identity this era carries.</param>
    /// <param name="codec">The era codec.</param>
    /// <param name="codecIdentity">The name recorded in the codec-identity pin; see the primary overload.</param>
    /// <exception cref="ArgumentException"><paramref name="type"/> sits in another phase or flow. The descriptor files every entry under the bound type's own phase and flow, so such a step would fill a registry the frame path never consults for this identifier and the packet would read as unregistered rather than as misbound. The name says <c>As</c> because that is the whole of what it may change.</exception>
    public PacketTimelineBuilder<TPacket> FromAs<TOther>(
        int fromProtocol,
        PacketType<TOther> type,
        PacketCodec<TOther> codec,
        [CallerArgumentExpression(nameof(codec))] string? codecIdentity = null)
        where TOther : class, IPacket
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(codec);
        if (type.Phase != _type.Phase || type.Flow != _type.Flow)
            throw new ArgumentException(
                $"{type.Id} is ({type.Phase}, {type.Flow}), but the timeline for {_type.Id} is ({_type.Phase}, {_type.Flow}).",
                nameof(type));

        _timeline.Add(
            fromProtocol,
            (ProtocolDescriptorBuilder b, int wireId, Identifier datasetId) =>
                b.Register(wireId, type, codec, codecIdentity, datasetId));
        return this;
    }

    /// <summary>Declares that the packet is registered but not yet implemented from <paramref name="fromProtocol"/> onward (until a later <see cref="From"/> supersedes it). Used where the era wire form has no codec yet and the frame must relay verbatim rather than be mis-decoded by a neighbouring era's codec.</summary>
    public PacketTimelineBuilder<TPacket> MarkerFrom(int fromProtocol)
    {
        _timeline.Add(fromProtocol, null);
        return this;
    }

    /// <summary>Declares that this packet is deliberately NOT bound from <paramref name="fromProtocol"/> onward, until a later <see cref="From"/> supersedes it, and says which class of deliberate it is. The conformance suite checks the declaration against the intentional-marker allowlist in both directions, so a reason written here is the same reason a reviewer reads there.</summary>
    /// <param name="fromProtocol">The first protocol this absence governs.</param>
    /// <param name="reason">Which <see cref="MarkerReason"/> the allowlist entry must carry.</param>
    /// <param name="why">The prose justification, held to the bar the allowlist entry is held to.</param>
    public PacketTimelineBuilder<TPacket> MarkerFrom(int fromProtocol, MarkerReason reason, string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        _timeline.AddMarker(fromProtocol, reason, why);
        return this;
    }

    /// <summary>Registers an additional dataset identifier resolving to this timeline across every protocol.</summary>
    public PacketTimelineBuilder<TPacket> AliasedAs(Identifier alias)
    {
        _bindings.AddAlias(_type.Phase, _type.Flow, alias, _timeline, int.MinValue, int.MaxValue);
        return this;
    }

    /// <summary>Registers an additional dataset identifier resolving to this timeline only for protocols in <c>[fromProtocol, untilProtocol]</c>. Used where a curated legacy name is reused by a later era for a packet that era leaves not implemented (so outside the range the identifier must fall through to a marker instead of resolving the timeline).</summary>
    public PacketTimelineBuilder<TPacket> AliasedAs(Identifier alias, int fromProtocol, int untilProtocol)
    {
        _bindings.AddAlias(_type.Phase, _type.Flow, alias, _timeline, fromProtocol, untilProtocol);
        return this;
    }
}
