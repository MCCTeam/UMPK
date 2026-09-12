namespace Umpk.Protocol.Java.Conformance;

/// <summary>Two bands that produce identical bytes and identical decoded values for every input, declared once with the vanilla reading that says why.</summary>
/// <param name="Phase">The protocol phase.</param>
/// <param name="Flow">The packet flow.</param>
/// <param name="Identifier">The canonical packet identifier, namespace included.</param>
/// <param name="Band">The lower band, named by its first protocol.</param>
/// <param name="Twin">The higher band, named by its first protocol.</param>
/// <param name="Why">Why no payload can separate them, cited to vanilla. Reviewed as code; there is no generator for it.</param>
internal sealed record TwinDeclaration(
    ProtocolPhase Phase,
    PacketFlow Flow,
    string Identifier,
    int Band,
    int Twin,
    string Why)
{
    /// <summary>The unordered pair, so a twin declared once covers the observation from both sides.</summary>
    internal (ProtocolPhase, PacketFlow, string, int, int) Pair =>
        (Phase, Flow, Identifier, Math.Min(Band, Twin), Math.Max(Band, Twin));
}

/// <summary>The declared twins: every pair of adjacent bands a witness provably cannot separate, because the two codecs read and write the same bytes for every input.</summary>
/// <remarks>
/// <para>This file is <c>IntentionalMarkers.cs</c>'s discipline applied to the witness column, for the same reason. A witness that its neighbours do not reject is not a witness; it is a gap that looks like coverage. So a non-rejection is a build failure unless it is written down here, by hand, with the wire behavior that makes it true. There is no regeneration path.</para>
/// <para>Two true twins are usually one band too many: if the codecs agree on every input, one of them can be deleted and its range folded into the other. The friction of adding an entry is the point, and an entry is as much a note that the tree carries a removable copy as it is an allowance.</para>
/// <para>The set is compared exactly, not as a floor. A pair that stops being a twin (because one side gained a field, or because a rebinding moved it) fails here rather than quietly passing.</para>
/// </remarks>
internal static class IntentionalTwins
{
    /// <summary>Every declared twin pair.</summary>
    internal static IReadOnlyList<TwinDeclaration> All { get; } =
    [
        new TwinDeclaration(
            ProtocolPhase.Play,
            PacketFlow.Clientbound,
            "minecraft:level_chunk_with_light",
            Band: 107,
            Twin: 393,
            Why:
            "ChunkCodecs.V1_9 and ChunkCodecs.V1_13 are one decode walk taking a flatState flag, and the " +
            "flag is discarded (ChunkCodecs.V1_9To1_13.cs:61): the two eras differ in what a palette " +
            "entry MEANS, (id << 4) | meta before the flattening and a flat state id after, and that is " +
            "not on the wire. The 1.13 int[256] biome array is on the wire, but it sits inside the " +
            "section buffer both codecs relay verbatim rather than decode, so nothing separates them. " +
            "The pair is a removable copy until the biome tail is decoded structurally."),

        new TwinDeclaration(
            ProtocolPhase.Play,
            PacketFlow.Clientbound,
            "minecraft:level_particles",
            Band: 393,
            Twin: 477,
            Why:
            "LevelParticlesV1_13 and LevelParticlesV1_14 write the same nine fields in the same order, " +
            "an int type id, the limiter bool, float x/y/z, three float offsets, a float speed and an " +
            "int count, and then the type-specific options as the frame remainder. Vanilla agrees: the " +
            "1.14 packet changed which particles exist, not how the frame is laid out, and the options " +
            "body neither codec decodes is where the registry difference would show. The position only " +
            "widens to double at 1.15."),

        new TwinDeclaration(
            ProtocolPhase.Play,
            PacketFlow.Clientbound,
            "minecraft:set_player_team",
            Band: 393,
            Twin: 477,
            Why:
            "1.14.4-decompiled ClientboundSetPlayerTeamPacket.java:61-77 reads readUtf(16) name, " +
            "readByte method, then for add or change readComponent display, readByte options, " +
            "readUtf(40) visibility, readUtf(40) collision, the colour, readComponent prefix and " +
            "readComponent suffix, then the member list. That is field for field what 1.13.2 reads, " +
            "and both eras carry the components as JSON strings; the visibility and collision rules " +
            "only become VarInt enums at 1.21.5. The 477 band exists because the wire id moved."),
    ];

    /// <summary>The declared pairs, unordered, for exact comparison against what the harness observed.</summary>
    internal static IReadOnlySet<(ProtocolPhase, PacketFlow, string, int, int)> Pairs { get; } =
        All.Select(static t => t.Pair).ToHashSet();
}
