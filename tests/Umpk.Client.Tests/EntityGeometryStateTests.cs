using System.IO.Compression;
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
        using (JsonDocument legacy = ReadLegacy())
        {
            foreach (JsonElement release in legacy.RootElement.GetProperty("releases").EnumerateArray())
            {
                JsonElement native = legacy.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
                for (int index = 0; index < native.GetProperty("rows").GetArrayLength(); index++)
                    yield return [release.GetProperty("protocol").GetInt32(), release.GetProperty("version").GetString()!, index];
            }
        }
        using JsonDocument modern = ReadNative("1.14.4");
        for (int index = 0; index < modern.RootElement.GetProperty("rows").GetArrayLength(); index++)
            yield return [498, "1.14.4", index];
    }

    [Theory]
    [MemberData(nameof(NativeTransitions))]
    public async Task ApplierBoundsMatchNativeMetadataAndPositionPhases(int protocol, string versionName, int index)
    {
        using JsonDocument document = ReadNative(versionName);
        await ReplayNativeAsync(protocol, document.RootElement, index, "villager");
    }

    public static IEnumerable<object[]> NativeRoleTransitions()
    {
        using JsonDocument document = ReadRoles();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            int caseIndex = release.GetProperty("case").GetInt32();
            JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
            for (int index = 0; index < native.GetProperty("rows").GetArrayLength(); index++)
                yield return [release.GetProperty("protocol").GetInt32(), caseIndex, index, release.GetProperty("entity").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(NativeRoleTransitions))]
    public async Task AuditedEntityBoundsMatchNativeMetadataAndPosePhases(int protocol, int caseIndex, int index, string entityName)
    {
        using JsonDocument document = ReadRoles();
        await ReplayNativeAsync(protocol, document.RootElement.GetProperty("cases")[caseIndex], index, entityName["minecraft:".Length..]);
    }

    private static JsonDocument ReadRoles()
    {
        using Stream stream = typeof(EntityGeometryStateTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityRoleGeometry.native.json")!;
        return JsonDocument.Parse(stream);
    }

    public static IEnumerable<object[]> NativeMappedRoles()
    {
        using JsonDocument document = ReadMappedRoles();
        Assert.Equal(1, document.RootElement.GetProperty("schema").GetInt32());
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("protocol").GetInt32(), release.GetProperty("version").GetString()!, release.GetProperty("case").GetInt32(), release.GetProperty("entity").GetString()!];
    }

    [Theory]
    [MemberData(nameof(NativeMappedRoles))]
    public async Task MappedEntityPhasesMatchEveryNativeWitness(int protocol, string release, int caseIndex, string entityName)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version!.Version.Protocol);
        using JsonDocument document = ReadMappedRoles();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        for (int index = 0; index < native.GetProperty("rows").GetArrayLength(); index++)
            await ReplayNativeAsync(protocol, native, index, entityName["minecraft:".Length..]);
    }

    private static JsonDocument ReadMappedRoles()
    {
        using Stream stream = typeof(EntityGeometryStateTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.MappedEntityRoleGeometry.native.json.gz")!;
        using var decompressed = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(decompressed);
    }

    private static async Task ReplayNativeAsync(int protocol, JsonElement native, int index, string entityName)
    {
        JsonElement row = native.GetProperty("rows")[index];
        bool legacy = protocol < 477;
        int babyIndex = native.GetProperty("metadata_index").GetInt32();
        JsonElement point = native.GetProperty("positions")[row.GetProperty("position").GetInt32()];
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft(entityName), out RegistryEntry<EntityTypeDefinition> villager));
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

        ValueTask SetAge(int value) => babyIndex == -1 ? ValueTask.CompletedTask : harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(babyIndex, MetadataValue.Boolean(value != 0))], [])));
        ValueTask SetPose(int value) => harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(6, MetadataValue.Pose(native.GetProperty("poses")[value].GetString() switch { "STANDING" => EntityPose.Standing, "FALL_FLYING" => EntityPose.FallFlying, "SLEEPING" => EntityPose.Sleeping, "SWIMMING" => EntityPose.Swimming, "SPIN_ATTACK" => EntityPose.SpinAttack, "SNEAKING" or "CROUCHING" => EntityPose.Crouching, "LONG_JUMPING" => EntityPose.LongJumping, "DYING" => EntityPose.Dying, "CROAKING" => EntityPose.Croaking, "USING_TONGUE" => EntityPose.UsingTongue, "SITTING" => EntityPose.Sitting, "ROARING" => EntityPose.Roaring, "SNIFFING" => EntityPose.Sniffing, "EMERGING" => EntityPose.Emerging, "DIGGING" => EntityPose.Digging, "SLIDING" => EntityPose.Sliding, "SHOOTING" => EntityPose.Shooting, "INHALING" => EntityPose.Inhaling, _ => throw new InvalidOperationException("Unmeasured native pose") }))], [])));
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
    [InlineData(107, 11, false)]
    [InlineData(108, 11, false)]
    [InlineData(109, 11, false)]
    [InlineData(110, 11, false)]
    [InlineData(210, 12, false)]
    [InlineData(315, 12, true)]
    [InlineData(316, 12, true)]
    [InlineData(335, 12, true)]
    [InlineData(338, 12, true)]
    [InlineData(340, 12, true)]
    [InlineData(393, 12, true)]
    [InlineData(401, 12, true)]
    [InlineData(404, 12, true)]
    [InlineData(477, 14, true)]
    [InlineData(480, 14, true)]
    [InlineData(485, 14, true)]
    [InlineData(490, 14, true)]
    [InlineData(498, 14, true)]
    [InlineData(755, 16, false)]
    public async Task MetadataCallbacksAndEqualPositionReapplicationKeepTheirNativeOrder(int protocol, int babyIndex, bool retainedGrowth)
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
        double expectedMinimum = retainedGrowth ? 1.5 - .3F / 2.0 : original.MinX;
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
        using Stream? stream = typeof(EntityGeometryStateTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.VillagerGeometry{version}.native.json");
        if (stream is not null)
            return JsonDocument.Parse(stream);
        using JsonDocument legacy = ReadLegacy();
        JsonElement release = legacy.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        return JsonDocument.Parse(legacy.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()].GetRawText());
    }

    private static JsonDocument ReadLegacy()
    {
        using Stream stream = typeof(EntityGeometryStateTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyVillagerGeometry.native.json")!;
        return JsonDocument.Parse(stream);
    }
}
