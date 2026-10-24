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

public sealed class LegacyPlayerBlockContextTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task NativeBlockUpdatesCannotInventSuccessfulBedContext(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty("context").EnumerateArray())
        {
            ApplierHarness harness = Create(version);
            JsonElement packets = row.GetProperty("world_packets"), phases = row.GetProperty("observations");
            int packetIndex = 0, phaseIndex = 1;
            if (packets[0].GetProperty("action").GetString() == "initial_full")
                await ApplyWorldPacket(packets[packetIndex++]);
            Entity actor = await Spawn(harness, protocol, phases[0], 2);
            AssertGeometry(harness, actor, protocol, phases[0]);
            while (packetIndex < packets.GetArrayLength())
            {
                await ApplyWorldPacket(packets[packetIndex++]);
                AssertGeometry(harness, actor, protocol, phases[phaseIndex++]);
            }
            object sleep = Decode(protocol, "use_bed", Convert.FromHexString(row.GetProperty("packets")[0].GetProperty("body").GetString()!));
            var useBed = Assert.IsType<ClientboundUseBedPacket>(sleep);
            Assert.Equal(42, useBed.PlayerId);
            Assert.Equal(Cell(row.GetProperty("bed")), useBed.Position);
            await harness.ApplyAsync(sleep);
            JsonElement afterSleep = phases[phaseIndex++];
            if (row.GetProperty("sleep").GetProperty("result").GetString() == "native_property_exception")
            {
                Assert.False(Known(out _));
                Assert.Equal(Point(afterSleep.GetProperty("state").GetProperty("position")), actor.Position);
                Assert.Single(row.GetProperty("packets").EnumerateArray());
            }
            else
            {
                AssertGeometry(harness, actor, protocol, afterSleep);
                var wake = Assert.IsType<ClientboundAnimatePacket>(Decode(protocol, "animate", Convert.FromHexString(row.GetProperty("packets")[1].GetProperty("body").GetString()!)));
                Assert.Equal(42, wake.EntityId);
                Assert.Equal(2, wake.Action);
                Vec3d beforeWake = actor.Position;
                await harness.ApplyAsync(wake);
                JsonElement afterWake = phases[phaseIndex++];
                if (Known(out _))
                    AssertGeometry(harness, actor, protocol, afterWake);
                else
                    Assert.Equal(beforeWake, actor.Position);
            }
            Assert.Equal(phases.GetArrayLength(), phaseIndex);
            scenarios++;
            continue;

            bool Known(out Aabb bounds) => harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out bounds, out _, protocol);
            Task ApplyWorldPacket(JsonElement packet) => harness.ApplyAsync(WorldPacket(protocol, native, packet, Cell(row.GetProperty("bed")))).AsTask();
        }
        Assert.Equal(32, scenarios);
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task NativeIgnoredEmptyChunkWritesDoNotBecomeResizeColliders(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty("resize").EnumerateArray())
        {
            ApplierHarness harness = Create(version);
            JsonElement packets = row.GetProperty("packets"), setup = row.GetProperty("setup"), phases = row.GetProperty("observations");
            int packetIndex = 0, setupIndex = 1;
            if (packets[0].GetProperty("action").GetString() == "initial_full")
                await ApplyWorldPacket(packets[packetIndex++]);
            Entity actor = await Spawn(harness, protocol, setup[0], row.GetProperty("flags").GetInt32());
            AssertGeometry(harness, actor, protocol, setup[0]);
            while (packetIndex < packets.GetArrayLength())
            {
                await ApplyWorldPacket(packets[packetIndex++]);
                AssertGeometry(harness, actor, protocol, setup[setupIndex++]);
            }
            Assert.Equal(setup.GetArrayLength(), setupIndex);
            AssertPhase(phases[0]);
            for (int tick = 0; tick < 8; tick++)
            {
                harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);
                AssertPhase(phases[2 * tick + 1]);
                harness.State.EntityMembership.FinishWorldPhase();
                AssertPhase(phases[2 * tick + 2]);
            }
            scenarios++;
            continue;

            void AssertPhase(JsonElement phase)
            {
                Assert.Equal(phase.GetProperty("added").GetBoolean() && phase.GetProperty("in_entity_list").GetBoolean(), harness.State.EntityMembership.CanTick(actor));
                AssertGeometry(harness, actor, protocol, phase);
            }
            Task ApplyWorldPacket(JsonElement packet) => harness.ApplyAsync(WorldPacket(protocol, native, packet, Cell(row.GetProperty("bed")))).AsTask();
        }
        Assert.Equal(48, scenarios);
    }

    private static ApplierHarness Create(JavaVersion version)
    {
        var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(version.Version.Protocol);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), version.Version.Protocol);
        return harness;
    }

    private static async Task<Entity> Spawn(ApplierHarness harness, int protocol, JsonElement initial, int flags)
    {
        JsonElement state = initial.GetProperty("state"), target = state.GetProperty("target");
        Vec3d point = Point(state.GetProperty("position"));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, point.X, point.Y, point.Z, 0, 0, 0, new EntityMetadataList([new(0, MetadataValue.Byte(unchecked((sbyte)flags)))], []))));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(42, target[0].GetDouble(), target[1].GetDouble(), target[2].GetDouble(), target[3].GetSingle(), target[4].GetSingle(), false, null, 0)));
        return harness.State.Entities.Get(42)!;
    }

    private static void AssertGeometry(ApplierHarness harness, Entity actor, int protocol, JsonElement expected)
    {
        JsonElement state = expected.GetProperty("state"), box = state.GetProperty("box");
        Assert.Equal(Point(state.GetProperty("position")), actor.Position);
        Assert.Equal(state.GetProperty("rotation")[0].GetSingle(), actor.Yaw);
        Assert.Equal(state.GetProperty("rotation")[1].GetSingle(), actor.Pitch);
        Assert.Equal(Point(expected.GetProperty("velocity")), actor.Velocity);
        Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out _, protocol));
        Assert.Equal(new Aabb(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble(), box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()), bounds);
    }

    private static object WorldPacket(int protocol, JsonElement native, JsonElement packet, BlockPos bed)
    {
        string action = packet.GetProperty("action").GetString()!;
        string identifier = action.Contains("update", StringComparison.Ordinal) && !action.Contains("full", StringComparison.Ordinal) && !action.Contains("unload", StringComparison.Ordinal) ? "block_update" : action.StartsWith("unload", StringComparison.Ordinal) && protocol != 47 ? "forget_level_chunk" : "level_chunk_with_light";
        byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
        Assert.Equal(packet.GetProperty("bytes").GetInt32(), body.Length);
        object decoded = Decode(protocol, identifier, body);
        if (decoded is ClientboundBlockUpdatePacket block)
        {
            Assert.Equal(bed, block.Position);
            if (action == "air_update")
                Assert.Equal(0, block.BlockStateId);
            else
                Assert.True(JavaGameData.BedInteractionData(protocol).TryGetBedFacing(block.BlockStateId, out _));
        }
        if (decoded is ClientboundLevelChunkPacket chunk)
        {
            Assert.Equal(bed.X >> 4, chunk.ChunkX);
            Assert.Equal(bed.Z >> 4, chunk.ChunkZ);
        }
        return decoded;
    }

    private static object Decode(int protocol, string identifier, byte[] body)
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

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static BlockPos Cell(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(LegacyPlayerBlockContextTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerBlockContext.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
