using Umpk;
using Umpk.Protocol.Java;

namespace Umpk.IntegrationTests;

/// <summary>Defines one live-validation representative per distinct wire protocol. <see cref="Collapsed"/> records the point releases covered by each representative.</summary>
public static class LiveMatrix
{
    /// <summary>One representative (release name + wire protocol) per distinct protocol number.</summary>
    public static readonly IReadOnlyList<(string Name, int Protocol)> Representatives =
    [
        ("1.8", 47),
        ("1.9", 107),
        ("1.9.1", 108),
        ("1.9.2", 109),
        ("1.9.4", 110),
        ("1.10", 210),
        ("1.11", 315),
        ("1.11.2", 316),
        ("1.12", 335),
        ("1.12.1", 338),
        ("1.12.2", 340),
        ("1.13", 393),
        ("1.13.1", 401),
        ("1.13.2", 404),
        ("1.14", 477),
        ("1.14.1", 480),
        ("1.14.2", 485),
        ("1.14.3", 490),
        ("1.14.4", 498),
        ("1.15", 573),
        ("1.15.1", 575),
        ("1.15.2", 578),
        ("1.16", 735),
        ("1.16.1", 736),
        ("1.16.2", 751),
        ("1.16.3", 753),
        ("1.16.5", 754),
        ("1.17", 755),
        ("1.17.1", 756),
        ("1.18.1", 757),
        ("1.18.2", 758),
        ("1.19", 759),
        ("1.19.2", 760),
        ("1.19.3", 761),
        ("1.19.4", 762),
        ("1.20.1", 763),
        ("1.20.2", 764),
        ("1.20.4", 765),
        ("1.20.6", 766),
        ("1.21.1", 767),
        ("1.21.3", 768),
        ("1.21.4", 769),
        ("1.21.5", 770),
        ("1.21.6", 771),
        ("1.21.8", 772),
        ("1.21.10", 773),
        ("1.21.11", 774),
        ("26.1", 775),
        ("26.2", 776),
        ("26.3", 777),
    ];

    /// <summary>The point releases collapsed onto each tested representative (same protocol, wire-identical). This is documentation of what one leg implicitly covers, not an assertion input.</summary>
    public static readonly IReadOnlyDictionary<int, string[]> Collapsed = new Dictionary<int, string[]>
    {
        [754] = ["1.16.4", "1.16.5"],
        [757] = ["1.18", "1.18.1"],
        [760] = ["1.19.1", "1.19.2"],
        [763] = ["1.20", "1.20.1"],
        [765] = ["1.20.3", "1.20.4"],
        [766] = ["1.20.5", "1.20.6"],
        [767] = ["1.21", "1.21.1"],
        [768] = ["1.21.2", "1.21.3"],
        [772] = ["1.21.7", "1.21.8"],
        [773] = ["1.21.9", "1.21.10"],
    };

    /// <summary>True when chunk decoding supplies a queryable block-state grid. Every supported protocol does, including legacy per-block, pre-flattening palette, padded palette, and full-column layouts.</summary>
    public static bool StructuralChunkColumn(int protocol) => protocol >= 47;

    /// <summary>True when the version has a server command tree (the <c>minecraft:commands</c> / declare-commands packet, added in 1.13, protocol 393). Pre-1.13 sends none, so the honest assertion is absence.</summary>
    public static bool HasCommandTree(int protocol) => protocol >= 393;

    /// <summary>True when the version has an IMPLEMENTED (encodable) serverbound Play codec for the named packet. A verbatim marker cannot be encoded from a typed packet, so the high-level client cannot drive that send action on the version. This is the honest, code-derived gate for the client-send features. The send surface the legs drive (chat, movement, dig, place, interact, container click) is implemented across the whole current matrix, with one exception: use_item_on is unbound at 1.8 (protocol 47), which carries the legacy block-place wire instead. The gate stays dynamic so any future registry change re-gates the legs honestly instead of leaving a stale protocol-range claim here.</summary>
    public static bool SendImplemented(JavaVersion version, string packetName)
    {
        var id = Identifier.Minecraft(packetName);
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        foreach ((int _, PacketType type) in registry.Packets)
            if (type.Id == id && registry.TryGetOutbound(type, out _, out BoundPacketCodec codec))
                return codec.IsImplemented;

        return false;
    }

    /// <summary>True when the version has an IMPLEMENTED (decodable) clientbound Play codec for the named packet. Where a packet is a verbatim marker its feature is not observable in decoded ClientState and the legs record n/a rather than assert. The receive surface the legs observe decodes across the whole current matrix except the honest per-era markers: open_screen on 1.9-1.13.2 (protocols 107-404), the commands tree on 1.16-1.18.2, and player_chat from 1.20.2 (764+, where the console broadcast still decodes via system_chat). The gate stays dynamic so the bands never go stale here.</summary>
    public static bool ReceiveImplemented(JavaVersion version, string packetName)
    {
        var id = Identifier.Minecraft(packetName);
        PhaseRegistry registry = version.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id && registry.TryGetInbound(wireId, out BoundPacketCodec codec))
                return codec.IsImplemented;

        return false;
    }

    /// <summary>The literal table's protocol column, read by <c>AllProtocolTableCoverageTests</c>.</summary>
    public static IReadOnlyList<int> Protocols() => [.. Representatives.Select(static row => row.Protocol)];

    /// <summary>The MemberData source: one row per representative protocol.</summary>
    public static IEnumerable<object[]> Legs()
    {
        foreach ((string name, int protocol) in Representatives)
            yield return [name, protocol];

    }
}
