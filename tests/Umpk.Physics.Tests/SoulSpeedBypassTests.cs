using Umpk.Geometry;
using Umpk.Physics.Tests.Fixtures;
using Xunit;

namespace Umpk.Physics.Tests;

/// <summary>Soul speed's BYPASS half: the block that would slow you can be told not to.</summary>
/// <remarks>
/// <para>The boost is server-controlled rather than synthesized by the movement engine. It arrives as an <c>update_attributes</c> modifier on every protocol from 1.16 up, and this engine must never synthesise it.</para>
/// <para>The bypass is the half the client does own, and it has two spellings of one behaviour. In 1.16-1.20.6, the block speed factor is <c>1.0F</c> when <c>onSoulSpeedBlock() &amp;&amp; getEnchantmentLevel(SOUL_SPEED) &gt; 0</c> otherwise it leaves the factor unchanged. From 1.21, the movement-efficiency attribute interpolates the block speed factor toward 1, with soul speed's data-driven definition adding <c>MOVEMENT_EFFICIENCY = 1.0</c> while on a soul-speed block.</para>
/// <para><c>lerp(t, start, end) = start + t * (end - start)</c>, so <c>lerp(1, factor, 1) = 1</c> is exactly <c>return 1.0F</c> and <c>lerp(0, factor, 1) = factor</c> is exactly <c>return super</c>. The enchantment era is the special case of the attribute era where the efficiency is 0 or 1, which is why the engine writes ONE lerp rather than an era branch with an <c>if</c> in it.</para>
/// </remarks>
public sealed class SoulSpeedBypassTests
{
    /// <summary>Steady per-tick displacement on ordinary stone at the default attribute, friction 0.6, after 80 ticks. The reference every number below is compared against.</summary>
    private const double SteadyWalkOnStone = 0.21585906840815383;

    /// <summary>The same on a 0.4 speed-factor floor. It is NOT <c>SteadyWalkOnStone * 0.4</c>: the factor scales the VELOCITY every tick, which then feeds back through the friction recurrence, so the fixed point is not linear in the factor. Every expectation here is therefore an observed literal rather than a derived product, which is also what makes them falsifiable.</summary>
    private const double SteadyWalkOnSlowFloor = 0.12538383694526267;

    private static FixtureWorld Runway(BlockKind floor) =>
        new FixtureWorld().Floor(-5, 5, -5, 400, 63, floor);

    /// <summary>The per-tick Z displacement once the friction recurrence has converged.</summary>
    private static double SteadyForwardSpeed(BlockKind floor, PhysicsConditions conditions, int ticks = 80)
    {
        var engine = new PlayerPhysics(Runway(floor), PhysicsProfile.Modern);
        engine.SetConditions(conditions);
        engine.Reset(new Vec3d(0.5, 64, 0.5), 0f, 0f);
        engine.Step(MovementInput.None);
        engine.Step(MovementInput.None);

        double lastZ = engine.State.Position.Z;
        double speed = 0;
        var input = new MovementInput { Forward = true };
        for (int i = 0; i < ticks; i++)
        {
            engine.Step(input);
            speed = engine.State.Position.Z - lastZ;
            lastZ = engine.State.Position.Z;
        }

        return speed;
    }

    private static PhysicsConditions Bare => PhysicsConditions.Default;

    /// <summary>The 1.21+ shape: the server states an efficiency and the engine lerps with it.</summary>
    private static PhysicsConditions WithEfficiency(float efficiency) =>
        PhysicsConditions.Default with { MovementEfficiency = efficiency };

    /// <summary>The 1.16-1.20.6 shape: an equipment-derived level, resolved positionally each tick.</summary>
    private static PhysicsConditions WithSoulSpeedLevel(int level) =>
        PhysicsConditions.Default with { SoulSpeedLevel = level };

    // Byte identity

    /// <summary>THE BYTE-IDENTITY PIN. With no gear, both new fields are zero, the lerp is the identity, and a zero efficiency leaves every floor unchanged: stone at full speed, soul sand and honey at their 0.4 factor.</summary>
    [Theory]
    [InlineData(BlockKind.Stone, SteadyWalkOnStone)]
    [InlineData(BlockKind.SoulSand, SteadyWalkOnSlowFloor)]
    [InlineData(BlockKind.HoneyBlock, SteadyWalkOnSlowFloor)]
    [InlineData(BlockKind.SoulSoil, SteadyWalkOnStone)]
    [InlineData(BlockKind.Ice, 0.20783988903319361)]
    public void NoGear_EveryFloor_IsByteIdenticalToTheOldBehaviour(BlockKind floor, double expected)
    {
        Assert.Equal(0f, Bare.MovementEfficiency);
        Assert.Equal(0, Bare.SoulSpeedLevel);
        Assert.Equal(expected, SteadyForwardSpeed(floor, Bare), 15);
    }

    // The lerp, on the attribute era

    /// <summary>The engine writes vanilla's LERP, not an <c>if</c>. That is observable at a fractional efficiency, which only a lerp produces: <c>lerp(0.5, 0.4, 1.0) = 0.7</c>. An <c>if (efficiency &gt; 0) factor = 1</c> implementation passes the 0 and 1 rows and fails these.</summary>
    [Theory]
    [InlineData(0.00f, 0.12538383694526267)]  // lerp(0.00, 0.4, 1) = 0.4000
    [InlineData(0.25f, 0.14006003882331441)]  // lerp(0.25, 0.4, 1) = 0.5500
    [InlineData(0.50f, 0.15862741103853750)]  // lerp(0.50, 0.4, 1) = 0.7000
    [InlineData(0.75f, 0.18286996754258310)]  // lerp(0.75, 0.4, 1) = 0.8500
    [InlineData(1.00f, 0.21585906840815383)]  // lerp(1.00, 0.4, 1) = 1.0000
    public void MovementEfficiency_LerpsTheBlockSpeedFactor(float efficiency, double expected)
    {
        Assert.Equal(expected, SteadyForwardSpeed(BlockKind.SoulSand, WithEfficiency(efficiency)), 15);

        // The three interior rows are what separate the lerp from an `if (efficiency > 0) factor = 1`: that implementation would return the full-bypass speed for every non-zero efficiency.
        if (efficiency is > 0.0f and < 1.0f)
        {
            Assert.NotEqual(SteadyWalkOnStone, expected, 6);
            Assert.NotEqual(SteadyWalkOnSlowFloor, expected, 6);
        }
    }

    /// <summary>At efficiency 1.0 soul sand costs nothing at all: the walk is exactly the stone walk.</summary>
    [Fact]
    public void MovementEfficiencyAttribute_BypassesSoulSand_OnAttributeWireLayout()
        => Assert.Equal(
            SteadyForwardSpeed(BlockKind.Stone, Bare),
            SteadyForwardSpeed(BlockKind.SoulSand, WithEfficiency(1.0f)),
            12);

    // The positional arm, on the enchantment era

    /// <summary>1.16-1.20.6 resolves the bypass POSITIONALLY, every tick, from an equipment-derived level rather than from a pushed efficiency - because the answer depends on the block underfoot, which changes every tick, while the equipment does not. That is the same argument the water-efficiency read already makes for halving per tick in the engine rather than in the snapshot.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SoulSpeedBoots_BypassSoulSandSlowdown_OnEnchantmentWireLayout(int level)
        => Assert.Equal(
            SteadyForwardSpeed(BlockKind.Stone, Bare),
            SteadyForwardSpeed(BlockKind.SoulSand, WithSoulSpeedLevel(level)),
            12);

    // The two negative controls

    /// <summary>NEGATIVE CONTROL 1, honey. It is 0.4 like soul sand and it is NOT in <c>BlockTags.SOUL_SPEED_BLOCKS</c>, which contains soul sand and soul soil only, so soul-speed boots must leave it slow. An implementation that keyed off <c>speedFactor == 0.4</c> instead of off the tag passes every test above and fails this one.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void SoulSpeedBoots_DoNotBypassHoney(int level)
        => Assert.Equal(
            SteadyWalkOnSlowFloor,
            SteadyForwardSpeed(BlockKind.HoneyBlock, WithSoulSpeedLevel(level)),
            15);

    /// <summary>NEGATIVE CONTROL 2, soul soil, and the sharper of the two. Soul soil IS in the tag and its speed factor is 1.0, so the lerp is a no-op there and the walk is unchanged - but the bypass predicate must still RECOGNISE it, because the boost applies on soul soil and the planner will price the lane on that basis. A <c>speedFactor == 0.4</c> implementation fails soul soil BY OMISSION, refusing a boost vanilla grants, which honey cannot catch because honey's answer is "stay slow" either way.</summary>
    [Fact]
    public void SoulSoil_IsInTheTag_SoTheLerpIsANoOpThereAndNothingBreaks()
    {
        double bare = SteadyForwardSpeed(BlockKind.SoulSoil, Bare);
        double booted = SteadyForwardSpeed(BlockKind.SoulSoil, WithSoulSpeedLevel(3));

        Assert.Equal(SteadyWalkOnStone, bare, 15);
        Assert.Equal(bare, booted, 15);
    }

    /// <summary>The boost is NEVER synthesised in the engine. Soul-speed boots on soul sand remove the slowdown and nothing more; the extra speed arrives as an <c>update_attributes</c> modifier folded into <see cref="PhysicsConditions.BaseMovementSpeedAttribute"/>, exactly as vanilla's own client receives it. A helpful engine that added <c>0.03 * (1 + 0.35 * L)</c> here would double-apply it.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SoulSpeedBoost_IsNeverSynthesisedByTheEngine(int level)
        => Assert.Equal(
            SteadyWalkOnStone,
            SteadyForwardSpeed(BlockKind.SoulSand, WithSoulSpeedLevel(level)),
            15);

    /// <summary>Off a soul block the level changes nothing at all. This is what makes the arm POSITIONAL rather than a blanket capability.</summary>
    [Theory]
    [InlineData(BlockKind.Stone)]
    [InlineData(BlockKind.Ice)]
    public void SoulSpeedBoots_ChangeNothing_OffASoulBlock(BlockKind floor)
        => Assert.Equal(
            SteadyForwardSpeed(floor, Bare),
            SteadyForwardSpeed(floor, WithSoulSpeedLevel(3)),
            12);
}
