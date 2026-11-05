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

public sealed class EntityIngressAngleTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement protocol in document.RootElement.GetProperty("protocols").EnumerateArray())
            foreach (JsonElement index in protocol.GetProperty("cases").EnumerateArray())
                yield return [protocol.GetProperty("protocol").GetInt32(), index.GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public async Task ByteAnglesReachEntityStateInTheVanillaSignedRange(int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        JsonElement expected = document.RootElement.GetProperty("cases")[index];
        string name = expected.GetProperty("packet").GetString()!;
        (ApplierHarness harness, Entity entity) = Create(protocol);
        byte[] body = Convert.FromHexString(expected.GetProperty("body_hex").GetString()!);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, name);
        Assert.NotNull(harness.State.Registries);
        var context = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
        object packet = codec.Decode(body, context);
        (float? Yaw, float? Pitch, float? Head) angles = packet switch
        {
            ClientboundAddEntityPacket add => (add.YRot, add.XRot, expected.TryGetProperty("head", out _) ? add.YHeadRot : null),
            ClientboundAddMobPacket mob => (mob.Yaw, mob.Pitch, mob.HeadPitch),
            ClientboundAddPlayerPacket player => (player.Yaw, player.Pitch, null),
            ClientboundRotateHeadPacket rotation => (null, null, rotation.HeadYaw),
            ClientboundTeleportEntityPacket teleport => (teleport.Yaw, teleport.Pitch, null),
            _ => throw new InvalidOperationException("Unexpected packet in the native ingress angle fixture."),
        };
        AssertUnsigned("yaw", angles.Yaw);
        AssertUnsigned("pitch", angles.Pitch);
        AssertUnsigned("head", angles.Head);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(body, buffer.WrittenSpan.ToArray());

        await harness.ApplyAsync(packet);

        Assert.True(harness.State.Entities.TryGet(42, out Entity? applied));
        Assert.NotNull(applied);
        entity = applied;
        if (angles.Yaw.HasValue)
            Assert.Equal(expected.GetProperty("yaw").GetSingle(), entity.Yaw);
        if (angles.Pitch.HasValue)
            Assert.Equal(expected.GetProperty("pitch").GetSingle(), entity.Pitch);
        if (angles.Head.HasValue)
            Assert.Equal(expected.GetProperty("head").GetSingle(), entity.HeadYaw);
        if (packet is ClientboundRotateHeadPacket)
        {
            Assert.Equal(170, entity.Yaw);
            Assert.Equal(-25, entity.Pitch);
        }

        void AssertUnsigned(string axis, float? value)
        {
            if (!value.HasValue)
                return;
            float native = expected.GetProperty(axis).GetSingle();
            Assert.Equal(native < 0 ? native + 360 : native, value.Value);
        }
    }

    [Theory]
    [InlineData(768)]
    [InlineData(769)]
    [InlineData(770)]
    [InlineData(773)]
    [InlineData(776)]
    [InlineData(777)]
    public async Task FloatAnglesKeepTheirFullTurns(int protocol)
    {
        foreach (bool sync in new[] { false, true })
        {
            (ApplierHarness harness, Entity entity) = Create(protocol);
            var destination = new Vec3d(20, 75, -10);
            var values = new PositionMoveRotation(destination, Vec3d.Zero, 450, 45);
            IPacket packet = sync
                ? new ClientboundEntityPositionSyncPacket(42, values, true)
                {
                    Path = protocol == 777 ? new EntityPositionPath.Linear(destination) : null,
                }
                : new ClientboundTeleportEntityPacket(42, destination.X, destination.Y, destination.Z, 450, 45, true, values, 0);
            object decoded = BoundDescriptorCodec.RoundTrip(protocol, sync ? "entity_position_sync" : "teleport_entity", packet);

            await harness.ApplyAsync(decoded);

            Assert.Equal(450, entity.Yaw);
            Assert.Equal(45, entity.Pitch);
        }
    }

    private static (ApplierHarness Harness, Entity Entity) Create(int protocol)
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("armor_stand"), out var type));
        var entity = new Entity(42, Guid.Empty, type) { Position = new Vec3d(100, 120, 130), Yaw = 170, Pitch = -25, HeadYaw = 15 };
        harness.State.Entities.Add(entity);
        return (harness, entity);
    }

    private static JsonDocument ReadNative()
    {
        using Stream source = typeof(EntityIngressAngleTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityIngressAngles.native.json")
            ?? throw new InvalidOperationException("The native ingress angle fixture is missing.");
        return JsonDocument.Parse(source);
    }
}
