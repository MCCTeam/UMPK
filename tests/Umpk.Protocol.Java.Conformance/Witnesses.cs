using System.Buffers;
using System.Collections.Concurrent;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Umpk.TestKit.Corpus;

namespace Umpk.Protocol.Java.Conformance;

/// <summary>Where a band's witness payload came from, which is what makes the circular fraction countable.</summary>
internal enum WitnessSource
{
    /// <summary>Real server bytes, lifted from the committed capture of a protocol inside the band.</summary>
    Corpus,

    /// <summary>An authored packet value, minted through the band's own encoder.</summary>
    Synthesized,
}

/// <summary>One hand-authored witness declaration: a packet, the payload that discriminates its eras, and why that payload discriminates them.</summary>
/// <remarks>The declaration is per PACKET, and the harness mints one witness per band from it. That is deliberate: the authoring work is choosing a payload the neighbouring layouts disagree about, and that choice is made once per packet, not once per band. It also means a new band cannot be added without a witness appearing for it and its rejection clause being proven.</remarks>
/// <param name="Phase">The protocol phase the packet is registered in.</param>
/// <param name="Flow">The packet flow.</param>
/// <param name="Identifier">The canonical packet identifier, namespace included.</param>
/// <param name="Why">What makes this payload discriminate, cited to vanilla where the boundary is a vanilla one. Reviewed as code; there is no generator for it.</param>
/// <param name="Values">The authored payload values, strongest first, each given the protocol it is minted at. A band takes the first one its own codec can carry and its neighbours reject. Most packets need one; a packet whose eras disagree about how much of the frame belongs to a trailing field needs two, because a payload the older era can read at all is by construction one the newer era reads the same way. Null only where every band of the packet is reached by a capture.</param>
/// <param name="PreferCorpus">When set, a real recorded frame of this packet from inside the band is used in preference to the authored value. It is the honest source when one exists: a capture is bytes a server actually sent, and the authored value is only the fallback for the bands no capture reaches.</param>
internal sealed record WitnessedPacket(
    ProtocolPhase Phase,
    PacketFlow Flow,
    string Identifier,
    string Why,
    IReadOnlyList<Func<int, object>>? Values = null,
    bool PreferCorpus = false)
{
    internal ProtocolTimeline.PacketKey Key => new(Phase, Flow, Identifier);
}

/// <summary>What one neighbouring band did with this band's witness.</summary>
/// <param name="Protocol">The neighbour band's first protocol, which is what the clause names.</param>
/// <param name="Rejected">False only when the neighbour read the same bytes to the same values.</param>
/// <param name="Mutual">Set when this band's payload did not separate the pair but the NEIGHBOUR's does. Separation is a property of the pair, not of one side: an era that reads a fixed argument run where its successor reads the frame remainder accepts everything the successor can encode and is rejected by everything it cannot, so exactly one of the two payloads can carry the proof. The pin says which.</param>
/// <param name="Detail">The fault, the differing consumed count, or the first field that disagreed.</param>
internal readonly record struct NeighbourVerdict(int Protocol, bool Rejected, bool Mutual, string Detail)
{
    /// <summary>True when the pair is separated at all, from either side.</summary>
    internal bool Separated => Rejected || Mutual;
}

/// <summary>One band's witness, resolved: the bytes, what the band's own codec made of them, and the neighbours.</summary>
internal sealed record ResolvedWitness(
    ProtocolTimeline.PacketKey Key,
    ProtocolTimeline.Band Band,
    int MintedAt,
    WitnessSource Source,
    byte[] Payload,
    int Consumed,
    string Canonical,
    IReadOnlyList<NeighbourVerdict> Neighbours)
{
    /// <summary>The bands nothing separates this one from; every one needs a twin entry.</summary>
    internal IEnumerable<int> Twins => Neighbours.Where(static n => !n.Separated).Select(static n => n.Protocol);
}

/// <summary>The witness harness. It resolves each declared packet's bands, mints one witness per band, and runs that witness through the nearest differently-bound band on each side.</summary>
/// <remarks>
/// <para>The all-zero probe already in the codec-identity pin answers "how many bytes does this codec read from nothing", and on 72 of the 112 multi-era packets two eras answer it identically. A witness is the same question asked with a payload chosen so they cannot: the pin then carries what the codec DECODED, and a clause naming the neighbouring eras that could not decode it the same way.</para>
/// <para>Two payload sources, and the pin says which. A recorded frame is real server bytes and carries no circularity at all; an authored value is minted through the band's own encoder, so it proves the two eras disagree but cannot catch an error symmetric across encode and decode. The <c>src:</c> column exists so that fraction is counted rather than assumed.</para>
/// </remarks>
internal static class Witnesses
{
    private static readonly ConcurrentDictionary<int, IReadOnlyDictionary<ProtocolTimeline.PacketKey, ResolvedWitness>> Cache = [];

    private static readonly Lazy<IReadOnlyDictionary<ProtocolTimeline.PacketKey, IReadOnlyList<ProtocolTimeline.Band>>> AllBands =
        new(static () => ProtocolTimeline.Build().ToDictionary(static e => e.Key, static e => e.Bands));

    private static readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<RecordedFrame>>> Captures =
        new(LoadCaptures);

    /// <summary>The declared packets, in the order the catalog states them.</summary>
    internal static IReadOnlyList<WitnessedPacket> Catalog => WitnessCatalog.All;

    /// <summary>Every witness that applies at one protocol, keyed by packet.</summary>
    internal static IReadOnlyDictionary<ProtocolTimeline.PacketKey, ResolvedWitness> For(int protocol) =>
        Cache.GetOrAdd(protocol, Resolve);

    /// <summary>The bound codec for one packet at one protocol, or null when it is not registered there.</summary>
    internal static BoundPacketCodec? Bind(int protocol, ProtocolTimeline.PacketKey key)
    {
        if (!JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version) ||
            !version!.Protocol.TryGetRegistry(key.Phase, key.Flow, out PhaseRegistry registry))
            return null;

        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id.ToString() == key.Identifier && registry.TryGetInbound(wireId, out BoundPacketCodec bound))
                return bound;

        return null;
    }

    /// <summary>The codec context for a protocol: its real static registries, the way a live session has them.</summary>
    internal static PacketCodecContext Context(int protocol) =>
        new(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);

    /// <summary>Runs a payload through one protocol's binding and classifies what happened.</summary>
    internal static (bool Ran, int Consumed, string Fault, string Canonical) Run(
        int protocol, ProtocolTimeline.PacketKey key, byte[] payload)
    {
        BoundPacketCodec? bound = Bind(protocol, key);
        if (bound is null || !bound.IsImplemented)
            return (false, 0, "unbound", string.Empty);

        PacketCodecContext context = Context(protocol);
        if (!bound.TryProbeShape(payload, context, out int consumed, out string fault))
            return (false, 0, fault, string.Empty);

        // A decode that stopped short read a different wire shape, and asking it for values would only report the prefix it happened to understand. The byte count is the whole answer there.
        if (consumed != payload.Length)
            return (true, consumed, string.Empty, string.Empty);

        return (true, consumed, string.Empty, WitnessDigest.Canonical(bound.Decode(payload, context)));
    }

    private static IReadOnlyDictionary<ProtocolTimeline.PacketKey, ResolvedWitness> Resolve(int protocol)
    {
        Dictionary<ProtocolTimeline.PacketKey, ResolvedWitness> resolved = [];
        foreach (WitnessedPacket declared in Catalog)
        {
            if (!AllBands.Value.TryGetValue(declared.Key, out IReadOnlyList<ProtocolTimeline.Band>? bands))
                continue;

            int index = IndexOfBandCovering(bands, protocol);
            if (index < 0)
                continue;

            ResolvedWitness? witness = Mint(declared, bands, index);
            if (witness is not null)
                resolved[declared.Key] = witness;

        }

        return resolved;
    }

    private static ResolvedWitness? Mint(
        WitnessedPacket declared, IReadOnlyList<ProtocolTimeline.Band> bands, int index)
    {
        ProtocolTimeline.Band band = bands[index];
        if (IsMarker(band))
            return null;

        List<ProtocolTimeline.Band> neighbours = [.. Neighbours(bands, index)];
        ResolvedWitness? weakest = null;
        int offered = 0;
        foreach ((int mintedAt, WitnessSource source, byte[] payload) in Candidates(declared, band))
        {
            offered++;
            (bool ran, int consumed, string _, string canonical) = Run(mintedAt, declared.Key, payload);

            // A payload its own band cannot read back whole is not a witness for that band. It is not an error either: a value authored to be too long for the era that reads a fixed-width tail is exactly how the NEXT band is separated from it, so it is simply skipped here.
            if (!ran || consumed != payload.Length)
                continue;

            List<NeighbourVerdict> verdicts =
                [.. neighbours.Select(n => Judge(declared, band, n, payload, consumed, canonical))];
            var candidate = new ResolvedWitness(
                declared.Key, band, mintedAt, source, payload, consumed, canonical, verdicts);
            if (verdicts.Count > 0 && verdicts.TrueForAll(static v => v.Rejected))
                return candidate;

            // No payload separates every neighbour, so take the one that separates the most and let the remainder be declared twins. Picking the first instead would hide a proof that exists.
            if (weakest is null || Proven(verdicts) > Proven(weakest.Neighbours))
                weakest = candidate;

        }

        if (weakest is null && offered > 0)
            throw new InvalidOperationException(
                $"None of the {offered} witness payload(s) for {declared.Key} survives its own codec on band " +
                $"{ProtocolTimeline.RangeOf(band)}.");

        return weakest;
    }

    private static int Proven(IReadOnlyList<NeighbourVerdict> verdicts) => verdicts.Count(static v => v.Rejected);

    private static NeighbourVerdict Judge(
        WitnessedPacket declared,
        ProtocolTimeline.Band band,
        ProtocolTimeline.Band neighbour,
        byte[] payload,
        int consumed,
        string canonical)
    {
        int protocol = neighbour.First;
        (bool ran, int theirConsumed, string fault, string theirCanonical) = Run(protocol, declared.Key, payload);
        if (!ran)
            return new NeighbourVerdict(protocol, true, false, fault);

        if (theirConsumed != consumed)
            return new NeighbourVerdict(protocol, true, false, $"read:{theirConsumed}");

        if (theirCanonical != canonical)
            return new NeighbourVerdict(
                protocol, true, false, WitnessDigest.FirstDifference(canonical, theirCanonical));

        return new NeighbourVerdict(protocol, false, Separates(declared, neighbour, band), "identical");
    }

    /// <summary>Whether any payload the <paramref name="from"/> band can carry is read differently by <paramref name="to"/>. Asked only when this band's own payload could not separate the two, and asked over the candidates rather than through the resolved witness so the question terminates: resolving the neighbour's witness would ask this one about it again.</summary>
    private static bool Separates(WitnessedPacket declared, ProtocolTimeline.Band from, ProtocolTimeline.Band to)
    {
        foreach ((int mintedAt, WitnessSource _, byte[] payload) in Candidates(declared, from))
        {
            (bool ran, int consumed, string fault, string canonical) = Run(mintedAt, declared.Key, payload);
            if (!ran || consumed != payload.Length)
                continue;

            (bool theirRan, int theirConsumed, string theirFault, string theirCanonical) =
                Run(to.First, declared.Key, payload);
            if (!theirRan || theirConsumed != consumed || theirCanonical != canonical)
                return true;

        }

        return false;
    }

    /// <summary>The payloads a band may be witnessed with, strongest provenance first. A recording is bytes a server actually sent and carries no circularity, so it is tried first where the catalog says the packet has one; the authored value is what covers the bands no capture reaches, and what covers the bands whose recording happens to be too bland to separate the eras (an empty container, a metadata set with one field). <see cref="Mint"/> takes the first candidate its neighbours reject, so a band never silently settles for the weaker of the two, and the <c>src:</c> column says which one carried it.</summary>
    private static IEnumerable<(int MintedAt, WitnessSource Source, byte[] Bytes)> Candidates(
        WitnessedPacket declared, ProtocolTimeline.Band band)
    {
        if (declared.PreferCorpus)
            foreach (int protocol in ProtocolTimeline.Covered(band))
            {
                byte[]? recorded = Recorded(protocol, declared.Key);
                if (recorded is not null)
                {
                    yield return (protocol, WitnessSource.Corpus, recorded);
                    break;
                }
            }

        // Nothing at all for this band means the pin renders src:none and CrossLayoutRejectionTests names the packet and the protocol, which is how a band that needs an authored value announces itself.
        foreach (Func<int, object> value in declared.Values ?? [])
        {
            byte[]? bytes = TryEncode(band.First, declared, value);
            if (bytes is not null)
                yield return (band.First, WitnessSource.Synthesized, bytes);

        }
    }

    /// <summary>Mints one authored value through a band's own encoder, or reports that the band cannot carry it. A value spelling a field the era has no serializer for is not a mistake in the catalog: it is how a payload authored for the eras that DO carry it stays out of the way of the ones that do not.</summary>
    private static byte[]? TryEncode(int protocol, WitnessedPacket declared, Func<int, object> value)
    {
        try
        {
            return Encode(protocol, declared, value);
        }
        catch (Exception ex) when (ex is ProtocolViolationException or FormatException or
            InvalidOperationException or ArgumentException or NotSupportedException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static byte[] Encode(int protocol, WitnessedPacket declared, Func<int, object> value)
    {
        BoundPacketCodec bound = Bind(protocol, declared.Key)
            ?? throw new InvalidOperationException($"{declared.Key} is not registered at protocol {protocol}.");
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        bound.Encode(ref writer, value(protocol), Context(protocol));
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>The first recorded frame of this packet in a protocol's own capture, if it carries one.</summary>
    private static byte[]? Recorded(int protocol, ProtocolTimeline.PacketKey key)
    {
        if (!Captures.Value.TryGetValue(protocol, out IReadOnlyList<RecordedFrame>? frames) ||
            !JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version) ||
            !version!.Protocol.TryGetRegistry(key.Phase, key.Flow, out PhaseRegistry registry))
            return null;

        int wireId = -1;
        foreach ((int id, PacketType type) in registry.Packets)
            if (type.Id.ToString() == key.Identifier)
            {
                wireId = id;
                break;
            }

        if (wireId < 0)
            return null;

        // Pre-1.20.2 recordings label the frames after LoginSuccess Login, because the recorder's phase machine lags one packet behind the client's; the same forward latch CorpusConformanceTests applies is what makes those frames resolve as the Play frames they are.
        bool loginTerminated = false;
        foreach (RecordedFrame frame in frames)
        {
            ProtocolPhase phase = CorpusEnumMapping.ToProtocolPhase(frame.Phase);
            PacketFlow flow = CorpusEnumMapping.ToFlow(frame.Direction);
            if (loginTerminated && phase == ProtocolPhase.Login && flow == PacketFlow.Clientbound)
                phase = ProtocolPhase.Play;

            if (phase == key.Phase && flow == key.Flow && frame.WireId == wireId && frame.Body.Length > 0)
                return frame.Body.ToArray();

            if (!loginTerminated && phase == ProtocolPhase.Login && flow == PacketFlow.Clientbound &&
                version.Protocol.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Clientbound, out PhaseRegistry login) &&
                login.TryGetInbound(frame.WireId, out BoundPacketCodec codec) &&
                codec.Type.Id == LoginPackets.Clientbound.LoginFinished.Id)
                loginTerminated = true;

        }

        return null;
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<RecordedFrame>> LoadCaptures()
    {
        Dictionary<int, IReadOnlyList<RecordedFrame>> byProtocol = [];
        foreach (string path in CorpusLoader.DiscoverCaptures(FixturePaths.CorpusRoot))
        {
            LoadedCorpus corpus = CorpusLoader.LoadFileAsync(path).GetAwaiter().GetResult();
            if (!byProtocol.ContainsKey(corpus.Protocol))
                byProtocol[corpus.Protocol] = corpus.Frames;

        }

        return byProtocol;
    }

    /// <summary>The nearest band on each side that is bound to a DIFFERENT codec. A band break can be an alias change rather than a codec change, and asking a codec to reject its own bytes is not a test.</summary>
    private static IEnumerable<ProtocolTimeline.Band> Neighbours(IReadOnlyList<ProtocolTimeline.Band> bands, int index)
    {
        for (int i = index - 1; i >= 0; i--)
            if (IsNeighbour(bands[index], bands[i]))
            {
                yield return bands[i];
                break;
            }

        for (int i = index + 1; i < bands.Count; i++)
            if (IsNeighbour(bands[index], bands[i]))
            {
                yield return bands[i];
                break;
            }

    }

    private static bool IsNeighbour(ProtocolTimeline.Band band, ProtocolTimeline.Band candidate) =>
        !IsMarker(candidate) && Codec(candidate) != Codec(band);

    private static bool IsMarker(ProtocolTimeline.Band band) => Codec(band) == "marker";

    /// <summary>The band's codec, with the resolving-alias suffix dropped: two bands can differ only there.</summary>
    private static string Codec(ProtocolTimeline.Band band)
    {
        int via = band.Value.IndexOf(" via:", StringComparison.Ordinal);
        return via < 0 ? band.Value : band.Value[..via];
    }

    private static int IndexOfBandCovering(IReadOnlyList<ProtocolTimeline.Band> bands, int protocol)
    {
        for (int i = 0; i < bands.Count; i++)
            if (protocol >= bands[i].First && protocol <= bands[i].Last && ProtocolTimeline.Covered(bands[i]).Contains(protocol))
                return i;

        return -1;
    }
}
