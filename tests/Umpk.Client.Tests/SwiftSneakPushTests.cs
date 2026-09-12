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
using Umpk.Protocol.Java.Packets;
using Xunit;
using AttributeModifierEntry = Umpk.Protocol.Java.Packets.AttributeModifierEntry;

namespace Umpk.Client.Tests;

/// <summary><see cref="PhysicsEngineHolder.PushConditions"/> must resolve the real crouch factor, from the leggings' swift-sneak enchantment below 1.21 and from the <c>minecraft:sneaking_speed</c> attribute from 1.21, instead of leaving the engine on its hardcoded 0.3.</summary>
/// <remarks>
/// <para>Both eras compute <c>clamp(0.3 + 0.15*L, 0, 1)</c>. Older protocols derive the value from the swift-sneak enchantment level. Newer protocols receive the <c>sneaking_speed</c> attribute.</para>
/// <para>The era gate is the DATASET, never a protocol literal: 766's <c>minecraft:attribute</c> registry carries no <c>sneaking_speed</c> and 767's does. That is exactly the 1.20.6/1.21 boundary, read out of the data.</para>
/// </remarks>
public sealed class SwiftSneakPushTests
{
    /// <summary>1.20.6, the last enchantment-era protocol.</summary>
    private const int EnchantmentEraProtocol = 766;

    /// <summary>1.21/1.21.1, the first protocol whose registry carries <c>player.sneaking_speed</c>.</summary>
    private const int AttributeEraProtocol = 767;

    /// <summary>1.21.11, the protocol the live course runs on.</summary>
    private const int LiveProtocol = 774;

    /// <summary>1.18.2, before swift sneak existed at all.</summary>
    private const int PreSwiftSneakProtocol = 758;

    private const int SelfEntityId = 1;

    /// <summary>The player-window (menu-space) slot index of the LEGGINGS slot. <c>PlayerInventorySlotMap</c>'s layout doc says the menu runs "5-8 armor, head first", so the order is HEAD 5, CHEST 6, LEGS 7, FEET 8. <see cref="LeggingsMenuSlot_IsSeven"/> pins it the way <c>InventoryPipelineTests</c> pins boots = 8, because the boots comment exists precisely because the naive index was wrong once already.</summary>
    private const int LeggingsMenuSlot = 7;

    private static readonly Identifier SneakingSpeed = Identifier.Minecraft("sneaking_speed");

    private static readonly Registry<ItemDefinition> Items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
        .Add(1, Identifier.Minecraft("netherite_leggings"), new ItemDefinition(1))
        .Build();

    /// <summary>The slot pin. Menu 5-8 is the armor block head-first, so leggings are 7 and boots are 8; the INVENTORY space runs the other way (FEET 36 .. HEAD 39), which is the trap this records.</summary>
    [Fact]
    public void LeggingsMenuSlot_IsSeven()
    {
        Assert.True(PlayerInventorySlotMap.TryToMenuSlot(39, out int head));
        Assert.True(PlayerInventorySlotMap.TryToMenuSlot(38, out int chest));
        Assert.True(PlayerInventorySlotMap.TryToMenuSlot(37, out int legs));
        Assert.True(PlayerInventorySlotMap.TryToMenuSlot(36, out int feet));

        Assert.Equal(5, head);
        Assert.Equal(6, chest);
        Assert.Equal(LeggingsMenuSlot, legs);
        Assert.Equal(8, feet);
    }

    /// <summary>With no leggings or attribute, every era pushes the byte-identical default factor of 0.3.</summary>
    [Theory]
    [InlineData(PreSwiftSneakProtocol)]
    [InlineData(EnchantmentEraProtocol)]
    [InlineData(AttributeEraProtocol)]
    [InlineData(LiveProtocol)]
    public void NoGear_SneakingSpeedFactor_IsByteIdenticalToTheOldLiteral(int protocol)
        => Assert.Equal(0.3f, PushedSneakingSpeed(protocol, ItemStack.Empty));

    /// <summary>The enchantment era resolves the level off the leggings and produces <c>clamp(0.3 + 0.15*L, 0, 1)</c>. Level 5 is constructible through NBT and must clamp at 1.0, which the 1.21 attribute's <c>[0,1]</c> range also enforces for free.</summary>
    [Theory]
    [InlineData(1, 0.45f)]
    [InlineData(2, 0.60f)]
    [InlineData(3, 0.75f)]
    [InlineData(5, 1.0f)]
    public void SwiftSneakLeggings_ProduceVanillaFactor_OnEnchantmentWireLayout(int level, float expected)
        => Assert.Equal(expected, PushedSneakingSpeed(EnchantmentEraProtocol, LeggingsWithSwiftSneak(level)), 5);

    /// <summary>Below 1.19 the enchantment simply does not exist, and the DATASET is what says so rather than a version check in the holder: protocol 758's <c>minecraft:enchantment</c> registry carries no <c>minecraft:swift_sneak</c>, so no legitimate server can put one on a stack and the read finds level 0. That is why the pre-1.19 band needs no era gate of its own - the formula degenerates to vanilla's 0.3 all by itself.</summary>
    /// <remarks>The test deliberately asserts the REGISTRY fact and an ordinary unenchanted stack, not a hand-injected swift-sneak component. <c>ItemStack.TryGetEnchantmentLevel</c> reads a resolved component or a namespaced NBT id without consulting any registry - which is exactly what makes it work across the whole legacy band - so feeding it an enchantment that version never had would be testing the fixture, not the client.</remarks>
    [Fact]
    public void PreSwiftSneakWireLayout_HasNoSuchEnchantment_SoTheFactorStaysAtPointThree()
    {
        Assert.False(
            JavaGameData.Registries(PreSwiftSneakProtocol).Enchantments
                .TryGet(Identifier.Minecraft("swift_sneak"), out _),
            "protocol 758 (1.18.2) must not declare minecraft:swift_sneak");
        Assert.True(
            JavaGameData.Registries(EnchantmentEraProtocol).Enchantments
                .TryGet(Identifier.Minecraft("swift_sneak"), out _),
            "protocol 766 (1.20.6) must declare minecraft:swift_sneak");

        Assert.Equal(0.3f, PushedSneakingSpeed(PreSwiftSneakProtocol, ItemStack.Empty));
    }

    /// <summary>The attribute era reads <c>minecraft:sneaking_speed</c> off the wire instead, so a server that states 0.75 is believed regardless of what the leggings say. That is vanilla's own behaviour: the 1.21 client never computes the bonus, it reads the attribute the server resolved.</summary>
    [Theory]
    [InlineData(AttributeEraProtocol)]
    [InlineData(LiveProtocol)]
    public async Task SneakingSpeedAttribute_IsRead_OnAttributeWireLayout(int protocol)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(protocol, ItemStack.Empty);
        await ApplySneakingSpeedAsync(harness, protocol, baseValue: 0.3, modifier: 0.45);
        holder.PushConditions();

        Assert.Equal(0.75f, Pushed(harness, holder).SneakingSpeedFactor, 5);
    }

    /// <summary>On the attribute era the LEGGINGS must not be consulted at all. Vanilla's 1.21 client has no enchantment arm here, and using one would be a divergence dressed up as an improvement: the server owns the number.</summary>
    [Fact]
    public void AttributeWireLayout_IgnoresTheLeggingsEnchantment()
        => Assert.Equal(0.3f, PushedSneakingSpeed(LiveProtocol, LeggingsWithSwiftSneak(3)));

    /// <summary>The resolved attribute is narrowed to <c>float</c> at the read, mirroring the <c>(float)</c> cast in newer layouts. It is not cosmetic: swift sneak's modifier amount is a <c>float</c> <c>0.15F</c> widened for the attribute API, so three levels resolve in double to <c>0.7500000178813935</c>, not <c>0.75</c>. Keeping the value in double diverges from vanilla at every level.</summary>
    [Fact]
    public async Task ResolvedAttribute_IsNarrowedToFloat_LikeVanillasRead()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(LiveProtocol, ItemStack.Empty);
        await ApplySneakingSpeedAsync(harness, LiveProtocol, baseValue: 0.3, modifier: 3 * (double)0.15f);
        holder.PushConditions();

        double asDouble = harness.State.Self.Attributes.Value(SneakingSpeed, fallback: -1);
        Assert.NotEqual(0.75, asDouble, 12);
        Assert.Equal(0.75f, Pushed(harness, holder).SneakingSpeedFactor);
    }

    /// <summary>The navigation surface affected by Swift Sneak is the sub-block approach controller, which predicts what the engine will do with a crouching press. It ranks coast against a sneaking press against a walking press by the error each would leave, and a controller whose crouch factor disagrees with the engine's ranks them wrongly.</summary>
    /// <remarks>
    /// <para>The controller and engine must use the same crouch factor. With Swift Sneak III the factor is 0.75; predicting 0.3 makes each press travel two and a half times farther than expected and causes an oscillation. At factor 0.75, each tested offset from 0.16 to 0.50 must settle within nine ticks.</para>
    /// <para>Course row G12e validates the same behavior in the maintained live fixture.</para>
    /// </remarks>
    [Theory]
    [InlineData(0.3f, 0.16)]
    [InlineData(0.3f, 0.25)]
    [InlineData(0.3f, 0.50)]
    [InlineData(0.45f, 0.16)]
    [InlineData(0.45f, 0.25)]
    [InlineData(0.45f, 0.50)]
    [InlineData(0.75f, 0.16)]
    [InlineData(0.75f, 0.20)]
    [InlineData(0.75f, 0.25)]
    [InlineData(0.75f, 0.30)]
    [InlineData(0.75f, 0.50)]
    [InlineData(1.0f, 0.25)]
    public void SubBlockApproach_Terminates_UnderEveryCrouchFactor(float factor, double offset)
    {
        const int Budget = 30;
        (double remaining, int ticks, bool done) = Approach(factor, offset, tolerance: 0.01, budget: Budget);

        Assert.True(
            done,
            $"approach at factor {factor} over {offset} did not finish inside {Budget} ticks "
            + $"({ticks} used, {remaining:F6} left) - this is the oscillation a mispredicted crouch "
            + "factor produces, not a tolerance problem");
    }

    /// <summary>Swift sneak makes the smallest sub-block approach step coarser, not finer.</summary>
    /// <remarks>
    /// <para>The controller's finest available input is one whole tick of sneaking press, and its size is proportional to the crouch factor. At the default 0.3 that is worth about 0.065 of a block once friction has finished with it; at swift sneak III's 0.75 it is worth about 0.162. So the settled residual can only get LARGER with the enchantment on, and the controller correctly stops early rather than swinging - which is what the "as close as vanilla movement can get" arm is for.</para>
    /// <para>A larger crouch factor cannot guarantee a smaller residual distance. The useful property is that the approach arrives without oscillating.</para>
    /// </remarks>
    [Fact]
    public void SwiftSneak_CoarsensTheFinestApproach_RatherThanRefiningIt()
    {
        (double bare, _, bool bareDone) = Approach(0.3f, offset: 0.16, tolerance: 0.01, budget: 30);
        (double geared, _, bool gearedDone) = Approach(0.75f, offset: 0.16, tolerance: 0.01, budget: 30);

        Assert.True(bareDone);
        Assert.True(gearedDone);
        Assert.True(
            geared >= bare,
            $"swift sneak should coarsen the approach, not refine it: bare={bare:F6}, geared={geared:F6}");
    }

    /// <summary>Runs the real approach loop and reports what it settled at.</summary>
    private static (double Remaining, int Ticks, bool Done) Approach(
        float factor, double offset, double tolerance, int budget)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(LiveProtocol, ItemStack.Empty);
        harness.State.Self.Attributes.GetOrCreate(
            SneakingSpeed,
            JavaGameData.Registries(LiveProtocol).Attributes[Identifier.Minecraft("sneaking_speed")])
            .BaseValue = factor;
        holder.PushConditions();
        Assert.Equal(factor, holder.EngineConditions!.Value.SneakingSpeedFactor, 5);

        Vec3d start = harness.State.Self.Position;
        var target = new Vec3d(start.X, start.Y, start.Z + offset);
        int ticks = 0;
        bool done = false;
        while (ticks < budget && !done)
        {
            done = holder.TickApproach(target, tolerance);
            ticks++;
        }

        Vec3d end = holder.EngineState!.Value.Position;
        return (Math.Abs(target.Z - end.Z), ticks, done);
    }

    private static ItemStack LeggingsWithSwiftSneak(int level)
    {
        Assert.True(Items.TryGet(1, out RegistryEntry<ItemDefinition> leggings));
        var enchants = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(1, Identifier.Minecraft("swift_sneak"), new EnchantmentDefinition(MaxLevel: 3))
            .Build();
        Assert.True(enchants.TryGet(
            Identifier.Minecraft("swift_sneak"), out RegistryEntry<EnchantmentDefinition> swiftSneak));
        return new ItemStack(leggings, 1)
            .With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(swiftSneak, level)]));
    }

    private static async Task ApplySneakingSpeedAsync(
        ApplierHarness harness, int protocol, double baseValue, double modifier)
    {
        Registry<AttributeDefinition> registry = JavaGameData.Registries(protocol).Attributes;
        Identifier raw = protocol >= 768
            ? Identifier.Minecraft("sneaking_speed")
            : Identifier.Minecraft("player.sneaking_speed");
        Assert.True(registry.TryGetNetworkId(raw, out int holderId), $"proto {protocol} has no {raw}");

        await harness.ApplyAsync(new ClientboundUpdateAttributesPacket(
            SelfEntityId,
            [new AttributeSnapshot(null, holderId, baseValue,
                [new AttributeModifierEntry(Guid.Empty, "minecraft:enchantment.swift_sneak", modifier, 0)])]));
    }

    private static float PushedSneakingSpeed(int protocol, ItemStack leggings)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(protocol, leggings);
        return Pushed(harness, holder).SneakingSpeedFactor;
    }

    /// <summary>Reads back the exact conditions <c>PushConditions()</c> computed, through the real production <see cref="PhysicsEngineHolder.CapturePlan"/> seam, the same way <c>WaterMovementEfficiencyPushTests</c> does.</summary>
    private static PhysicsConditions Pushed(ApplierHarness harness, PhysicsEngineHolder holder)
    {
        BlockPos here = BlockPos.Containing(harness.State.Self.Position);
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(here));
        Assert.NotNull(capture);
        return capture!.Value.Conditions;
    }

    private static (ApplierHarness Harness, PhysicsEngineHolder Holder) Build(int protocol, ItemStack leggings)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Terrain = true, Inventory = true, Entities = true });
        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;

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

        var holder = new PhysicsEngineHolder(services, JavaGameData.BlockShapes(protocol), NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
            new RegistryBlockDataSource(blocks, isLegacy: protocol < 393),
            WorldFactory.EmptyBiomes());
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
                world.SetBlockStateId(new BlockPos(x, 63, z), stone.DefaultStateId);

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, 64, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        harness.State.Self.Attributes.SeedPlayerDefaults(harness.State.Registries, known: true);
        harness.State.Inventory.SetSlot(InventoryState.PlayerWindowId, LeggingsMenuSlot, leggings);

        holder.EnsureEngine();
        return (harness, holder);
    }
}
