using System.Text.Json;
using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EntityPacketPositionTests
{
    public static IEnumerable<object[]> NativeReleases
    {
        get
        {
            using JsonDocument document = ReadNativePositions();
            foreach (JsonElement row in document.RootElement.GetProperty("releases").EnumerateArray())
                yield return [row.GetProperty("version").GetString()!, row.GetProperty("protocol").GetInt32(), row.GetProperty("profile").GetInt32()];
        }
    }

    [Theory]
    [MemberData(nameof(NativeReleases))]
    public void PacketCoordinateArithmeticMatchesTheNativeRelease(string release, int protocol, int profile)
    {
        using JsonDocument document = ReadNativePositions();
        EntityPacketPosition decoder = EntityPacketPosition.ForProtocol(protocol);
        int sequences = 0;
        foreach (JsonElement row in document.RootElement.GetProperty("profiles")[profile].EnumerateArray())
        {
            Vec3d current = Vector(row.GetProperty("base"));
            JsonElement steps = row.GetProperty("steps"), expected = row.GetProperty("positions");
            Assert.Equal(steps.GetArrayLength(), expected.GetArrayLength());
            for (int index = 0; index < steps.GetArrayLength(); index++)
            {
                JsonElement step = steps[index];
                current = decoder.Decode(current, step[0].GetInt32(), step[1].GetInt32(), step[2].GetInt32());
                Assert.True(current == Vector(expected[index]), $"Native coordinates differ for {release}, sequence {sequences}, step {index}: {row.GetRawText()}");
            }
            sequences++;
        }
        Assert.Equal(protocol == 47 ? 36 : 72, sequences);
    }

    private static Vec3d Vector(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());

    private static JsonDocument ReadNativePositions()
    {
        using Stream stream = typeof(EntityPacketPositionTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityPacketPosition.native.json")!;
        return JsonDocument.Parse(stream);
    }

    [Theory]
    [InlineData(47, 2.25, 65.5, -2.75)]
    [InlineData(107, 1.10009765625, 65.199951171875, -2.300048828125)]
    [InlineData(340, 1.10009765625, 65.199951171875, -2.300048828125)]
    [InlineData(498, 1.10009765625, 65.199951171875, -2.300048828125)]
    [InlineData(736, 1.10009765625, 65.199951171875, -2.300048828125)]
    [InlineData(751, 1.10009765625, 65.2, -2.3)]
    [InlineData(760, 1.10009765625, 65.2, -2.3)]
    [InlineData(761, 1.100341796875, 65.2, -2.3)]
    [InlineData(777, 1.100341796875, 65.2, -2.3)]
    public async Task RelativePacketsUseTheNativeCoordinateGrid(int protocol, double x, double y, double z)
    {
        foreach (bool rotation in new[] { false, true })
        {
            (ApplierHarness harness, Entity entity) = Create(protocol);
            short delta = protocol == 47 ? (short)32 : (short)1;
            IPacket packet = rotation
                ? new ClientboundMoveEntityPosRotPacket(42, delta, 0, 0, 90, 45, true)
                : new ClientboundMoveEntityPosPacket(42, delta, 0, 0, true);
            object decoded = BoundDescriptorCodec.RoundTrip(protocol, rotation ? "move_entity_pos_rot" : "move_entity_pos", packet);

            await harness.ApplyAsync(decoded);

            Assert.Equal(new Vec3d(x, y, z), entity.Position);
            Assert.True(entity.OnGround);
            if (rotation)
                Assert.Equal(90, entity.Yaw);
        }
    }

    [Theory]
    [InlineData(498, 1.099853515625, 65.199951171875, -2.300048828125)]
    [InlineData(736, 1.099853515625, 65.199951171875, -2.300048828125)]
    [InlineData(751, 1.1, 65.2, -2.3)]
    [InlineData(761, 1.1, 65.2, -2.3)]
    [InlineData(777, 1.1, 65.2, -2.3)]
    public async Task RotationOnlyUsesTheEraSpecificPacketBase(int protocol, double x, double y, double z)
    {
        (ApplierHarness harness, Entity entity) = Create(protocol);
        int moved = 0;
        using IDisposable subscription = harness.Events.Subscribe<EntityMoved>(_ => moved++);
        object decoded = BoundDescriptorCodec.RoundTrip(protocol, "move_entity_rot", new ClientboundMoveEntityRotPacket(42, 90, 45, true));

        await harness.ApplyAsync(decoded);

        Assert.Equal(new Vec3d(x, y, z), entity.Position);
        Assert.Equal(new Vec3d(x, y, z) == new Vec3d(1.1, 65.2, -2.3) ? 0 : 1, moved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingSteppedDeltasStillQuantizeAnAxisThatMoved(bool rotation)
    {
        (ApplierHarness harness, Entity entity) = Create(777);
        EntityMoveStep[] steps = [new(2, 1, 0, 0), new(3, -1, 0, 0)];
        IPacket packet = rotation
            ? new ClientboundMoveEntityPosRotPacket(42, 1, 0, 0, 90, 45, true) { Steps = steps }
            : new ClientboundMoveEntityPosPacket(42, 1, 0, 0, true) { Steps = steps };
        object decoded = BoundDescriptorCodec.RoundTrip(777, rotation ? "move_entity_pos_rot" : "move_entity_pos", packet);

        await harness.ApplyAsync(decoded);

        Assert.Equal(new Vec3d(1.10009765625, 65.2, -2.3), entity.Position);
    }

    private static (ApplierHarness Harness, Entity Entity) Create(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!);
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("cow"), out RegistryEntry<EntityTypeDefinition> cow));
        var entity = new Entity(42, Guid.Empty, cow) { Position = protocol == 47 ? new Vec3d(1.25, 65.5, -2.75) : new Vec3d(1.1, 65.2, -2.3) };
        harness.State.Entities.Add(entity);
        return (harness, entity);
    }
}
