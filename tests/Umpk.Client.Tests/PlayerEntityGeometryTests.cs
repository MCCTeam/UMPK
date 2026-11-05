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

public sealed class PlayerEntityGeometryTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        foreach (string resource in new[] { "PlayerEntityGeometry", "LegacyPosePlayerGeometry" })
        {
            using JsonDocument document = ReadNative(resource);
            foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [release.GetProperty("protocol").GetInt32(), release.GetProperty("version").GetString()!, release.GetProperty("case").GetInt32(), resource];
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task TrackedPlayerBoundsMatchNativePoseAndPositionCallbacks(int protocol, string releaseName, int caseIndex, string resource)
    {
        Assert.True(JavaVersions.TryGetByName(releaseName, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative(resource);
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            JsonElement point = native.GetProperty("positions")[row.GetProperty("position").GetInt32()];
            Vec3d position = new(point[0].GetDouble(), point[1].GetDouble(), point[2].GetDouble());
            var harness = new ApplierHarness(version);
            harness.State.Registries = JavaGameData.Registries(protocol);
            object spawn;
            if (protocol < 764)
                spawn = BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.NewGuid(), position.X, position.Y, position.Z, 0, 0, 0, new EntityMetadataList([], [])));
            else
            {
                Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("player"), out RegistryEntry<EntityTypeDefinition> type));
                spawn = BoundDescriptorCodec.RoundTrip(protocol, "add_entity", new ClientboundAddEntityPacket(42, Guid.NewGuid(), type.NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
            }
            await harness.ApplyAsync(spawn);
            Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(42));
            IEntityGeometrySource source = JavaGameData.EntityGeometryData(protocol);
            await SetPose(row.GetProperty("from").GetInt32());
            // The native fixture explicitly reapplies position between callback phases. This call covers that owner operation, independently of packet interpolation.
            harness.State.EntityGeometry.ObservePosition(entity);
            AssertBox("before");
            await SetPose(row.GetProperty("to").GetInt32());
            AssertBox("after_sync");
            harness.State.EntityGeometry.ObservePosition(entity);
            AssertBox("after_position");

            ValueTask SetPose(int index)
            {
                string name = native.GetProperty("poses")[index].GetString()!;
                EntityPose pose = name == "SNEAKING" ? EntityPose.Crouching : Enum.Parse<EntityPose>(name.Replace("_", ""), ignoreCase: true);
                object packet = BoundDescriptorCodec.RoundTrip(protocol, "set_entity_data", new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(6, MetadataValue.Pose(pose))], [])));
                return harness.ApplyAsync(packet);
            }

            void AssertBox(string phase)
            {
                JsonElement box = row.GetProperty(phase);
                Assert.True(harness.State.EntityGeometry.TryGet(entity, source, out Aabb actual, out bool blocksBuilding, protocol));
                Assert.True(blocksBuilding);
                Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), actual);
                Assert.Equal(position, entity.Position);
            }
        }
    }

    private static JsonDocument ReadNative(string resource)
    {
        using Stream stream = typeof(PlayerEntityGeometryTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.{resource}.native.json.gz")!;
        using var decompressed = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(decompressed);
    }
}
