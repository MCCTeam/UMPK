using Umpk.Client.Actions;
using Umpk.Client.Internal;
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

public sealed class CreakingHeartPlacementSessionRuntimeTests
{
    private static readonly int[] Protocols = [768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly int[] Boundaries = [768, 769, 770, 777];
    private static readonly string[] Contexts = ["matching-logs", "wrong-first-axis", "nonmember-before-missing", "unknown-tags-safe-axes", "unknown-tags-wrong-first-axis", "member-without-axis", "member-horizontal-axis", "unknown-tags-without-axis", "missing-positive", "missing-negative"];

    public static IEnumerable<object[]> NoLogCases() => Protocols.Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> FaceCases() => Boundaries.SelectMany(protocol => Enum.GetValues<Direction>().Select(face => new object[] { protocol, face }));
    public static IEnumerable<object[]> ContextCases() => Boundaries.SelectMany(protocol => Contexts.Select(context => new object[] { protocol, context }));
    public static IEnumerable<object[]> AdventureCases() => Boundaries.Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> NegativeDestinationCases() => Boundaries.SelectMany(protocol => new[] { Direction.West, Direction.North }.Select(face => new object[] { protocol, face }));

    [Fact]
    public void FiniteNativeInventoryRetainsAll165FullUnitStatesAndFortyAbsentCatalogs()
    {
        int count = 0;
        foreach (int protocol in Protocols)
        {
            RegistryAccess registries = JavaGameData.Registries(protocol);
            Assert.True(registries.Items.TryGet(Identifier.Minecraft("creaking_heart"), out _));
            Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetItem(Identifier.Minecraft("creaking_heart"), out ItemUseOnInfo item));
            Assert.Equal(ItemUseOnKind.Unknown, item.Kind);
            Assert.Equal(Identifier.Minecraft("creaking_heart"), item.PlacedBlock);
            BlockDefinition block = registries.Blocks[Identifier.Minecraft("creaking_heart")].Value;
            var source = new RegistryBlockDataSource(registries.Blocks, false);
            var grids = Assert.IsAssignableFrom<IBlockCollisionGridSource>(JavaGameData.BlockShapes(protocol));
            for (int state = block.MinStateId; state <= block.MaxStateId; state++)
            {
                Assert.True(grids.GetCollisionGrid(new BlockState(source, state))!.CanonicalFullBlock);
                count++;
            }
        }
        Assert.Equal(165, count);
        int[] absent = [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340, 393, 401, 404, 477, 480, 485, 490, 498, 573, 575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767];
        Assert.Equal(40, absent.Length);
        Assert.Equal(40, absent.Distinct().Count());
        foreach (int protocol in absent)
            Assert.False(JavaGameData.Registries(protocol).Items.TryGet(Identifier.Minecraft("creaking_heart"), out _));
    }

    [Theory]
    [MemberData(nameof(NoLogCases))]
    public async Task OrdinaryNoLogPlacementUsesTheRealCallerOnEveryParticipatingDataset(int protocol)
    {
        await using Session session = await Ready(protocol);
        await session.Hold("creaking_heart");
        await Use(session, Session.Target, Direction.Up, BlockUseResult.Success);
    }

    [Theory]
    [MemberData(nameof(FaceCases))]
    public async Task AllNativeAxesAndSchemaBoundariesKeepTheFullPlacementBox(int protocol, Direction face)
    {
        await using Session session = await Ready(protocol);
        await session.Hold("creaking_heart");
        await session.Block(Session.Target.Offset(face), "air");
        await Use(session, Session.Target, face, BlockUseResult.Success);
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task OrderedNeighborAndTagKnowledgeDoesNotInventASafeGetter(int protocol, string context)
    {
        bool missingTags = context.StartsWith("unknown-tags-", StringComparison.Ordinal);
        string? customMember = context == "member-without-axis" ? "stone" : context == "member-horizontal-axis" ? "nether_portal" : null;
        await using Session session = await Ready(protocol, missingTags, customMember);
        await session.Hold("creaking_heart");
        BlockPos clicked = Session.Target;
        Direction face = Direction.Up;
        BlockPos destination = clicked.Above();
        if (context is "nonmember-before-missing" or "unknown-tags-wrong-first-axis" or "missing-negative")
        {
            clicked = new(1, 66, 1);
            face = Direction.West;
            destination = clicked.Offset(face);
            await session.Block(clicked, "stone");
            await session.Block(destination, "air");
            if (context != "nonmember-before-missing")
                await Axis(session, clicked, "pale_oak_log", context == "missing-negative" ? "x" : "y");
        }
        else if (context == "missing-positive")
        {
            clicked = new(14, 66, 1);
            face = Direction.East;
            destination = clicked.Offset(face);
            await session.Block(clicked, "stone");
            await session.Block(destination, "air");
        }
        else
        {
            if (context is "matching-logs" or "wrong-first-axis" or "unknown-tags-safe-axes")
            {
                await Axis(session, destination.Above(), "pale_oak_log", context == "wrong-first-axis" ? "x" : "y");
                await Axis(session, destination.Below(), "pale_oak_log", "y");
            }
            if (customMember is not null)
            {
                if (customMember == "nether_portal")
                    await Axis(session, destination.Above(), customMember, "x");
                else
                    await session.Block(destination.Above(), customMember);
            }
        }
        bool safe = context is "matching-logs" or "wrong-first-axis" or "nonmember-before-missing" or "unknown-tags-safe-axes" or "unknown-tags-wrong-first-axis";
        await Use(session, clicked, face, safe ? BlockUseResult.Success : BlockUseResult.Unknown);
    }

    [Theory]
    [MemberData(nameof(AdventureCases))]
    public async Task DefaultAdventureDenialPassesAfterUseBeforeAnUnsafeGetter(int protocol)
    {
        await using Session session = await Ready(protocol, customMember: "stone");
        await session.Hold("creaking_heart");
        await session.Block(Session.Target.Above().Above(), "stone");
        await session.Mode(GameMode.Adventure, instantBuild: false);
        await Use(session, Session.Target, Direction.Up, BlockUseResult.Pass);
    }

    [Theory]
    [MemberData(nameof(NegativeDestinationCases))]
    public async Task SynthesizedNegativeEdgeDestinationDoesNotEstablishNativeGetterContext(int protocol, Direction face)
    {
        await using Session session = await Ready(protocol);
        await session.Hold("creaking_heart");
        BlockPos clicked = face == Direction.West ? new(0, 66, 1) : new(1, 66, 0);
        BlockPos destination = clicked.Offset(face);
        await session.Block(clicked, "stone");
        await session.Block(destination, "air");
        Assert.Empty(await session.Barrier());
        Assert.Equal(face == Direction.West ? new BlockPos(-1, 66, 1) : new BlockPos(1, 66, -1), destination);
        Assert.NotNull(session.Client.State.World.GetColumn(clicked));
        Assert.True(session.Client.State.EntityMembership.HasColumn(clicked));
        // Block updates create public storage; only a real chunk packet establishes terrain membership.
        Assert.NotNull(session.Client.State.World.GetColumn(destination));
        Assert.True(session.Client.State.World.GetBlock(destination).IsValid);
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        Assert.False(session.Client.State.EntityMembership.HasColumn(destination));
        Direction positive = face == Direction.West ? Direction.East : Direction.South;
        BlockPos positiveCell = destination.Offset(positive);
        Assert.Equal(clicked, positiveCell);
        BlockState positiveState = session.Client.State.JukeboxPrediction.GetBlock(session.Client.State.World, positiveCell);
        Assert.True(positiveState.IsValid);
        Assert.Equal(Identifier.Minecraft("stone"), positiveState.Block.Id);
        Assert.Equal((bool?)false, session.Client.State.RegistryTags.Contains(RegistryIds.Block, Identifier.Minecraft("pale_oak_logs"), positiveState.Block.NetworkId));
        await Use(session, clicked, face, BlockUseResult.Unknown);
        Assert.NotNull(session.Client.State.World.GetColumn(destination));
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        Assert.False(session.Client.State.EntityMembership.HasColumn(destination));
    }

    private static Task<Session> Ready(int protocol, bool missingTags = false, string? customMember = null)
    {
        string[] names = customMember is null ? ["pale_oak_log", "stripped_pale_oak_log", "pale_oak_wood", "stripped_pale_oak_wood"] : [customMember];
        int[] members = names.Select(name => JavaGameData.Registries(protocol).Blocks[Identifier.Minecraft(name)].NetworkId).ToArray();
        IPacket[] configuration = missingTags ? [] : [new ClientboundConfigUpdateTagsPacket([new TagRegistry(RegistryIds.Block, [new TagEntry(Identifier.Minecraft("pale_oak_logs"), members)])])];
        return Session.Create(protocol, "stone", physics: true, configurationPackets: configuration);
    }

    private static async Task Axis(Session session, BlockPos cell, string block, string axis)
    {
        BlockDefinition definition = session.Client.State.Registries!.Blocks[Identifier.Minecraft(block)].Value;
        int state = Enumerable.Range(definition.MinStateId, definition.StateCount).First(value => definition.TryGetPropertyValue(value, "axis", out string actual) && actual == axis);
        await session.Send(new ClientboundBlockUpdatePacket(cell, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(cell).StateId);
    }

    private static async Task Use(Session session, BlockPos clicked, Direction face, BlockUseResult expected)
    {
        Assert.Empty(await session.Barrier());
        Vec3d cursor = face switch { Direction.Down => new(.5, 0, .5), Direction.Up => new(.5, 1, .5), Direction.North => new(.5, .5, 0), Direction.South => new(.5, .5, 1), Direction.West => new(0, .5, .5), _ => new(1, .5, .5) };
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(clicked, face, cursor, Hand.Main), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        BlockUseSwing swing = expected == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, prediction.Swing);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((clicked, (int)face, 0), (use.Position, use.Face, use.Hand));
        Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
        if (swing == BlockUseSwing.Packet)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        Assert.False(action.IsCompleted);
        await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
        await action.WaitAsync(session.Token);
        Assert.True(action.IsCompletedSuccessfully);
        // These are public received-state contracts, not proof of native local block or stack prediction.
        Assert.True(session.Client.State.World.GetBlock(clicked.Offset(face)).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }
}
