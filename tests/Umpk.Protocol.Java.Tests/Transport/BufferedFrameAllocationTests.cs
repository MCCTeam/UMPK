using System.IO.Pipelines;
using System.Reflection;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public sealed class BufferedFrameAllocationTests
{
    [Fact]
    public async Task IdleTimeoutStillAppliesAfterBufferedFramesAreDrained()
    {
        var pair = DuplexPipePair.Create();
        await using var connection = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            ReadIdleTimeout = TimeSpan.FromMilliseconds(200),
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
        });
        using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pair.Right.Output.WriteAsync(new byte[] { 1, 1, 1, 2 }, bounded.Token);
        connection.Start();
        Assert.Equal(1, (await connection.ReceiveAsync(bounded.Token)).Frame.WireId);
        Assert.Equal(2, (await connection.ReceiveAsync(bounded.Token)).Frame.WireId);
        ConnectionClosedException closed = await Assert.ThrowsAsync<ConnectionClosedException>(
            async () => await connection.ReceiveAsync(bounded.Token));
        Assert.Equal(CloseReason.IdleTimeout, closed.Reason);
    }

    [Fact]
    public async Task BufferedFramesDoNotAllocateIdleTimeoutTimers()
    {
        var pair = DuplexPipePair.Create();
        await using var connection = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            ReadIdleTimeout = TimeSpan.FromSeconds(30),
        });
        MethodInfo method = typeof(JavaConnection).GetMethod("ReadFrameWithTimeoutAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var read = method.CreateDelegate<Func<CancellationToken, ValueTask<FramePayload?>>>(connection);
        const int iterations = 1000;
        var wire = new byte[(iterations + 20) * 3];
        for (int i = 0; i < wire.Length; i += 3)
        {
            wire[i] = 2;
            wire[i + 1] = 1;
        }

        await pair.Right.Output.WriteAsync(wire);
        for (int i = 0; i < 20; i++)
        {
            FramePayload frame = (await read(CancellationToken.None))!.Value;
            FrameReader.ReturnFrame(frame.Buffer);
        }

        int checksum = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            FramePayload frame = (await read(CancellationToken.None))!.Value;
            checksum += frame.Span[0];
            FrameReader.ReturnFrame(frame.Buffer);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(iterations, checksum);
        Assert.True(allocated <= iterations * 16, $"Buffered frame reads allocated {allocated / iterations} bytes per frame; budget is 16 bytes.");
    }
}
