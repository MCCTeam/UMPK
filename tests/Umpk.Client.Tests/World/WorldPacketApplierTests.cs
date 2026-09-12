using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests.PacketApplication.World;

/// <summary>World packet values that must survive codec selection and reach client state.</summary>
public sealed class WorldPacketApplierTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(210)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task Respawn_ChangesTheDimension(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        var packet = new ClientboundRespawnPacket(
            SpawnInfo: null,
            DataToKeep: 0,
            new LegacyRespawnFields(Dimension: -1, Difficulty: 3, GameMode: 1, LevelType: "flat"));

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(harness, protocol, "respawn", packet);

        Assert.Equal(Identifier.Minecraft("the_nether"), harness.State.World.Dimension.DimensionName);
        Assert.Equal(GameMode.Creative, harness.State.Self.GameMode);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task ForgetLevelChunk_UnloadsNamedChunk(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        ChunkUnloaded? seen = null;
        harness.Events.Subscribe<ChunkUnloaded>(value => seen = value);

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(
            harness,
            protocol,
            "forget_level_chunk",
            new ClientboundForgetLevelChunkPacket(new ChunkPos(-7, 19)));

        Assert.Equal(new ChunkPos(-7, 19), seen!.Position);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task ChangeDifficulty_PublishesValue(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        DifficultyChanged? seen = null;
        harness.Events.Subscribe<DifficultyChanged>(value => seen = value);

        await BoundPacketApplierHarness.RoundTripAndApplyAsync(
            harness,
            protocol,
            "change_difficulty",
            new ClientboundChangeDifficultyPacket(3, Locked: false));

        Assert.Equal(3, seen!.Difficulty);
        Assert.False(seen.Locked);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(340)]
    [InlineData(404)]
    public async Task MapItemData_ReachesMapState(int protocol)
    {
        ApplierHarness harness = await BoundPacketApplierHarness.JoinedAsync(protocol);
        var icons = new MapIcon[] { new(6, 10, -20, 3, DisplayName: null) };
        var packet = new ClientboundMapItemDataPacket(
            7,
            Scale: 2,
            Locked: false,
            icons,
            new MapPatch(3, 2, 4, 5, [1, 2, 3, 4, 5, 6]),
            TrackingPosition: true);
        MapDataReceived? seen = null;
        harness.Events.Subscribe<MapDataReceived>(value => seen = value);

        object decoded = await BoundPacketApplierHarness.RoundTripAndApplyAsync(
            harness,
            protocol,
            "map_item_data",
            packet);

        Assert.Equal(7, seen!.MapId);
        Assert.True(harness.State.Maps.TryGet(7, out _));
        var wire = (ClientboundMapItemDataPacket)decoded;
        Assert.True(wire.TrackingPosition);
        MapIcon icon = Assert.Single(wire.Icons!);
        Assert.Equal(6, icon.Type);
        Assert.Equal(3, icon.Rotation);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, wire.Patch.Colors);
    }
}
