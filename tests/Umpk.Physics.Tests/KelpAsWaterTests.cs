using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>
/// A block that CONTAINS water behaves as water to the engine, and kelp is such a block.
///
/// <para>Kelp and seagrass cells are full water source cells for every fluid query. They do not expose a <c>waterlogged</c> property, so the dataset identifies them with a curated list rather than a property-derived flag.</para>
///
/// <para>These cases drive the ENGINE against a fixture kind flagged exactly the way <c>BlockAttributeResolver</c> flags kelp: <c>Waterlogged</c>, no <c>Fluid</c>, no collision shape. That the real generated table actually says that is pinned separately and independently, by <c>Umpk.Data.Java.Tests.BlockAttributeTests.Kelp_And_Seagrass_Carry_Water</c>, so neither test derives its expectation from the thing it is checking.</para>
/// </summary>
public sealed class KelpAsWaterTests
{
    private static PlayerPhysics Engine(FixtureWorld world, int protocol, Vec3d at)
    {
        var engine = new PlayerPhysics(world, PhysicsProfile.ForProtocol(protocol));
        engine.SetConditions(PhysicsConditions.Default);
        engine.Reset(at, 0f, 0f);
        engine.SetRotation(0f, 0f);
        return engine;
    }

    /// <summary>A kelp forest over a stone bed, with plain air above it.</summary>
    private static FixtureWorld KelpForest()
        => new FixtureWorld()
            .Fill(-4, 59, -4, 4, 59, 4, BlockKind.Stone)
            .Fill(-4, 60, -4, 4, 68, 4, BlockKind.Kelp);

    [Theory]
    [InlineData(47)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(772)]
    [InlineData(776)]
    public void ABodyInsideKelp_IsInWater(int protocol)
    {
        PlayerPhysics engine = Engine(KelpForest(), protocol, new Vec3d(0.5, 64.0, 0.5));

        engine.Step(MovementInput.None);

        Assert.True(engine.State.InWater, "kelp is a full water source cell");
        Assert.True(engine.State.IsUnderWater, "and the eye is inside it too");
    }

    /// <summary>A body outside water falls under air gravity; a body inside kelp sinks at the fluid rate, roughly one fifth as fast. This verifies that kelp selects fluid travel rather than air travel.</summary>
    [Fact]
    public void ABodyInsideKelp_SinksAtTheFluidRateNotTheAirRate()
    {
        PlayerPhysics inKelp = Engine(KelpForest(), 772, new Vec3d(0.5, 64.0, 0.5));
        PlayerPhysics inAir = Engine(
            new FixtureWorld().Fill(-4, 59, -4, 4, 59, 4, BlockKind.Stone), 772, new Vec3d(0.5, 64.0, 0.5));

        for (int i = 0; i < 20; i++)
        {
            inKelp.Step(MovementInput.None);
            inAir.Step(MovementInput.None);
        }

        double kelpDrop = 64.0 - inKelp.State.Position.Y;
        double airDrop = 64.0 - inAir.State.Position.Y;

        Assert.True(kelpDrop > 0.0, "a body in water still sinks");
        Assert.True(
            airDrop > kelpDrop * 4.0,
            $"free fall ({airDrop:F4}) should dwarf a fluid sink ({kelpDrop:F4})");
    }

    /// <summary>The ledge-hop probe refuses any cell with a non-empty fluid state, which a kelp cell does.</summary>
    [Fact]
    public void ContainsAnyLiquid_SeesAKelpCell()
    {
        var world = new FixtureWorld()
            .Fill(-4, 59, -4, 4, 59, 4, BlockKind.Stone)
            .Set(0, 60, 0, BlockKind.Kelp);

        PlayerPhysics engine = Engine(world, 772, new Vec3d(0.5, 70.0, 0.5));

        Assert.True(engine.ContainsAnyLiquid(new Aabb(0.2, 60.1, 0.2, 0.8, 60.9, 0.8)));
        Assert.False(engine.ContainsAnyLiquid(new Aabb(0.2, 61.1, 0.2, 0.8, 61.9, 0.8)));
    }
}
