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
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The Depth Strider read must work on the bound wire path, not only on a stack whose enchantment component a test injected by hand.
/// <para><see cref="ItemStack.Enchantments"/> reads the 1.20.5+ structured <c>minecraft:enchantments</c> component. Legacy-format item codecs instead write stack NBT: <c>ReadLegacyStack</c>, <c>ReadShortIdStack</c> (393/401), <c>ReadPresentIdStack</c> and <c>ReadVarIntIdStack</c> (764/765) all stuff the stack's NBT verbatim into <c>DataComponents.LegacyNbt</c>. Tests must therefore create legacy boots through the bound codec rather than inject <c>DataComponents.Enchantments</c> directly.</para>
/// <para>Every case starts from bytes and decodes them through the codec the version catalog binds for <c>minecraft:container_set_slot</c> at that protocol (<see cref="BoundDescriptorCodec"/>), applies the decoded packet through the real <c>InventoryApplier</c>, and only then reads <c>WaterMovementEfficiency</c> back out of the real <see cref="PhysicsEngineHolder.CapturePlan"/> seam. Both legacy NBT shapes are covered: protocol 340 (1.12.2) carries the pre-1.13 <c>ench</c> list with a NUMERIC id, protocol 754 (1.16.5) the 1.13+ <c>Enchantments</c> list with a namespaced STRING id.</para>
/// </summary>
public sealed class WireDecodedWaterMovementEfficiencyTests
{
    private const int NumericIdProtocol = 340; // 1.12.2: legacy stack, "ench" list, numeric enchantment ids.
    private const int StringIdProtocol = 754;  // 1.16.5: present-id stack, "Enchantments" list, string ids.
    private const int ComponentEraProtocol = 766; // 1.20.6: first component-era protocol, still pre-attribute.

    private const int BootsMenuSlot = 8;
    private const int FloorY = 55;
    private const int WaterTop = 75;

    /// <summary>
    /// A 1.12.2 <c>container_set_slot</c> for window 0, slot 8 (FEET), holding one diamond_boots whose NBT is <c>{ench:[{id:8s,lvl:3s}]}</c> - depth strider III in the pre-1.13 numeric-id shape.
    /// <para>Bytes, in order: <c>00</c> container id 0 (sbyte) | <c>0008</c> slot 8 (short) | <c>0139</c> item id 313 (diamond_boots item id, damage 0, composite key 20512768) | <c>01</c> count | <c>0000</c> damage (short) | then the NAMED-root NBT this band uses: <c>0A 0000</c> TAG_Compound with an empty name, <c>09 0004 "ench"</c> TAG_List, <c>0A</c> element type TAG_Compound, <c>00000001</c> one element, <c>02 0002 "id" 0008</c> TAG_Short id = 8, <c>02 0003 "lvl" 0003</c> TAG_Short lvl = 3, <c>00</c> end of element, <c>00</c> end of root.</para>
    /// <para>Numeric id 8 is depth strider in the legacy registry table.</para>
    /// </summary>
    private const string DepthStrider3Frame340 =
        "00000801390100000A0000090004656E63680A00000001020002696400080200036C766C00030000";

    /// <summary>The same 1.12.2 frame with <c>lvl</c> 5, over vanilla's cap of 3.</summary>
    private const string DepthStrider5Frame340 =
        "00000801390100000A0000090004656E63680A00000001020002696400080200036C766C00050000";

    /// <summary>The same 1.12.2 frame carrying <c>ench</c> id 16 (sharpness), not depth strider.</summary>
    private const string Sharpness3Frame340 =
        "00000801390100000A0000090004656E63680A00000001020002696400100200036C766C00030000";

    /// <summary>The same 1.12.2 boots with no NBT at all (a bare TAG_End under the named root).</summary>
    private const string PlainBootsFrame340 = "000008013901000000";

    /// <summary>
    /// A 1.16.5 <c>container_set_slot</c> for window 0, slot 8, holding one diamond_boots whose NBT is <c>{Enchantments:[{id:"minecraft:depth_strider",lvl:2s}]}</c> - the 1.13+ namespaced-string shape.
    /// <para>Bytes: <c>00</c> container id | <c>0008</c> slot | <c>01</c> present | <c>FD04</c> VarInt item id 637 (<c>minecraft:diamond_boots</c> = 637) | <c>01</c> count | then NAMED-root NBT: <c>0A 0000</c>, <c>09 000C "Enchantments"</c> TAG_List of <c>0A</c> compounds, <c>00000001</c> one element, <c>08 0002 "id" 0017 "minecraft:depth_strider"</c> TAG_String, <c>02 0003 "lvl" 0002</c> TAG_Short lvl = 2, <c>00</c>, <c>00</c>.</para>
    /// </summary>
    private const string DepthStrider2Frame754 =
        "00000801FD04010A000009000C456E6368616E746D656E74730A00000001080002696400176D696E656" +
        "3726166743A64657074685F737472696465720200036C766C00020000";

    /// <summary>The pre-1.13 numeric shape, decoded from real bytes: depth strider III on the boots has to reach <c>WaterMovementEfficiency</c> as 3/3. This is the case the component-only read got wrong: it read a component the 1.12.2 codec never writes.</summary>
    [Fact]
    public async Task NumericIdWireLayout_WireDecodedDepthStrider3Boots_ReachesPushConditions()
    {
        Assert.Equal(1.0f, await WireEfficiencyAsync(NumericIdProtocol, DepthStrider3Frame340), 5);
    }

    /// <summary>Vanilla caps the enchantment's contribution at 3, so a stored level 5 is still 3/3.</summary>
    [Fact]
    public async Task NumericIdWireLayout_WireDecodedOverCapLevel_ClampsToOne()
    {
        Assert.Equal(1.0f, await WireEfficiencyAsync(NumericIdProtocol, DepthStrider5Frame340), 5);
    }

    /// <summary>A different enchantment in the same NBT shape must not be mistaken for depth strider: the numeric table has to be consulted per id, not just "the boots carry an ench list".</summary>
    [Fact]
    public async Task NumericIdWireLayout_WireDecodedSharpnessBoots_StayAtZero()
    {
        Assert.Equal(0.0f, await WireEfficiencyAsync(NumericIdProtocol, Sharpness3Frame340));
    }

    /// <summary>Unenchanted wire-decoded boots retain the byte-identical zero value.</summary>
    [Fact]
    public async Task NumericIdWireLayout_WireDecodedPlainBoots_StayAtZero()
    {
        Assert.Equal(0.0f, await WireEfficiencyAsync(NumericIdProtocol, PlainBootsFrame340));
    }

    /// <summary>The 1.13+ string-id shape, decoded from real bytes: depth strider II has to reach <c>WaterMovementEfficiency</c> as 2/3. Inert under the component-only read, same as the numeric case.</summary>
    [Fact]
    public async Task StringIdWireLayout_WireDecodedDepthStrider2Boots_ReachesPushConditions()
    {
        Assert.Equal(2.0f / 3.0f, await WireEfficiencyAsync(StringIdProtocol, DepthStrider2Frame754), 5);
    }

    /// <summary>The decode is pinned separately to ensure the cases above exercise the intended representation: on both legacy protocols the bound codec puts enchantment NBT in <c>DataComponents.LegacyNbt</c> and leaves <see cref="ItemStack.Enchantments"/> EMPTY. That is the whole reason the component-only read could not work, stated as a test.</summary>
    [Theory]
    [InlineData(NumericIdProtocol, DepthStrider3Frame340, "ench")]
    [InlineData(StringIdProtocol, DepthStrider2Frame754, "Enchantments")]
    public void LegacyCodecs_PutEnchantmentsInLegacyNbtOnly_NotInTheModernComponent(
        int protocol, string frame, string nbtKey)
    {
        ItemStack boots = DecodeSlotPacket(protocol, frame).Item;

        Assert.False(boots.IsEmpty);
        Assert.Equal(Identifier.Minecraft("diamond_boots"), boots.Item.Id);
        Assert.Empty(boots.Enchantments);
        Assert.True(boots.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy));
        Assert.NotNull(legacy!.Nbt.GetList(nbtKey));

        // ... and the era-neutral read gets the level out of exactly that residual NBT.
        Assert.True(boots.TryGetEnchantmentLevel(
            Identifier.Minecraft("depth_strider"),
            JavaGameData.LegacyItemBridgeEra,
            JavaGameData.LegacyItemBridgeSource,
            out int level));
        Assert.True(level > 0);
    }

    /// <summary>The end-to-end proof for a legacy protocol: wire bytes in, real applier, real <c>PushConditions()</c>, real engine ticks - and the player has to actually swim measurably faster than the same player with nothing on their feet. The earlier equivalent test passed only because it injected the component; fed real 1.12.2 bytes it would report identical distances.</summary>
    [Fact]
    public async Task NumericIdWireLayout_WireDecodedDepthStrider3Boots_MoveThePlayerFasterThroughWater()
    {
        double bare = await WireForwardDistanceAsync(NumericIdProtocol, PlainBootsFrame340);
        double geared = await WireForwardDistanceAsync(NumericIdProtocol, DepthStrider3Frame340);

        Assert.True(
            geared > bare + 0.3,
            $"wire-decoded depth-strider boots did not speed up water travel: bare={bare:F4}, geared={geared:F4}");
    }

    /// <summary>
    /// A 1.20.6 <c>container_set_slot</c> for window 0, state 0, slot 8 (FEET), holding one diamond_boots carrying the structured <c>minecraft:enchantments</c> component with depth strider III.
    /// <para>Bytes, in order: <c>00</c> container id 0 (sbyte) | <c>00</c> VarInt state id 0 | <c>0008</c> slot 8 (short) - the field order is <c>ItemPacketCodecShared.MakeSetSlot</c>'s, matching the wire layout <c>ClientboundContainerSetSlotPacket</c>. Then the 1.20.5 component stack (<c>ItemStackCodecs.WriteModernStackCore</c>): <c>01</c> VarInt count 1 | <c>E706</c> VarInt item id 871 (<c>minecraft:diamond_boots</c>) | <c>01</c> one added component | <c>00</c> no removed components | <c>09</c> VarInt component type 9 (<c>minecraft:enchantments</c> is component id 9) | then that component's own payload: <c>01</c> one map entry | <c>08</c> VarInt enchantment HOLDER id 8 | <c>03</c> VarInt level 3 | <c>01</c> the trailing <c>show_in_tooltip</c> bool the pre-1.21.5 <c>ItemEnchantments.STREAM_CODEC</c> carries.</para>
    /// <para>Holder id 8 is depth strider on this protocol, and NOTHING on the wire says so: the id is only meaningful against the protocol's enchantment registry, which pairs <c>minecraft:depth_strider</c> with id 8.</para>
    /// </summary>
    private const string DepthStrider3Frame766 = "0000000801E70601000901080301";

    /// <summary>The same 1.20.6 frame carrying holder id 13, which is sharpness on this protocol.</summary>
    private const string Sharpness3Frame766 = "0000000801E706010009010D0301";

    /// <summary>
    /// Protocol 766 resolves a component-era stack's numeric holder id against <c>Registries.Enchantments</c>. The installed per-protocol table must preserve both the level and enchantment identity.
    /// <para>The test uses wire bytes rather than a codec round trip, covering the bound codec, production registry, applier, and <c>PushConditions()</c> without deriving input from the same encoder.</para>
    /// </summary>
    [Fact]
    public async Task ComponentWireLayout766_WireDecodedDepthStrider3Boots_ResolveThroughThePopulatedRegistry()
    {
        ItemStack boots = DecodeSlotPacket(ComponentEraProtocol, DepthStrider3Frame766).Item;

        // The component era puts the enchantment in the MODERN component, not in LegacyNbt.
        Assert.Equal(Identifier.Minecraft("diamond_boots"), boots.Item.Id);
        Assert.False(boots.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? _));

        EnchantmentInstance survivor = Assert.Single(boots.Enchantments);
        Assert.Equal(3, survivor.Level);
        Assert.False(survivor.Enchantment.IsDefault);
        Assert.Equal(Identifier.Minecraft("depth_strider"), survivor.Enchantment.Id);
        Assert.Equal(8, survivor.Enchantment.NetworkId);

        // ... and it reaches PushConditions end to end: level 3, capped at 3, divided by 3.
        Assert.Equal(1.0f, await WireEfficiencyAsync(ComponentEraProtocol, DepthStrider3Frame766), 5);
    }

    /// <summary>The negative control for the case above: on 766 the holder id is the ONLY thing distinguishing one enchantment from another, so a frame differing in a single byte (13, sharpness) must not be read as depth strider. Without this, a registry that mapped every id to the same entry would pass.</summary>
    [Fact]
    public async Task ComponentWireLayout766_WireDecodedSharpnessBoots_StayAtZero()
    {
        ItemStack boots = DecodeSlotPacket(ComponentEraProtocol, Sharpness3Frame766).Item;

        EnchantmentInstance survivor = Assert.Single(boots.Enchantments);
        Assert.Equal(Identifier.Minecraft("sharpness"), survivor.Enchantment.Id);
        Assert.Equal(0.0f, await WireEfficiencyAsync(ComponentEraProtocol, Sharpness3Frame766));
    }

    /// <summary>The end-to-end movement proof for 766, mirroring the legacy-band one above: wire bytes in, real applier, real engine ticks, and the player has to actually swim measurably faster than the same player wearing the sharpness boots (the closest possible control - same item, same component, one byte apart).</summary>
    [Fact]
    public async Task ComponentWireLayout766_WireDecodedDepthStrider3Boots_MoveThePlayerFasterThroughWater()
    {
        double bare = await WireForwardDistanceAsync(ComponentEraProtocol, Sharpness3Frame766);
        double geared = await WireForwardDistanceAsync(ComponentEraProtocol, DepthStrider3Frame766);

        Assert.True(
            geared > bare + 0.3,
            $"wire-decoded 766 depth-strider boots did not speed up water travel: bare={bare:F4}, geared={geared:F4}");
    }

    private static ClientboundContainerSetSlotPacket DecodeSlotPacket(int protocol, string frameHex)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "container_set_slot");
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        return (ClientboundContainerSetSlotPacket)codec.Decode(Convert.FromHexString(frameHex), context);
    }

    private static Task<float> WireEfficiencyAsync(int protocol, string frameHex) =>
        WireEfficiencyAsync(protocol, DecodeSlotPacket(protocol, frameHex));

    private static async Task<float> WireEfficiencyAsync(int protocol, ClientboundContainerSetSlotPacket slot)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync(protocol, slot);
        BlockPos here = BlockPos.Containing(harness.State.Self.Position);
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(here));
        Assert.NotNull(capture);
        return capture!.Value.Conditions.WaterMovementEfficiency;
    }

    private static async Task<double> WireForwardDistanceAsync(int protocol, string frameHex)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) =
            await BuildAsync(protocol, DecodeSlotPacket(protocol, frameHex));
        Vec3d start = harness.State.Self.Position;
        var target = new Vec3d(start.X, start.Y, start.Z + 200.0);
        for (int i = 0; i < 40; i++)
            holder.TickApproach(target, tolerance: 0.1);

        return holder.EngineState!.Value.Position.Z - start.Z;
    }

    /// <summary>A client submerged in still water, whose boots slot was filled by APPLYING the given decoded set-slot packet through the production applier chain rather than by writing the slot directly.</summary>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder)> BuildAsync(
        int protocol, ClientboundContainerSetSlotPacket slot)
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

        var holder = new PhysicsEngineHolder(services, JavaGameData.BlockShapes(protocol), NullLogger.Instance);

        Registry<BlockDefinition> blocks = JavaGameData.Registries(protocol).Blocks;
        var world = new Umpk.Game.World.World(
            WorldFactory.CreateDimension(new CommonWorldSetup("minecraft:overworld", 0), registries: null, protocol),
            new RegistryBlockDataSource(blocks, isLegacy: protocol < 393),
            WorldFactory.EmptyBiomes());

        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        for (int x = -4; x <= 4; x++)
            for (int z = -4; z <= 20; z++)
                for (int y = FloorY; y <= WaterTop; y++)
                    world.SetBlockStateId(new BlockPos(x, y, z), water.DefaultStateId);

        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, 60.0, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;

        // The whole point: the boots reach the slot the way a server puts them there.
        await harness.ApplyAsync(slot);
        Assert.Equal(slot.Item, harness.State.Inventory.PlayerSlots[BootsMenuSlot]);

        holder.EnsureEngine();
        holder.PushConditions();
        return (harness, holder);
    }
}
