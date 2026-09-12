using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>The netty-modern (protocols 735-758, MC 1.16-1.18.2) JoinGame era codecs. These tests pin the shape deltas that the registrar keys off: 1.16 uses a bit-packed gamemode byte, a resource-key-string dimension type, and a single-byte max-players; 1.16.2-1.17.1 split hardcore into its own bool, send the dimension type as an inline NBT compound, and use a VarInt max-players; 1.18 appends simulation distance.</summary>
public sealed class JoinGameWireShapeTests
{
    private static NbtCompound Registry()
    {
        var tag = new NbtCompound();
        tag.PutString("marker", "registry");
        return tag;
    }

    private static NbtCompound DimensionType()
    {
        var tag = new NbtCompound();
        tag.PutString("effects", "minecraft:overworld");
        return tag;
    }

    private static CommonPlayerSpawnInfo Spawn(string? dimensionTypeName) =>
        new(
            DimensionTypeId: 0,
            Dimension: "minecraft:overworld",
            Seed: 123456789L,
            GameType: 1,
            PreviousGameType: -1,
            IsDebug: false,
            IsFlat: true,
            LastDeathDimensionAndPos: null,
            PortalCooldown: 0,
            SeaLevel: 0)
        {
            DimensionTypeName = dimensionTypeName,
        };

    [Fact]
    public void V1_16_PacksGamemodeByte_StringDimensionType_ByteMaxPlayers()
    {
        var packet = new ClientboundLoginPacket(
            PlayerId: 7, Hardcore: true, Dimensions: ["minecraft:overworld"], MaxPlayers: 30,
            ViewDistance: 10, SimulationDistance: 0, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: Spawn("minecraft:overworld"), OnlineMode: false,
            EnforcesSecureChat: false, Legacy: null)
        {
            JoinGameRegistry = Registry(),
            DimensionTypeNbt = null,
        };

        byte[] frame = CodecRoundTrip.Encode(JoinGameCodecs.V1_16, packet);
        // playerId int (4 bytes), then the bit-packed gamemode byte: gameType 1 | hardcore bit 0x08.
        Assert.Equal(0x09, frame[4]);

        ClientboundLoginPacket back = CodecRoundTrip.Cycle(JoinGameCodecs.V1_16, packet);
        Assert.True(back.Hardcore);
        Assert.Equal((sbyte)1, back.SpawnInfo.GameType);
        Assert.Equal(30, back.MaxPlayers);
        Assert.Equal("minecraft:overworld", back.SpawnInfo.DimensionTypeName);
        Assert.Null(back.DimensionTypeNbt);
    }

    [Fact]
    public void V1_16_2_HardcoreOwnBool_InlineNbtDimensionType_NoSimulationDistance()
    {
        var packet = new ClientboundLoginPacket(
            PlayerId: 7, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 500,
            ViewDistance: 12, SimulationDistance: 9, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: Spawn(null), OnlineMode: false,
            EnforcesSecureChat: false, Legacy: null)
        {
            JoinGameRegistry = Registry(),
            DimensionTypeNbt = DimensionType(),
        };

        ClientboundLoginPacket back = CodecRoundTrip.Cycle(JoinGameCodecs.V1_16_2, packet);
        Assert.False(back.Hardcore);
        // MaxPlayers 500 does not fit in a byte: proves the VarInt path round-trips.
        Assert.Equal(500, back.MaxPlayers);
        Assert.NotNull(back.DimensionTypeNbt);
        Assert.Equal("minecraft:overworld", Assert.IsType<NbtCompound>(back.DimensionTypeNbt).GetString("effects"));
        // 1.16.2-1.17.1 carry no simulation distance, so it decodes back to zero.
        Assert.Equal(0, back.SimulationDistance);
    }

    [Fact]
    public void V1_18_AppendsSimulationDistance()
    {
        var packet = new ClientboundLoginPacket(
            PlayerId: 7, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 12, SimulationDistance: 11, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: Spawn(null), OnlineMode: false,
            EnforcesSecureChat: false, Legacy: null)
        {
            JoinGameRegistry = Registry(),
            DimensionTypeNbt = DimensionType(),
        };

        ClientboundLoginPacket back = CodecRoundTrip.Cycle(JoinGameCodecs.V1_18, packet);
        Assert.Equal(11, back.SimulationDistance);

        // The 1.16.2 codec (no simulation distance) produces a strictly shorter frame for the same packet, confirming V1_18 adds the trailing field rather than reinterpreting an existing one.
        byte[] v18 = CodecRoundTrip.Encode(JoinGameCodecs.V1_18, packet);
        byte[] v1162 = CodecRoundTrip.Encode(JoinGameCodecs.V1_16_2, packet);
        Assert.Equal(v1162.Length + 1, v18.Length);
    }
}
