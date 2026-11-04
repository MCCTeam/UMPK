using Umpk.Client.Actions;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class ModernStairPlacementSessionTests
{
    private static readonly int[] Protocols = [393, 401, 404, 477, 480, 485, 490, 498, 573, 575, 578, 735, 736, 751, 753, 754, 755, 756, 757, 758, 759, 760, 761, 762, 763, 764, 765, 766, 767, 768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly int[] Boundaries = [393, 477, 777];

    public static IEnumerable<object[]> ProtocolCases() => Protocols.Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public async Task DefaultStairReachesTheConnectedPlacementResultAndSwing(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_stairs");
        await Use(session, Session.Target, Direction.Up, new(.5, 1, .5), BlockUseResult.Success);
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    public static IEnumerable<object[]> GeometryCases()
        => new[] { 393, 777 }.SelectMany(p => new[] { 0F, 90F, 180F, 270F }.SelectMany(yaw => new[] { false, true }.SelectMany(top => new[] { "straight", "outer_left", "outer_right", "inner_left", "inner_right" }.Select(shape => new object[] { p, yaw, top, shape }))));

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public async Task NativeFacingHalfAndCornerSelectTheActualObstructionGrid(int protocol, float yaw, bool top, string shape)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_stairs");
        await Yaw(session, yaw);
        Direction facing = yaw switch { 0 => Direction.South, 90 => Direction.West, 180 => Direction.North, _ => Direction.East };
        BlockPos destination = Session.Target.Offset(Direction.East);
        string half = top ? "top" : "bottom";
        await ConfigureCorner(session, destination, facing, half, shape);
        await ProbeQuarters(session, destination, facing, half, shape, Session.Target, Direction.East, new(1, top ? .75 : .25, .5));
    }

    [Theory]
    [InlineData(764)]
    [InlineData(765)]
    public async Task ClickedStairNativePassSurvivesThePassiveDataBoundary(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        await Yaw(session, 90);
        BlockPos destination = Session.Target.Offset(Direction.East);
        await ConfigureCorner(session, destination, Direction.West, "bottom", "outer_left");
        BlockState clicked = session.Client.State.World.GetBlock(Session.Target);
        Assert.Equal(Identifier.Minecraft("stone_brick_stairs"), clicked.Block.Id);
        Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetBlock(clicked.Block.Id, out BlockInteractionInfo info));
        Assert.Equal(protocol == 765, info.PassiveUse);
        await Use(session, Session.Target, Direction.East, new(1, .25, .5), BlockUseResult.Success);
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        await session.Hold("oak_stairs");
        await ProbeQuarters(session, destination, Direction.West, "bottom", "outer_left", Session.Target, Direction.East, new(1, .25, .5));
    }

    public static IEnumerable<object[]> YawBoundaryCases()
    {
        // Original Direction.fromYRot/Mth.floor witness: 774 and 775, 32 rows. Earlier 393 uses the old source formula; 777 retains 775's formula.
        (int Bits, Direction Old, Direction Modern)[] cases =
        [
            (-1022951423, Direction.North, Direction.North),
            (-1022951424, Direction.East, Direction.East),
            (-1022951425, Direction.East, Direction.East),
            (-1036779519, Direction.East, Direction.East),
            (-1036779520, Direction.South, Direction.South),
            (-1036779521, Direction.South, Direction.South),
            (1110704127, Direction.South, Direction.South),
            (1110704128, Direction.West, Direction.West),
            (1110704129, Direction.West, Direction.West),
            (1124532223, Direction.West, Direction.West),
            (1124532224, Direction.North, Direction.North),
            (1124532225, Direction.North, Direction.North),
            (-762596663, Direction.East, Direction.South),
            (1384886985, Direction.East, Direction.East),
            (-8388609, Direction.East, Direction.South),
            (2139095039, Direction.East, Direction.East),
        ];
        return new[] { 393, 774, 775, 777 }.SelectMany(p => cases.Select(c => new object[] { p, c.Bits, p < 775 ? c.Old : c.Modern }));
    }

    [Theory]
    [MemberData(nameof(YawBoundaryCases))]
    public async Task LocalFiniteYawRetainsNativeBoundaryAndSaturatingFloorFacing(int protocol, int bits, Direction facing)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_stairs");
        float yaw = BitConverter.Int32BitsToSingle(bits);
        int before = session.ObservedOutbound.Count;
        // These are explicit local input values, distinct from the packet-populated cardinal headings above. The outgoing bound rotation frame must retain their exact float bits.
        Task rotation = await session.Client.InvokeAsync(c => c.Actions.Movement.SetRotationAsync(yaw, 0, session.Token), session.Token);
        await rotation.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        var reported = Assert.Single(session.ObservedOutbound.Skip(before).OfType<ServerboundMovePlayerRotPacket>());
        Assert.Equal(bits, BitConverter.SingleToInt32Bits(reported.Yaw));
        Assert.Equal(bits, await session.Client.InvokeAsync(c => BitConverter.SingleToInt32Bits(c.State.Self.Yaw), session.Token));
        await ProbeQuarters(session, Session.Target.Above(), facing, "bottom", "straight", Session.Target, Direction.Up, new(.5, 1, .5));
    }

    public static IEnumerable<object[]> CornerGuardCases()
        => Boundaries.SelectMany(p => new[] { "front-precedence", "side-blocker", "different-half" }.Select(s => new object[] { p, s }));

    [Theory]
    [MemberData(nameof(CornerGuardCases))]
    public async Task NativeOuterPrecedenceAndConditionalSideGuardsArePreserved(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_stairs");
        BlockPos destination = Session.Target.Above();
        await Stair(session, destination.Offset(Direction.South), "oak_stairs", "east", scenario == "different-half" ? "top" : "bottom");
        if (scenario == "front-precedence")
            await Stair(session, destination.Offset(Direction.North), "stone_brick_stairs", "west", "bottom");
        if (scenario == "side-blocker")
            await Stair(session, destination.Offset(Direction.West), "oak_stairs", "south", "bottom");
        string expected = scenario == "front-precedence" ? "outer_left" : "straight";
        await ProbeQuarters(session, destination, Direction.South, "bottom", expected, Session.Target, Direction.Up, new(.5, 1, .5));
    }

    public static IEnumerable<object[]> PrecisionCases()
    {
        (double Y, bool FloatTop, bool AbsoluteTop)[] cases = [(.5, false, false), (Math.BitIncrement(.5), false, false), (.500000001, false, true), ((double)MathF.BitIncrement(.5F), true, true), (.75, true, true)];
        return Boundaries.SelectMany(p => cases.Select(c => new object[] { p, c.Y, p <= 404 ? c.FloatTop : c.AbsoluteTop }));
    }

    [Theory]
    [MemberData(nameof(PrecisionCases))]
    public async Task CursorHalfComparisonRetainsNativeArithmetic(int protocol, double y, bool top)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_stairs");
        BlockPos destination = Session.Target.Offset(Direction.East);
        await ProbeQuarters(session, destination, Direction.South, top ? "top" : "bottom", "straight", Session.Target, Direction.East, new(1, y, .5));
    }

    public static IEnumerable<object[]> ContextCases()
        => Boundaries.SelectMany(p => new[] { "missing-front", "missing-back", "missing-side", "unused-back", "physics", "custom", "unloaded", "water", "flowing-water" }.Select(s => new object[] { p, s }));

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task RequiredNeighborKnowledgeStaysUnknownAndIrrelevantFluidDoesNotChangeCollision(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: scenario != "physics");
        await session.Hold("oak_stairs", custom: scenario == "custom");
        BlockPos clicked = scenario is "missing-front" or "missing-back" or "unused-back" ? new(1, 65, 0)
            : scenario == "missing-side" ? new(0, 65, 1) : Session.Target;
        await session.Block(clicked, "stone");
        await session.Block(clicked.Above(), "air");
        if (scenario == "missing-front")
            await Yaw(session, 180);
        if (scenario is "missing-side" or "unused-back")
            await Stair(session, clicked.Above().Offset(Direction.South), "oak_stairs", "east", "bottom");
        if (scenario is "water" or "flowing-water")
        {
            BlockDefinition water = session.Client.State.Registries!.Blocks[Identifier.Minecraft("water")].Value;
            int state = Enumerable.Range(water.MinStateId, water.StateCount).First(s => water.TryGetPropertyValue(s, "level", out string level) && level == (scenario == "water" ? "0" : "1"));
            await session.Send(new ClientboundBlockUpdatePacket(clicked.Above(), state));
        }
        if (scenario == "unloaded")
        {
            await session.Forget();
            await session.Block(clicked, "stone");
            await session.Block(clicked.Above(), "air");
        }
        BlockUseResult expected = scenario is "unused-back" or "water" or "flowing-water" ? BlockUseResult.Success : BlockUseResult.Unknown;
        await Use(session, clicked, Direction.Up, new(.5, 1, .5), expected);
    }

    public static IEnumerable<object[]> OrderingCases()
        => Boundaries.SelectMany(p => new[] { "blocked", "offhand", "cooldown", "activation", "spectator", "empty" }.Select(s => new object[] { p, s }));

    [Theory]
    [MemberData(nameof(OrderingCases))]
    public async Task ExistingPermissionActivationHandAndSwingOwnersKeepTheirOrder(int protocol, string scenario)
    {
        await using Session session = await Session.Create(protocol, scenario == "activation" ? "crafting_table" : "stone", physics: true);
        await session.Hold(scenario == "empty" ? null : "oak_stairs");
        Hand hand = Hand.Main;
        BlockUseResult expected = BlockUseResult.Success;
        if (scenario == "blocked") { await session.Block(Session.Target.Above(), "stone"); expected = BlockUseResult.Fail; }
        if (scenario == "offhand") { await session.Hold(null); await session.Offhand("oak_stairs"); hand = Hand.Off; }
        if (scenario == "cooldown") { await session.Cooldown(); expected = BlockUseResult.Pass; }
        if (scenario == "empty") expected = BlockUseResult.Pass;
        if (scenario == "spectator") { await session.Mode(GameMode.Spectator, false); expected = protocol < 768 ? BlockUseResult.Success : BlockUseResult.Consume; }
        await Use(session, Session.Target, Direction.Up, new(.5, 1, .5), expected, hand);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(340)]
    public async Task LegacyStairsRetainTheirSeparatePrecheckUncertainty(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Hold("oak_stairs");
        await Use(session, Session.Target, Direction.Up, new(.5, 1, .5), BlockUseResult.Unknown);
    }

    private static async Task ConfigureCorner(Session session, BlockPos destination, Direction facing, string half, string shape)
    {
        if (shape == "straight") return;
        Direction neighborFacing = shape.EndsWith("left", StringComparison.Ordinal) ? CounterClockwise(facing) : CounterClockwise(facing).Opposite();
        BlockPos neighbor = destination.Offset(shape.StartsWith("outer", StringComparison.Ordinal) ? facing : facing.Opposite());
        await Stair(session, neighbor, "stone_brick_stairs", Name(neighborFacing), half);
    }

    private static async Task ProbeQuarters(Session session, BlockPos destination, Direction facing, string half, string shape, BlockPos clicked, Direction face, Vec3d cursor)
    {
        int expectedState = StairState(session, "oak_stairs", Name(facing), half, shape);
        var shapes = Assert.IsAssignableFrom<IBlockCollisionGridSource>(JavaGameData.BlockShapes(session.Protocol));
        BlockCollisionGrid grid = Assert.IsType<BlockCollisionGrid>(shapes.GetCollisionGrid(new BlockState(session.Client.State.World.BlockData, expectedState)));
        // Probe just inside each quadrant and outside the base half; expected geometry is the retained native grid for literal expected properties.
        foreach ((double x, double z) in new[] { (.1, .1), (.1, .9), (.9, .1), (.9, .9) })
        {
            await Villager(session, new(destination.X + x, destination.Y + (half == "bottom" ? .75 : -1.75), destination.Z + z));
            Aabb bounds = await session.Client.InvokeAsync(client =>
            {
                var actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
                Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
                Assert.True(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(session.Protocol), out Aabb box, out bool building, session.Protocol));
                Assert.True(building);
                return box;
            }, session.Token);
            bool blocked = grid.Boxes.ToArray().Any(b => new Aabb(destination.X + b.MinX, destination.Y + b.MinY, destination.Z + b.MinZ, destination.X + b.MaxX, destination.Y + b.MaxY, destination.Z + b.MaxZ).Intersects(bounds));
            await Use(session, clicked, face, cursor, blocked ? BlockUseResult.Fail : BlockUseResult.Success);
            await session.Send(new ClientboundRemoveEntitiesPacket([42]));
            Assert.Empty(await session.Barrier());
        }
    }

    private static int StairState(Session session, string item, string facing, string half, string shape)
    {
        BlockDefinition block = session.Client.State.Registries!.Blocks[Identifier.Minecraft(item)].Value;
        return Enumerable.Range(block.MinStateId, block.StateCount).Single(s => block.TryGetPropertyValue(s, "facing", out string f) && f == facing
            && block.TryGetPropertyValue(s, "half", out string h) && h == half && block.TryGetPropertyValue(s, "shape", out string sh) && sh == shape
            && block.TryGetPropertyValue(s, "waterlogged", out string w) && w == "false");
    }

    private static async Task Stair(Session session, BlockPos cell, string item, string facing, string half)
    {
        int state = StairState(session, item, facing, half, "straight");
        await session.Send(new ClientboundBlockUpdatePacket(cell, state));
        Assert.Empty(await session.Barrier());
        Assert.Equal(state, session.Client.State.World.GetBlock(cell).StateId);
    }

    private static async Task Yaw(Session session, float yaw)
    {
        Vec3d position = session.Client.State.Self.Position;
        await session.Send(new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, yaw, 0, 0, 21, new PositionMoveRotation(position, Vec3d.Zero, yaw, 0)));
        Assert.Empty(await session.Barrier());
        Assert.Equal(yaw, session.Client.State.Self.Yaw);
    }

    private static async Task Villager(Session session, Vec3d position)
    {
        int protocol = session.Protocol;
        RegistryEntry<EntityTypeDefinition> type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft("villager")];
        Assert.True(JavaGameData.EntityGeometryData(protocol).TryGetRule(type.Id, out EntityGeometryRule rule));
        var metadata = new EntityMetadataList([new(rule.MetadataIndex, MetadataValue.Boolean(false))], []);
        if (protocol < 759)
            await session.Send(new ClientboundAddMobPacket(42, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        else
            await session.Send(new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        await session.Send(new ClientboundSetEntityDataPacket(42, metadata));
        Assert.Empty(await session.Barrier());
        Assert.Equal(position, session.Client.State.Entities.Get(42)!.Position);
    }

    private static async Task Use(Session session, BlockPos clicked, Direction face, Vec3d cursor, BlockUseResult expected, Hand hand = Hand.Main)
    {
        Assert.Empty(await session.Barrier());
        BlockUsePrediction prediction = await session.Client.InvokeAsync(c => c.Actions.Interaction.PredictBlockUse(clicked, face, cursor, hand), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        BlockUseSwing swing = expected == BlockUseResult.Success ? session.Protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, prediction.Swing);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(clicked, face, cursor, hand, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        if (session.Protocol == 47)
        {
            var legacy = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            Assert.Equal((clicked, (int)face), (legacy.Position, legacy.Face));
        }
        else
        {
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal((clicked, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
            Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
            if (session.Protocol >= 759)
            {
                Assert.False(action.IsCompleted);
                await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
            }
        }
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, p => p is ServerboundPunchPacket);
        await action.WaitAsync(session.Token);
    }

    private static Direction CounterClockwise(Direction f) => f switch { Direction.North => Direction.West, Direction.West => Direction.South, Direction.South => Direction.East, _ => Direction.North };
    private static string Name(Direction f) => f switch { Direction.North => "north", Direction.South => "south", Direction.West => "west", _ => "east" };
}
