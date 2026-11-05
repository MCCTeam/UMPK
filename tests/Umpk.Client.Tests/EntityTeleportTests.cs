using System.Text.Json;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EntityTeleportTests
{
    public static TheoryData<int> Protocols => [768, 769, 770, 771, 772, 773, 774, 775, 776, 777];
    private static readonly Vec3d Initial = new(10.25, 64.5, -2.75);
    private static readonly Vec3d Velocity = new(0.3, -0.2, 0.7);

    public static IEnumerable<object[]> NativeReleases
    {
        get
        {
            using JsonDocument document = ReadNative();
            foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [release.GetProperty("version").GetString()!];
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void EveryRelativeFlagCombinationMatchesNative(string version)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        JsonElement group = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
        EntityTeleportProfile profile = EntityTeleportProfile.ForProtocol(release.GetProperty("protocol").GetInt32());
        foreach (JsonElement row in group.GetProperty("rows").EnumerateArray())
        {
            JsonElement input = group.GetProperty("inputs")[row.GetProperty("input").GetInt32()];
            PositionMoveRotation actual = profile.Resolve(Values(input.GetProperty("source")), Values(input.GetProperty("change")), row.GetProperty("flags").GetInt32());
            AssertValues(Values(row.GetProperty("result")), actual);
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task RemoteTeleportTargetsMatchNativeAfterCodecDispatch(string version)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        JsonElement group = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
        int protocol = release.GetProperty("protocol").GetInt32();
        (ApplierHarness harness, Entity entity) = Create(protocol);
        foreach (JsonElement row in group.GetProperty("rows").EnumerateArray())
        {
            JsonElement input = group.GetProperty("inputs")[row.GetProperty("input").GetInt32()];
            PositionMoveRotation source = Values(input.GetProperty("source"));
            entity.Position = source.Position;
            entity.Velocity = source.DeltaMovement;
            entity.Yaw = source.YRot;
            entity.Pitch = source.XRot;

            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", Teleport(Values(input.GetProperty("change")), row.GetProperty("flags").GetInt32())));

            AssertValues(Values(row.GetProperty("result")), new PositionMoveRotation(entity.Position, entity.Velocity, entity.Yaw, entity.Pitch));
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public async Task LocalTeleportStateMatchesNativeTransformAndPitchSetter(string version)
    {
        using JsonDocument document = ReadNative();
        JsonElement release = document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        JsonElement group = document.RootElement.GetProperty("groups")[release.GetProperty("group").GetInt32()];
        int protocol = release.GetProperty("protocol").GetInt32();
        (ApplierHarness harness, Entity _) = Create(protocol);
        foreach (JsonElement row in group.GetProperty("rows").EnumerateArray())
        {
            JsonElement input = group.GetProperty("inputs")[row.GetProperty("input").GetInt32()];
            PositionMoveRotation source = Values(input.GetProperty("source")), change = Values(input.GetProperty("change"));
            harness.State.Self.Position = source.Position;
            harness.State.Self.Velocity = source.DeltaMovement;
            harness.State.Self.Yaw = source.YRot;
            harness.State.Self.Pitch = source.XRot;
            var packet = new ClientboundPlayerPositionPacket(change.Position.X, change.Position.Y, change.Position.Z, change.YRot, change.XRot, row.GetProperty("flags").GetInt32(), 7, change);

            await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "player_position", packet));

            PositionMoveRotation expected = Values(row.GetProperty("result"));
            // Native Entity.setXRot applies remainder and clamp after calculateAbsolute, independently of the earlier transform clamp.
            expected = expected with { XRot = Math.Clamp(expected.XRot % 360.0F, -90.0F, 90.0F) };
            AssertValues(expected, new PositionMoveRotation(harness.State.Self.Position, harness.State.Self.Velocity, harness.State.Self.Yaw, harness.State.Self.Pitch));
        }
    }

    private static JsonDocument ReadNative()
    {
        using Stream stream = typeof(EntityTeleportTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityTeleport.native.json")!;
        return JsonDocument.Parse(stream);
    }

    private static PositionMoveRotation Values(JsonElement value)
        => new(new Vec3d(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble()), new Vec3d(value[3].GetDouble(), value[4].GetDouble(), value[5].GetDouble()), value[6].GetSingle(), value[7].GetSingle());

    private static void AssertValues(PositionMoveRotation expected, PositionMoveRotation actual)
    {
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Position.X), BitConverter.DoubleToInt64Bits(actual.Position.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Position.Y), BitConverter.DoubleToInt64Bits(actual.Position.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.Position.Z), BitConverter.DoubleToInt64Bits(actual.Position.Z));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.DeltaMovement.X), BitConverter.DoubleToInt64Bits(actual.DeltaMovement.X));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.DeltaMovement.Y), BitConverter.DoubleToInt64Bits(actual.DeltaMovement.Y));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected.DeltaMovement.Z), BitConverter.DoubleToInt64Bits(actual.DeltaMovement.Z));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.YRot), BitConverter.SingleToInt32Bits(actual.YRot));
        Assert.Equal(BitConverter.SingleToInt32Bits(expected.XRot), BitConverter.SingleToInt32Bits(actual.XRot));
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task ModernTeleportResolvesRelativePositionRotationAndVelocity(int protocol)
    {
        (ApplierHarness harness, Entity entity) = Create(protocol);
        var values = new PositionMoveRotation(new Vec3d(2.5, -3.25, 4.75), new Vec3d(0.01, 0.02, -0.03), 13.125f, 17.625f);

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", Teleport(values, 255)));

        Assert.Equal(new Vec3d(12.75, 61.25, 2), entity.Position);
        Assert.Equal(new Vec3d(0.3 + 0.01, -0.2 + 0.02, 0.7 - 0.03), entity.Velocity);
        Assert.Equal(50.375f, entity.Yaw);
        Assert.Equal(-6.125f, entity.Pitch);
        Assert.True(entity.OnGround);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task AbsoluteModernTeleportReplacesVelocity(int protocol)
    {
        (ApplierHarness harness, Entity entity) = Create(protocol);
        var values = new PositionMoveRotation(new Vec3d(2.5, -3.25, 4.75), new Vec3d(0.01, 0.02, -0.03), 13.125f, 17.625f);

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", Teleport(values, 0)));

        Assert.Equal(values.Position, entity.Position);
        Assert.Equal(values.DeltaMovement, entity.Velocity);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task PositionSyncPreservesVelocity(int protocol)
    {
        (ApplierHarness harness, Entity entity) = Create(protocol);
        var destination = new Vec3d(30.25, 84.5, -22.75);
        var packet = new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(destination, new Vec3d(5, 6, 7), 15, 20), true)
        {
            Path = protocol == 777 ? new EntityPositionPath.Linear(destination) : null,
        };

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "entity_position_sync", packet));

        Assert.Equal(destination, entity.Position);
        Assert.Equal(Velocity, entity.Velocity);
    }

    private static ClientboundTeleportEntityPacket Teleport(PositionMoveRotation values, int flags)
        => new(42, values.Position.X, values.Position.Y, values.Position.Z, values.YRot, values.XRot, true, values, flags);

    private static (ApplierHarness Harness, Entity Entity) Create(int protocol)
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("armor_stand"), out RegistryEntry<EntityTypeDefinition> type));
        var entity = new Entity(42, Guid.NewGuid(), type) { Position = Initial, Velocity = Velocity, Yaw = 37.25f, Pitch = -23.75f };
        harness.State.Entities.Add(entity);
        return (harness, entity);
    }
}
