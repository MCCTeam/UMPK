using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Seeded round-trip coverage for the distinct join-game field layouts.</summary>
public sealed class JoinGameRoundTripTests
{
    private static readonly CommonPlayerSpawnInfo Spawn = new(
        DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 123456789L,
        GameType: 0, PreviousGameType: -1, IsDebug: false, IsFlat: true,
        LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(7919)]
    public void CompactLayout_RoundTrips(int seed)
    {
        var random = new Random(seed);
        var packet = new ClientboundLoginPacket(
            PlayerId: random.Next(), Hardcore: random.Next(2) == 0, Dimensions: [],
            MaxPlayers: random.Next(1, 100), ViewDistance: 0, SimulationDistance: 0,
            ReducedDebugInfo: random.Next(2) == 0, ShowDeathScreen: true, DoLimitedCrafting: false,
            SpawnInfo: Spawn with { GameType = 1 }, OnlineMode: true, EnforcesSecureChat: false,
            Legacy: new LegacyLoginFields((sbyte)random.Next(-1, 2), (byte)random.Next(0, 4), "flat"));

        var decoded = CodecRoundTrip.Cycle(JoinGameCodecs.V1_8, packet);
        Assert.Equal(packet.PlayerId, decoded.PlayerId);
        Assert.Equal(packet.Hardcore, decoded.Hardcore);
        Assert.Equal(packet.Legacy, decoded.Legacy);
        Assert.Equal(packet.MaxPlayers, decoded.MaxPlayers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public void RegistryLayout_RoundTrips(int seed)
    {
        var packet = RegistryLogin(new Random(seed), onlineMode: false);
        AssertRegistryFieldsEqual(packet, CodecRoundTrip.Cycle(JoinGameCodecs.V1_21_2, packet));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(555)]
    public void OnlineModeLayout_RoundTrips(int seed)
    {
        var packet = RegistryLogin(new Random(seed), onlineMode: true);
        AssertRegistryFieldsEqual(packet, CodecRoundTrip.Cycle(JoinGameCodecs.V26_2, packet));
    }

    [Fact]
    public void OnlineModeField_AddsOneByte()
    {
        var packet = RegistryLogin(new Random(1), onlineMode: false);
        byte[] withoutField = CodecRoundTrip.Encode(JoinGameCodecs.V26_1, packet);
        byte[] withField = CodecRoundTrip.Encode(JoinGameCodecs.V26_2, packet);
        Assert.Equal(withoutField.Length + 1, withField.Length);
    }

    private static ClientboundLoginPacket RegistryLogin(Random random, bool onlineMode) => new(
        PlayerId: random.Next(), Hardcore: random.Next(2) == 0,
        Dimensions: ["minecraft:overworld", "minecraft:the_nether"],
        MaxPlayers: random.Next(1, 200), ViewDistance: random.Next(2, 32), SimulationDistance: random.Next(2, 32),
        ReducedDebugInfo: random.Next(2) == 0, ShowDeathScreen: random.Next(2) == 0,
        DoLimitedCrafting: random.Next(2) == 0, SpawnInfo: Spawn, OnlineMode: onlineMode,
        EnforcesSecureChat: random.Next(2) == 0, Legacy: null);

    private static void AssertRegistryFieldsEqual(ClientboundLoginPacket expected, ClientboundLoginPacket actual)
    {
        IReadOnlyList<string> sharedEmpty = [];
        Assert.Equal(expected with { Dimensions = sharedEmpty }, actual with { Dimensions = sharedEmpty });
        Assert.Equal(expected.Dimensions, actual.Dimensions);
    }
}
