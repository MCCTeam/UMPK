using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Events;
using Umpk.Client.Internal;
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

public sealed class LegacyPlayerRuntimeTests
{
    private static readonly Lazy<IReadOnlyDictionary<int, byte[]>> NativeColumnBodies = new(ReadColumnBodies);

    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("case").GetInt32()];
    }

    public static IEnumerable<object[]> OccupiedProtocols()
    {
        using JsonDocument document = ReadNative("LegacyBedOccupied");
        for (int i = 0; i < document.RootElement.GetProperty("cases").GetArrayLength(); i++)
            yield return [document.RootElement.GetProperty("cases")[i].GetProperty("protocol").GetInt32(), i];
    }

    [Theory]
    [MemberData(nameof(OccupiedProtocols))]
    public async Task BoundWakeClearsOccupiedExactlyAsOriginalWorldMutation(int protocol, int caseIndex)
    {
        using JsonDocument document = ReadNative("LegacyBedOccupied");
        var harness = Create(protocol, terrain: true);
        var bed = new BlockPos(0, 64, 0);
        for (int x = -1; x <= 0; x++)
            for (int z = -1; z <= 0; z++)
            {
                await LoadColumnAsync(harness, protocol, new ChunkPos(x, z));
                harness.State.World.SetBlockStateId(new BlockPos(x * 16, 0, z * 16), 0);
            }
        IBlockInteractionSource source = JavaGameData.BlockInteractionData(protocol);
        foreach (JsonElement row in document.RootElement.GetProperty("cases")[caseIndex].GetProperty("states").EnumerateArray())
        {
            int before = row[0].GetInt32();
            harness.State.World.SetBlockStateId(bed, before);
            Assert.True(source.TryGetBedFacing(before, out Direction facing));
            var state = harness.State.World.GetBlock(bed);
            bool head = state.LegacyMeta >= 0 ? (state.LegacyMeta & 8) != 0 : state.TryGetProperty("part", out string part) && part == "head";
            BlockPos paired = bed.Offset(head ? facing.Opposite() : facing);
            int pairState;
            if (state.LegacyMeta >= 0)
                pairState = (before & ~4) ^ 8;
            else
            {
                BlockDefinition block = state.Block.Value;
                pairState = Enumerable.Range(block.MinStateId, block.StateCount).First(candidate => block.Properties.All(property =>
                    block.TryGetPropertyValue(before, property.Name, out string current) && block.TryGetPropertyValue(candidate, property.Name, out string other)
                    && (property.Name == "part" ? current != other : current == other)));
            }
            harness.State.World.SetBlockStateId(paired, pairState);
            Entity entity = await Spawn(harness, protocol, new Vec3d(.5, 64, .5));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "use_bed", new ClientboundUseBedPacket(42, bed)));

            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "animate", new ClientboundAnimatePacket(42, 2)));

            Assert.Equal(row[1].GetInt32(), harness.State.World.GetBlockStateId(bed));
            Assert.True(TryBounds(harness, entity, protocol, out _));
            Assert.Equal(new Vec3d(.5, 65.0999984741211, .5), entity.Position);
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task BoundSleepAndRemovedBedWakeMatchNativeCallbacks(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        IBlockInteractionSource data = JavaGameData.BlockInteractionData(protocol);
        using JsonDocument beds = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Umpk.TestKit.FixturePaths.RepoRoot(), "data", "java", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "bed-facings.json")));
        int count = 0;
        foreach (JsonElement row in native.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("flags").GetInt32() != 0 || row.GetProperty("sequence").GetInt32() != 0)
                continue;
            var harness = Create(protocol, terrain: true);
            Vec3d point = Point(native.GetProperty("positions")[row.GetProperty("position").GetInt32()]);
            Entity entity = await Spawn(harness, protocol, point);
            Assert.True(TryBounds(harness, entity, protocol, out _));
            BlockPos bed = Cell(row.GetProperty("bed"));
            await LoadColumnAsync(harness, protocol, ChunkPos.Containing(bed));
            JsonElement vector = native.GetProperty("facings")[row.GetProperty("facing").GetInt32()];
            Direction facing = Facing(vector);
            int state = beds.RootElement.GetProperty("states").EnumerateArray()
                .Select(entry => entry[0].GetInt32()).First(id => data.TryGetBedFacing(id, out Direction value) && value == facing);
            harness.State.World.SetBlockStateId(bed, state);
            entity.Velocity = new Vec3d(.125, .25, -.375);

            Assert.True(await harness.TryApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "use_bed", new ClientboundUseBedPacket(42, bed))));

            JsonElement sleep = row.GetProperty("transitions").EnumerateArray().Single(step => step.GetProperty("operation").GetString() == "sleep");
            Assert.Equal(Point(sleep.GetProperty("position")), entity.Position);
            Assert.True(TryBounds(harness, entity, protocol, out Aabb asleep));
            Assert.Equal(Box(sleep.GetProperty("box")), asleep);
            Assert.Equal(Vec3d.Zero, entity.Velocity);
            harness.State.World.SetBlockStateId(bed, 0);

            Assert.True(await harness.TryApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "animate", new ClientboundAnimatePacket(42, 2))));

            JsonElement wake = row.GetProperty("transitions").EnumerateArray().Single(step => step.GetProperty("operation").GetString() == "wake_after_bed_removal");
            Assert.Equal(Point(wake.GetProperty("position")), entity.Position);
            Assert.True(TryBounds(harness, entity, protocol, out Aabb awake));
            Assert.Equal(Box(wake.GetProperty("box")), awake);
            // Waking changes size without an explicit position callback. An equal-coordinate teleport must still reapply the cached box.
            harness.State.EntityGeometry.ObservePosition(entity);
            Assert.True(TryBounds(harness, entity, protocol, out Aabb repositioned));
            JsonElement final = row.GetProperty("transitions").EnumerateArray().Last();
            Assert.Equal(Box(final.GetProperty("box")), repositioned);
            count++;
        }
        Assert.Equal(24, count);
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task BoundDeathCallbackUpdatesBoundsBeforePublishingStatus(string release, int protocol, int caseIndex)
    {
        Assert.True(JavaVersions.TryGetByName(release, out JavaVersion? version));
        Assert.Equal(protocol, version.Version.Protocol);
        using JsonDocument document = ReadNative();
        JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        JsonElement row = native.GetProperty("rows").EnumerateArray().First(value => value.GetProperty("flags").GetInt32() == 0 && value.GetProperty("sequence").GetInt32() == 1);
        var harness = Create(protocol, terrain: false);
        Entity entity = await Spawn(harness, protocol, Point(native.GetProperty("positions")[row.GetProperty("position").GetInt32()]));
        entity.Position = Point(row.GetProperty("transitions")[0].GetProperty("position"));
        harness.State.EntityGeometry.ObservePosition(entity);
        JsonElement expected = row.GetProperty("transitions").EnumerateArray().Single(step => step.GetProperty("operation").GetString() == "death_event");
        int observed = 0;
        using IDisposable subscription = harness.Events.Subscribe<EntityStatusChanged>(status =>
        {
            Assert.Equal(42, status.EntityId);
            Assert.True(TryBounds(harness, entity, protocol, out Aabb box));
            Assert.Equal(Box(expected.GetProperty("box")), box);
            observed++;
        });

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "entity_event", new ClientboundEntityEventPacket(42, 3)));

        Assert.Equal(1, observed);
        Assert.True(TryBounds(harness, entity, protocol, out Aabb result));
        Assert.Equal(Box(expected.GetProperty("box")), result);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(107)]
    [InlineData(315)]
    [InlineData(404)]
    public async Task BedWithoutWorldDoesNotInventKnownGeometry(int protocol)
    {
        var harness = Create(protocol, terrain: false);
        Entity entity = await Spawn(harness, protocol, new Vec3d(1.5, 64, 1.5));
        Assert.True(TryBounds(harness, entity, protocol, out _));

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "use_bed", new ClientboundUseBedPacket(42, new BlockPos(1, 64, 1))));

        Assert.False(TryBounds(harness, entity, protocol, out _));
    }

    [Fact]
    public void UnknownWakeCandidateStopsBeforeAnyLaterCandidate()
    {
        var geometry = new Actions.VanillaLegacyPlayerGeometry(404, new Vec3d(1.5, 64, 1.5));
        var bed = new BlockPos(1, 64, 1);
        geometry.BeginSleep(bed, Direction.North);
        Vec3d sleepingPosition = geometry.Position;
        List<BlockPos> reads = [];

        bool known = geometry.TryWakeFromIntactBed(bed, Direction.North, UnknownFirst, out BlockPos? exit);

        Assert.False(known);
        Assert.Null(exit);
        Assert.Equal([new BlockPos(0, 64, 0)], reads);
        Assert.Equal(sleepingPosition, geometry.Position);
        Assert.True(geometry.Sleeping);
        Assert.Equal(.6F, geometry.Width);
        Assert.Equal(1.8F, geometry.Height);
        return;

        bool UnknownFirst(BlockPos cell, out bool canStand)
        {
            reads.Add(cell);
            canStand = true;
            return false;
        }
    }

    [Theory]
    [InlineData(47)]
    [InlineData(404)]
    public async Task UnknownWakeTerrainAndReplacementEntityKeepSeparateState(int protocol)
    {
        var harness = Create(protocol, terrain: true);
        var bed = new BlockPos(0, 64, 0);
        using JsonDocument beds = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Umpk.TestKit.FixturePaths.RepoRoot(), "data", "java", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "bed-facings.json")));
        await LoadColumnAsync(harness, protocol, ChunkPos.Containing(bed));
        harness.State.World.SetBlockStateId(bed, beds.RootElement.GetProperty("states")[0][0].GetInt32());
        Entity previous = await Spawn(harness, protocol, new Vec3d(1.5, 64, 1.5));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "use_bed", new ClientboundUseBedPacket(42, bed)));
        Assert.True(TryBounds(harness, previous, protocol, out _));

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "animate", new ClientboundAnimatePacket(42, 2)));

        Assert.False(TryBounds(harness, previous, protocol, out _));
        Entity replacement = await Spawn(harness, protocol, new Vec3d(4.5, 64, 4.5));
        Assert.NotSame(previous, replacement);
        Assert.True(TryBounds(harness, replacement, protocol, out Aabb box));
        Assert.Equal(1.8F, (float)box.YSize);
        Assert.False(TryBounds(harness, previous, protocol, out _));
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        Assert.False(TryBounds(harness, replacement, protocol, out _));
    }

    private static ApplierHarness Create(int protocol, bool terrain)
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = terrain, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        if (terrain)
            WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        return harness;
    }

    private static async Task<Entity> Spawn(ApplierHarness harness, int protocol, Vec3d point)
    {
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, point.X, point.Y, point.Z, 0, 0, 0, new EntityMetadataList([], []))));
        return harness.State.Entities.Get(42)!;
    }

    private static bool TryBounds(ApplierHarness harness, Entity entity, int protocol, out Aabb bounds)
        => harness.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(protocol), out bounds, out _, protocol);

    private static async Task LoadColumnAsync(ApplierHarness harness, int protocol, ChunkPos position)
    {
        // The retained native section payload supplies full terrain. Only the setup coordinates are translated; these bytes are not claimed as a new vanilla capture.
        byte[] body = NativeColumnBodies.Value[protocol].ToArray();
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(0, 4), position.X);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4, 4), position.Z);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "level_chunk_with_light");
        var context = new PacketCodecContext(harness.State.Registries!, IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundLevelChunkPacket>(codec.Decode(body, context));
        Assert.True(packet.FullChunk);
        Assert.Equal(position.X, packet.ChunkX);
        Assert.Equal(position.Z, packet.ChunkZ);
        Assert.Equal(position, packet.Column.Position);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(body, output.WrittenSpan.ToArray());
        await harness.ApplyAsync(packet);
    }

    private static IReadOnlyDictionary<int, byte[]> ReadColumnBodies()
    {
        using JsonDocument document = LegacyPlayerMembershipRuntimeTests.ReadNative();
        var result = new Dictionary<int, byte[]>();
        foreach (var group in document.RootElement.GetProperty("releases").EnumerateArray().GroupBy(row => row.GetProperty("protocol").GetInt32()))
        {
            JsonElement native = document.RootElement.GetProperty("cases")[group.Last().GetProperty("case").GetInt32()];
            JsonElement packet = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 1).GetProperty("packets")[0];
            Assert.Equal("initial_full", packet.GetProperty("action").GetString());
            byte[] body = Convert.FromHexString(native.GetProperty("packet_bodies").GetProperty(packet.GetProperty("sha256").GetString()!).GetString()!);
            Assert.Equal(packet.GetProperty("bytes").GetInt32(), body.Length);
            Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(0, 4)));
            Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(4, 4)));
            result.Add(group.Key, body);
        }
        return result;
    }

    private static Direction Facing(JsonElement value) => (value[0].GetInt32(), value[1].GetInt32()) switch
    {
        (0, -1) => Direction.North,
        (0, 1) => Direction.South,
        (-1, 0) => Direction.West,
        (1, 0) => Direction.East,
        _ => throw new InvalidOperationException("The native bed facing is not horizontal."),
    };

    private static Vec3d Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static BlockPos Cell(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());
    private static Aabb Box(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble(), value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble());

    private static JsonDocument ReadNative(string resource = "LegacyPlayerLifecycle")
    {
        using Stream stream = typeof(LegacyPlayerRuntimeTests).Assembly.GetManifestResourceStream($"Umpk.Client.Tests.Fixtures.{resource}.native.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
