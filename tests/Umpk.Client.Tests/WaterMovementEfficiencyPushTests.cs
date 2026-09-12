using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding.Goals;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// <see cref="PhysicsEngineHolder.PushConditions"/> must wire the real depth-strider water-movement-efficiency blend from the player's own boots, instead of the hardcoded <c>0f</c> that must reach the production fluid-movement path.
/// <para>
/// Water-movement-efficiency formula across both sides of the attribute boundary (protocol 766/767, 1.20.6/1.21.1):
/// <list type="bullet">
/// <item><description>Through protocol 766, depth-strider levels 0-3 normalize to [0,1].</description></item>
/// <item><description>From protocol 767, the server sends the attribute directly in [0,1].</description></item>
/// </list>
/// <see cref="Umpk.Physics.PlayerPhysics"/>'s own <c>TravelInWater</c> already mirrors the 1.21.1+ shape (no <c>/3.0F</c>, no cap check, halves off-ground itself), so <see cref="PhysicsConditions.WaterMovementEfficiency"/> must always be the pre-normalized [0,1] fraction: for the legacy era this method must divide by 3 itself.
/// </para>
/// <para>The attribute era (1.21+) is wired, and the two eras take DIFFERENT arms of the same method for the reason vanilla does: below the boundary the client computes the blend from the boots, and above it the server owns the number and sends it. Which arm is taken is decided by <c>PhysicsEngineHolder.SessionCarriesAttribute</c> - the dataset's own answer - and by no protocol literal. <see cref="AttributeEra_TheServersAttributeIsWhatIsRead"/> proves the attribute is what is read, using boots whose enchantment would resolve to a DIFFERENT number, and <see cref="AttributeEra_DepthStriderBoots_WithNoServerAttribute_StaysZero"/> pins the conservative fallback for a session that never received it.</para>
/// </summary>
public sealed class WaterMovementEfficiencyPushTests
{
    private const int LegacyProtocol = 766; // 1.20.6: last enchantment-era protocol.

    /// <summary>The self entity id, so an update_attributes packet lands on the player.</summary>
    private const int SelfEntityId = 1;
    private const int AttributeEraProtocol = 767; // 1.21/1.21.1: first attribute-era protocol.

    /// <summary>Player-window (menu-space) FEET slot. <c>InventoryPipelineTests</c> pins this exact index ("FEET -> menu 8, not menu 5").</summary>
    private const int BootsMenuSlot = 8;

    private const int FloorY = 55;
    private const int WaterTop = 75;

    private static readonly Registry<ItemDefinition> Items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
        .Add(1, Identifier.Minecraft("diamond_boots"), new ItemDefinition(1))
        .Build();

    private static readonly Registry<EnchantmentDefinition> Enchants =
        new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(1, Identifier.Minecraft("depth_strider"), new EnchantmentDefinition(MaxLevel: 3))
            .Build();

    [Fact]
    public void NoBoots_WaterMovementEfficiency_IsByteIdenticalToTodaysHardcodedZero()
    {
        Assert.Equal(0.0f, ReadWaterMovementEfficiency(LegacyProtocol, ItemStack.Empty));
    }

    [Theory]
    [InlineData(1, 1.0f / 3.0f)]
    [InlineData(2, 2.0f / 3.0f)]
    [InlineData(3, 1.0f)]
    [InlineData(5, 1.0f)] // vanilla re-caps EnchantmentHelper's contribution at 3.0F regardless of stored level.
    public void DepthStriderBoots_WaterMovementEfficiency_MatchesVanillaFormula(int level, float expected)
    {
        float actual = ReadWaterMovementEfficiency(LegacyProtocol, BootsWithDepthStrider(level));
        Assert.Equal(expected, actual, 5);
    }

    /// <summary>The attribute era with the SAME depth-strider boots and no <c>update_attributes</c> from the server still reads <c>0.0f</c>.</summary>
    /// <remarks>
    /// <para>This replaces <c>AttributeEraProtocol_SameDepthStriderBoots_StaysInert_HonestGap</c>, which asserted the same number for a different reason: that pin was a claim about a PROTOCOL TEST short-circuiting the read, and it was written to go red when the read landed. It did.</para>
    /// <para>The claim now is about the ATTRIBUTE PATH, and it is a real one: on the attribute era the server owns this number, so a session that never received it falls back to <c>0.0</c> and NOT to the boots. That is the conservative direction - it under-states the bot's speed, so a plan is over-priced rather than under-priced - and it is the rule <c>ReadSneakingSpeed</c> already states, that computing it here would be a divergence dressed up as an improvement.</para>
    /// </remarks>
    [Fact]
    public void AttributeWireLayout_DepthStriderBoots_WithNoServerAttribute_StaysZero()
    {
        float actual = ReadWaterMovementEfficiency(AttributeEraProtocol, BootsWithDepthStrider(3));
        Assert.Equal(0.0f, actual);
    }

    /// <summary>The attribute era reads what the SERVER sent, through a real <c>update_attributes</c> packet applied by the real applier, and not what the boots say.</summary>
    /// <remarks>The boots here carry depth strider I, which on the enchantment era resolves to <c>1/3</c>. The <c>1.0</c> case is the discriminating one: reading 1.0 proves the arm taken is the attribute and not the enchantment, which the two would have hidden had they agreed.</remarks>
    [Theory]
    [InlineData(0.0, 0.0f)]
    [InlineData(1.0 / 3.0, 1.0f / 3.0f)]
    [InlineData(1.0, 1.0f)]
    public async Task AttributeWireLayout_TheServersAttributeIsWhatIsRead(double sent, float expected)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) =
            Build(AttributeEraProtocol, BootsWithDepthStrider(1));
        await ApplyWaterMovementEfficiencyAsync(harness, AttributeEraProtocol, sent);
        holder.PushConditions();

        BlockPos here = BlockPos.Containing(harness.State.Self.Position);
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(here));
        Assert.NotNull(capture);
        Assert.Equal(expected, capture!.Value.Conditions.WaterMovementEfficiency, 5);
    }

    /// <summary>Sends the attribute the way a server does, through the real <c>ClientboundUpdateAttributesPacket</c> applier path, resolving the holder id out of the protocol's own registry rather than assuming one.</summary>
    /// <remarks>The raw id differs across vanilla's 1.21.2 attribute rename (<c>generic.water_movement_efficiency</c> on 767, <c>water_movement_efficiency</c> from 768), which is the rename <c>AttributeIds.Canonical</c> exists to absorb: the LOOKUP uses the era's own spelling and the READ uses the canonical one.</remarks>
    private static async Task ApplyWaterMovementEfficiencyAsync(
        ApplierHarness harness, int protocol, double value)
    {
        Registry<AttributeDefinition> registry = JavaGameData.Registries(protocol).Attributes;
        Identifier raw = protocol >= 768
            ? Identifier.Minecraft("water_movement_efficiency")
            : Identifier.Minecraft("generic.water_movement_efficiency");
        Assert.True(registry.TryGetNetworkId(raw, out int holderId), $"proto {protocol} has no {raw}");

        await harness.ApplyAsync(new Umpk.Protocol.Java.Packets.ClientboundUpdateAttributesPacket(
            SelfEntityId, [new Umpk.Protocol.Java.Packets.AttributeSnapshot(null, holderId, value, [])]));
    }

    [Fact]
    public void DepthStrider3Boots_MakesWaterTravelFaster_ThanNoGear()
    {
        double bare = ForwardDistanceInWater(LegacyProtocol, ItemStack.Empty);
        double geared = ForwardDistanceInWater(LegacyProtocol, BootsWithDepthStrider(3));

        Assert.True(
            geared > bare + 0.3,
            $"depth-strider boots did not measurably speed up water travel: bare={bare:F4}, geared={geared:F4}");
    }

    private static ItemStack BootsWithDepthStrider(int level)
    {
        Assert.True(Items.TryGet(1, out RegistryEntry<ItemDefinition> boots));
        Assert.True(Enchants.TryGet(
            Identifier.Minecraft("depth_strider"), out RegistryEntry<EnchantmentDefinition> depthStrider));
        return new ItemStack(boots, 1)
            .With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(depthStrider, level)]));
    }

    /// <summary>Reads back the exact <see cref="PhysicsConditions"/> <c>PushConditions()</c> computed, through the real production <see cref="PhysicsEngineHolder.CapturePlan"/> seam - the only public accessor for the pushed conditions (the goal itself is irrelevant to <c>Conditions</c>; a goal already satisfied at the start block keeps the region probe trivial).</summary>
    private static float ReadWaterMovementEfficiency(int protocol, ItemStack boots)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(protocol, boots);
        BlockPos here = BlockPos.Containing(harness.State.Self.Position);
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(here));
        Assert.NotNull(capture);
        return capture!.Value.Conditions.WaterMovementEfficiency;
    }

    /// <summary>Drives real forward motion through the ONLY public holder API that presses a directional input (<c>TickApproach</c>, toward a target far enough away that "walk" wins every tick from rest) and returns the +Z distance covered. This is the end-to-end proof that the value <see cref="ReadWaterMovementEfficiency"/> pins actually reaches and changes real engine motion, not just an intermediate field.</summary>
    private static double ForwardDistanceInWater(int protocol, ItemStack boots)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(protocol, boots);
        Vec3d start = harness.State.Self.Position;
        var target = new Vec3d(start.X, start.Y, start.Z + 200.0);
        for (int i = 0; i < 40; i++)
            holder.TickApproach(target, tolerance: 0.1);

        Vec3d end = holder.EngineState!.Value.Position;
        return end.Z - start.Z;
    }

    private static (ApplierHarness Harness, PhysicsEngineHolder Holder) Build(int protocol, ItemStack boots)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Terrain = true, Inventory = true, Entities = true });
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = new ChannelSessionScheduler(),
        };

        IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
        var holder = new PhysicsEngineHolder(services, shapes, NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        var data = new RegistryBlockDataSource(blocks, isLegacy: protocol < 393);
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
            data,
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 20; z++)
                for (int y = FloorY; y <= WaterTop; y++)
                    world.SetBlockStateId(new BlockPos(x, y, z), water.DefaultStateId);

        // This suite injects fully-resolved EnchantmentInstances into the modern component, so it covers the FORMULA and the PushConditions()/CapturePlan() seam only. The real wire path - legacy NBT decoded by the bound per-protocol item codec, which never writes that component - is covered by WireDecodedWaterMovementEfficiencyTests; ReadWaterMovementEfficiency() itself needs no registry.
        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;
        harness.State.InstallWorld(world);
        harness.State.Inventory.SetSlot(InventoryState.PlayerWindowId, BootsMenuSlot, boots);
        harness.State.Self.Position = new Vec3d(0.5, 60.0, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        holder.EnsureEngine();
        holder.PushConditions();

        return (harness, holder);
    }
}
