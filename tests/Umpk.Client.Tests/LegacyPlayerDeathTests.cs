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

public sealed class LegacyPlayerDeathTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task NativeHealthAndStatusFramesApplyDistinctDeathEffectsBeforePublication(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        JsonElement baseline = native.GetProperty("rows").EnumerateArray().First(row => row.GetProperty("flags").GetInt32() == 0 && row.GetProperty("health").GetSingle() == 20);
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            JsonElement initial = row.GetProperty("observations")[0];
            Vec3d point = Point(initial.GetProperty("state").GetProperty("position"));
            float yaw = initial.GetProperty("state").GetProperty("rotation")[0].GetSingle();
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, point.X, point.Y, point.Z, yaw, 0, 0, new EntityMetadataList([], []))));
            Entity actor = harness.State.Entities.Get(42)!;
            // Native constructors already supply health 20 and flags 0. Seed the tracked defaults through a retained native metadata frame before the measured operations.
            await harness.ApplyAsync(Decode(baseline.GetProperty("packets")[0], "set_entity_data"));
            JsonElement target = initial.GetProperty("state").GetProperty("target");
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(42, target[0].GetDouble(), target[1].GetDouble(), target[2].GetDouble(), target[3].GetSingle(), target[4].GetSingle(), false, null, 0)));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "set_entity_motion", new ClientboundSetEntityMotionPacket(42, 1000, 2000, -3000)));
            AssertState(initial, Observe());
            List<(int Status, Snapshot State)> published = [];
            using IDisposable subscription = harness.Events.Subscribe<EntityStatusChanged>(status => published.Add((status.EventId, Observe())));
            int operation = 1;
            foreach (JsonElement packet in row.GetProperty("packets").EnumerateArray())
            {
                string action = packet.GetProperty("action").GetString()!;
                object decoded = Decode(packet, action == "health_metadata" ? "set_entity_data" : "entity_event");
                if (decoded is ClientboundEntityEventPacket status)
                {
                    Assert.Equal(42, status.EntityId);
                    Assert.Equal(action == "hurt_status" ? 2 : 3, status.EventId);
                }
                else
                    Assert.Equal(42, Assert.IsType<ClientboundSetEntityDataPacket>(decoded).EntityId);

                await harness.ApplyAsync(decoded);

                JsonElement expected = row.GetProperty("observations")[operation++];
                AssertState(expected, Observe());
                if (action != "health_metadata")
                {
                    AssertState(expected, published[^1].State);
                    Assert.Equal(action == "hurt_status" ? 2 : 3, published[^1].Status);
                }
            }
            Assert.Equal(row.GetProperty("hurt").GetBoolean() ? 3 : 2, published.Count);
            Assert.Equal(row.GetProperty("observations").GetArrayLength(), operation);
            scenarios++;
            continue;

            object Decode(JsonElement packet, string identifier)
            {
                byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
                Assert.Equal(packet.GetProperty("bytes").GetInt32(), body.Length);
                BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, identifier);
                var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
                object decoded = codec.Decode(body, context);
                Assert.Equal(body, Encode(codec, decoded, context));
                return decoded;
            }

            Snapshot Observe()
            {
                bool healthKnown = actor.Metadata.TryGet(EntityMetadataKeys.Health, out float health);
                bool flagsKnown = actor.Metadata.TryGet(0, out MetadataValue flags) && flags.Kind == MetadataValueKind.Byte;
                bool boundsKnown = harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out _, protocol);
                return new(healthKnown, health, flagsKnown, flagsKnown ? unchecked((byte)flags.AsByte()) : (byte)0, actor.Position, actor.Velocity, actor.Yaw, actor.Pitch, boundsKnown, bounds);
            }
        }
        Assert.Equal(192, scenarios);
    }

    private static void AssertState(JsonElement native, Snapshot actual)
    {
        Assert.True(actual.HealthKnown);
        Assert.Equal(native.GetProperty("health").GetSingle(), actual.Health);
        Assert.True(actual.FlagsKnown);
        Assert.Equal(native.GetProperty("flags").GetInt32(), actual.Flags);
        Vec3d velocity = Point(native.GetProperty("velocity"));
        Assert.Equal(velocity, actual.Velocity);
        Assert.Equal(BitConverter.DoubleToInt64Bits(velocity.X), BitConverter.DoubleToInt64Bits(actual.Velocity.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(velocity.Y), BitConverter.DoubleToInt64Bits(actual.Velocity.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(velocity.Z), BitConverter.DoubleToInt64Bits(actual.Velocity.Z));
        JsonElement state = native.GetProperty("state"), rotation = state.GetProperty("rotation"), box = state.GetProperty("box");
        Assert.Equal(Point(state.GetProperty("position")), actual.Position);
        Assert.Equal(rotation[0].GetSingle(), actual.Yaw);
        Assert.Equal(rotation[1].GetSingle(), actual.Pitch);
        Assert.True(actual.BoundsKnown);
        Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), actual.Bounds);
    }

    private readonly record struct Snapshot(bool HealthKnown, float Health, bool FlagsKnown, byte Flags, Vec3d Position, Vec3d Velocity, float Yaw, float Pitch, bool BoundsKnown, Aabb Bounds);

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(LegacyPlayerDeathTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerDeath.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
