using System.IO.Compression;
using System.Text.Json;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ShulkerGeometryEdgeRuntimeTests
{
    public static IEnumerable<object[]> NativeEdges()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            for (int index = 0; index < 16; index++)
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index];
    }

    [Theory]
    [MemberData(nameof(NativeEdges))]
    public async Task PassengerCoordinatesPeekTargetsAndVelocityMatchNativeCallbacks(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        Assert.Equal(protocol, release.GetProperty("protocol").GetInt32());
        JsonElement scenario = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()][index];
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol));
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(Internal.WorldFactory.InstallWorld(harness.State, new Internal.CommonWorldSetup("minecraft:overworld", 0), protocol));
        harness.State.World.LoadColumn(new ChunkPos(0, -1));
        Assert.True(harness.State.Registries.EntityTypes.TryGetNetworkId(Identifier.Minecraft("shulker"), out int shulker));
        Assert.True(harness.State.Registries.EntityTypes.TryGetNetworkId(Identifier.Minecraft("oak_boat"), out int boat));
        await harness.ApplyAsync(new ClientboundAddEntityPacket(42, Guid.Empty, shulker, 1.2, 64.2, -2.8, 0, 0, 0, 0, 0, 0, 0, null));
        await harness.ApplyAsync(new ClientboundAddEntityPacket(43, Guid.NewGuid(), boat, .5, 64, .5, 0, 0, 0, 0, 0, 0, 0, null));
        await harness.ApplyAsync(new ClientboundAddEntityPacket(44, Guid.NewGuid(), boat, .5, 64, .5, 0, 0, 0, 0, 0, 0, 0, null));
        Entity entity = Assert.IsType<Entity>(harness.State.Entities.Get(42));
        Assert.True(harness.State.Registries.Attributes.TryGetNetworkId(Identifier.Minecraft("scale"), out int scale));
        await harness.ApplyAsync(new ClientboundUpdateAttributesPacket(42, [new AttributeSnapshot(null, scale, scenario.GetProperty("scale").GetDouble(), [])]));
        await Metadata(16, MetadataValue.Direction(Enum.Parse<Direction>(scenario.GetProperty("face").GetString()!, ignoreCase: true)));
        await Position(new Vec3d(1.2, 64.2, -2.8));
        await Metadata(6, MetadataValue.Pose(scenario.GetProperty("pose").GetString() == "SLEEPING" ? EntityPose.Sleeping : EntityPose.Standing));
        AssertState(scenario.GetProperty("initial"));
        foreach (JsonElement step in scenario.GetProperty("steps").EnumerateArray())
        {
            switch (step.GetProperty("operation").GetString())
            {
                case "prepare":
                    await Metadata(17, MetadataValue.Byte(100));
                    for (int tick = 0; tick < step.GetProperty("input").GetInt32(); tick++)
                        Tick();
                    break;
                case "velocity":
                    await harness.ApplyAsync(new ClientboundSetEntityMotionPacket(42, 2000, 4000, -6000, null));
                    break;
                case "mount":
                    bool mounted = step.GetProperty("input").GetBoolean();
                    await harness.ApplyAsync(new ClientboundSetPassengersPacket(mounted ? 43 : entity.Vehicle?.Id ?? 43, mounted ? [42] : []));
                    break;
                case "pose":
                    await Metadata(6, MetadataValue.Pose(EntityPose.Sleeping));
                    break;
                case "transfer":
                    await harness.ApplyAsync(new ClientboundSetPassengersPacket(44, [42]));
                    break;
                case "position":
                    await Position(Vector(step.GetProperty("input")));
                    break;
                case "peek":
                    await Metadata(17, MetadataValue.Byte((sbyte)step.GetProperty("input").GetInt32()));
                    break;
                case "tick":
                    Tick();
                    break;
                default:
                    throw new InvalidOperationException("Unknown native shulker edge operation.");
            }
            AssertState(step);
        }

        ValueTask Metadata(int field, MetadataValue value) => harness.ApplyAsync(new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(field, value)], [])));
        ValueTask Position(Vec3d value) => harness.ApplyAsync(new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(value, Vec3d.Zero, 0, 0), false));
        void Tick() => harness.State.EntityGeometry.TickLivingDimensions(harness.State.Entities, harness.State.World);
        void AssertState(JsonElement expected)
        {
            JsonElement state = expected.GetProperty("state");
            Assert.Equal(Vector(state.GetProperty("position")), entity.Position);
            Assert.Equal(expected.GetProperty("passenger").GetBoolean(), entity.Vehicle is not null);
            Assert.Equal(expected.GetProperty("pose").GetString() == "SLEEPING" ? EntityPose.Sleeping : EntityPose.Standing, entity.Pose);
            Assert.Equal(Vector(expected.GetProperty("velocity")), entity.Velocity);
            Assert.True(harness.State.EntityGeometry.TryGet(entity, JavaGameData.EntityGeometryData(protocol), out Aabb box, out bool blocking));
            Assert.True(blocking);
            JsonElement bounds = state.GetProperty("bounds");
            Assert.Equal(new Aabb(bounds[0].GetDouble(), bounds[1].GetDouble(), bounds[2].GetDouble(), bounds[3].GetDouble(), bounds[4].GetDouble(), bounds[5].GetDouble()), box);
            sbyte raw = entity.Metadata.TryGet(17, out MetadataValue peek) ? peek.AsByte() : (sbyte)0;
            Assert.Equal((sbyte)state.GetProperty("raw_peek").GetInt32(), raw);
            Direction face = entity.Metadata.TryGet(16, out MetadataValue attachment) ? attachment.AsDirection() : Direction.Down;
            Assert.Equal(Enum.Parse<Direction>(state.GetProperty("face").GetString()!, ignoreCase: true), face);
        }
    }

    private static Vec3d Vector(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(ShulkerGeometryEdgeRuntimeTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.ShulkerGeometryEdges.native.json.gz")!;
        using var decompressed = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(decompressed);
    }
}
