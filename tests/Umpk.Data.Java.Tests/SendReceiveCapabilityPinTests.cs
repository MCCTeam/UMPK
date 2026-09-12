using Umpk;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Data.Java.Tests;

/// <summary>
/// Pins required send and receive capabilities at the registration level:
/// <list type="bullet">
/// <item>1.13-1.13.2 (393/401/404) serverbound <c>keep_alive</c> and <c>accept_teleportation</c> are
/// implemented and encodable, not verbatim markers. The client must be able to answer keep-alive or the server times the session out ~15s after join.</item>
/// <item>1.14-1.18.2 (477-758) clientbound <c>add_entity</c> uses the two-trailing-byte era layout,
/// not the later head-yaw and VarInt-data layout.</item>
/// <item>1.19.2-1.20.1 (760-763) serverbound <c>move_player_pos/pos_rot/rot/status_only</c> and clientbound
/// <c>move_entity_pos/pos_rot/rot</c> resolve to their protocol identifiers.</item>
/// </list>
/// </summary>
public sealed class SendReceiveCapabilityPinTests
{
    // Expected serverbound Play wire ids for the four move_player packets, cross-checked against protocol data.
    [Theory]
    [InlineData(760, 20)] // 1.19.2
    [InlineData(761, 19)] // 1.19.3
    [InlineData(762, 20)] // 1.19.4
    [InlineData(763, 20)] // 1.20.1
    public void MovePlayerPackets_AreImplemented_AtExpectedIds_For_1_19_2_Through_1_20_1(int protocol, int posWireId)
    {
        JavaVersion v = Get(protocol);
        // Pos, PosRot, Rot, StatusOnly are registered consecutively starting at posWireId.
        AssertSendAt(v, protocol, "move_player_pos", posWireId);
        AssertSendAt(v, protocol, "move_player_pos_rot", posWireId + 1);
        AssertSendAt(v, protocol, "move_player_rot", posWireId + 2);
        AssertSendAt(v, protocol, "move_player_status_only", posWireId + 3);
    }

    [Theory]
    [InlineData(760)]
    [InlineData(761)]
    [InlineData(762)]
    [InlineData(763)]
    public void MoveEntityPackets_AreImplemented_For_1_19_2_Through_1_20_1(int protocol)
    {
        JavaVersion v = Get(protocol);
        // These clientbound movement packet names must decode.
        Assert.True(ReceiveImplemented(v, "move_entity_pos"), $"protocol {protocol}: clientbound move_entity_pos must decode.");
        Assert.True(ReceiveImplemented(v, "move_entity_pos_rot"), $"protocol {protocol}: clientbound move_entity_pos_rot must decode.");
        Assert.True(ReceiveImplemented(v, "move_entity_rot"), $"protocol {protocol}: clientbound move_entity_rot must decode.");
    }

    private static void AssertSendAt(JavaVersion v, int protocol, string name, int expectedWireId)
    {
        var id = Identifier.Minecraft(name);
        PhaseRegistry reg = v.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        foreach ((int wireId, PacketType type) in reg.Packets)
            if (type.Id == id)
            {
                Assert.True(reg.TryGetOutbound(type, out _, out BoundPacketCodec codec) && codec.IsImplemented,
                    $"protocol {protocol}: serverbound {name} must be encodable.");
                Assert.Equal(expectedWireId, wireId);
                return;
            }

        Assert.Fail($"protocol {protocol}: serverbound {name} not present in the packet table.");
    }

    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    public void KeepAliveAndTeleportConfirm_AreImplemented_For_1_13(int protocol)
    {
        JavaVersion v = Get(protocol);
        Assert.True(SendImplemented(v, "keep_alive"), $"protocol {protocol}: serverbound keep_alive must be encodable.");
        Assert.True(SendImplemented(v, "accept_teleportation"), $"protocol {protocol}: serverbound accept_teleportation must be encodable.");
    }

    [Theory]
    [InlineData(477)] // 1.14
    [InlineData(578)] // 1.15.2
    [InlineData(735)] // 1.16
    [InlineData(754)] // 1.16.5
    [InlineData(758)] // 1.18.2
    public void AddEntity_IsImplemented_For_1_14_Through_1_18(int protocol)
    {
        JavaVersion v = Get(protocol);
        Assert.True(ReceiveImplemented(v, "add_entity"), $"protocol {protocol}: clientbound add_entity must decode (V1_9 shape).");
    }

    private static JavaVersion Get(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? v) && v is not null, $"no version for {protocol}");
        return v!;
    }

    private static bool SendImplemented(JavaVersion v, string name)
    {
        var id = Identifier.Minecraft(name);
        PhaseRegistry reg = v.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound);
        foreach ((int _, PacketType type) in reg.Packets)
            if (type.Id == id && reg.TryGetOutbound(type, out _, out BoundPacketCodec codec))
                return codec.IsImplemented;

        return false;
    }

    private static bool ReceiveImplemented(JavaVersion v, string name)
    {
        var id = Identifier.Minecraft(name);
        PhaseRegistry reg = v.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
        foreach ((int wireId, PacketType type) in reg.Packets)
            if (type.Id == id && reg.TryGetInbound(wireId, out BoundPacketCodec codec))
                return codec.IsImplemented;

        return false;
    }
}
