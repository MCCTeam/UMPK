using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class OrdinaryCubeDestinationMembershipSessionRuntimeTests
{
    private static readonly int[] ModernBoundaries = [498, 735, 755, 759, 766, 770, 777];
    private static readonly int[] LegacyControls = [47, 315, 340];
    private static readonly Direction[] Edges = [Direction.West, Direction.North];
    private static readonly string[] Availability = ["synthesized-air", "synthesized-solid", "unloaded-air", "known-air", "known-void-air", "known-solid"];

    public static IEnumerable<object[]> ModernCases() => ModernBoundaries.SelectMany(protocol => Edges.SelectMany(face => Availability.Select(availability => new object[] { protocol, face, availability })));

    public static IEnumerable<object[]> LegacyCases() => LegacyControls.SelectMany(protocol => Edges.SelectMany(face => new[] { false, true }.Select(member => new object[] { protocol, face, member })));

    [Theory]
    [MemberData(nameof(ModernCases))]
    public async Task OrdinaryCubeKeepsDestinationAvailabilitySeparateFromAirIdentity(int protocol, Direction face, string availability)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Hold("stone");
        Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetItem(Identifier.Minecraft("stone"), out ItemUseOnInfo item));
        Assert.Equal(ItemUseOnKind.SimpleCubePlacement, item.Kind);
        BlockPos clicked = face == Direction.West ? new(0, 66, 1) : new(1, 66, 0);
        BlockPos destination = clicked.Offset(face);
        ChunkPos destinationChunk = ChunkPos.Containing(destination);
        Assert.Equal(face == Direction.West ? new ChunkPos(-1, 0) : new ChunkPos(0, -1), destinationChunk);
        await session.Block(clicked, "stone");
        bool member = availability.StartsWith("known-", StringComparison.Ordinal);
        if (member || availability == "unloaded-air")
            await session.Terrain(destinationChunk.X, destinationChunk.Z);
        if (availability == "unloaded-air")
        {
            Assert.True(session.Client.State.EntityMembership.HasColumn(destination));
            await session.Send(new ClientboundForgetLevelChunkPacket(destinationChunk));
            Assert.Empty(await session.Barrier());
            Assert.False(session.Client.State.EntityMembership.HasColumn(destination));
            Assert.Null(session.Client.State.World.GetColumn(destination));
        }
        string storedName = availability is "synthesized-solid" or "known-solid" ? "stone" : availability == "known-void-air" ? "void_air" : "air";
        await session.Block(destination, storedName);
        Assert.Empty(await session.Barrier());
        Assert.Equal(face == Direction.West ? new BlockPos(-1, 66, 1) : new BlockPos(1, 66, -1), destination);
        Assert.True(session.Client.State.EntityMembership.HasColumn(clicked));
        Assert.Equal(Identifier.Minecraft("stone"), session.Client.State.World.GetBlock(clicked).Block.Id);
        Assert.NotNull(session.Client.State.World.GetColumn(destination));
        BlockState stored = session.Client.State.World.GetBlock(destination);
        Assert.True(stored.IsValid);
        Assert.Equal(Identifier.Minecraft(storedName), stored.Block.Id);
        Assert.Equal(member, session.Client.State.EntityMembership.HasColumn(destination));
        BlockState queried = session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, destination);
        Assert.True(queried.IsValid);
        Assert.Equal(Identifier.Minecraft(member ? storedName : "void_air"), queried.Block.Id);
        // Unknown is the library availability policy. Native EmptyLevelChunk write failure is a separate source fact; this test does not claim a native result was measured.
        BlockUsePrediction expected = !member ? BlockUsePrediction.Unknown : availability == "known-solid" ? BlockUsePrediction.Fail : BlockUsePrediction.Success(protocol == 777);
        await AssertUse(session, clicked, face, expected);
        Assert.Equal(member, session.Client.State.EntityMembership.HasColumn(destination));
        Assert.Equal(stored.StateId, session.Client.State.World.GetBlock(destination).StateId);
        // Original successful default stone placement consumes one item before USE; ACK restores terrain independently of inventory correction.
        int expectedCount = protocol is >= 766 and <= 777 && availability == "known-air" ? 0 : 1;
        Assert.Equal(expectedCount, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [MemberData(nameof(LegacyCases))]
    public async Task LegacyPrecheckRetainsItsExistingDestinationAvailabilityDecision(int protocol, Direction face, bool member)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Mode(GameMode.Survival, instantBuild: false);
        await session.Hold("stone");
        BlockPos clicked = face == Direction.West ? new(0, 66, 1) : new(1, 66, 0);
        BlockPos destination = clicked.Offset(face);
        Assert.Equal(face == Direction.West ? new ChunkPos(-1, 0) : new ChunkPos(0, -1), ChunkPos.Containing(destination));
        if (member)
        {
            ChunkPos column = ChunkPos.Containing(destination);
            await session.Terrain(column.X, column.Z);
        }
        await session.Block(clicked, "stone");
        await session.Block(destination, "air");
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.EntityMembership.HasColumn(clicked));
        Assert.NotNull(session.Client.State.World.GetColumn(destination));
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        Assert.Equal(member, session.Client.State.EntityMembership.HasColumn(destination));
        await AssertUse(session, clicked, face, member ? BlockUsePrediction.Success(false) : BlockUsePrediction.Unknown);
        Assert.Equal(member, session.Client.State.EntityMembership.HasColumn(destination));
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    private static async Task AssertUse(Session session, BlockPos clicked, Direction face, BlockUsePrediction expected)
    {
        Assert.Empty(await session.Barrier());
        Vec3d cursor = face == Direction.West ? new(0, .5, .5) : new(.5, .5, 0);
        BlockUsePrediction observed = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(clicked, face, cursor, Hand.Main), session.Token);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        // Finish the actual packet group's acknowledgment before assertions, including the anticipated red run.
        if (session.Protocol >= 759 && packets.FirstOrDefault() is ServerboundUseItemOnPacket actualUse)
        {
            Assert.False(action.IsCompleted);
            await session.Send(new ClientboundBlockChangedAckPacket(actualUse.Sequence));
        }
        await action.WaitAsync(session.Token);
        Assert.True(action.IsCompletedSuccessfully);
        Assert.Equal(expected, observed);
        Assert.Equal(expected.Swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        if (session.Protocol == 47)
        {
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal((clicked, (int)face), (use.Position, use.Face));
            Assert.Equal(((byte)(cursor.X * 16), (byte)(cursor.Y * 16), (byte)(cursor.Z * 16)), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.Equal(Identifier.Minecraft("stone"), use.HeldItem.Item.Id);
            Assert.Equal(1, use.HeldItem.Count);
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((clicked, (int)face, 0), (use.Position, use.Face, use.Hand));
            Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.False(use.Inside);
            Assert.False(use.WorldBorderHit);
        }
        if (expected.Swing == BlockUseSwing.Packet)
        {
            var swing = Assert.IsType<ServerboundSwingPacket>(packets[1]);
            Assert.Equal(0, swing.Hand);
            Assert.Equal(session.Protocol >= 107, swing.HasHand);
        }
        Assert.DoesNotContain(packets, packet => packet is ServerboundPlayerActionPacket or ServerboundPunchPacket);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }
}
