using System.Buffers;
using System.Text.Json;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class RemotePlayerPositionRuntimeTests
{
    public static IEnumerable<object[]> NativeReleases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("group").GetInt32()];
    }

    public static IEnumerable<object[]> LegacyReleases()
    {
        using JsonDocument document = ReadLegacy();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), release.GetProperty("group").GetInt32()];
    }

    public static IEnumerable<object[]> ModernTeleportReleases()
    {
        using JsonDocument document = ReadTeleports();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            if (release.GetProperty("protocol").GetInt32() < 770)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task BoundMovementFramesAdvanceCurrentPoseOnlyOnTheEntityClock(string release, int protocol, int group)
    {
        using JsonDocument document = ReadNative();
        Assert.Equal(protocol, document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release).GetProperty("protocol").GetInt32());
        foreach (JsonElement row in document.RootElement.GetProperty("groups")[group].EnumerateArray())
        {
            var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            IPacket spawn;
            if (protocol < 764)
                spawn = new ClientboundAddPlayerPacket(42, Guid.Empty, 100.25, 120.5, 130.75, 0, 0, 0, new EntityMetadataList([], []));
            else
            {
                Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("player"), out var type));
                spawn = new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, 100.25, 120.5, 130.75, 0, 0, 0, 0, 0, 0, 0, null);
            }
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, protocol < 764 ? "add_player" : "add_entity", spawn));
            Entity entity = harness.State.Entities.Get(42)!;
            JsonElement before = row.GetProperty("before");
            JsonElement initial = document.RootElement.GetProperty("initial_pose");
            entity.Position = Vector(initial);
            entity.Yaw = initial[3].GetSingle();
            entity.Pitch = initial[4].GetSingle();
            int phase = row.GetProperty("phase").GetInt32();
            if (phase > 0)
            {
                Assert.True(harness.State.EntityMotion.TryObserveInterpolation(entity, new Vec3d(40.5, 67, -1.5), -170, 35));
                if (phase == 2)
                    harness.State.EntityMotion.Tick(harness.State.Entities);
            }
            AssertVector(Vector(before), entity.Position);
            Assert.Equal(BitConverter.SingleToInt32Bits(before[3].GetSingle()), BitConverter.SingleToInt32Bits(entity.Yaw));
            Assert.Equal(BitConverter.SingleToInt32Bits(before[4].GetSingle()), BitConverter.SingleToInt32Bits(entity.Pitch));
            entity.OnGround = before[5].GetBoolean();
            harness.State.PacketBases.Set(entity, Vector(before, 6));
            byte[] body = Convert.FromHexString(row.GetProperty("body_hex").GetString()!);
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, row.GetProperty("packet").GetString()!);
            var context = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
            object packet = codec.Decode(body, context);
            var encoded = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(encoded);
            codec.Encode(ref writer, packet, context);
            Assert.Equal(body, encoded.WrittenSpan.ToArray());

            await harness.ApplyAsync(packet);

            AssertState(harness, entity, row.GetProperty("after"));
            foreach (JsonElement tick in row.GetProperty("ticks").EnumerateArray())
            {
                harness.State.EntityMotion.Tick(harness.State.Entities);
                AssertState(harness, entity, tick);
            }
        }
    }

    [Theory]
    [MemberData(nameof(LegacyReleases))]
    public async Task LegacyTeleportFramesPreserveTheNativeCancellationBoundary(string release, int protocol, int group)
    {
        using JsonDocument document = ReadLegacy();
        Assert.Equal(protocol, document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release).GetProperty("protocol").GetInt32());
        int exercised = 0;
        foreach (JsonElement row in document.RootElement.GetProperty("groups")[group].EnumerateArray())
        {
            Vec3d requested = Vector(row.GetProperty("requested"));
            var teleport = new ClientboundTeleportEntityPacket(42, requested.X, requested.Y, requested.Z, -90, -45, true, null, 0);
            object decoded = BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", teleport);
            var wire = Assert.IsType<ClientboundTeleportEntityPacket>(decoded);
            // The source-derived setter probe includes sub-grid inputs that protocol 47 cannot carry.
            if (new Vec3d(wire.X, wire.Y, wire.Z) != requested)
                continue;
            var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, 1.5, 64, 1.5, 0, 0, 0, new EntityMetadataList([], []))));
            Entity entity = harness.State.Entities.Get(42)!;
            entity.Yaw = 170;
            entity.Pitch = -25;
            int phase = row.GetProperty("phase").GetInt32();
            if (phase > 0)
            {
                Assert.True(harness.State.EntityMotion.TryObserveInterpolation(entity, new Vec3d(4.5, 67, -1.5), 90, 30));
                if (phase == 2)
                    harness.State.EntityMotion.Tick(harness.State.Entities);
            }
            AssertPose(entity, row.GetProperty("before"));

            await harness.ApplyAsync(decoded);

            AssertPose(entity, row.GetProperty("after_setter"));
            Assert.Equal(requested, harness.State.PacketBases.Get(entity));
            Assert.True(entity.OnGround);
            foreach (JsonElement tick in row.GetProperty("ticks").EnumerateArray())
            {
                harness.State.EntityMotion.Tick(harness.State.Entities);
                AssertPose(entity, tick.GetProperty("after"));
                Assert.Equal(requested, harness.State.PacketBases.Get(entity));
            }
            exercised++;
        }
        Assert.Equal(protocol == 47 ? 12 : 18, exercised);
    }

    private static void AssertPose(Entity entity, JsonElement expected)
    {
        AssertVector(Vector(expected.GetProperty("position")), entity.Position);
        JsonElement rotation = expected.GetProperty("rotation");
        Assert.Equal(BitConverter.SingleToInt32Bits(rotation[0].GetSingle()), BitConverter.SingleToInt32Bits(entity.Yaw));
        Assert.Equal(BitConverter.SingleToInt32Bits(rotation[1].GetSingle()), BitConverter.SingleToInt32Bits(entity.Pitch));
    }

    [Theory]
    [MemberData(nameof(ModernTeleportReleases))]
    public async Task ModernTeleportFramesUsePendingPositionAndPreserveVelocityTargets(string release, int protocol)
    {
        using JsonDocument document = ReadTeleports();
        JsonElement native = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == release);
        Assert.Equal(protocol, native.GetProperty("protocol").GetInt32());
        JsonElement cases = document.RootElement.GetProperty("cases"), states = document.RootElement.GetProperty("states");
        int exercised = 0;
        foreach (JsonElement index in native.GetProperty("cases").EnumerateArray())
        {
            JsonElement row = cases[index.GetInt32()];
            if (!row.GetProperty("interpolate").GetBoolean())
                continue;
            JsonElement initial = states[row.GetProperty("initial").GetInt32()];
            var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
            harness.State.Registries = JavaGameData.Registries(protocol);
            Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("player"), out var type));
            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_entity", new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, initial[0].GetDouble(), initial[1].GetDouble(), initial[2].GetDouble(), 0, 0, 0, 0, 0, 0, 0, null)));
            Entity entity = harness.State.Entities.Get(42)!;
            entity.Yaw = initial[6].GetSingle();
            entity.Pitch = initial[7].GetSingle();
            entity.Velocity = Vector(initial, 3);
            harness.State.EntityMotion.ObserveVelocity(entity, Vector(document.RootElement.GetProperty("initial_motion_target")));
            int phase = row.GetProperty("phase").GetInt32();
            if (phase > 0)
                Assert.True(harness.State.EntityMotion.TryObserveInterpolation(entity, new Vec3d(40.5, 67, -1.5), -170, 35));
            if (phase == 2)
                harness.State.EntityMotion.Tick(harness.State.Entities);
            AssertModernPose(entity, states[row.GetProperty("before").GetInt32()]);
            JsonElement change = row.GetProperty("change");
            var values = new PositionMoveRotation(Vector(change), Vector(change, 3), change[6].GetSingle(), change[7].GetSingle());
            var teleport = new ClientboundTeleportEntityPacket(42, values.Position.X, values.Position.Y, values.Position.Z, values.YRot, values.XRot, true, values, row.GetProperty("flags").GetInt32());

            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", teleport));

            AssertModernPose(entity, states[row.GetProperty("after").GetInt32()]);
            foreach (JsonElement tick in row.GetProperty("ticks").EnumerateArray())
            {
                harness.State.EntityMotion.Tick(harness.State.Entities);
                AssertModernPose(entity, states[tick.GetProperty("state").GetInt32()]);
            }
            exercised++;
        }
        Assert.Equal(40, exercised);
    }

    [Theory]
    [InlineData(768)]
    [InlineData(769)]
    public async Task PositionSyncKeepsCurrentVelocityAndSnapsOnlyBeyondTheDistanceBoundary(int protocol)
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("player"), out var type));
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_entity", new ClientboundAddEntityPacket(42, Guid.Empty, type.NetworkId, 0, 65, 0, 0, 0, 0, 0, 0, 0, 0, null)));
        Entity entity = harness.State.Entities.Get(42)!;
        var velocity = new Vec3d(0.25, 0.5, -0.75);
        entity.Velocity = velocity;
        var near = new Vec3d(64, 65, 0);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "entity_position_sync", new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(near, new Vec3d(7, 8, 9), 450, 135), true)));
        Assert.Equal(new Vec3d(0, 65, 0), entity.Position);
        Assert.Equal(velocity, entity.Velocity);
        Assert.Equal(near, harness.State.PacketBases.Get(entity));
        var far = new Vec3d(65, 65, 0);

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "entity_position_sync", new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(far, Vec3d.Zero, -450, -135), false)));

        Assert.Equal(far, entity.Position);
        Assert.Equal(-450, entity.Yaw);
        Assert.Equal(-90, entity.Pitch);
        Assert.Equal(velocity, entity.Velocity);
        Assert.Equal(far, harness.State.PacketBases.Get(entity));
        harness.State.EntityMotion.Tick(harness.State.Entities);
        Assert.InRange(entity.Position.X, 64.6, 64.7);
        Assert.Equal(velocity, entity.Velocity);
        Assert.Equal(far, harness.State.PacketBases.Get(entity));
    }

    private static void AssertModernPose(Entity entity, JsonElement expected)
    {
        AssertVector(Vector(expected), entity.Position);
        AssertVector(Vector(expected, 3), entity.Velocity);
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[6].GetSingle()), BitConverter.SingleToInt32Bits(entity.Yaw));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[7].GetSingle()), BitConverter.SingleToInt32Bits(entity.Pitch));
    }

    private static JsonDocument ReadTeleports()
    {
        using Stream source = typeof(RemotePlayerPositionRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityTeleportController.native.json")
            ?? throw new InvalidOperationException("The native teleport controller fixture is missing.");
        return JsonDocument.Parse(source);
    }

    private static JsonDocument ReadLegacy()
    {
        using Stream source = typeof(RemotePlayerPositionRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.LegacyTeleportInterpolation.native.json")
            ?? throw new InvalidOperationException("The native legacy teleport fixture is missing.");
        return JsonDocument.Parse(source);
    }

    private static void AssertState(ApplierHarness harness, Entity entity, JsonElement expected)
    {
        AssertVector(Vector(expected), entity.Position);
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[3].GetSingle()), BitConverter.SingleToInt32Bits(entity.Yaw));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected[4].GetSingle()), BitConverter.SingleToInt32Bits(entity.Pitch));
        Assert.Equal(expected[5].GetBoolean(), entity.OnGround);
        AssertVector(Vector(expected, 6), harness.State.PacketBases.Get(entity));
    }

    private static void AssertVector(Vec3d expected, Vec3d actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.X), BitConverter.DoubleToInt64Bits(actual.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Y), BitConverter.DoubleToInt64Bits(actual.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Z), BitConverter.DoubleToInt64Bits(actual.Z));
    }

    private static Vec3d Vector(JsonElement row, int offset = 0) => new(row[offset].GetDouble(), row[offset + 1].GetDouble(), row[offset + 2].GetDouble());

    private static JsonDocument ReadNative()
    {
        using Stream source = typeof(RemotePlayerPositionRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.MappedPlayerInterpolation.native.json")
            ?? throw new InvalidOperationException("The native mapped player interpolation fixture is missing.");
        return JsonDocument.Parse(source);
    }
}
