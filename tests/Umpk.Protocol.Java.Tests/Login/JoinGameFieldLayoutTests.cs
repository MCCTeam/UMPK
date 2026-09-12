using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Join-game fields whose presence or wire representation changes at codec boundaries.</summary>
public sealed class JoinGameFieldLayoutTests
{
    private static ClientboundLoginPacket Login(CommonPlayerSpawnInfo spawn, bool enforces) => new(
        PlayerId: 77, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
        ViewDistance: 10, SimulationDistance: 10, ReducedDebugInfo: false, ShowDeathScreen: true,
        DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: enforces,
        Legacy: null);

    [Fact]
    public void StringDimensionType_OmitsSecureChatFlag()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: -1, Dimension: "minecraft:overworld", Seed: 42L, GameType: 0,
            PreviousGameType: -1, IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null,
            PortalCooldown: 0, SeaLevel: 0)
        { DimensionTypeName = "minecraft:overworld" };

        var decoded = CodecRoundTrip.Cycle(JoinGameCodecs.V1_20_2, Login(spawn, enforces: true));
        Assert.Equal("minecraft:overworld", decoded.SpawnInfo.DimensionTypeName);
        Assert.False(decoded.EnforcesSecureChat);
    }

    [Fact]
    public void NumericDimensionType_PreservesSecureChatFlag()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 3, Dimension: "minecraft:the_end", Seed: -7L, GameType: 1,
            PreviousGameType: 0, IsDebug: false, IsFlat: true, LastDeathDimensionAndPos: null,
            PortalCooldown: 5, SeaLevel: 0);

        var decoded = CodecRoundTrip.Cycle(JoinGameCodecs.V1_20_5, Login(spawn, enforces: true));
        Assert.Equal(3, decoded.SpawnInfo.DimensionTypeId);
        Assert.True(decoded.EnforcesSecureChat);
        Assert.Equal("minecraft:the_end", decoded.SpawnInfo.Dimension);
        Assert.Equal(5, decoded.SpawnInfo.PortalCooldown);
    }

    [Fact]
    public void LayoutBeforeSeaLevel_OmitsSeaLevel()
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0L, GameType: 0,
            PreviousGameType: -1, IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null,
            PortalCooldown: 0, SeaLevel: 63);
        byte[] encoded = CodecRoundTrip.Encode(JoinGameCodecs.V1_20_5, Login(spawn, enforces: false));
        byte[] baseline = CodecRoundTrip.Encode(JoinGameCodecs.V1_21_2, Login(spawn, enforces: false));
        Assert.Equal(baseline.Length - 1, encoded.Length);
    }
}
