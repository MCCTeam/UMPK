using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyPlayerPartialContextTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("case").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task NativePartialPayloadControlsBedReadsAndWake(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty("callbacks").EnumerateArray())
        {
            ApplierHarness harness = Create(version);
            JsonElement phases = row.GetProperty("observations");
            Entity actor = await Spawn(harness, protocol, phases[0]);
            AssertGeometry(harness, actor, protocol, phases[0]);
            AssertCells(harness, actor, protocol, row, phases[0], partialReceived: false);
            int phase = 1;
            bool partialReceived = false;
            bool fillsSharedSection = phases.EnumerateArray().Single(value => value.GetProperty("operation").GetString() == "after_partial_payload")
                .GetProperty("reads")[row.GetProperty("partial_cell").GetInt32()].GetProperty("empty_chunk").GetBoolean();
            foreach (JsonElement packet in row.GetProperty("world_packets").EnumerateArray())
            {
                await harness.ApplyAsync(WorldPacket(harness, protocol, native, row, packet));
                partialReceived |= packet.GetProperty("action").GetString() == "partial_payload";
                AssertGeometry(harness, actor, protocol, phases[phase]);
                AssertCells(harness, actor, protocol, row, phases[phase++], partialReceived && fillsSharedSection);
            }
            var sleep = Assert.IsType<ClientboundUseBedPacket>(Decode(protocol, "use_bed", Convert.FromHexString(row.GetProperty("packets")[0].GetProperty("body").GetString()!)));
            Assert.Equal(42, sleep.PlayerId);
            Assert.Equal(Cell(row.GetProperty("cells")[0]), sleep.Position);
            Vec3d previous = actor.Position;
            await harness.ApplyAsync(sleep);
            JsonElement afterSleep = phases[phase++];
            AssertCells(harness, actor, protocol, row, afterSleep, partialReceived && fillsSharedSection);
            if (row.GetProperty("sleep").GetProperty("result").GetString() == "native_property_exception")
            {
                Assert.False(Known());
                Assert.Equal(previous, actor.Position);
                Assert.Single(row.GetProperty("packets").EnumerateArray());
            }
            else
            {
                AssertGeometry(harness, actor, protocol, afterSleep);
                var wake = Assert.IsType<ClientboundAnimatePacket>(Decode(protocol, "animate", Convert.FromHexString(row.GetProperty("packets")[1].GetProperty("body").GetString()!)));
                Assert.Equal(42, wake.EntityId);
                Assert.Equal(2, wake.Action);
                previous = actor.Position;
                await harness.ApplyAsync(wake);
                JsonElement afterWake = phases[phase++];
                AssertCells(harness, actor, protocol, row, afterWake, partialReceived && fillsSharedSection);
                if (Known())
                    AssertGeometry(harness, actor, protocol, afterWake);
                else
                    Assert.Equal(previous, actor.Position);
            }
            Assert.Equal(phases.GetArrayLength(), phase);
            scenarios++;
            continue;

            bool Known() => harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol);
        }
        Assert.Equal(64, scenarios);
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public Task NativePartialCreationAndMutationPreserveEligibleTicksAndWorldQueries(string release, int protocol, int caseIndex)
        => AssertNativeTraversal(release, protocol, caseIndex, full: false);

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public Task NativeFullPayloadPreservesOrReplacesTheOriginalChunkMembership(string release, int protocol, int caseIndex)
        => AssertNativeTraversal(release, protocol, caseIndex, full: true);

    private static async Task AssertNativeTraversal(string release, int protocol, int caseIndex, bool full)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        int scenarios = 0;
        foreach (JsonElement row in native.GetProperty(full ? "full" : "traversal").EnumerateArray())
        {
            ApplierHarness harness = Create(version);
            JsonElement packets = row.GetProperty("world_packets"), setup = row.GetProperty("setup"), phases = row.GetProperty("observations");
            for (int packet = 0; packet < packets.GetArrayLength() - 1; packet++)
                await harness.ApplyAsync(WorldPacket(harness, protocol, native, row, packets[packet]));
            Entity actor = await Spawn(harness, protocol, setup[0]);
            AssertPhase(setup[0]);
            await harness.ApplyAsync(WorldPacket(harness, protocol, native, row, packets[packets.GetArrayLength() - 1]));
            AssertPhase(setup[1]);
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

            void AssertPhase(JsonElement expected)
            {
                AssertGeometry(harness, actor, protocol, expected);
                bool fillsSharedSection = !full && setup[1].GetProperty("reads")[row.GetProperty("partial_cell").GetInt32()].GetProperty("empty_chunk").GetBoolean();
                AssertCells(harness, actor, protocol, row, expected, partialReceived: fillsSharedSection && expected.GetProperty("operation").GetString() != "after_spawn");
                Assert.Equal(expected.GetProperty("added").GetBoolean() && expected.GetProperty("in_entity_list").GetBoolean(), harness.State.EntityMembership.CanTick(actor));
                Assert.Equal(!expected.GetProperty("placement_clear").GetBoolean(), harness.State.EntityMembership.IsWorldQueryMember(actor));
            }
        }
        Assert.Equal(64, scenarios);
    }

    private static ApplierHarness Create(JavaVersion version)
    {
        var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(version.Version.Protocol);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), version.Version.Protocol);
        return harness;
    }

    private static async Task<Entity> Spawn(ApplierHarness harness, int protocol, JsonElement initial)
    {
        JsonElement state = initial.GetProperty("state"), target = state.GetProperty("target");
        Vec3d point = Point(state.GetProperty("position"));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, point.X, point.Y, point.Z, 0, 0, 0, new EntityMetadataList([new(0, MetadataValue.Byte(2))], []))));
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

    private static void AssertCells(ApplierHarness harness, Entity actor, int protocol, JsonElement row, JsonElement phase, bool partialReceived)
    {
        for (int index = 0; index < 3; index++)
        {
            JsonElement native = phase.GetProperty("reads")[index];
            int state = harness.State.EntityGeometry.ReadLegacyBlockState(actor, harness.State.World, Cell(row.GetProperty("cells")[index]), harness.State.EntityMembership);
            if (state < 0)
            {
                Assert.True(native.GetProperty("empty_chunk").GetBoolean());
                Assert.False(protocol == 47 && partialReceived, "An accepted shared section must retain its observed cells, including native air and the occupied-state wake write.");
            }
            else
            {
                int expected = native.GetProperty("bed_native_state").GetInt32();
                Assert.Equal(protocol < 393 ? ((expected & 4095) << 4) | (expected >> 12) : expected, state);
            }
        }
    }

    private static object WorldPacket(ApplierHarness harness, int protocol, JsonElement native, JsonElement row, JsonElement packet)
    {
        string action = packet.GetProperty("action").GetString()!;
        string identifier = action == "seed_bed_a" ? "block_update" : action == "unload_a" && protocol != 47 ? "forget_level_chunk" : "level_chunk_with_light";
        byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
        Assert.Equal(packet.GetProperty("bytes").GetInt32(), body.Length);
        object decoded = Decode(protocol, identifier, body);
        int cell = action == "full_b" ? 1 : action is "partial_payload" or "full_payload" ? row.GetProperty("partial_cell").GetInt32() : 0;
        BlockPos at = Cell(row.GetProperty("cells")[cell]);
        if (decoded is ClientboundBlockUpdatePacket block)
        {
            Assert.Equal(at, block.Position);
            Assert.True(JavaGameData.BedInteractionData(protocol).TryGetBedFacing(block.BlockStateId, out _));
        }
        if (decoded is ClientboundLevelChunkPacket chunk)
        {
            Assert.Equal(ChunkPos.Containing(at), chunk.Column.Position);
            if (action is "partial_payload" or "full_payload")
            {
                Assert.Equal(action == "full_payload", chunk.FullChunk);
                if (action == "partial_payload")
                {
                    Assert.NotNull(chunk.Column.GetSectionForY(64));
                    Assert.Single(Enumerable.Range(0, chunk.Column.SectionCount), index => chunk.Column.GetSection(index) is not null);
                }
                int state = chunk.Column.GetBlockStateId(at.X, at.Y, at.Z);
                if (row.GetProperty("payload").GetInt32() == 0)
                    Assert.Equal(0, state);
                else
                {
                    Assert.True(JavaGameData.BedInteractionData(protocol).TryGetBedFacing(state, out _));
                    var bed = harness.State.World.BlockData.GetState(state);
                    if (bed.LegacyMeta >= 0)
                        Assert.NotEqual(0, bed.LegacyMeta & 4);
                    else
                    {
                        Assert.True(bed.TryGetProperty("occupied", out string occupied));
                        Assert.Equal("true", occupied);
                    }
                }
            }
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
    internal static JsonDocument ReadNative()
    {
        using Stream stream = typeof(LegacyPlayerPartialContextTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerPartialContext.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
