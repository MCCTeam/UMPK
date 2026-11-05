using System.Buffers;
using System.Text.Json;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class Door315NativeAmbientGeometryTests
{

    [Theory]
    [InlineData(-1, "control")]
    [InlineData(1942, "bat")]
    [InlineData(838, "squid")]
    public async Task NativeIronPassAndClearStonePlacementMustSendUseThenSwing(int ambientFrame, string family)
    {
        using JsonDocument capture = LegacyAmbientMobNativeReplayTests.ReadNative();
        Assert.Equal("9697f79aec0e9c413fb193265ffaa7af2b1475b347f4bee80913aed36fb5f384", capture.RootElement.GetProperty("capture_sha256").GetString());
        Assert.True(JavaVersions.TryGetByProtocol(315, out JavaVersion? version));
        await using BlockUseSession session = await BlockUseSession.Create(315, "iron_door", physics: true);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var clicked = new BlockPos(66, 100, 192);
        var neighbor = clicked.Offset(Direction.West);
        await SendOriginal(577); // Actual full chunk(4,12) from the failed native session.
        await SendOriginal(893); // Actual correction to(64.5,100,192.5).
        await session.Block(clicked, "iron_door");
        await session.Block(clicked.Above(), "iron_door", upper: true);
        await session.Block(neighbor, "air");
        await SendOriginal(4484); // Actual native held stone8 stack frame, before the failed use.
        if (ambientFrame >= 0)
            await SendOriginal(ambientFrame);
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityMembership.HasColumn(clicked));
            Assert.Equal(new Vec3d(64.5, 100, 192.5), client.State.Self.Position);
            Assert.Equal(1136, client.State.World.GetBlock(clicked).StateId);
            Assert.Equal(0, client.State.World.GetBlock(neighbor).StateId);
            Assert.Equal(8, client.State.Inventory.GetSlot(36).Count);
            Assert.True(client.State.Inventory.GetSlot(36).Components.HasNoPatch);
            if (ambientFrame >= 0)
            {
                Entity actor = Assert.Single(client.State.Entities.All);
                Assert.Equal(Identifier.Minecraft(family), actor.Type.Id);
                Assert.True(actor.Position.Y < 60);
                Assert.False(JavaGameData.BlockInteractionData(315).IsAlwaysNonObstructingEntity(actor.Type.Id));
            }
            return true;
        }, session.Token);
        await session.Client.Actions.Interaction.PlaceBlockAsync(clicked, Direction.West, new Vec3d(0, .5, .5), Hand.Main, ct: session.Token);
        List<IPacket> sent = await session.Barrier();
        Assert.Collection(sent, packet => Assert.IsType<ServerboundUseItemOnPacket>(packet), packet => Assert.IsType<ServerboundSwingPacket>(packet));
        Assert.Equal(8, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.True(session.Client.State.World.GetBlock(neighbor).IsAir);

        async Task SendOriginal(int index)
        {
            JsonElement frame = capture.RootElement.GetProperty("captured_frames").GetProperty(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            int wire = frame.GetProperty("wire").GetInt32();
            byte[] body = frame.GetProperty("body_base64").GetBytesFromBase64();
            var registry = version!.Protocol.GetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound);
            Assert.True(registry.TryGetInbound(wire, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented);
            object packet = codec.Decode(body, context);
            var encoded = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(encoded);
            codec.Encode(ref writer, packet, context);
            Assert.Equal(body, encoded.WrittenSpan.ToArray());
            await session.Server.SendFrameAsync(wire, body, session.Token);
        }
    }
}
