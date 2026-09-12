using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Xunit;
using Xunit.Abstractions;

namespace Umpk.Client.Tests;

/// <summary>What a bubble column does to the lung, and whether the client predicts it.</summary>
/// <remarks>
/// <para>A bubble column counts as water for fluid movement but refills air at four units per tick. This rule is the same in versions 1.14.4, 1.20.6, and 26.2.</para>
/// <para><c>PhysicsEngineHolder.TickAirSupply</c> must distinguish a bubble column from plain water before it calls <c>AirSupplyRule.Next</c>. Otherwise, the client predicts air loss until the next <c>set_entity_data</c> update corrects it.</para>
/// </remarks>
public sealed class BubbleColumnAirTests
{
    private const int Protocol = 772;
    private const int BedY = 64;

    private readonly ITestOutputHelper _output;

    public BubbleColumnAirTests(ITestOutputHelper output) => _output = output;

    /// <summary>A body whose eye is inside an upward bubble column refills its lung, exactly as vanilla's <c>else if</c> arm does.</summary>
    [Fact]
    public async Task ABubbleColumnRefillsTheLung()
    {
        int air = await AirAfterAsync(column: true, ticks: 40, startAir: 100);

        _output.WriteLine($"bubble column: air 100 -> {air} over 40 ticks");

        Assert.True(air > 100, $"a bubble column must refill the lung, not drain it: 100 -> {air}");
    }

    /// <summary>The control, so the row above is about the COLUMN and not about the fixture: the same shaft filled with plain water drains.</summary>
    /// <remarks>Verified to ablate. The two fixtures differ in one block type and agree on everything else, and they disagree in the outcome.</remarks>
    [Fact]
    public async Task PlainWaterInTheSameShaftDrains()
    {
        int air = await AirAfterAsync(column: false, ticks: 40, startAir: 100);

        _output.WriteLine($"plain water:   air 100 -> {air} over 40 ticks");

        Assert.True(air < 100, $"plain water must drain the lung: 100 -> {air}");
    }

    /// <summary>Fills a shaft with a bubble column or with plain water, parks a body in it, and runs the client's own air prediction for a number of ticks.</summary>
    private static async Task<int> AirAfterAsync(bool column, int ticks, int startAir)
    {
        Assert.True(JavaVersions.TryGetByProtocol(Protocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Terrain = true });
        var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };

        IBlockShapeSource shapes = JavaGameData.BlockShapes(Protocol);
        var holder = new PhysicsEngineHolder(services, shapes, NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(Protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: false);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, Protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("bubble_column"), out BlockDefinition? bubble));

        for (int x = -3; x <= 3; x++)
            for (int y = BedY - 2; y <= BedY + 20; y++)
                for (int z = -3; z <= 3; z++)
                    world.SetBlockStateId(new BlockPos(x, y, z), stone.DefaultStateId);

        // The shaft, deep enough that the body's eye is nowhere near the top of it.
        int fill = column ? bubble.MinStateId : water.MinStateId;
        for (int y = BedY; y <= BedY + 16; y++)
            world.SetBlockStateId(new BlockPos(0, y, 0), fill);

        await using (scheduler)
        {
            harness.State.Registries = JavaGameData.Registries(Protocol);
            harness.State.InstallWorld(world);
            harness.State.Self.Position = new Vec3d(0.5, BedY + 4, 0.5);
            harness.State.Self.Velocity = Vec3d.Zero;
            holder.EnsureEngine();
            holder.TickIdle();
            harness.State.Self.AirSupply = startAir;

            Assert.True(
                holder.EngineState!.Value.IsUnderWater,
                "the fixture must submerge the eye, or neither row measures anything");

            for (int tick = 0; tick < ticks; tick++)
            {
                holder.TickAirSupply();
                holder.TickIdle();
            }

            return harness.State.Self.AirSupply;
        }
    }
}
