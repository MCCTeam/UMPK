using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
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
/// The local player's own status effects have to reach <see cref="PhysicsConditions"/>, and the effect the conditions read has to be the one the server actually applied - on every era.
/// <para><c>PhysicsEngineHolder.PushConditions</c> derived four condition flags from a private <c>EffectIds</c> table of bare literals (<c>JumpBoost = 8</c>, <c>Levitation = 25</c>, <c>SlowFalling = 28</c>, <c>DolphinsGrace = 30</c>). Those are the PRE-1.20.2 one-based registry ids. <c>minecraft:mob_effect</c> went zero-based at protocol 764, which <see cref="WireDecodedMobEffectTests"/> already pins at the applier layer, so on every protocol from 764 up each literal named the effect one slot ABOVE the intended one: 8 is <c>minecraft:nausea</c>, 25 is <c>minecraft:luck</c>, 28 is <c>minecraft:conduit_power</c> and 30 is <c>minecraft:bad_omen</c> from protocol 764 onward. Jump power was therefore computed from whether the player was NAUSEOUS.</para>
/// <para>The effect must be read from <c>SelfState.ActiveEffects</c>, not <c>EntityStore.TryGet(Self.EntityId)</c>, and self is not a member of the shared entity store. The local player's effects land on <c>SelfState.ActiveEffects</c> (<c>EntityApplier.ApplyEffectAsync</c>'s <c>isSelf</c> arm), so the store lookup missed on EVERY protocol and all four flags were permanently false. That is the same wall <c>SelfAirSupplyTests</c> documents for metadata and <c>WireDecodedWaterMovementEfficiencyTests</c> documents for attributes.</para>
/// <para>Every case starts from bytes and decodes them through the codec bound by the version catalog for <c>minecraft:update_mob_effect</c> at that protocol, applies the decoded packet through the real applier chain, and only then reads the conditions back out of the real <see cref="PhysicsEngineHolder.CapturePlan"/> seam. The wire ids are stated as literals in the theory rows rather than looked up from the registry under test, so a row cannot pass by agreeing with the dataset value it is verifying.</para>
/// </summary>
public sealed class SelfEffectPhysicsConditionsTests
{
    /// <summary>1.8 uses the curated one-based legacy registry table.</summary>
    private const int LegacyProtocol = 47;

    /// <summary>1.20.1, the last one-based mob-effect era.</summary>
    private const int OneBasedProtocol = 763;

    /// <summary>1.21.8, well inside the zero-based era that starts at 764.</summary>
    private const int ModernProtocol = 772;

    private const int SelfEntityId = 1;
    private const int FloorY = 60;

    // The headline: the effect the server applied is the effect the conditions report

    /// <summary>
    /// Each effect <c>PushConditions</c> consumes, applied to the local player by its era-correct wire id, has to show up in the pushed conditions.
    /// <para>Frame shapes, all addressed to entity 1 (self), amplifier 1, duration 600 (<c>D804</c>), flags <c>06</c> = show particles + show icon:</para>
    /// <list type="bullet">
    /// <item>772 binds <c>EntityEffectCodecs.UpdateMobEffectV1_20_5</c> (from <c>V1_20_5</c>): VarInt
    /// entity | VarInt holder | VarInt amplifier | VarInt duration | byte flags.</item>
    /// <item>763 binds <c>UpdateMobEffectV1_19</c>: VarInt entity | VarInt effect | BYTE amplifier |
    /// VarInt duration | byte flags | the <c>writeNullable</c> false for <c>FactorData</c>.</item>
    /// <item>47 binds <c>UpdateMobEffectV1_8</c>: VarInt entity | BYTE effect | BYTE amplifier | VarInt
    /// duration | byte flags.</item>
    /// </list>
    /// <para>1.8 has no levitation, slow falling or dolphin's grace at all - they arrive in 1.9 (levitation) and 1.13 (the other two) - so protocol 47 carries the jump-boost row only, which is exactly what its 23-entry curated table contains.</para>
    /// </summary>
    [Theory]
    // Zero-based era (764+): jump_boost 7, levitation 24, slow_falling 27, dolphins_grace 29.
    [InlineData(ModernProtocol, "jump_boost", 7, "010701D80406")]
    [InlineData(ModernProtocol, "levitation", 24, "011801D80406")]
    [InlineData(ModernProtocol, "slow_falling", 27, "011B01D80406")]
    [InlineData(ModernProtocol, "dolphins_grace", 29, "011D01D80406")]
    // One-based era: jump_boost 8, levitation 25, slow_falling 28, dolphins_grace 30.
    [InlineData(OneBasedProtocol, "jump_boost", 8, "010801D8040600")]
    [InlineData(OneBasedProtocol, "levitation", 25, "011901D8040600")]
    [InlineData(OneBasedProtocol, "slow_falling", 28, "011C01D8040600")]
    [InlineData(OneBasedProtocol, "dolphins_grace", 30, "011E01D8040600")]
    // 1.8's curated table, same one-based jump-boost id through a different codec.
    [InlineData(LegacyProtocol, "jump_boost", 8, "010801D80406")]
    public async Task ASelfEffect_ReachesTheMatchingPhysicsCondition(
        int protocol, string effect, int expectedWireId, string frameHex)
    {
        PhysicsConditions conditions = await ConditionsAfterEffectAsync(protocol, expectedWireId, frameHex);

        Assert.True(
            Flag(conditions, effect),
            $"protocol {protocol}: minecraft:{effect} (wire id {expectedWireId}) did not reach PhysicsConditions");
    }

    /// <summary>The amplifier travels with the two effects whose STRENGTH the engine consumes: jump boost scales the jump impulse and levitation replaces gravity outright, so an amplifier dropped on the way in would move the player at the wrong speed rather than merely miss a flag.</summary>
    [Theory]
    [InlineData(ModernProtocol, "jump_boost", 7, "010701D80406")]
    [InlineData(ModernProtocol, "levitation", 24, "011801D80406")]
    [InlineData(OneBasedProtocol, "jump_boost", 8, "010801D8040600")]
    [InlineData(OneBasedProtocol, "levitation", 25, "011901D8040600")]
    public async Task TheAmplifierOfASelfEffect_ReachesThePhysicsConditions(
        int protocol, string effect, int expectedWireId, string frameHex)
    {
        PhysicsConditions conditions = await ConditionsAfterEffectAsync(protocol, expectedWireId, frameHex);

        int amplifier = effect == "jump_boost" ? conditions.JumpBoostAmplifier : conditions.LevitationAmplifier;
        Assert.Equal(1, amplifier);
    }

    // The local player's active effect must drive the physics condition.

    /// <summary>
    /// The neighbour that the one-based literals actually matched on a zero-based protocol. Each frame here carries, on protocol 772, the exact numeric id the removed <c>EffectIds</c> table held - and that id names a DIFFERENT effect in the zero-based era. None of them may set the condition the literal was meant to detect.
    /// <para>This is the strongest available statement of the bug: under the literals every row passed the positive test above for the wrong reason, and every row here went green when it must be red.</para>
    /// </summary>
    [Theory]
    [InlineData("jump_boost", "nausea", 8, "010801D80406")]
    [InlineData("levitation", "luck", 25, "011901D80406")]
    [InlineData("slow_falling", "conduit_power", 28, "011C01D80406")]
    [InlineData("dolphins_grace", "bad_omen", 30, "011E01D80406")]
    public async Task OnAZeroBasedProtocol_TheOldLiteralsNeighbour_DoesNotSetTheCondition(
        string intended, string actualEffect, int legacyLiteralId, string frameHex)
    {
        // The literal resolves to that other effect in the bound registry.
        Assert.True(
            JavaGameData.Registries(ModernProtocol).MobEffects.TryGetKey(legacyLiteralId, out Identifier resolved));
        Assert.Equal(Identifier.Minecraft(actualEffect), resolved);

        PhysicsConditions conditions = await ConditionsAfterEffectAsync(ModernProtocol, legacyLiteralId, frameHex);

        Assert.False(
            Flag(conditions, intended),
            $"minecraft:{actualEffect} (id {legacyLiteralId}) was read as minecraft:{intended}");
    }

    /// <summary>The control for the whole file: with no effect applied at all, every flag is false. Without it a holder that reported jump boost unconditionally would pass every positive row above.</summary>
    [Theory]
    [InlineData(LegacyProtocol)]
    [InlineData(OneBasedProtocol)]
    [InlineData(ModernProtocol)]
    public async Task WithNoEffectApplied_EveryEffectConditionIsFalse(int protocol)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync(protocol);
        _ = harness;
        holder.PushConditions();

        PhysicsConditions conditions = Conditions(holder);

        Assert.False(conditions.HasJumpBoost);
        Assert.False(conditions.HasLevitation);
        Assert.False(conditions.HasSlowFalling);
        Assert.False(conditions.HasDolphinsGrace);
        Assert.Equal(0, conditions.JumpBoostAmplifier);
        Assert.Equal(0, conditions.LevitationAmplifier);
    }

    /// <summary>The applier marks the conditions dirty for a SELF effect, which is what makes the live client push them without anything asking. Pinned separately because every other case in this file calls <c>PushConditions()</c> by hand, so all of them would still pass against a client that never re-pushed at all.</summary>
    [Fact]
    public async Task ASelfEffectFrame_MarksThePhysicsConditionsDirty()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync(ModernProtocol);
        _ = holder;
        int before = harness.ConditionsPushCount;

        await harness.ApplyAsync(Decode(ModernProtocol, "010701D80406"));

        Assert.True(harness.ConditionsPushCount > before, "a self effect did not mark the conditions dirty");
    }

    /// <summary>
    /// The 13 protocols between 1.8 and 1.14 ship no <c>minecraft:mob_effect</c> registry in the dataset. Only protocol 47 has a curated stand-in.
    /// <para>Name-based resolution therefore cannot answer on that band, and the holder reports no effect there. This test makes the missing-registry boundary explicit so adding those tables later changes behavior deliberately.</para>
    /// </summary>
    [Theory]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(110)]
    [InlineData(210)]
    [InlineData(315)]
    [InlineData(316)]
    [InlineData(335)]
    [InlineData(338)]
    [InlineData(340)]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    public void TheDisclosedGap_TheseProtocolsCarryNoMobEffectRegistry(int protocol)
    {
        Assert.False(
            JavaGameData.Registries(protocol).MobEffects.TryGetNetworkId(
                Identifier.Minecraft("jump_boost"), out int _),
            $"protocol {protocol} now carries minecraft:mob_effect; the disclosed gap is stale");
    }

    /// <summary>Every covered protocol carries the registry table required by this path.</summary>
    [Theory]
    [InlineData(LegacyProtocol, 8)]
    [InlineData(477, 8)]
    [InlineData(OneBasedProtocol, 8)]
    [InlineData(764, 7)]
    [InlineData(ModernProtocol, 7)]
    [InlineData(774, 7)]
    public void TheServedProtocols_ResolveJumpBoostToItsWireLayoutCorrectWireId(int protocol, int expected)
    {
        Assert.True(JavaGameData.Registries(protocol).MobEffects.TryGetNetworkId(
            Identifier.Minecraft("jump_boost"), out int id));
        Assert.Equal(expected, id);
    }

    // Support

    private static bool Flag(in PhysicsConditions conditions, string effect) => effect switch
    {
        "jump_boost" => conditions.HasJumpBoost,
        "levitation" => conditions.HasLevitation,
        "slow_falling" => conditions.HasSlowFalling,
        "dolphins_grace" => conditions.HasDolphinsGrace,
        _ => throw new Xunit.Sdk.XunitException($"minecraft:{effect} is not a condition PushConditions derives"),
    };

    private static async Task<PhysicsConditions> ConditionsAfterEffectAsync(
        int protocol, int expectedWireId, string frameHex)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder) = await BuildAsync(protocol);

        ClientboundUpdateMobEffectPacket decoded = Decode(protocol, frameHex);

        // The decode itself, pinned so the assertions above cannot pass for the wrong reason.
        Assert.Equal(SelfEntityId, decoded.EntityId);
        Assert.Equal(expectedWireId, decoded.EffectId);
        Assert.Equal(1, decoded.Amplifier);
        Assert.Equal(600, decoded.Duration);

        await harness.ApplyAsync(decoded);

        // The self arm of the applier put it on SelfState, keyed by the raw wire id.
        Assert.True(harness.State.Self.ActiveEffects.ContainsKey(expectedWireId));

        holder.PushConditions();
        return Conditions(holder);
    }

    private static ClientboundUpdateMobEffectPacket Decode(int protocol, string frameHex)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "update_mob_effect");
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        return (ClientboundUpdateMobEffectPacket)codec.Decode(Convert.FromHexString(frameHex), context);
    }

    /// <summary>Reads the conditions back through the real <see cref="PhysicsEngineHolder.CapturePlan"/> seam, which is the same snapshot the planner and the executor are handed.</summary>
    private static PhysicsConditions Conditions(PhysicsEngineHolder holder)
    {
        PlanCapture? capture = holder.CapturePlan(new GoalBlock(new BlockPos(0, FloorY + 1, 0)));
        Assert.NotNull(capture);
        return capture!.Value.Conditions;
    }

    /// <summary>A client standing on a small stone floor, with the production registries installed.</summary>
    private static async Task<(ApplierHarness Harness, PhysicsEngineHolder Holder)> BuildAsync(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(
            version!, new ClientFeatures { Physics = true, Terrain = true, Entities = true });
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
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stone!.DefaultStateId);

        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;
        harness.State.InstallWorld(world);
        harness.State.Self.Position = new Vec3d(0.5, FloorY + 1.0, 0.5);
        harness.State.Self.Velocity = Vec3d.Zero;

        holder.EnsureEngine();
        await Task.CompletedTask;
        return (harness, holder);
    }
}
