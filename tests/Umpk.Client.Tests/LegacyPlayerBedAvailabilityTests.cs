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

public sealed class LegacyPlayerBedAvailabilityTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task OriginalBedFramesPreserveSuccessfulCallbacksAndExposeMissingContext(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0, failedNativeCallbacks = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            var bed = Cell(row.GetProperty("bed"));
            JsonElement initial = row.GetProperty("observations")[0];
            if (row.TryGetProperty("terrain_packet", out JsonElement terrain))
            {
                byte[] body = Convert.FromHexString(native.GetProperty("terrain_bodies").GetProperty(terrain.GetProperty("sha256").GetString()!).GetString()!);
                Assert.Equal(terrain.GetProperty("bytes").GetInt32(), body.Length);
                await harness.ApplyAsync(Decode(protocol, "level_chunk_with_light", body));
            }
            if (row.TryGetProperty("bed_write_accepted", out JsonElement write) && write.GetBoolean())
            {
                int state = initial.GetProperty("bed_native_state").GetInt32();
                int wireState = protocol < 393 ? ((state & 4095) << 4) | (state >> 12) : state;
                Assert.True(JavaGameData.BedInteractionData(protocol).TryGetBedFacing(wireState, out _));
                harness.State.World.SetBlockStateId(bed, wireState);
            }
            Vec3d point = Point(initial.GetProperty("state").GetProperty("position"));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, point.X, point.Y, point.Z, 0, 0, 0, new EntityMetadataList([], []))));
            Entity actor = harness.State.Entities.Get(42)!;
            JsonElement target = initial.GetProperty("state").GetProperty("target");
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(42, target[0].GetDouble(), target[1].GetDouble(), target[2].GetDouble(), target[3].GetSingle(), target[4].GetSingle(), false, null, 0)));
            Assert.True(KnownBounds(out _));
            JsonElement sleepPacket = row.GetProperty("packets")[0];
            var sleep = Assert.IsType<ClientboundUseBedPacket>(Decode(protocol, "use_bed", Convert.FromHexString(sleepPacket.GetProperty("body").GetString()!)));
            Assert.Equal(42, sleep.PlayerId);
            Assert.Equal(bed, sleep.Position);

            await harness.ApplyAsync(sleep);

            scenarios++;
            if (row.GetProperty("sleep").GetProperty("result").GetString() == "native_property_exception")
            {
                // Original clients abort this callback. Its partial state is retained in the fixture, not treated as successful sleep or as clear geometry.
                Assert.False(KnownBounds(out _));
                Assert.Equal(Point(row.GetProperty("observations")[1].GetProperty("state").GetProperty("position")), actor.Position);
                Assert.Equal(1, row.GetProperty("packets").GetArrayLength());
                failedNativeCallbacks++;
                continue;
            }
            AssertGeometry(row.GetProperty("observations")[1]);
            Assert.Equal(Vec3d.Zero, actor.Velocity);
            if (row.GetProperty("scene").GetInt32() == 3 && row.GetProperty("bed_write_accepted").GetBoolean())
                harness.State.World.SetBlockStateId(bed, 0);
            JsonElement wakePacket = row.GetProperty("packets")[1];
            var wake = Assert.IsType<ClientboundAnimatePacket>(Decode(protocol, "animate", Convert.FromHexString(wakePacket.GetProperty("body").GetString()!)));
            Assert.Equal(42, wake.EntityId);
            Assert.Equal(2, wake.Action);

            await harness.ApplyAsync(wake);

            // The original world's missing columns read as air. The tracked client's absent terrain cannot prove those exit candidates are clear.
            int position = row.GetProperty("position").GetInt32();
            bool missingExitTerrain = row.GetProperty("scene").GetInt32() == 2 && (position == 1 || protocol >= 110 && position is 4 or 5 or 6 or 7);
            if (missingExitTerrain)
                Assert.False(KnownBounds(out _));
            else
                AssertGeometry(row.GetProperty("observations")[2]);
            continue;

            bool KnownBounds(out Aabb bounds) => harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out bounds, out _, protocol);

            void AssertGeometry(JsonElement observation)
            {
                Assert.Equal(Point(observation.GetProperty("state").GetProperty("position")), actor.Position);
                Assert.True(KnownBounds(out Aabb bounds));
                JsonElement expected = observation.GetProperty("state").GetProperty("box");
                Assert.Equal(new Aabb(expected[0].GetDouble(), expected[1].GetDouble(), expected[2].GetDouble(), expected[3].GetDouble(), expected[4].GetDouble(), expected[5].GetDouble()), bounds);
            }
        }
        Assert.Equal(44, scenarios);
        Assert.Equal(protocol < 110 ? 10 : 30, failedNativeCallbacks);
    }

    private static object Decode(int protocol, string identifier, byte[] body)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, identifier);
        var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
        object result = codec.Decode(body, context);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, result, context);
        Assert.Equal(body, buffer.WrittenSpan.ToArray());
        return result;
    }

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static BlockPos Cell(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(LegacyPlayerBedAvailabilityTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerBedAvailability.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
