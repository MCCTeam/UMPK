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

public sealed class EntityPacketAngleTests
{
    public static IEnumerable<object[]> NativeCases()
    {
        using JsonDocument document = ReadNative();
        foreach (JsonElement release in document.RootElement.GetProperty("releases").EnumerateArray())
            foreach (JsonElement index in release.GetProperty("cases").EnumerateArray())
                yield return [release.GetProperty("version").GetString()!, release.GetProperty("protocol").GetInt32(), index.GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeCases))]
    public async Task OriginalAngleBytesReachTheRuntimeInTheNativeSignedRange(string version, int protocol, int index)
    {
        using JsonDocument document = ReadNative();
        Assert.Equal(protocol, document.RootElement.GetProperty("releases").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version).GetProperty("protocol").GetInt32());
        JsonElement expected = document.RootElement.GetProperty("cases")[index];
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("armor_stand"), out var type));
        var entity = new Entity(42, Guid.Empty, type) { Position = new Vec3d(100.25, 120.5, 130.75), Yaw = 170, Pitch = -25 };
        harness.State.Entities.Add(entity);
        byte[] body = Convert.FromHexString(expected.GetProperty("body_hex").GetString()!);
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, expected.GetProperty("packet").GetString()!);
        var context = new PacketCodecContext(harness.State.Registries, IConnectionCodecState.Empty);
        object packet = codec.Decode(body, context);
        (float yaw, float pitch) = packet switch
        {
            ClientboundMoveEntityRotPacket rotation => (rotation.Yaw, rotation.Pitch),
            ClientboundMoveEntityPosRotPacket movement => (movement.Yaw, movement.Pitch),
            _ => throw new InvalidOperationException("The native angle fixture contains an unexpected packet."),
        };
        float nativeYaw = expected.GetProperty("yaw").GetSingle(), nativePitch = expected.GetProperty("pitch").GetSingle();
        Assert.Equal(nativeYaw < 0 ? nativeYaw + 360 : nativeYaw, yaw);
        Assert.Equal(nativePitch < 0 ? nativePitch + 360 : nativePitch, pitch);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        Assert.Equal(body, buffer.WrittenSpan.ToArray());

        await harness.ApplyAsync(packet);

        Assert.Equal(nativeYaw, entity.Yaw);
        Assert.Equal(nativePitch, entity.Pitch);
        Assert.Equal(expected.GetProperty("on_ground").GetBoolean(), entity.OnGround);
    }

    private static JsonDocument ReadNative()
    {
        using Stream source = typeof(EntityPacketAngleTests).Assembly.GetManifestResourceStream("Umpk.Client.Tests.Fixtures.EntityPacketAngles.native.json")
            ?? throw new InvalidOperationException("The original entity packet angle fixture is missing.");
        return JsonDocument.Parse(source);
    }
}
