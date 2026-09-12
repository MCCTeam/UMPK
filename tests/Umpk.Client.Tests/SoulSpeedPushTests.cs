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

/// <summary><see cref="PhysicsEngineHolder.PushConditions"/> must resolve soul speed's BYPASS on both eras and must never resolve its BOOST on either.</summary>
/// <remarks>
/// <para>The split is not legacy-versus-modern, it is boost-versus-bypass. The boost has been server-side on every protocol from 1.16, so it arrives as an <c>update_attributes</c> modifier and is delivered by the attribute applier, with no soul-speed-specific code at all. The BYPASS is the half the client owns: computed positionally from the boots on 1.16-1.20.6, read off <c>minecraft:movement_efficiency</c> from 1.21.</para>
/// <para>The era gate is the DATASET: 766's <c>minecraft:attribute</c> registry has no <c>movement_efficiency</c> and 767's does.</para>
/// </remarks>
public sealed class SoulSpeedPushTests
{
    /// <summary>1.20.6, the last protocol whose client computes the bypass itself.</summary>
    private const int EnchantmentEraProtocol = 766;

    /// <summary>1.21/1.21.1, the first protocol carrying <c>movement_efficiency</c>.</summary>
    private const int AttributeEraProtocol = 767;

    /// <summary>1.21.11, the protocol the live course runs on.</summary>
    private const int LiveProtocol = 774;

    private const int SelfEntityId = 1;

    /// <summary>The player-window (menu-space) FEET slot; <c>InventoryPipelineTests</c> pins it.</summary>
    private const int BootsMenuSlot = 8;

    private static readonly Identifier MovementEfficiency = Identifier.Minecraft("movement_efficiency");
    private static readonly Identifier MovementSpeed = Identifier.Minecraft("movement_speed");
    private static readonly Identifier Sprinting = Identifier.Minecraft("sprinting");

    private static readonly Registry<ItemDefinition> Items = new RegistryBuilder<ItemDefinition>(RegistryIds.Item)
        .Add(1, Identifier.Minecraft("netherite_boots"), new ItemDefinition(1))
        .Build();

    /// <summary>THE BYTE-IDENTITY PIN. With no boots and no attribute, both fields are zero on every era, the engine's lerp is the identity, and nothing about block speed factors moves.</summary>
    [Theory]
    [InlineData(EnchantmentEraProtocol)]
    [InlineData(AttributeEraProtocol)]
    [InlineData(LiveProtocol)]
    public void NoGear_BothFieldsAreZero(int protocol)
    {
        PhysicsConditions pushed = PushedWith(protocol, ItemStack.Empty);
        Assert.Equal(0f, pushed.MovementEfficiency);
        Assert.Equal(0, pushed.SoulSpeedLevel);
    }

    /// <summary>The enchantment era pushes a LEVEL, and pushes no efficiency at all: the positional half belongs to the engine, which reads the block below the feet every tick.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EnchantmentWireLayout_PushesTheBootsLevel_AndNoEfficiency(int level)
    {
        PhysicsConditions pushed = PushedWith(EnchantmentEraProtocol, BootsWithSoulSpeed(level));
        Assert.Equal(level, pushed.SoulSpeedLevel);
        Assert.Equal(0f, pushed.MovementEfficiency);
    }

    /// <summary>The attribute era pushes the EFFICIENCY off the wire and a level of zero, even with the very same boots on. Vanilla's 1.21 client has no enchantment arm here and neither does this: the server owns the number, and computing it locally would be more accurate than vanilla, which is a divergence rather than a fix.</summary>
    [Theory]
    [InlineData(AttributeEraProtocol)]
    [InlineData(LiveProtocol)]
    public async Task AttributeWireLayout_ReadsTheWireValue_AndIgnoresTheBoots(int protocol)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(protocol, BootsWithSoulSpeed(3));

        // Before the packet: nothing, which is vanilla's own behaviour at a lane entrance.
        holder.PushConditions();
        Assert.Equal(0f, holder.EngineConditions!.Value.MovementEfficiency);
        Assert.Equal(0, holder.EngineConditions!.Value.SoulSpeedLevel);

        await ApplyEfficiencyAsync(harness, protocol, 1.0);
        holder.PushConditions();

        Assert.Equal(1.0f, holder.EngineConditions!.Value.MovementEfficiency);
        Assert.Equal(0, holder.EngineConditions!.Value.SoulSpeedLevel);
    }

    /// <summary>A FRACTIONAL efficiency survives the read intact, so the engine's lerp has something to lerp with. A read that collapsed the attribute to a boolean would pass every full-bypass test and fail this one.</summary>
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public async Task AttributeWireLayout_PreservesAFractionalEfficiency(double efficiency)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(LiveProtocol, ItemStack.Empty);
        await ApplyEfficiencyAsync(harness, LiveProtocol, efficiency);
        holder.PushConditions();

        Assert.Equal((float)efficiency, holder.EngineConditions!.Value.MovementEfficiency, 5);
    }

    /// <summary>THE BOOST ARRIVES AS A WIRE MODIFIER, NOT AS CLIENT ARITHMETIC. Nothing anywhere synthesises <c>0.03 * (1 + 0.35 * L)</c>; the movement-speed the engine sees is whatever <c>update_attributes</c> resolved, and with no such packet it is exactly the seeded 0.1 even with level-3 boots on. This is the guard against someone helpfully adding the boost a second time.</summary>
    [Fact]
    public void SoulSpeedBoots_AloneDoNotChangeMovementSpeed()
    {
        PhysicsConditions bare = PushedWith(EnchantmentEraProtocol, ItemStack.Empty);
        PhysicsConditions booted = PushedWith(EnchantmentEraProtocol, BootsWithSoulSpeed(3));

        Assert.Equal(0.1f, bare.BaseMovementSpeedAttribute);
        Assert.Equal(bare.BaseMovementSpeedAttribute, booted.BaseMovementSpeedAttribute);
    }

    /// <summary>When the server sends the value, the ordinary attribute applier handles it without soul-speed-specific code. Level 3 is <c>0.0405 + 0.0105 * 2 = 0.0615</c>, byte-identical to 1.20.6's <c>0.03F * (1.0F + 3 * 0.35F)</c>.</summary>
    [Fact]
    public async Task SoulSpeedBoost_LandsThroughTheOrdinaryAttributeApplier()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(LiveProtocol, ItemStack.Empty);

        Registry<AttributeDefinition> registry = JavaGameData.Registries(LiveProtocol).Attributes;
        Assert.True(registry.TryGetNetworkId(Identifier.Minecraft("movement_speed"), out int holderId));
        await harness.ApplyAsync(new ClientboundUpdateAttributesPacket(
            SelfEntityId,
            [new AttributeSnapshot(null, holderId, 0.1,
                [new AttributeModifierEntry(Guid.Empty, "minecraft:enchantment.soul_speed", 0.0615, 0)])]));
        holder.PushConditions();

        Assert.Equal(0.1615f, holder.EngineConditions!.Value.BaseMovementSpeedAttribute, 5);
        Assert.Equal(
            0.1615,
            harness.State.Self.Attributes.ValueExcluding(MovementSpeed, Sprinting, fallback: -1),
            10);
    }

    private static ItemStack BootsWithSoulSpeed(int level)
    {
        Assert.True(Items.TryGet(1, out RegistryEntry<ItemDefinition> boots));
        Registry<EnchantmentDefinition> enchants = new RegistryBuilder<EnchantmentDefinition>(RegistryIds.Enchantment)
            .Add(1, Identifier.Minecraft("soul_speed"), new EnchantmentDefinition(MaxLevel: 3))
            .Build();
        Assert.True(enchants.TryGet(Identifier.Minecraft("soul_speed"), out RegistryEntry<EnchantmentDefinition> soulSpeed));
        return new ItemStack(boots, 1)
            .With(DataComponents.Enchantments, new EnchantmentsComponent([new EnchantmentInstance(soulSpeed, level)]));
    }

    private static async Task ApplyEfficiencyAsync(ApplierHarness harness, int protocol, double efficiency)
    {
        Registry<AttributeDefinition> registry = JavaGameData.Registries(protocol).Attributes;
        Identifier raw = protocol >= 768
            ? Identifier.Minecraft("movement_efficiency")
            : Identifier.Minecraft("generic.movement_efficiency");
        Assert.True(registry.TryGetNetworkId(raw, out int holderId), $"proto {protocol} has no {raw}");

        await harness.ApplyAsync(new ClientboundUpdateAttributesPacket(
            SelfEntityId, [new AttributeSnapshot(null, holderId, efficiency, [])]));
    }

    private static PhysicsConditions PushedWith(int protocol, ItemStack boots)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = Build(protocol, boots);
        BlockPos here = BlockPos.Containing(harness.State.Self.Position);
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(here));
        Assert.NotNull(capture);
        return capture!.Value.Conditions;
    }

    private static (ApplierHarness Harness, PhysicsEngineHolder Holder) Build(int protocol, ItemStack boots)
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
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("soul_sand"), out BlockDefinition? soulSand));
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 4; z++)
                world.SetBlockStateId(new BlockPos(x, 63, z), soulSand.DefaultStateId);

        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, 64, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;
        harness.State.Self.Attributes.SeedPlayerDefaults(harness.State.Registries, known: true);
        harness.State.Inventory.SetSlot(InventoryState.PlayerWindowId, BootsMenuSlot, boots);
        holder.EnsureEngine();
        return (harness, holder);
    }
}
