using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.TestKit.Server;
using Umpk.TestKit.Time;
using Umpk.TestKit.World;
using Xunit;

namespace Umpk.PacketRecorder.Tests;

/// <summary>Smoke tests for the TestKit surface downstream suites consume.</summary>
public sealed class TestKitSurfaceTests
{
    [Fact]
    public async Task FakeJavaServer_Script_ExpectAndSend_Runs()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var clientOptions = new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        };
        await using var client = new JavaConnection(server.ClientPipe, clientOptions);
        client.BindCodec(null, PacketFlow.Clientbound);
        client.SetDecodeFilter(PacketDecodeFilter.None);
        client.Start();

        var script = FakeServerScript.Create()
            .ExpectWireId(0x00)
            .SendFrame(0x02, [0xAB, 0xCD]);

        Task serverRun = server.RunAsync(script, ct);

        // Client sends the frame the script expects, then reads the scripted response.
        await client.SendFrameAsync(0x00, new byte[] { 0x01 }, ct);
        await serverRun;

        InboundFrame response = default;
        await foreach (InboundFrame f in client.ReceiveFramesAsync(ct))
        {
            response = f;
            break;
        }

        Assert.Equal(0x02, response.WireId);
        Assert.Equal(new byte[] { 0xAB, 0xCD }, response.Payload.ToArray());
        await client.CloseAsync(CloseReason.Local, ct);
    }

    [Fact]
    public void VoxelWorld_Floor_ResolvesBlocksAndShapes()
    {
        VoxelWorld world = new VoxelWorldBuilder()
            .Floor(-2, -2, 2, 2, 63, VoxelBlock.Stone)
            .Set(0, 64, 0, VoxelBlock.Slab)
            .Build();

        Assert.Equal(VoxelBlock.Stone, world.GetKind(new BlockPos(0, 63, 0)));
        Assert.Equal(VoxelBlock.Slab, world.GetKind(new BlockPos(0, 64, 0)));
        Assert.Equal(VoxelBlock.Air, world.GetKind(new BlockPos(0, 70, 0)));

        Umpk.Game.Blocks.BlockState stone = world.GetBlock(new BlockPos(0, 63, 0));
        Assert.False(world.GetCollisionShapes(stone).IsEmpty);
        Assert.True(world.IsChunkLoaded(new BlockPos(0, 63, 0)));
        Assert.False(world.IsChunkLoaded(new BlockPos(10000, 63, 0)));
    }

    [Fact]
    public async Task TestScheduler_PumpsWorkDeterministically()
    {
        await using var scheduler = new TestScheduler();
        var order = new List<int>();
        scheduler.Post(() => order.Add(1));
        scheduler.Post(() => order.Add(2));

        Assert.Empty(order); // nothing runs until pumped
        int ran = await scheduler.PumpAsync();
        Assert.Equal(2, ran);
        Assert.Equal([1, 2], order);
    }

    [Fact]
    public async Task TestTickSource_AdvanceFires()
    {
        var ticks = new TestTickSource();
        ticks.Advance(3);
        ticks.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int count = 0;
        await foreach (long _ in ticks.Ticks(cts.Token))
            count++;

        Assert.Equal(3, count);
    }
}
