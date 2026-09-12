using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Tier 2 of entity metadata: the semantic keys projected onto the typed <c>Entity</c> properties.</summary>
/// <remarks>
/// <para>The raw values were already stored index-keyed on every protocol, but nothing bound a key source and nothing wrote the typed properties, so a live run found <c>Entity.CustomName</c> empty on all 18 versions tested while the server held <c>{"text":"ISOMOB"}</c> for the same mob. Both halves failed independently: entities were constructed with a null key source (every tier-2 read short-circuits to false) AND no projection existed.</para>
/// <para>The custom name changes wire TYPE at the flattening: a plain STRING on 47-340 and an <c>Optional&lt;Component&gt;</c> from 393. Both are asserted here, from the era's real serializer table through the era's real codec.</para>
/// </remarks>
public sealed class EntityCustomNameTierTwoTests
{
    private const int MobId = 0x2BCD;
    private const string Name = "ISOMOB";

    /// <summary>The pre-flattening band, where the custom name is a bare string.</summary>
    public static TheoryData<int> LegacyNameProtocols => [47, 107, 210, 340];

    /// <summary>The flattened band, where it is an optional component.</summary>
    public static TheoryData<int> ComponentNameProtocols =>
        [393, 404, 477, 578, 735, 754, 758, 760, 763, 764, 765, 767, 768, 769, 770, 774, 775, 776];

    [Theory]
    [MemberData(nameof(LegacyNameProtocols))]
    public async Task ALegacyStringCustomName_ReachesEntityCustomName(int protocol)
    {
        Entity entity = await ApplyAsync(protocol, [new EntityDataEntry(2, MetadataValue.String(Name))]);
        Assert.Equal(Name, entity.CustomName!.ToPlainText());
    }

    [Theory]
    [MemberData(nameof(ComponentNameProtocols))]
    public async Task AnOptionalComponentCustomName_ReachesEntityCustomName(int protocol)
    {
        Entity entity = await ApplyAsync(
            protocol,
            [new EntityDataEntry(2, MetadataValue.OptionalComponent(Component.Text(Name)))]);
        Assert.Equal(Name, entity.CustomName!.ToPlainText());
    }

    /// <summary>Vanilla's pre-1.13 form writes an EMPTY string for "this entity has no custom name", which is not a name of "". Clearing has to survive the projection or every unnamed mob reads as named-with-nothing.</summary>
    [Theory]
    [MemberData(nameof(LegacyNameProtocols))]
    public async Task AnEmptyLegacyString_ClearsTheCustomName(int protocol)
    {
        (ApplierHarness harness, Entity entity) =
            await SpawnAndApplyAsync(protocol, [new EntityDataEntry(2, MetadataValue.String(Name))]);
        Assert.NotNull(entity.CustomName);

        await ApplyToAsync(protocol, harness, [new EntityDataEntry(2, MetadataValue.String(string.Empty))]);
        Assert.Null(entity.CustomName);
    }

    /// <summary>An absent optional clears it on the flattened band, the same way.</summary>
    [Theory]
    [InlineData(404)]
    [InlineData(754)]
    [InlineData(776)]
    public async Task AnAbsentOptionalComponent_ClearsTheCustomName(int protocol)
    {
        (ApplierHarness harness, Entity entity) = await SpawnAndApplyAsync(
            protocol, [new EntityDataEntry(2, MetadataValue.OptionalComponent(Component.Text(Name)))]);
        Assert.NotNull(entity.CustomName);

        await ApplyToAsync(protocol, harness, [new EntityDataEntry(2, MetadataValue.OptionalComponent(null))]);
        Assert.Null(entity.CustomName);
    }

    /// <summary>The siblings the same key source unlocks. Name visibility and health are read through the tier-2 accessor; the pose has its own typed property, and it does not exist below 1.14.</summary>
    [Theory]
    [InlineData(107, 6, -1)]
    [InlineData(340, 7, -1)]
    [InlineData(404, 7, -1)]
    [InlineData(754, 8, 6)]
    [InlineData(758, 9, 6)]
    [InlineData(770, 9, 6)]
    [InlineData(776, 9, 6)]
    public async Task NameVisibility_Health_AndPose_ReachTheirTypedSurfaces(int protocol, int healthIndex, int poseIndex)
    {
        var entries = new List<EntityDataEntry>
        {
            new(2, LegacyNameValue(protocol)),
            new(3, MetadataValue.Boolean(true)),
            new(healthIndex, MetadataValue.Float(17.5f)),
        };

        if (poseIndex >= 0)
            entries.Add(new EntityDataEntry(poseIndex, MetadataValue.Pose(EntityPose.FallFlying)));

        Entity entity = await ApplyAsync(protocol, entries);

        Assert.Equal(Name, entity.CustomName!.ToPlainText());
        Assert.True(entity.Metadata.TryGet(EntityMetadataKeys.CustomNameVisible, out bool visible));
        Assert.True(visible);
        Assert.True(entity.Metadata.TryGet(EntityMetadataKeys.Health, out float health));
        Assert.Equal(17.5f, health);
        Assert.Equal(poseIndex >= 0 ? EntityPose.FallFlying : EntityPose.Standing, entity.Pose);
    }

    /// <summary>1.8 is the one era that cannot answer the name-visibility key: its format has no BOOLEAN serializer at all and vanilla stores the flag as a BYTE The index still resolves and the raw value is still readable, so the limitation is in the projection's type and not in the table. Health, which is a float on 1.8 as everywhere else, resolves normally.</summary>
    [Fact]
    public async Task On1_8_NameVisibilityIsAByte_AndHealthStillResolves()
    {
        Entity entity = await ApplyAsync(47,
        [
            new EntityDataEntry(2, MetadataValue.String(Name)),
            new EntityDataEntry(3, MetadataValue.Byte(1)),
            new EntityDataEntry(6, MetadataValue.Float(17.5f)),
        ]);

        Assert.Equal(Name, entity.CustomName!.ToPlainText());
        Assert.True(entity.Metadata.TryGet(3, out MetadataValue rawVisible));
        Assert.Equal(MetadataValueKind.Byte, rawVisible.Kind);
        Assert.Equal(1, rawVisible.AsByte());
        Assert.False(entity.Metadata.TryGet(EntityMetadataKeys.CustomNameVisible, out bool _));

        Assert.True(entity.Metadata.TryGet(EntityMetadataKeys.Health, out float health));
        Assert.Equal(17.5f, health);
    }

    /// <summary>The negative control that proves the index is the ERA's and not a coincidence: the same name written one slot too low resolves nothing, which is exactly the silence a wrong table produces.</summary>
    [Theory]
    [InlineData(340)]
    [InlineData(754)]
    [InlineData(776)]
    public async Task AName_AtTheWrongIndex_DoesNotReachCustomName(int protocol)
    {
        Entity entity = await ApplyAsync(protocol, [new EntityDataEntry(1, MetadataValue.VarInt(271))]);

        Assert.Null(entity.CustomName);
        Assert.True(entity.Metadata.TryGet(1, out MetadataValue air));
        Assert.Equal(271, air.AsVarInt());
    }

    /// <summary>Health on the wrong side of a band boundary reads nothing rather than reading the neighbour's field, because the 1.16 living-flags BYTE at 7 and the 1.17 ticks-frozen VarInt at 7 are different kinds and the accessor treats a kind mismatch as a miss.</summary>
    [Fact]
    public async Task HealthWrittenAtTheNeighbouringBandsIndex_IsNotRead()
    {
        Entity entity = await ApplyAsync(758, [new EntityDataEntry(8, MetadataValue.Float(17.5f))]);
        Assert.False(entity.Metadata.TryGet(EntityMetadataKeys.Health, out float _));
    }

    private static MetadataValue LegacyNameValue(int protocol) =>
        protocol < 393
            ? MetadataValue.String(Name)
            : MetadataValue.OptionalComponent(Component.Text(Name));

    private static async Task<Entity> ApplyAsync(int protocol, IReadOnlyList<EntityDataEntry> entries) =>
        (await SpawnAndApplyAsync(protocol, entries)).Entity;

    private static async Task<(ApplierHarness Harness, Entity Entity)> SpawnAndApplyAsync(
        int protocol, IReadOnlyList<EntityDataEntry> entries)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);

        await harness.ApplyAsync(new ClientboundAddMobPacket(
            MobId, 0, 8.5, 64.0, -12.25, 0f, 0f, 0f, 0, 0, 0, new EntityMetadataList([], [])));

        Entity? entity = harness.State.Entities.Get(MobId);
        Assert.NotNull(entity);
        await ApplyToAsync(protocol, harness, entries);
        return (harness, entity!);
    }

    private static async Task ApplyToAsync(int protocol, ApplierHarness harness, IReadOnlyList<EntityDataEntry> entries)
    {
        // Through the era's real bound codec, so the serializer table the wire uses is the one under test rather than a hand-picked id.
        object packet = BoundDescriptorCodec.RoundTrip(
            protocol,
            "set_entity_data",
            new ClientboundSetEntityDataPacket(MobId, new EntityMetadataList([.. entries], [])));
        await harness.ApplyAsync(packet);
    }
}
