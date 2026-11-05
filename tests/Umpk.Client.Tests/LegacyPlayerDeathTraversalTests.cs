using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
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

public sealed class LegacyPlayerDeathTraversalTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task NativeHealthFramesAndEligibleTicksRetainLookupAndMatchWorldLifetime(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            JsonElement phases = document.RootElement.GetProperty("observations")[row.GetProperty("observation_case").GetInt32()];
            var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            int mode = row.GetProperty("mode").GetInt32(), terrain = row.GetProperty("terrain").GetInt32(), packetIndex = 0;
            if (terrain is 0 or 2)
                await Apply("initial_full");
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, 1.5, 64, 1.5, 0, 0, 0, new EntityMetadataList([], []))));
            Entity actor = harness.State.Entities.Get(42)!;
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(42, 3.5, 65, 3.5, 90, 45, false, null, 0)));
            await Apply("initial_health");
            if (mode is 2 or 3)
                await Apply("death_status");
            AssertPhase(phases[0]);
            for (int tick = 0; tick < 40; tick++)
            {
                if (terrain == 2 && tick == 5)
                    await Apply("unload_5");
                if (terrain == 2 && tick == 12 || terrain == 3 && tick == 5)
                    await Apply($"full_{tick}");
                if (mode is 3 or 4 && tick == 10)
                    await Apply("revive_10");
                if (mode == 4 && tick == 15)
                    await Apply("zero_15");

                harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);
                AssertPhase(phases[2 * tick + 1]);
                harness.State.EntityMembership.FinishWorldPhase();
                AssertPhase(phases[2 * tick + 2]);
            }
            Assert.Equal(row.GetProperty("packets").GetArrayLength(), packetIndex);
            scenarios++;
            continue;

            void AssertPhase(JsonElement expected)
            {
                Assert.Same(actor, harness.State.Entities.Get(42));
                Assert.True(expected.GetProperty("lookup_same").GetBoolean());
                Assert.Equal(expected.GetProperty("added").GetBoolean() && expected.GetProperty("in_entity_list").GetBoolean(), harness.State.EntityMembership.CanTick(actor));
                Assert.Equal(!expected.GetProperty("placement_clear").GetBoolean(), harness.State.EntityMembership.IsWorldQueryMember(actor));
                Assert.True(actor.Metadata.TryGet(EntityMetadataKeys.Health, out float health));
                Assert.Equal(expected.GetProperty("health").GetSingle(), health);
                JsonElement state = expected.GetProperty("state"), box = state.GetProperty("box");
                Assert.Equal(Point(state.GetProperty("position")), actor.Position);
                Assert.Equal(state.GetProperty("rotation")[0].GetSingle(), actor.Yaw);
                Assert.Equal(state.GetProperty("rotation")[1].GetSingle(), actor.Pitch);
                Assert.Equal(Point(expected.GetProperty("velocity")), actor.Velocity);
                Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out bool blocksBuilding, protocol));
                Assert.True(blocksBuilding);
                Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), bounds);
            }

            async Task Apply(string action)
            {
                JsonElement packet = row.GetProperty("packets")[packetIndex++];
                Assert.Equal(action, packet.GetProperty("action").GetString());
                byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
                Assert.Equal(packet.GetProperty("bytes").GetInt32(), body.Length);
                string identifier = action == "death_status" ? "entity_event" : action is "initial_health" or "revive_10" or "zero_15" ? "set_entity_data" : action == "unload_5" && protocol != 47 ? "forget_level_chunk" : "level_chunk_with_light";
                BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, identifier);
                var context = new PacketCodecContext(harness.State.Registries!, IConnectionCodecState.Empty);
                object decoded = codec.Decode(body, context);
                var output = new ArrayBufferWriter<byte>();
                var writer = new PacketWriter(output);
                codec.Encode(ref writer, decoded, context);
                Assert.Equal(body, output.WrittenSpan.ToArray());
                if (decoded is ClientboundEntityEventPacket status)
                {
                    Assert.Equal(42, status.EntityId);
                    Assert.Equal(3, status.EventId);
                }
                if (decoded is ClientboundSetEntityDataPacket metadata)
                    Assert.Equal(42, metadata.EntityId);
                if (decoded is ClientboundLevelChunkPacket chunk)
                {
                    Assert.Equal(0, chunk.ChunkX);
                    Assert.Equal(0, chunk.ChunkZ);
                }
                await harness.ApplyAsync(decoded);
            }
        }
        Assert.Equal(60, scenarios);
    }

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(LegacyPlayerDeathTraversalTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerDeathTraversal.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
