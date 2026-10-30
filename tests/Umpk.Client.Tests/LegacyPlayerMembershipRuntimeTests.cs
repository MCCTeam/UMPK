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

public sealed class LegacyPlayerMembershipRuntimeTests
{
    public static IEnumerable<object[]> Protocols()
    {
        using JsonDocument document = ReadNative();
        foreach (var group in document.RootElement.GetProperty("releases").EnumerateArray().GroupBy(row => row.GetProperty("protocol").GetInt32()))
        {
            JsonElement release = group.Last();
            yield return [release.GetProperty("version").GetString()!, group.Key, release.GetProperty("case").GetInt32()];
        }
    }

    public static IEnumerable<object[]> UnknownWakeCases()
    {
        foreach (int protocol in new[] { 47, 107, 315, 404 })
            foreach (bool crouching in new[] { false, true })
                yield return [protocol, crouching, false];
        foreach (int protocol in new[] { 107, 315, 404 })
            yield return [protocol, true, true];
    }

    [Theory]
    [MemberData(nameof(UnknownWakeCases))]
    public async Task UnknownWakeRetainsUncertaintyUntilExplicitPositionReapplication(int protocol, bool crouching, bool blockedEndpoint)
    {
        var version = Support.BoundPacketApplierHarness.Version(protocol);
        var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Last(row => row.GetProperty("protocol").GetInt32() == protocol);
        JsonElement native = document.RootElement.GetProperty("cases")[release.GetProperty("case").GetInt32()];
        JsonElement packet = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 1).GetProperty("packets")[0];
        await harness.ApplyAsync(DecodeNative(harness, protocol, native, packet));
        using JsonDocument beds = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Umpk.TestKit.FixturePaths.RepoRoot(), "data", "java", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "bed-facings.json")));
        var bed = new BlockPos(0, 64, 0);
        harness.State.World.SetBlockStateId(bed, beds.RootElement.GetProperty("states")[0][0].GetInt32());
        if (blockedEndpoint)
        {
            Assert.True(harness.State.Registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out var stone));
            harness.State.World.SetBlockStateId(new BlockPos(3, 66, 3), stone.DefaultStateId);
        }
        var metadata = new EntityMetadataList([new(0, MetadataValue.Byte(crouching ? (sbyte)2 : (sbyte)0))], []);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, 1.5, 64, 1.5, 0, 0, 0, metadata)));
        var endpoint = new Vec3d(3.5, 65, 3.5);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(42, endpoint.X, endpoint.Y, endpoint.Z, 90, 45, false, null, 0)));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "use_bed", new ClientboundUseBedPacket(42, bed)));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "animate", new ClientboundAnimatePacket(42, 2)));
        Entity actor = harness.State.Entities.Get(42)!;
        Vec3d observed = actor.Position;
        Assert.False(KnownBounds(out _));
        for (int tick = 0; tick < 2; tick++)
        {
            List<int> moved = [];
            harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), moved);
            harness.State.EntityMembership.FinishWorldPhase();
            Assert.False(KnownBounds(out _));
            Assert.Equal(observed, actor.Position);
            Assert.Empty(moved);
        }

        harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);

        // The native final division step is arithmetic, not a snap independent of an unknown start. Only an explicit known position reapplication restores its position contract here.
        Assert.Equal(observed, actor.Position);
        Assert.False(KnownBounds(out _));
        actor.Position = endpoint;
        harness.State.EntityGeometry.ObservePosition(actor);
        harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);
        Assert.Equal(endpoint, actor.Position);
        Assert.Equal(!blockedEndpoint, KnownBounds(out Aabb bounds));
        if (!blockedEndpoint)
            Assert.Equal(crouching && protocol != 47 ? 1.65F : 1.8F, (float)bounds.YSize);
        return;

        bool KnownBounds(out Aabb bounds) => harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out bounds, out _, protocol);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task NativeChunkFramesDriveEligiblePlayerMotionAndCachedBounds(string release, int protocol, int caseIndex)
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
            Vec3d at = Point(native.GetProperty("positions")[row.GetProperty("position").GetInt32()]);
            int scene = row.GetProperty("scene").GetInt32(), packetIndex = 0;
            if (scene is 1 or 5 or 6 or 7)
                await ApplyNative("initial_full");
            Entity actor = await Spawn(at);
            if (scene == 2)
                await ApplyNative("after_spawn_full");
            if (scene == 3)
                await ApplyNative("after_spawn_empty");
            AssertGeometry(harness, actor, protocol, row.GetProperty("observations")[0]);

            for (int tick = 0; tick < 8; tick++)
            {
                if (scene == 4 && tick == 2)
                    await ApplyNative("late_full");
                if (scene == 5 && tick == 2)
                    await ApplyNative("unload");
                if (scene == 5 && tick == 5)
                    await ApplyNative("reload");
                if (scene == 6 && tick == 2)
                    actor = await Spawn(at);
                if (scene == 7 && tick == 2)
                {
                    actor.Position = at.Add(16, 0, 0);
                    harness.State.EntityGeometry.ObservePosition(actor);
                    await Target(actor.Position);
                }
                if (scene == 7 && tick == 4)
                    await ApplyNative("crossing_full");

                harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);

                AssertGeometry(harness, actor, protocol, row.GetProperty("observations")[2 * tick + 1]);
                harness.State.EntityMembership.FinishWorldPhase();
                AssertGeometry(harness, actor, protocol, row.GetProperty("observations")[2 * tick + 2]);
            }
            Assert.Equal(row.GetProperty("packets").GetArrayLength(), packetIndex);
            scenarios++;
            continue;

            async Task<Entity> Spawn(Vec3d point)
            {
                var metadata = new EntityMetadataList([new(0, MetadataValue.Byte(2))], []);
                await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, point.X, point.Y, point.Z, 0, 0, 0, metadata)));
                await Target(point);
                return harness.State.Entities.Get(42)!;
            }

            async Task Target(Vec3d point)
                => await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(42, point.X + 2, point.Y + 1, point.Z + 2, 90, 45, false, null, 0)));

            async Task ApplyNative(string action)
            {
                JsonElement packet = row.GetProperty("packets")[packetIndex++];
                Assert.Equal(action, packet.GetProperty("action").GetString());
                await harness.ApplyAsync(DecodeNative(harness, protocol, native, packet));
            }
        }
        Assert.Equal(32, scenarios);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task NativeMultiPlayerUnloadPreservesReentryLimitAndTickSelection(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        JsonElement packets = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 5).GetProperty("packets");
        await harness.ApplyAsync(DecodeNative(harness, protocol, native, packets[0]));
        JsonElement phases = native.GetProperty("reentry");
        foreach (JsonElement row in phases[0].EnumerateArray())
        {
            int id = row.GetProperty("id").GetInt32();
            var metadata = new EntityMetadataList([new(0, MetadataValue.Byte(2))], []);
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(id, new Guid(id, 0, 0, new byte[8]), 1.5, 64, 1.5, 0, 0, 0, metadata)));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", new ClientboundTeleportEntityPacket(id, 3.5, 65, 3.5, 90, 45, false, null, 0)));
        }
        harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);
        await harness.ApplyAsync(DecodeNative(harness, protocol, native, packets[1]));
        AssertPhase(phases[0]);
        for (int tick = 0; tick < 4; tick++)
        {
            harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);
            AssertPhase(phases[2 * tick + 1]);
            harness.State.EntityMembership.FinishWorldPhase();
            AssertPhase(phases[2 * tick + 2]);
        }
        return;

        void AssertPhase(JsonElement actors)
        {
            foreach (JsonElement row in actors.EnumerateArray())
                AssertGeometry(harness, harness.State.Entities.Get(row.GetProperty("id").GetInt32())!, protocol, row);
        }
    }

    internal static object DecodeNative(ApplierHarness harness, int protocol, JsonElement native, JsonElement packet)
    {
        byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, packet.GetProperty("action").GetString() == "unload" && protocol != 47 ? "forget_level_chunk" : "level_chunk_with_light");
        var context = new PacketCodecContext(harness.State.Registries!, IConnectionCodecState.Empty);
        object decoded = codec.Decode(body, context);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, decoded, context);
        Assert.Equal(body, output.WrittenSpan.ToArray());
        return decoded;
    }

    private static void AssertGeometry(ApplierHarness harness, Entity actor, int protocol, JsonElement expected)
    {
        JsonElement state = expected.GetProperty("state");
        Assert.Equal(Point(state.GetProperty("position")), actor.Position);
        Assert.Equal(state.GetProperty("rotation")[0].GetSingle(), actor.Yaw);
        Assert.Equal(state.GetProperty("rotation")[1].GetSingle(), actor.Pitch);
        Assert.True(harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(protocol), out Aabb box, out bool blocksBuilding, protocol));
        Assert.True(blocksBuilding);
        JsonElement bounds = state.GetProperty("box");
        Assert.Equal(new Aabb(bounds[0].GetDouble(), bounds[1].GetDouble(), bounds[2].GetDouble(), bounds[3].GetDouble(), bounds[4].GetDouble(), bounds[5].GetDouble()), box);
    }

    private static Vec3d Point(JsonElement row) => new(row[0].GetDouble(), row[1].GetDouble(), row[2].GetDouble());

    internal static JsonDocument ReadNative()
    {
        using Stream resource = typeof(LegacyPlayerMembershipRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyPlayerTraversal.native.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
