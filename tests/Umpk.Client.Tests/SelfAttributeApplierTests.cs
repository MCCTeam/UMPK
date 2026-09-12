using Umpk.Client.Internal;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><c>update_attributes</c> must reach the local player's own attribute map and every tracked entity's, with replacement semantics.</summary>
/// <remarks>
/// <para>Before this, no applier anywhere applied a decoded attribute snapshot to anything: the packet was decoded at the codec layer. <c>AttributeMap.GetOrCreate</c> must receive the result through the production call sites, and <c>PhysicsEngineHolder.ReadMovementSpeed</c> probed the shared <c>EntityStore</c> for the local player - which is not in it on any protocol - so it returned its 0.1 fallback unconditionally on every session ever run.</para>
/// <para>The load-bearing test here is <see cref="RevokedModifier_IsDroppedOnTheNextSnapshot"/>. Each attribute snapshot replaces the base value and modifier set, rather than merging. The natural implementation here is a MERGE, because <c>AttributeInstance.AddOrReplaceModifier</c> sits next to <c>ClearModifiers</c>. A merge keeps a revoked soul-speed modifier forever and nothing on the wire ever contradicts it.</para>
/// </remarks>
public sealed class SelfAttributeApplierTests
{
    /// <summary>1.21.11 - the protocol the live pathfinding course runs on.</summary>
    private const int LiveProtocol = 774;

    /// <summary>1.20.6 - the last protocol whose modifiers are keyed by UUID on the wire.</summary>
    private const int LegacyModifierProtocol = 766;

    /// <summary>1.21 - first protocol carrying movement_efficiency / sneaking_speed.</summary>
    private const int AttributeEraProtocol = 767;

    /// <summary>1.16.5 - the ResourceLocation-string era of the packet's attribute id field.</summary>
    private const int StringKeyProtocol = 754;

    private const int SelfEntityId = 1;
    private const int OtherEntityId = 42;

    private static readonly Identifier MovementSpeed = Identifier.Minecraft("movement_speed");
    private static readonly Identifier SneakingSpeed = Identifier.Minecraft("sneaking_speed");
    private static readonly Identifier MovementEfficiency = Identifier.Minecraft("movement_efficiency");
    private static readonly Identifier Sprinting = Identifier.Minecraft("sprinting");
    private static readonly Identifier SoulSpeedModifier = Identifier.Minecraft("enchantment.soul_speed");

    /// <summary>The sprint modifier's pre-1.21 UUID, verbatim from this rule.</summary>
    private static readonly Guid SprintUuid = Guid.Parse("662A6B8D-DA3E-4C1C-8813-96EA6097278D");

    /// <summary>The soul-speed modifier's pre-1.21 UUID, verbatim from this default.</summary>
    private static readonly Guid SoulSpeedUuid = Guid.Parse("87f46a96-686f-4796-b035-22e16ee9e038");

    // The applier lands

    [Fact]
    public async Task UpdateAttributes_ForSelf_LandsOnSelfState()
    {
        ApplierHarness harness = await JoinedAsync(LiveProtocol);

        await harness.ApplyAsync(SelfSpeed(LiveProtocol, baseValue: 0.15));

        Assert.Equal(0.15, harness.State.Self.Attributes.Value(MovementSpeed, fallback: -1), 10);
        Assert.True(harness.State.Self.Attributes.IsServerStated(MovementSpeed));
    }

    [Fact]
    public async Task UpdateAttributes_ForTrackedEntity_LandsOnEntityAttributeMap()
    {
        ApplierHarness harness = await JoinedAsync(LiveProtocol);
        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            OtherEntityId, Guid.NewGuid(), TypeId: 0, X: 0, Y: 64, Z: 0,
            XRot: 0, YRot: 0, YHeadRot: 0, Data: 0,
            VelocityX: 0, VelocityY: 0, VelocityZ: 0, ModernVelocityRaw: null));

        await harness.ApplyAsync(Attributes(OtherEntityId, Snapshot(LiveProtocol, "movement_speed", 0.23)));

        Assert.True(harness.State.Entities.TryGet(OtherEntityId, out Umpk.Game.Entities.Entity? entity));
        Assert.NotNull(entity);
        Assert.True(entity!.Attributes.TryGet(MovementSpeed, out AttributeInstance instance));
        Assert.Equal(0.23, instance.Value, 10);
    }

    /// <summary>The self arm must raise <c>PhysicsConditionsDirty</c> so <c>PushConditions</c> re-captures; another entity's attributes feed nothing in physics and must NOT.</summary>
    [Fact]
    public async Task OnlyTheSelfArm_InvalidatesThePhysicsConditionSnapshot()
    {
        ApplierHarness harness = await JoinedAsync(LiveProtocol);
        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            OtherEntityId, Guid.NewGuid(), TypeId: 0, X: 0, Y: 64, Z: 0,
            XRot: 0, YRot: 0, YHeadRot: 0, Data: 0,
            VelocityX: 0, VelocityY: 0, VelocityZ: 0, ModernVelocityRaw: null));
        int before = harness.ConditionsPushCount;

        await harness.ApplyAsync(Attributes(OtherEntityId, Snapshot(LiveProtocol, "movement_speed", 0.23)));
        Assert.Equal(before, harness.ConditionsPushCount);

        await harness.ApplyAsync(SelfSpeed(LiveProtocol, baseValue: 0.23));
        Assert.Equal(before + 1, harness.ConditionsPushCount);
    }

    /// <summary>
    /// A snapshot REPLACES an attribute's modifier set.
    /// <para>The concrete failure a merge produces is this item's own critical path: the bot steps onto soul sand and the server sends <c>movement_speed</c> carrying <c>minecraft:enchantment.soul_speed</c>; it steps off and the server sends the same attribute with that modifier absent. A merging applier keeps the boost for the rest of the session, on every block, and no later packet ever says otherwise.</para>
    /// </summary>
    [Fact]
    public async Task RevokedModifier_IsDroppedOnTheNextSnapshot()
    {
        ApplierHarness harness = await JoinedAsync(LiveProtocol);

        await harness.ApplyAsync(Attributes(SelfEntityId, Snapshot(
            LiveProtocol, "movement_speed", 0.1,
            new AttributeModifierEntry(Guid.Empty, "minecraft:enchantment.soul_speed", 0.0615, 0))));
        Assert.Equal(0.1615, harness.State.Self.Attributes.Value(MovementSpeed, fallback: -1), 10);

        // Same attribute, no modifiers: the server has revoked the boost.
        await harness.ApplyAsync(SelfSpeed(LiveProtocol, baseValue: 0.1));

        Assert.Equal(0.1, harness.State.Self.Attributes.Value(MovementSpeed, fallback: -1), 10);
        Assert.True(harness.State.Self.Attributes.Map.TryGet(MovementSpeed, out AttributeInstance instance));
        Assert.False(instance.HasModifier(SoulSpeedModifier));
        Assert.Empty(instance.Modifiers);
    }

    /// <summary>The same replace semantics on the base value: the packet always carries <c>base</c>, so the seeded player default is a pre-first-packet value rather than a floor.</summary>
    [Fact]
    public async Task BaseValue_IsReplacedByEveryFrame_NotFlooredByTheSeed()
    {
        ApplierHarness harness = await JoinedAsync(LiveProtocol);

        await harness.ApplyAsync(SelfSpeed(LiveProtocol, baseValue: 0.05));

        Assert.Equal(0.05, harness.State.Self.Attributes.Value(MovementSpeed, fallback: -1), 10);
    }

    // THE TRIPWIRE: the sprint modifier must resolve to the id the engine excludes

    /// <summary>A 766 frame carries the sprint modifier by UUID. Without the UUID bridge it lands under an unrecognised id, <c>ValueExcluding(minecraft:sprinting)</c> fails to exclude it, and the engine then multiplies by its own 1.3 on top: 0.169 rather than 0.13, a 69% overspeed rather than 30%.</summary>
    [Fact]
    public async Task LegacySprintModifierUuid_IsExcludedFromBaseMovementSpeed()
    {
        ApplierHarness harness = await JoinedAsync(LegacyModifierProtocol);

        await harness.ApplyAsync(Attributes(SelfEntityId, Snapshot(
            LegacyModifierProtocol, "generic.movement_speed", 0.1,
            new AttributeModifierEntry(SprintUuid, null, 0.3, 2))));

        SelfAttributes attributes = harness.State.Self.Attributes;
        Assert.Equal(0.13, attributes.Value(MovementSpeed, fallback: -1), 10);
        Assert.Equal(0.1, attributes.ValueExcluding(MovementSpeed, Sprinting, fallback: -1), 10);
    }

    /// <summary>The soul-speed UUID bridges too, and is NOT excluded: it is the boost this whole item exists to deliver, and the engine does not apply it for itself.</summary>
    [Fact]
    public async Task LegacySoulSpeedModifierUuid_ResolvesAndIsIncluded()
    {
        ApplierHarness harness = await JoinedAsync(LegacyModifierProtocol);

        await harness.ApplyAsync(Attributes(SelfEntityId, Snapshot(
            LegacyModifierProtocol, "generic.movement_speed", 0.1,
            new AttributeModifierEntry(SoulSpeedUuid, null, 0.0615, 0))));

        SelfAttributes attributes = harness.State.Self.Attributes;
        Assert.Equal(0.1615, attributes.ValueExcluding(MovementSpeed, Sprinting, fallback: -1), 10);
        Assert.True(attributes.Map.TryGet(MovementSpeed, out AttributeInstance instance));
        Assert.True(instance.HasModifier(SoulSpeedModifier));
    }

    /// <summary>Two modifiers whose UUIDs the bridge does not know must not collide. An <c>AttributeInstance</c> holds one modifier per id, so folding unknowns onto a single "unknown" key would silently drop all but the last - which for a stack's own <c>attribute_modifiers</c> changes the resolved value.</summary>
    [Fact]
    public async Task TwoUnknownModifierUuids_DoNotCollapseOntoOneId()
    {
        ApplierHarness harness = await JoinedAsync(LegacyModifierProtocol);

        await harness.ApplyAsync(Attributes(SelfEntityId, Snapshot(
            LegacyModifierProtocol, "generic.movement_speed", 0.1,
            new AttributeModifierEntry(Guid.Parse("00000000-0000-4000-8000-00000000000a"), null, 0.01, 0),
            new AttributeModifierEntry(Guid.Parse("00000000-0000-4000-8000-00000000000b"), null, 0.02, 0))));

        Assert.True(harness.State.Self.Attributes.Map.TryGet(MovementSpeed, out AttributeInstance instance));
        Assert.Equal(2, instance.Modifiers.Count);
        Assert.Equal(0.13, instance.Value, 10);
    }

    // Canonicalisation across the 1.21.2 rename

    /// <summary>767 names the attribute <c>minecraft:generic.movement_speed</c> and 768+ name it <c>minecraft:movement_speed</c>, with no semantic change. Both must land on one canonical key, or <c>PhysicsEngineHolder</c>'s <c>Identifier.Minecraft("movement_speed")</c> probe misses on 766 and 767 even with a fully populated registry - which is a silent 0.1 forever, not a crash.</summary>
    [Theory]
    [InlineData(AttributeEraProtocol)]
    [InlineData(LiveProtocol)]
    public async Task AttributeNames_Canonicalise_AcrossThePrefixRename(int protocol)
    {
        ApplierHarness harness = await JoinedAsync(protocol);

        await harness.ApplyAsync(SelfSpeed(protocol, baseValue: 0.42));

        Assert.Equal(0.42, harness.State.Self.Attributes.Value(MovementSpeed, fallback: -1), 10);
    }

    /// <summary>735-765 name the attribute with a ResourceLocation STRING rather than a holder id, so the resolution path is different code. It must reach the same canonical key.</summary>
    [Fact]
    public async Task StringKeyedWireLayout_ResolvesThroughTheRegistryAndCanonicalises()
    {
        ApplierHarness harness = await JoinedAsync(StringKeyProtocol);

        await harness.ApplyAsync(Attributes(SelfEntityId, new AttributeSnapshot(
            "minecraft:generic.movement_speed", 0, 0.37, [])));

        Assert.Equal(0.37, harness.State.Self.Attributes.Value(MovementSpeed, fallback: -1), 10);
    }

    /// <summary>A player who joins and does nothing receives NO <c>update_attributes</c> at all: the server sends the owning player only DIRTY attributes, while the full syncable set goes to other trackers. So the map must carry the player's default attribute seeds, and an empty map must never read as zero speed.</summary>
    [Fact]
    public async Task QuietJoin_SelfAttributes_CarryVanillaPlayerSeeds()
    {
        ApplierHarness harness = await JoinedAsync(LiveProtocol);

        SelfAttributes attributes = harness.State.Self.Attributes;
        Assert.Equal(0.1, attributes.Value(MovementSpeed, fallback: -1), 10);
        Assert.Equal(0.3, attributes.Value(SneakingSpeed, fallback: -1), 10);
        Assert.Equal(0.0, attributes.Value(MovementEfficiency, fallback: -1), 10);
        Assert.True(attributes.Known);
        Assert.Empty(attributes.ServerStated);
    }

    /// <summary>THE BYTE-IDENTITY PIN. <c>movement_speed</c>'s REGISTRY default is 0.7 - the mob value - and seeding a player from it would make the bot walk seven times too fast on every session before the first packet. The player's base is 0.1, which is exactly the fallback <c>ReadMovementSpeed</c> hardcoded before this applier existed. That is what makes the whole of commit 1 behaviour-neutral for a session the server says nothing to.</summary>
    [Theory]
    [InlineData(StringKeyProtocol)]
    [InlineData(LegacyModifierProtocol)]
    [InlineData(AttributeEraProtocol)]
    [InlineData(LiveProtocol)]
    public async Task QuietSession_MovementSpeed_IsExactlyTheOldHardcodedFallback(int protocol)
    {
        ApplierHarness harness = await JoinedAsync(protocol);

        Assert.Equal(
            0.1,
            harness.State.Self.Attributes.ValueExcluding(MovementSpeed, Sprinting, fallback: 0.1),
            10);
    }

    /// <summary>The polarity test. A session with no entity tracking can never observe an attribute, and must say so rather than present its seeds as observations. Values are still the seeds, because that IS vanilla's behaviour and physics has to run.</summary>
    [Fact]
    public async Task SelfAttributes_AreUnknown_WhenEntityTrackingIsOff()
    {
        Assert.True(JavaVersions.TryGetByProtocol(LiveProtocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Entities = false });
        harness.State.Registries = JavaGameData.Registries(LiveProtocol);
        harness.State.Self.EntityId = SelfEntityId;
        await JoinAsync(harness);

        SelfAttributes attributes = harness.State.Self.Attributes;
        Assert.False(attributes.Known);
        Assert.Equal(0.1, attributes.Value(MovementSpeed, fallback: -1), 10);
    }

    /// <summary>47-578 carry no <c>minecraft:attribute</c> registry, so nothing on the wire can be resolved to a definition. That is UNKNOWN, not NONE, and the seeds are absent because there is no registry to seed from - the consumer's own fallback is the right answer there.</summary>
    [Fact]
    public async Task ProtocolWithoutAnAttributeRegistry_IsUnknown()
    {
        ApplierHarness harness = await JoinedAsync(47);

        SelfAttributes attributes = harness.State.Self.Attributes;
        Assert.False(attributes.Known);
        Assert.Equal(0, attributes.Map.Count);
        Assert.Equal(0.1, attributes.Value(MovementSpeed, fallback: 0.1), 10);
    }

    // Respawn

    /// <summary>The reset is UNCONDITIONAL, unlike the sibling effect rule, which gates on <c>DataToKeep == 0</c>. Respawn rebuilds the local player's attribute map on BOTH arms; the keep flag carries only sneaking and sprinting state across. A naive copy of the effect rule would pass for <c>DataToKeep == 0</c> and leave a dead player's soul-speed modifier on the map forever for the other two.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Respawn_ClearsSelfAttributes_RegardlessOfDataToKeep(byte dataToKeep)
    {
        ApplierHarness harness = await JoinedAsync(LiveProtocol);
        await harness.ApplyAsync(Attributes(SelfEntityId, Snapshot(
            LiveProtocol, "movement_speed", 0.1,
            new AttributeModifierEntry(Guid.Empty, "minecraft:enchantment.soul_speed", 0.0615, 0))));
        Assert.Equal(0.1615, harness.State.Self.Attributes.Value(MovementSpeed, fallback: -1), 10);

        await harness.ApplyAsync(new ClientboundRespawnPacket(Spawn(), dataToKeep, Legacy: null));

        SelfAttributes attributes = harness.State.Self.Attributes;
        Assert.Equal(0.1, attributes.Value(MovementSpeed, fallback: -1), 10);
        Assert.Empty(attributes.ServerStated);
    }

    // Helpers

    private static CommonPlayerSpawnInfo Spawn() => new(
        DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
        IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);

    private static async Task JoinAsync(ApplierHarness harness) =>
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: SelfEntityId, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: Spawn(), OnlineMode: false, EnforcesSecureChat: false,
            Legacy: null));

    private static async Task<ApplierHarness> JoinedAsync(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Terrain = true });
        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;
        await JoinAsync(harness);
        return harness;
    }

    private static ClientboundUpdateAttributesPacket Attributes(int entityId, params AttributeSnapshot[] snapshots)
        => new(entityId, snapshots);

    private static ClientboundUpdateAttributesPacket SelfSpeed(int protocol, double baseValue)
        => Attributes(SelfEntityId, Snapshot(
            protocol, protocol >= 768 ? "movement_speed" : "generic.movement_speed", baseValue));

    /// <summary>Builds one snapshot the way the protocol's own codec would: from 766 the attribute is named by holder VarInt (so the id is looked up in this session's registry), below that by string. The holder id is READ FROM THE REGISTRY here rather than hardcoded, because these tests are about the applier. <c>Umpk.Data.Java.Tests.AttributeRegistryTests</c> verifies the registry IDs.</summary>
    private static AttributeSnapshot Snapshot(
        int protocol, string path, double baseValue, params AttributeModifierEntry[] modifiers)
    {
        Identifier id = Identifier.Minecraft(path);
        if (protocol < 766)
            return new AttributeSnapshot(id.ToString(), 0, baseValue, modifiers);

        Registry<AttributeDefinition> registry = JavaGameData.Registries(protocol).Attributes;
        Assert.True(registry.TryGetNetworkId(id, out int holder), $"proto {protocol} has no attribute {id}");
        return new AttributeSnapshot(null, holder, baseValue, modifiers);
    }
}
