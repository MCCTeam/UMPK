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

public sealed class LegacyPlayerEmptyAliasTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task NativeMissingColumnAliasesDriveBedGeometry(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
            JsonElement packets = row.GetProperty("world_packets"), phases = row.GetProperty("observations");
            int packetIndex = 0, phaseIndex = 1;
            if (packets[0].GetProperty("action").GetString()!.StartsWith("initial_", StringComparison.Ordinal))
                await ApplyWorldPacket(packets[packetIndex++]);
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, 1.5, 64, 1.5, 0, 0, 0, new EntityMetadataList([new(0, MetadataValue.Byte(2))], []))));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(42, 3.5, 65, 3.5, 90, 45, false, null, 0)));
            Entity actor = harness.State.Entities.Get(42)!;
            AssertGeometry(phases[0]);
            while (packetIndex < packets.GetArrayLength())
            {
                await ApplyWorldPacket(packets[packetIndex++]);
                AssertGeometry(phases[phaseIndex++]);
            }
            var sleep = Assert.IsType<ClientboundUseBedPacket>(Decode("use_bed", Convert.FromHexString(row.GetProperty("packets")[0].GetProperty("body").GetString()!)));
            Assert.Equal(42, sleep.PlayerId);
            Assert.Equal(Cell(row.GetProperty("cells")[row.GetProperty("sleep_cell").GetInt32()]), sleep.Position);
            Vec3d previous = actor.Position;
            await harness.ApplyAsync(sleep);
            JsonElement afterSleep = phases[phaseIndex++];
            if (row.GetProperty("sleep").GetProperty("result").GetString() == "native_property_exception")
            {
                Assert.False(Known(out _));
                Assert.Equal(previous, actor.Position);
                Assert.Single(row.GetProperty("packets").EnumerateArray());
            }
            else
            {
                AssertGeometry(afterSleep);
                var wake = Assert.IsType<ClientboundAnimatePacket>(Decode("animate", Convert.FromHexString(row.GetProperty("packets")[1].GetProperty("body").GetString()!)));
                Assert.Equal(42, wake.EntityId);
                Assert.Equal(2, wake.Action);
                previous = actor.Position;
                await harness.ApplyAsync(wake);
                JsonElement afterWake = phases[phaseIndex++];
                if (Known(out _))
                    AssertGeometry(afterWake);
                else
                    Assert.Equal(previous, actor.Position);
            }
            Assert.Equal(phases.GetArrayLength(), phaseIndex);
            scenarios++;
            continue;

            bool Known(out Aabb bounds) => harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out bounds, out _, protocol);
            void AssertGeometry(JsonElement expected)
            {
                JsonElement state = expected.GetProperty("state"), box = state.GetProperty("box");
                Assert.Equal(Point(state.GetProperty("position")), actor.Position);
                Assert.Equal(state.GetProperty("rotation")[0].GetSingle(), actor.Yaw);
                Assert.Equal(state.GetProperty("rotation")[1].GetSingle(), actor.Pitch);
                Assert.Equal(Point(expected.GetProperty("velocity")), actor.Velocity);
                Assert.True(Known(out Aabb bounds));
                Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), bounds);
            }
            async Task ApplyWorldPacket(JsonElement packet)
            {
                string action = packet.GetProperty("action").GetString()!;
                bool update = action is "bed_a" or "bed_b" or "air_b" or "air_c" or "restore_bed_a";
                string identifier = update ? "block_update" : action == "unload_a" && protocol != 47 ? "forget_level_chunk" : "level_chunk_with_light";
                byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
                Assert.Equal(packet.GetProperty("bytes").GetInt32(), body.Length);
                object decoded = Decode(identifier, body);
                int index = action.EndsWith("_b", StringComparison.Ordinal) ? 1 : action.EndsWith("_c", StringComparison.Ordinal) ? 2 : 0;
                BlockPos at = Cell(row.GetProperty("cells")[index]);
                if (decoded is ClientboundBlockUpdatePacket block)
                {
                    Assert.Equal(at, block.Position);
                    if (action.StartsWith("air_", StringComparison.Ordinal))
                        Assert.Equal(0, block.BlockStateId);
                    else
                        Assert.True(JavaGameData.BedInteractionData(protocol).TryGetBedFacing(block.BlockStateId, out _));
                }
                if (decoded is ClientboundLevelChunkPacket chunk)
                    Assert.Equal(ChunkPos.Containing(at), chunk.Column.Position);
                await harness.ApplyAsync(decoded);
            }
        }
        Assert.Equal(64, scenarios);
        return;

        object Decode(string identifier, byte[] body)
        {
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, identifier);
            var context = new PacketCodecContext(JavaGameData.Registries(protocol), IConnectionCodecState.Empty);
            object decoded = codec.Decode(body, context);
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(body, output.WrittenSpan.ToArray());
            return decoded;
        }
    }

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static BlockPos Cell(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());
    internal static JsonDocument ReadNative()
    {
        using Stream stream = typeof(LegacyPlayerEmptyAliasTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerEmptyAlias.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
