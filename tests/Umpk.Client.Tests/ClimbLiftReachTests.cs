using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Pathfinding;
using Umpk.Physics;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>How high the climb lift carries a body above the top rung of a climbable, measured on the real engine over a real <c>minecraft:vine</c>. This number decides which jump-family arms a hanging body may be offered.</summary>
/// <remarks>
/// <para>A hanging body has no ground jump. Its climb lift starts at <c>0.2</c> vertical velocity, which becomes <c>(0.2 - 0.08) * 0.98 = 0.1176</c> after gravity and drag. The lift stops when the feet cell is no longer climbable.</para>
/// <para>So the lift is not unlimited: it carries the body a fixed distance past the last rung on residual velocity, and that distance is what a step-off gets to spend. Measured here so that <c>Umpk.Pathfinding.Tests.Moves.HangingTakeoffTests</c> can say WHY the cardinal one-block step-off is kept while the corner is refused.</para>
/// </remarks>
public sealed class ClimbLiftReachTests
{
    private const int Protocol = 772;
    private const int FloorY = 64;

    /// <summary>The top rung's feet cell.</summary>
    private const int TopRungY = FloorY + 3;

    /// <summary>Holding Jump on the top rung and nothing else, the body peaks 1.2520 blocks above the rung's floor, within 0.0002 of a grounded jump's own 1.2522 apex. A full block of rise is therefore available to a step-off, and only just: there is 0.25 of margin, not a block and a half.</summary>
    [Fact]
    public void TheLiftCarriesABodyExactlyOneBlockPastTheTopRung()
    {
        PlayerPhysics engine = OnTheTopRung();

        double peak = engine.State.Position.Y;
        for (int tick = 0; tick < 40; tick++)
        {
            engine.Step(new MovementInput { Jump = true });
            peak = Math.Max(peak, engine.State.Position.Y);
        }

        Assert.Equal(1.2520, peak - TopRungY, 3);
    }

    /// <summary>The same lift, released, does NOT keep the body up: with no input at all the climbable clamp (<c>-0.15</c>) walks it straight back down the column. This is the control for the test above - the 1.2520 is bought by the press, not by the block.</summary>
    [Fact]
    public void ReleasingEverythingSinksBackDownTheColumn()
    {
        PlayerPhysics engine = OnTheTopRung();

        for (int tick = 0; tick < 40; tick++)
            engine.Step(MovementInput.None);

        Assert.True(
            engine.State.Position.Y < TopRungY,
            $"a released climber should sink, and it sat at {engine.State.Position}");
    }

    /// <summary>A body centred on the top rung of a 3-tall vine hanging on a stone column.</summary>
    private static PlayerPhysics OnTheTopRung()
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        int vine = VineAttachedToTheEastFace(blocks, data);

        for (int x = -6; x <= 6; x++)
            for (int z = -6; z <= 6; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stone.DefaultStateId);

        for (int y = FloorY + 1; y <= TopRungY; y++)
        {
            world.SetBlockStateId(new BlockPos(0, y, 0), stone.DefaultStateId);
            world.SetBlockStateId(new BlockPos(-1, y, 0), vine);
        }

        var view = PlanningWorldView.Capture(
            world, shapes, new BlockPos(-1, TopRungY, 0), new BlockPos(-1, TopRungY, 0), margin: 12);
        var engine = new PlayerPhysics(view, PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(new Vec3d(-0.5, TopRungY, 0.5), 0f, 0f);
        engine.Step(MovementInput.None);
        Assert.True(engine.State.OnClimbable, "the fixture did not put the body on the vine");
        return engine;
    }

    private static int VineAttachedToTheEastFace(Registry<BlockDefinition> blocks, IBlockDataSource data)
    {
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("vine"), out BlockDefinition? definition));
        for (int id = definition.MinStateId; id <= definition.MaxStateId; id++)
        {
            bool matched = true;
            foreach (string property in Faces)
            {
                Assert.True(data.TryGetPropertyValue(id, property, out string value), property);
                bool wanted = string.Equals(property, "east", StringComparison.Ordinal);
                if (!string.Equals(value, wanted ? "true" : "false", StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return id;

        }

        Assert.Fail("no vine state attached to the east face");
        return 0;
    }

    private static readonly string[] Faces = ["east", "north", "south", "up", "west"];
}
