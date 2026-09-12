using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.Support;

internal static class BoundPacketApplierHarness
{
    internal static JavaVersion Version(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        return version!;
    }

    internal static async Task<ApplierHarness> JoinedAsync(int protocol)
    {
        var harness = new ApplierHarness(Version(protocol));
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: -1, Dimension: "minecraft:overworld", Seed: 0, GameType: 0,
            PreviousGameType: -1, IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null,
            PortalCooldown: 0, SeaLevel: 0);
        var join = new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: [], MaxPlayers: 20,
            ViewDistance: 0, SimulationDistance: 0, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: true, EnforcesSecureChat: false,
            Legacy: new LegacyLoginFields(Dimension: 0, Difficulty: 2, LevelType: "default"));
        await harness.ApplyAsync(join);
        return harness;
    }

    internal static async Task<object> RoundTripAndApplyAsync(
        ApplierHarness harness,
        int protocol,
        string identifier,
        object packet)
    {
        object decoded = BoundDescriptorCodec.RoundTrip(protocol, identifier, packet);
        await harness.ApplyAsync(decoded);
        return decoded;
    }
}
