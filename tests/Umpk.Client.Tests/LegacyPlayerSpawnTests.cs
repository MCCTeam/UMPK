using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyPlayerSpawnTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task OriginalSpawnAndMetadataFramesPreserveNativeBoxesAndReplaceOwnedState(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            JsonElement observations = row.GetProperty("observations");
            int published = 0, packetIndex = 0;
            using IDisposable subscription = harness.Events.Subscribe<EntitySpawned>(spawn =>
            {
                Assert.Equal(42, spawn.EntityId);
                AssertGeometry(harness.State.Entities.Get(42)!, observations[published == 0 ? 0 : 3]);
                published++;
            });
            if (row.GetProperty("terrain").GetBoolean())
                await Apply("terrain", "level_chunk_with_light");

            var spawn = Assert.IsType<ClientboundAddPlayerPacket>(await Apply("spawn", "add_player"));

            Assert.Equal(42, spawn.EntityId);
            Assert.Equal(Guid.Parse("00000000-0000-0000-0000-00000000002a"), spawn.Uuid);
            Entity actor = harness.State.Entities.Get(42)!;
            AssertGeometry(actor, observations[0]);

            var metadata = Assert.IsType<ClientboundSetEntityDataPacket>(await Apply("metadata", "set_entity_data"));

            Assert.Equal(42, metadata.EntityId);
            AssertGeometry(actor, observations[1]);
            // This native observation is an explicit unchanged-coordinate callback, not an interpolating teleport packet.
            harness.State.EntityGeometry.ObservePosition(actor);
            AssertGeometry(actor, observations[2]);

            await Apply("replacement", "add_player");

            Entity replacement = harness.State.Entities.Get(42)!;
            Assert.NotSame(actor, replacement);
            AssertGeometry(replacement, observations[3]);
            Assert.False(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol));
            Assert.Equal(2, published);
            Assert.Equal(row.GetProperty("packets").GetArrayLength(), packetIndex);
            scenarios++;
            continue;

            async Task<object> Apply(string action, string identifier)
            {
                JsonElement packet = row.GetProperty("packets")[packetIndex++];
                Assert.Equal(action, packet.GetProperty("action").GetString());
                byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
                Assert.Equal(packet.GetProperty("bytes").GetInt32(), body.Length);
                BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, identifier);
                var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
                object decoded = codec.Decode(body, context);
                Assert.Equal(body, Encode(codec, decoded, context));
                await harness.ApplyAsync(decoded);
                return decoded;
            }

            void AssertGeometry(Entity entity, JsonElement observation)
            {
                JsonElement state = observation.GetProperty("state"), position = state.GetProperty("position"), rotation = state.GetProperty("rotation"), box = state.GetProperty("box");
                Assert.Equal(new Vec3d(position[0].GetDouble(), position[1].GetDouble(), position[2].GetDouble()), entity.Position);
                Assert.Equal(rotation[0].GetSingle(), entity.Yaw);
                Assert.Equal(rotation[1].GetSingle(), entity.Pitch);
                Assert.True(entity.Metadata.TryGet(0, out MetadataValue flags));
                Assert.Equal(MetadataValueKind.Byte, flags.Kind);
                Assert.Equal(observation.GetProperty("flags").GetInt32(), unchecked((byte)flags.AsByte()));
                Assert.True(harness.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out _, protocol));
                Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), bounds);
                Assert.Equal(observation.GetProperty("added").GetBoolean() && observation.GetProperty("in_entity_list").GetBoolean(), harness.State.EntityMembership.CanTick(entity));
            }
        }
        Assert.Equal(32, scenarios);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(LegacyPlayerSpawnTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerSpawn.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
