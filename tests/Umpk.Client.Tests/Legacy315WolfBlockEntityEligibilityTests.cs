using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Game.Entities;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class Legacy315WolfBlockEntityEligibilityTests
{
    // Original als.l and its alm/aml writers identify all 47 registered block-entity families. Only spawner, chest and trapped chest have source-proven non-displacing client ticks.
    private static ReadOnlySpan<int> NativeBlockEntities =>
    [
        23, 25, 36, 52, 54, 61, 62, 63, 68, 84, 116, 117, 119, 130, 137, 138,
        140, 144, 146, 149, 150, 151, 154, 158, 176, 177, 178, 209, 210, 211,
        219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232,
        233, 234, 255,
    ];

    [Fact]
    public async Task EveryRegisteredBlockAndMetadataValueUsesItsNativeBlockEntityFlag()
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        await session.Client.InvokeAsync(client =>
        {
            int[] registered = client.State.Registries!.Blocks.Select(block => block.NetworkId).Order().ToArray();
            Assert.Equal(Enumerable.Range(0, 235).Append(255), registered);
            var native = new HashSet<int>(NativeBlockEntities.ToArray());
            var differences = new List<string>();
            int checkedStates = 0;
            foreach (int block in registered)
                for (int metadata = 0; metadata < 16; metadata++)
                {
                    InstallIsolatedState(client.State.World, block << 4 | metadata);
                    bool expected = !native.Contains(block) || block is 52 or 54 or 146;
                    bool actual = LegacyWolfPlacementEnvelope.WorldContextIsKnown(client.State.World);
                    if (actual != expected)
                        differences.Add($"block={block}, metadata={metadata}, nativeContextKnown={expected}, actual={actual}");
                    checkedStates++;
                }
            Assert.Equal(3776, checkedStates);
            Assert.Empty(differences);
            return true;
        }, session.Token);
    }

    [Fact]
    public async Task EveryUnregisteredLegacySlotRemainsUnknownForAllMetadataValues()
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        await session.Client.InvokeAsync(client =>
        {
            int checkedStates = 0;
            for (int block = 235; block < 255; block++)
            {
                Assert.False(client.State.Registries!.Blocks.ContainsNetworkId(block));
                for (int metadata = 0; metadata < 16; metadata++)
                {
                    InstallIsolatedState(client.State.World, block << 4 | metadata);
                    Assert.False(LegacyWolfPlacementEnvelope.WorldContextIsKnown(client.State.World));
                    checkedStates++;
                }
            }
            Assert.Equal(320, checkedStates);
            return true;
        }, session.Token);
    }

    [Theory]
    [InlineData(315, "grass_path", 208, true)]
    [InlineData(315, "orange_shulker_box", 220, false)]
    [InlineData(210, "grass_path", 208, false)]
    [InlineData(316, "grass_path", 208, false)]
    public async Task NativeBlockEntityContextControlsTheActualPlacementCaller(int protocol, string block, int nativeId, bool clear)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        await session.Block(BlockUseSession.Target.Offset(Direction.West), "air");
        Assert.Empty(await session.Barrier());
        await session.Hold("stone");
        var contextCell = new BlockPos(8, 65, 8);
        int state = session.Client.State.Registries!.Blocks[Identifier.Minecraft(block)].Value.DefaultStateId;
        Assert.Equal(nativeId << 4, state);
        var update = Assert.IsType<ClientboundBlockUpdatePacket>(BoundDescriptorCodec.RoundTrip(protocol, "block_update", new ClientboundBlockUpdatePacket(contextCell, state)));
        Assert.Equal((contextCell, state), (update.Position, update.BlockStateId));
        await session.Send(update);
        Assert.Empty(await session.Barrier());
        Assert.Equal(nativeId, session.Client.State.World.GetBlock(contextCell).Block.NetworkId);
        Assert.Null(session.Client.State.World.GetBlockEntityNbt(contextCell));
        await AssertUse(session, clear: true);
        var spawn = new ClientboundAddMobPacket(27, 95, 82.40966796875, 79, 153.26318359375, 0, 0, 0, 0, 0, 0, new([new(12, MetadataValue.Boolean(false))], []), Guid.Empty);
        var decoded = Assert.IsType<ClientboundAddMobPacket>(BoundDescriptorCodec.RoundTrip(protocol, "add_mob", spawn));
        Assert.Equal((27, 95, spawn.X, spawn.Y, spawn.Z, (short)0, (short)0, (short)0), (decoded.EntityId, decoded.TypeId, decoded.X, decoded.Y, decoded.Z, decoded.VelocityX, decoded.VelocityY, decoded.VelocityZ));
        EntityDataEntry age = Assert.Single(decoded.Metadata.Entries);
        Assert.Equal((12, 6, MetadataValueKind.Boolean), (age.Index, age.WireSerializerId, age.Value.Kind));
        Assert.False(age.Value.AsBoolean());
        Assert.Empty(decoded.Metadata.RawTail);
        await session.Send(decoded);
        Assert.Empty(await session.Barrier());
        await AssertUse(session, clear);
        await session.Client.InvokeAsync(client =>
        {
            Entity wolf = client.State.Entities.Get(27)!;
            Assert.Equal(Identifier.Minecraft("wolf"), wolf.Type.Id);
            Assert.False(client.State.EntityGeometry.TryGetDimensions(wolf, out _));
            Assert.False(client.State.EntityMotion.TryGetLegacyAmbientInterpolation(wolf, out _));
            Assert.False(client.State.EntityMotion.IsTrackedOrdinaryLiving(wolf));
            return true;
        }, session.Token);
    }

    private static void InstallIsolatedState(World world, int state)
    {
        var column = new ChunkColumn(new ChunkPos(8, 8), world.Dimension);
        column.SetBlockStateId(128, 65, 128, state);
        world.LoadColumn(column);
    }

    private static async Task AssertUse(BlockUseSession session, bool clear)
    {
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(BlockUseSession.Target, Direction.West, new(0, .5, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        await action.WaitAsync(session.Token);
        Assert.Equal(clear ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal(BlockUseSession.Target, use.Position);
        Assert.Equal(((int)Direction.West, (int)Hand.Main, 0F, .5F, .5F), (use.Face, use.Hand, use.CursorX, use.CursorY, use.CursorZ));
        if (clear)
            Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Offset(Direction.West)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }
}
