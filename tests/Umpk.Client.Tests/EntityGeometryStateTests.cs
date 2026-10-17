using System.Text.Json;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EntityGeometryStateTests
{
    [Theory]
    [InlineData(EntityPose.Standing, 1.0, true)]
    [InlineData(EntityPose.Standing, 2.0, false)]
    [InlineData(EntityPose.Sleeping, 2.0, true)]
    [InlineData(EntityPose.Unknown, 1.0, false)]
    public void UnmeasuredScaleAndUnknownPoseDoNotUseDefaultStandingBounds(EntityPose pose, double scaleValue, bool known)
    {
        var registries = JavaGameData.Registries(776);
        Assert.True(registries.EntityTypes.TryGet(Identifier.Minecraft("villager"), out RegistryEntry<EntityTypeDefinition> villager));
        Assert.True(registries.Attributes.TryGet(Identifier.Minecraft("scale"), out RegistryEntry<AttributeDefinition> scale));
        var entity = new Entity(42, Guid.Empty, villager) { Position = new Vec3d(1.5, 64, 1.5), Pose = pose };
        entity.Attributes.GetOrCreate(scale).BaseValue = scaleValue;
        var geometry = new Internal.EntityGeometryState();
        IEntityGeometrySource source = Assert.IsAssignableFrom<IEntityGeometrySource>(JavaGameData.BlockInteractionData(776));

        Assert.Equal(known, geometry.TryGet(entity, source, out _, out _));
    }

    public static IEnumerable<object[]> NativeTransitions()
    {
        foreach ((int protocol, string version) in new[] { (210, "1.10.2"), (315, "1.11"), (498, "1.14.4") })
        {
            using JsonDocument document = ReadNative(version);
            for (int index = 0; index < document.RootElement.GetProperty("rows").GetArrayLength(); index++)
                yield return [protocol, version, index];
        }
    }

    [Theory]
    [MemberData(nameof(NativeTransitions))]
    public async Task ApplierBoundsMatchNativeMetadataAndPositionPhases(int protocol, string versionName, int index)
    {
        using JsonDocument document = ReadNative(versionName);
        JsonElement native = document.RootElement, row = native.GetProperty("rows")[index];
        bool legacy = protocol < 477;
        int babyIndex = native.GetProperty("metadata_index").GetInt32();
        JsonElement point = native.GetProperty("positions")[row.GetProperty("position").GetInt32()];
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("villager"), out RegistryEntry<EntityTypeDefinition> villager));
        await harness.ApplyAsync(new ClientboundAddMobPacket(42, villager.NetworkId, point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble(), 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(42));
        IEntityGeometrySource source = Assert.IsAssignableFrom<IEntityGeometrySource>(JavaGameData.BlockInteractionData(protocol));
        await SetAge(row.GetProperty(legacy ? "from" : "from_age").GetInt32());
        if (!legacy)
            await SetPose(row.GetProperty("from_pose").GetInt32());
        await Reapply();
        AssertBox("before");

        await SetAge(row.GetProperty(legacy ? "to" : "to_age").GetInt32());
        AssertBox(legacy ? "after_sync" : "after_age_sync");
        if (!legacy)
        {
            await SetPose(row.GetProperty("to_pose").GetInt32());
            AssertBox("after_pose_sync");
        }
        await Reapply();
        AssertBox("after_position");

        ValueTask SetAge(int value) => harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(babyIndex, MetadataValue.Boolean(value != 0))], [])));
        ValueTask SetPose(int value) => harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(6, MetadataValue.Pose(native.GetProperty("poses")[value].GetString() == "SLEEPING" ? EntityPose.Sleeping : EntityPose.Standing))], [])));
        ValueTask Reapply() => harness.ApplyAsync(new ClientboundMoveEntityPosPacket(42, 0, 0, 0, false));
        void AssertBox(string phase)
        {
            JsonElement box = row.GetProperty(phase);
            Assert.True(harness.State.EntityGeometry.TryGet(entity, source, out Aabb actual, out bool blocking));
            Assert.True(blocking);
            Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), actual);
        }
    }

    [Theory]
    [InlineData(210, 12)]
    [InlineData(315, 12)]
    [InlineData(498, 14)]
    [InlineData(755, 16)]
    public async Task MetadataCallbacksAndEqualPositionReapplicationKeepTheirNativeOrder(int protocol, int babyIndex)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("villager"), out RegistryEntry<EntityTypeDefinition> villager));
        await harness.ApplyAsync(new ClientboundAddMobPacket(42, villager.NetworkId, 1.5, 64, 1.5, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(42));
        IEntityGeometrySource source = Assert.IsAssignableFrom<IEntityGeometrySource>(JavaGameData.BlockInteractionData(protocol));
        Assert.True(harness.State.EntityGeometry.TryGet(entity, source, out Aabb original, out bool blocking));
        Assert.True(blocking);
        Assert.Equal(1.5 - .6F / 2F, original.MinX);

        await harness.ApplyAsync(Metadata(true));
        await harness.ApplyAsync(Metadata(false));

        Assert.True(harness.State.EntityGeometry.TryGet(entity, source, out Aabb grown, out _));
        double expectedMinimum = protocol is 315 or 498 ? 1.5 - .3F / 2.0 : original.MinX;
        Assert.Equal(expectedMinimum, grown.MinX);
        Assert.Equal(expectedMinimum + .6F, grown.MaxX);
        Assert.Equal(new Vec3d(1.5, 64, 1.5), entity.Position);

        await harness.ApplyAsync(new ClientboundMoveEntityPosPacket(42, 0, 0, 0, false));

        Assert.True(harness.State.EntityGeometry.TryGet(entity, source, out Aabb reapplied, out _));
        Assert.Equal(original, reapplied);

        ClientboundSetEntityDataPacket Metadata(bool baby) => new(42, new EntityMetadataList([new(babyIndex, MetadataValue.Boolean(baby))], []));
    }

    private static JsonDocument ReadNative(string version)
    {
        using Stream stream = typeof(EntityGeometryStateTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.VillagerGeometry{version}.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
