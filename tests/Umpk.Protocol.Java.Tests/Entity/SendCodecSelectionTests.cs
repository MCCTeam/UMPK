using System.Buffers;
using Umpk;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>
/// Pins the per-protocol serverbound codec selection the registrar makes for the two send-side wire changes that a single shared codec key cannot resolve:
/// <list type="bullet">
/// <item>block dig (player_action): the trailing sequence VarInt only exists from 1.19 (protocol 759),
/// so 1.14-1.18.2 must select the no-sequence 1.14 codec while 1.19+ selects the sequence codec Sharing the 1.19+ codec on the mid eras appended a spurious byte the server rejected.</item>
/// <item>block place (use_item_on) on the V1_9_4 key (110/210/315/316): the cursor coords flip from i8
/// to f32 at 1.11 (protocol 315), so the codec must be picked by protocol rather than the shared key.</item>
/// </list>
/// </summary>
public class SendCodecSelectionTests
{
    private static ProtocolDescriptorBuilder NewBuilder(int protocol) =>
        new(new GameVersion(GameEdition.Java, "test", protocol), new ProtocolFeatures());

    private static byte[] EncodeOutbound(int protocol, string id, string key, object packet)
        => EncodeRegistered(protocol, PacketFlow.Serverbound, id, key, packet);

    private static byte[] EncodeRegistered(int protocol, PacketFlow flow, string id, string key, object packet)
    {
        var builder = NewBuilder(protocol);
        PacketRegistrar.Register(builder, ProtocolPhase.Play, flow, 0x00, id);
        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, flow, out PhaseRegistry reg));
        Assert.True(reg.TryGetInbound(0x00, out BoundPacketCodec codec));
        Assert.True(codec.IsImplemented, $"{id} on protocol {protocol} (key {key}) must be an implemented codec, not a marker.");

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }

    // The JoinGame (minecraft:login) dimension field is an i8 on 1.9.0 (protocol 107, byte-identical to 1.8) and widened to an i32 from 1.9.1 (protocol 108). The codec key "V1_9" spans both, so the codec must be selected by protocol. The 107 conformance corpus is byte-exact only with the i8 member. Regressing this drops the 1.9.1 live login (10 trailing bytes) or breaks the 107 corpus decode.
    [Theory]
    [InlineData(107, 14)] // i8 dimension:  playerId(4)+gameMode(1)+dim(1)+difficulty(1)+maxPlayers(1)+levelType("flat"=1+4)+rdi(1)
    [InlineData(108, 17)] // i32 dimension: same but dim is 4 bytes (+3)
    [InlineData(109, 17)] // 1.9.2 int dimension
    public void JoinGame_V1_9Key_SelectsDimensionWidth_ByProtocol(int protocol, int expectedLength)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0L, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: true, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        var login = new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: [], MaxPlayers: 20, ViewDistance: 0, SimulationDistance: 0,
            ReducedDebugInfo: false, ShowDeathScreen: true, DoLimitedCrafting: false, SpawnInfo: spawn with { GameType = 0 },
            OnlineMode: true, EnforcesSecureChat: false, Legacy: new LegacyLoginFields(0, 0, "flat"));

        byte[] frame = EncodeRegistered(protocol, PacketFlow.Clientbound, "minecraft:login", "V1_9", login);
        Assert.Equal(expectedLength, frame.Length);
    }

    [Theory]
    // 1.14-1.18.2: no trailing sequence -> varint action (1) + packed pos (8) + face (1) = 10 bytes.
    [InlineData(477, "V1_14", 10)]
    [InlineData(498, "V1_14", 10)]
    [InlineData(578, "V1_15", 10)]
    [InlineData(735, "V1_16", 10)]
    [InlineData(758, "V1_18", 10)]
    // 1.19+: adds a sequence VarInt (1 byte for a small value) = 11 bytes.
    [InlineData(759, "V1_19", 11)]
    [InlineData(770, "V1_21_5", 11)]
    public void PlayerAction_SelectsSequenceCodec_ByProtocol(int protocol, string key, int expectedLength)
    {
        var dig = new ServerboundPlayerActionPacket(Action: 0, Position: new BlockPos(-5, 10, 300), Direction: 5, Sequence: 0);
        byte[] frame = EncodeOutbound(protocol, "minecraft:player_action", key, dig);
        Assert.Equal(expectedLength, frame.Length);
    }

    [Theory]
    // 1.9.4 / 1.10 (protocols 110/210): i8 cursors -> pos(8)+face(1)+hand(1)+3 cursor bytes = 13.
    [InlineData(110, 13)]
    [InlineData(210, 13)]
    // 1.11 / 1.11.2 (protocols 315/316): f32 cursors -> pos(8)+face(1)+hand(1)+12 cursor bytes = 22.
    [InlineData(315, 22)]
    [InlineData(316, 22)]
    public void UseItemOn_V1_9_4Key_SelectsCursorForm_ByProtocol(int protocol, int expectedLength)
    {
        // The cursor is the record's 0..1 fraction on every era; the byte band scales it by 16 inside the codec. Passing raw wire sixteenths would make the codec scale them a second time.
        var place = new ServerboundUseItemOnPacket(
            Hand: 0, new BlockPos(5, 60, 5), Face: 1, CursorX: 0.5f, CursorY: 0.9375f, CursorZ: 0f,
            Inside: false, WorldBorderHit: false, Sequence: 0);
        byte[] frame = EncodeOutbound(protocol, "minecraft:use_item_on", "V1_9_4", place);
        Assert.Equal(expectedLength, frame.Length);
    }

    [Theory]
    // The 1.14 block-hit wire is hand(1) + pos(8) + face(1) + cursor f32 x3 (12) + inside(1) = 23 bytes. 1.14-1.18.2: no trailing fields = 23. 1.19-1.21.1: + sequence VarInt = 24. 1.21.2+: + world-border bool + sequence = 25. The world-border flag starts at protocol 768; the sequence starts at 759.
    [InlineData(477, "V1_14", 23)]
    [InlineData(578, "V1_15", 23)]
    [InlineData(735, "V1_16", 23)]
    [InlineData(758, "V1_18", 23)]
    [InlineData(759, "V1_19", 24)]
    [InlineData(763, "V1_20", 24)]
    [InlineData(767, "V1_21", 24)]
    [InlineData(768, "V1_21_2", 25)]
    [InlineData(770, "V1_21_5", 25)]
    public void UseItemOn_Modern_SelectsTrailingFields_ByProtocol(int protocol, string key, int expectedLength)
    {
        var place = new ServerboundUseItemOnPacket(
            Hand: 0, new BlockPos(5, 60, 5), Face: 1, CursorX: 0.5f, CursorY: 1.0f, CursorZ: 0.5f,
            Inside: false, WorldBorderHit: false, Sequence: 0);
        byte[] frame = EncodeOutbound(protocol, "minecraft:use_item_on", key, place);
        Assert.Equal(expectedLength, frame.Length);
    }
}
