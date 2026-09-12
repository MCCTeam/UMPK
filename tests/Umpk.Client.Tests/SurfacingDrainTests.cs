using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Client.Tests;

/// <summary>What the emergency ascent's held <c>Sprint</c> bit is actually worth, measured against the lung it exists to save.</summary>
/// <remarks>
/// <para><b>What actually happens</b>, and it is a clean dichotomy with no middle:</para>
/// <list type="bullet">
/// <item><description><b>Wherever air is reachable</b> (any collar at all above the water) BOTH inputs
/// refill the lung to full. The sprinting body is the one that gets there first, because <c>Jump + Sprint</c> gives more lift than <c>Jump</c> alone.</description></item>
/// <item><description><b>Wherever air is not reachable</b> (a lid resting straight on the water) BOTH
/// inputs drown. Releasing <c>Sprint</c> there does not gain a single tick of air and costs 1.2 blocks of height.</description></item>
/// </list>
/// <para>The swim latch opens by itself. A <c>Jump</c>-holding body floats until its eye clears the surface, at which point <c>_isUnderWater</c> goes false and <c>PlayerPhysics.IsSwimmingState()</c> stops being satisfied however hard <c>Sprint</c> is held. The only thing that can pin a swimmer below that line is a lid within 0.2 of the surface - and a lid that low also excludes a 1.8-tall standing body, so there is no air under it to breathe either. The equilibrium is therefore not a pose bug; it is a sealed bore, where drowning is the correct outcome and no input bit can change it.</para>
/// <para><see cref="ASealedLid_DrownsWhicheverWaySprintIsSet"/> reproduces BOTH of them in ONE geometry: <c>y=101.40 eye=101.80 pose=Swimming</c> for the sprinting arm and <c>y=100.20 eye=101.82 pose=Standing</c> for the released arm. In the same fixture, both drown.</para>
/// <para>Every row drives the controller EXACTLY as <c>PhysicsEngineHolder.TickNavigation</c>'s <c>LifeSafetyAction.Surfacing</c> branch does - <c>SetRotation</c> from the controller's own pitch step, then <c>Step</c> with the controller's own input - so nothing here restates the controller in the test's own words.</para>
/// </remarks>
public sealed class SurfacingDrainTests
{
    private const int Protocol = 772;

    /// <summary>The bed of the shaft.</summary>
    private const int BedY = 90;

    /// <summary>The top water cell.</summary>
    private const int SurfaceY = 101;

    /// <summary>The column the body sits in.</summary>
    private const int ColumnX = 25;

    /// <summary>The lung the body arrives with. This value is low enough that an ascent which does not gain reaches the drowning point inside the supervisor's own <c>MaxSurfacingTicksTotal</c>.</summary>
    private const int ArrivalAir = 100;

    /// <summary>The window each row runs over. <c>LifeSafetySupervisor.MaxSurfacingTicksTotal</c> is 150, so 120 ticks is inside the budget the supervisor would actually grant the ascent.</summary>
    private const int HoldTicks = 120;

    private readonly ITestOutputHelper _output;

    public SurfacingDrainTests(ITestOutputHelper output) => _output = output;

    /// <summary>A lid resting straight on the water: both inputs drown, and releasing <c>Sprint</c> makes the body's position strictly worse while buying no air at all.</summary>
    /// <remarks>
    /// <para>This is the geometry the "stable drowning equilibrium" was measured in, and it reproduces exactly - for BOTH inputs. The swimming body is pinned at <c>y=101.40</c> because a 0.6-tall swim box cannot rise past the stone at 102; its eye at <c>+0.4</c> is 101.80, under the surface. The standing body is pinned LOWER, at <c>y=100.20</c>, because a 1.8-tall box cannot rise past the same stone; its eye at <c>+1.62</c> is 101.82, also under the surface. Two poses, two pins, one outcome.</para>
    /// <para>Releasing <c>Sprint</c> buys 0.02 blocks of eye height, which is not enough to breathe, and costs 1.20 blocks of body height, which is the whole climb. Worse, C-3's stated trigger - "release <c>Sprint</c> once the eye is out of the water" - cannot fire in this geometry because the eye never gets out.</para>
    /// </remarks>
    [Fact]
    public void ASealedLid_DrownsWhicheverWaySprintIsSet()
    {
        Ascent held = Hold(sprint: true, collarHeight: 0);
        Ascent released = Hold(sprint: false, collarHeight: 0);

        _output.WriteLine($"  {{Jump,Sprint}} : {held}");
        _output.WriteLine($"  {{Jump}}        : {released}");

        // Both drown. This is correct: there is no air under this lid.
        Assert.True(held.EndAir < held.StartAir, $"sealed lid, sprint held: {held}");
        Assert.True(released.EndAir < released.StartAir, $"sealed lid, sprint released: {released}");

        // The two measured poses share one fixture and both drown.
        Assert.Equal(EntityPose.Swimming, held.EndPose);
        Assert.Equal(EntityPose.Standing, released.EndPose);
        Assert.Equal(101.40, held.EndY, 2);
        Assert.Equal(100.20, released.EndY, 2);

        // Releasing Sprint costs height and buys no air.
        Assert.True(
            released.EndY < held.EndY - 1.0,
            $"releasing sprint must be measured as the height loss it is: {held.EndY} -> {released.EndY}");
        Assert.Equal(held.EndAir, released.EndAir);
    }

    /// <summary>Any collar above the water lets the controller refill the lung completely.</summary>
    /// <remarks>The row that makes the one above a statement about the GEOMETRY rather than about the input. One cell of air over the water is enough, and the sprinting body gets there and fills to 300.</remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AReachableOpening_FillsTheLungWhicheverWaySprintIsSet(int collarHeight)
    {
        Ascent held = Hold(sprint: true, collarHeight: collarHeight);
        Ascent released = Hold(sprint: false, collarHeight: collarHeight);

        _output.WriteLine($"collar={collarHeight}  {{Jump,Sprint}} : {held}");
        _output.WriteLine($"collar={collarHeight}  {{Jump}}        : {released}");

        Assert.Equal(AirSupplyRule.TotalAirSupply, held.EndAir);
        Assert.Equal(AirSupplyRule.TotalAirSupply, released.EndAir);

        // Jump+Sprint lifts harder, so the sprinting body reaches the air and starts gaining no later than the other.
        Assert.True(
            held.GainingTicks >= released.GainingTicks - 2,
            $"the sprinting ascent must not be the slower one: {held} vs {released}");
    }

    /// <summary>The mechanism: the swim latch opens on its own, so <c>Sprint</c> cannot pin a body under water that it has the headroom to rise out of.</summary>
    /// <remarks>Staying in the swimming state requires water, and UMPK models the rule at <c>PlayerPhysics.IsSwimmingState() =&gt; !CreativeFlying &amp;&amp; _sprinting &amp;&amp; _isUnderWater</c>, whose <c>_isUnderWater</c> is <c>_eyeInWater &amp;&amp; _inWater</c>. So the moment the floating body's eye clears the surface the latch opens with <c>Sprint</c> still held, and it does not re-close, because re-entering needs the eye wet again and the taller standing eye is higher still.</remarks>
    [Fact]
    public void TheSwimLatchOpensByItself_OnceTheBodyHasHeadroom()
    {
        Ascent held = Hold(sprint: true, collarHeight: 2);

        _output.WriteLine($"sprint held, with headroom: {held}");

        // Sprint was held for every one of the 120 ticks, and the body is not swimming at the end of them.
        Assert.NotEqual(EntityPose.Swimming, held.EndPose);
        Assert.Equal(AirSupplyRule.TotalAirSupply, held.EndAir);
    }

    /// <summary>What one held ascent did to the body and to its lung.</summary>
    private readonly record struct Ascent(
        int StartAir, int EndAir, int MinAir, double EndY, double EndEyeY, EntityPose EndPose, int GainingTicks)
    {
        public override string ToString()
            => $"air {StartAir} -> {EndAir} (min {MinAir}, gaining {GainingTicks}/{HoldTicks})  "
                + $"y={EndY:F2} eyeY={EndEyeY:F2} pose={EndPose}";
    }

    /// <summary>Holds the emergency ascent for <see cref="HoldTicks"/> ticks and reports what happened to the lung using the client's own <see cref="AirSupplyRule"/>.</summary>
    /// <param name="sprint">Whether to hold <c>Sprint</c> alongside <c>Jump</c>. True uses <see cref="SurfacingController.Next"/>; false is the same controller with the one bit released, which isolates the effect of that input bit.</param>
    /// <param name="collarHeight">Air cells between the water surface and the stone lid.</param>
    /// <remarks>The air rule runs BEFORE the movement step, so the rule reads the water state the previous tick's movement left behind. <c>PhysicsEngineHolder</c> calls <c>TickAirSupply</c> ahead of its physics step for exactly that reason.</remarks>
    private Ascent Hold(bool sprint, int collarHeight)
    {
        PlayerPhysics engine = Engine(collarHeight);

        int air = ArrivalAir;
        int min = air;
        int gaining = 0;

        for (int tick = 0; tick < HoldTicks; tick++)
        {
            int before = air;
            air = AirSupplyRule.Next(air, engine.State.IsUnderWater, drowningImmune: false);
            if (air > before)
                gaining++;

            min = Math.Min(min, air);

            // Exactly PhysicsEngineHolder.TickNavigation's Surfacing branch, with the one bit under test.
            (MovementInput input, float yaw, float pitch) = SurfacingController.Next(engine.State);
            if (!sprint)
                input = input with { Sprint = false };

            engine.SetRotation(yaw, pitch);
            engine.Step(input);
        }

        PhysicsState end = engine.State;
        return new Ascent(ArrivalAir, air, min, end.Position.Y, end.EyePosition.Y, end.Pose, gaining);
    }

    /// <summary>A 1x1 water shaft in a stone casing whose top water cell is <see cref="SurfaceY"/>, with <paramref name="collarHeight"/> cells of air above it and a stone lid closing over those. Zero is the sealed lid; one or more is a reachable opening.</summary>
    private static PlayerPhysics Engine(int collarHeight)
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        int stoneState = stone.DefaultStateId;
        int waterState = water.MinStateId;

        for (int x = ColumnX - 4; x <= ColumnX + 4; x++)
            for (int y = BedY - 2; y <= SurfaceY + 8; y++)
                for (int z = -4; z <= 4; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stoneState);

        for (int y = BedY; y <= SurfaceY; y++)
            world.SetBlockStateId(new BlockPos(ColumnX, y, 0), waterState);

        for (int y = SurfaceY + 1; y <= SurfaceY + collarHeight; y++)
            world.SetBlockStateId(new BlockPos(ColumnX, y, 0), 0);

        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var engine = new PlayerPhysics(new WorldPhysicsView(world, shapes), PhysicsProfile.ForProtocol(Protocol));
        engine.SetConditions(PhysicsConditions.Default);

        // The body arrives already swimming, which is what a surfacing body always is: the supervisor only fires on a route that is under water, and the executor's swim template holds Sprint. Started THREE CELLS DOWN and settled with Sprint held, because the swim pose can only BEGIN with the eye already wet (swimming-state update's asymmetry); a body reset at the surface never enters it at all, and would make this fixture measure the wrong thing.
        engine.Reset(new Vec3d(ColumnX + 0.5, SurfaceY - 3, 0.5), 0f, 0f);
        for (int settle = 0; settle < 8; settle++)
            engine.Step(new MovementInput { Sprint = true, Jump = true });

        Assert.Equal(EntityPose.Swimming, engine.State.Pose);
        return engine;
    }
}
