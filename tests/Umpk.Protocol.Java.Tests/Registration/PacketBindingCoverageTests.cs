using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Registration;

/// <summary>Confirms that every packet under test resolves to a concrete codec at each catalog point.</summary>
public sealed class PacketBindingCoverageTests
{
    public static readonly int[] Protocols = [107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404];

    public static readonly string[] ClientboundPackets =
    [
        "animate", "block_destruction", "block_entity_data", "boss_event", "change_difficulty",
        "command_suggestions", "container_close", "container_set_data", "cooldown", "explode",
        "forget_level_chunk", "level_event", "level_particles", "map_item_data",
        "open_sign_editor", "player_abilities", "respawn", "set_camera", "set_display_objective",
        "set_entity_link", "set_objective", "set_passengers", "set_player_team", "set_score",
        "take_item_entity", "use_bed", "add_painting", "sound", "resource_pack",
    ];

    public static readonly string[] ServerboundPackets =
    [
        "command_suggestion", "container_button_click", "move_vehicle", "paddle_boat",
        "player_abilities", "player_input", "resource_pack",
    ];

    public static TheoryData<int, string> ClientboundBindings => Build(ClientboundPackets);

    public static TheoryData<int, string> ServerboundBindings => Build(ServerboundPackets);

    [Theory]
    [MemberData(nameof(ClientboundBindings))]
    public void ClientboundPacket_ResolvesImplementedCodec(int protocol, string identifier) =>
        Assert.True(BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:" + identifier).IsImplemented);

    [Theory]
    [MemberData(nameof(ServerboundBindings))]
    public void ServerboundPacket_ResolvesImplementedCodec(int protocol, string identifier) =>
        Assert.True(BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:" + identifier).IsImplemented);

    [Fact]
    public void PacketCatalog_ContainsNoMarkerCodecs()
    {
        var misses = new List<string>();
        foreach (int protocol in Protocols)
            foreach (string identifier in ClientboundPackets)
                TryResolve(misses, protocol, PacketFlow.Clientbound, identifier);

        foreach (int protocol in Protocols)
            foreach (string identifier in ServerboundPackets)
                TryResolve(misses, protocol, PacketFlow.Serverbound, identifier);

        Assert.Empty(misses);
    }

    private static TheoryData<int, string> Build(IEnumerable<string> identifiers)
    {
        var data = new TheoryData<int, string>();
        foreach (int protocol in Protocols)
            foreach (string identifier in identifiers)
                data.Add(protocol, identifier);

        return data;
    }

    private static void TryResolve(List<string> misses, int protocol, PacketFlow flow, string identifier)
    {
        try
        {
            BoundCodec.At(protocol, flow, "minecraft:" + identifier);
        }
        catch (Exception)
        {
            misses.Add($"{protocol} {flow} minecraft:{identifier}");
        }
    }
}
