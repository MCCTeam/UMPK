using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Section-level block updates must publish <see cref="BlockChanged"/>, on both the legacy and the modern branch.</summary>
/// <remarks>
/// <para>The multi-block applier wrote every change straight into the world and published nothing, on every protocol, while the single-block path published normally. So a consumer subscribed to <see cref="BlockChanged"/> saw one block being broken and was blind to every section-level update: an explosion, a piston, a bulk fill, a WorldEdit paste. The world state was already correct, which is exactly why this survived a large green gate, and it is why these tests assert the EVENTS and not just the world.</para>
/// <para>Both branches are covered because they decode the packed position completely differently (the legacy short is x:4 hi, z:4, y:8 lo with an ABSOLUTE y and a chunk origin from two ints; the modern one is a 12-bit local xzy against a packed SectionPos); both decoding paths must agree or half the supported range silent.</para>
/// </remarks>
public sealed class SectionBlocksUpdateEventTests
{
    // The 1.8 legacy form and a modern one. 47 is the only shape with the absolute-Y packing.
    private const int LegacyProtocol = 47;
    private const int ModernProtocol = 770;

    [Fact]
    public async Task Legacy_Branch_Publishes_One_BlockChanged_Per_Change()
    {
        var harness = await JoinedAsync(LegacyProtocol);
        var seen = new List<BlockChanged>();
        harness.Events.Subscribe<BlockChanged>(seen.Add);

        // Chunk (2, -3) so the origin arithmetic is exercised with a negative axis. Packed legacy short: x:4 hi, z:4, y:8 lo, y ABSOLUTE.
        short a = Packed(localX: 5, localZ: 9, absoluteY: 70);
        short b = Packed(localX: 0, localZ: 15, absoluteY: 12);
        await harness.ApplyAsync(new ClientboundSectionBlocksUpdatePacket(
            SectionPos: 0,
            LegacyChunkX: 2,
            LegacyChunkZ: -3,
            Changes: [new SectionBlockChange(a, 41), new SectionBlockChange(b, 57)],
            IsLegacy: true));

        Assert.Equal(2, seen.Count);
        Assert.Equal(new BlockChanged(new BlockPos((2 << 4) + 5, 70, (-3 << 4) + 9), 41), seen[0]);
        Assert.Equal(new BlockChanged(new BlockPos((2 << 4) + 0, 12, (-3 << 4) + 15), 57), seen[1]);

        // The world write is asserted alongside the event, so publishing the events but stopped writing the blocks would not pass.
        Assert.Equal(41, harness.State.World.GetBlockStateId(new BlockPos((2 << 4) + 5, 70, (-3 << 4) + 9)));
        Assert.Equal(57, harness.State.World.GetBlockStateId(new BlockPos((2 << 4) + 0, 12, (-3 << 4) + 15)));
    }

    [Fact]
    public async Task Modern_Branch_Publishes_One_BlockChanged_Per_Change()
    {
        var harness = await JoinedAsync(ModernProtocol);
        var seen = new List<BlockChanged>();
        harness.Events.Subscribe<BlockChanged>(seen.Add);

        // SectionPos packs x:22, z:22, y:20. Section (2, 4, -3) puts the section base at (32, 64, -48).
        long sectionPos = SectionPos(2, 4, -3);
        short a = (short)((5 << 8) | (9 << 4) | 3);   // localX 5, localZ 9, localY 3
        short b = (short)((15 << 8) | (0 << 4) | 15); // localX 15, localZ 0, localY 15
        await harness.ApplyAsync(new ClientboundSectionBlocksUpdatePacket(
            SectionPos: sectionPos,
            LegacyChunkX: 0,
            LegacyChunkZ: 0,
            Changes: [new SectionBlockChange(a, 1), new SectionBlockChange(b, 9)],
            IsLegacy: false));

        Assert.Equal(2, seen.Count);
        Assert.Equal(new BlockChanged(new BlockPos(32 + 5, 64 + 3, -48 + 9), 1), seen[0]);
        Assert.Equal(new BlockChanged(new BlockPos(32 + 15, 64 + 15, -48 + 0), 9), seen[1]);

        Assert.Equal(1, harness.State.World.GetBlockStateId(new BlockPos(32 + 5, 64 + 3, -48 + 9)));
        Assert.Equal(9, harness.State.World.GetBlockStateId(new BlockPos(32 + 15, 64 + 15, -48 + 0)));
    }

    /// <summary>A section update carrying many changes announces all of them. A batched-event design was considered and rejected: publishing per change keeps one event describing one block change however the server framed it, which is what the single-block path already does. The volume is bounded by the wire itself, since vanilla's packet covers one 16x16x16 section.</summary>
    [Fact]
    public async Task Every_Change_In_A_Large_Section_Update_Is_Announced()
    {
        var harness = await JoinedAsync(ModernProtocol);
        int count = 0;
        harness.Events.Subscribe<BlockChanged>(_ => count++);

        var changes = new List<SectionBlockChange>();
        for (int i = 0; i < 512; i++)
        {
            int localX = i & 0xF;
            int localZ = (i >> 4) & 0xF;
            int localY = (i >> 8) & 0xF;
            changes.Add(new SectionBlockChange((short)((localX << 8) | (localZ << 4) | localY), 1));
        }

        await harness.ApplyAsync(new ClientboundSectionBlocksUpdatePacket(
            SectionPos: SectionPos(0, 4, 0), LegacyChunkX: 0, LegacyChunkZ: 0, Changes: changes, IsLegacy: false));

        Assert.Equal(512, count);
    }

    private static short Packed(int localX, int localZ, int absoluteY) =>
        (short)((localX << 12) | (localZ << 8) | (absoluteY & 0xFF));

    private static long SectionPos(long x, long y, long z) =>
        ((x & 0x3FFFFF) << 42) | ((z & 0x3FFFFF) << 20) | (y & 0xFFFFF);

    private static async Task<ApplierHarness> JoinedAsync(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null));
        return harness;
    }
}
